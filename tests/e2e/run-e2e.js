/*
 * Сквозной тест всей цепочки (кроме перехвата клавиатуры WinAPI и окна WPF):
 *
 *   харнесс «приложения» (PlayerController + PipeServer, тот же код, что вызывают горячие клавиши)
 *     ⇄ именованный канал ⇄ VkHotkeys.Bridge (настоящий процесс, запущенный браузером через Native Messaging)
 *     ⇄ расширение (service worker + content script + page.js) в настоящем Chromium
 *     ⇄ тестовая страница https://vk.com/audio с имитацией плеера VK.
 *
 * Запуск (из корня репозитория, после dotnet build):
 *   node tests/e2e/run-e2e.js
 * Работает на Linux/macOS/Windows; нужен Playwright с Chromium.
 */
'use strict';

const { chromium } = require('playwright');
const { spawn } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');
const readline = require('readline');

const ROOT = path.resolve(__dirname, '..', '..');
const EXT = path.join(ROOT, 'extension');
const BRIDGE_DLL = path.join(ROOT, 'src', 'VkHotkeys.Bridge', 'bin', 'Debug', 'net10.0', 'VkHotkeys.Bridge.dll');
const HARNESS_DLL = path.join(__dirname, 'harness', 'bin', 'Debug', 'net10.0', 'E2EHarness.dll');
const EXT_ID = 'jpmdbkdhjihibaiicamanjichgkpgila';
const PIPE = `vkhk-e2e-${process.pid}`;

let failures = 0;
function check(name, cond, details) {
  if (cond) console.log(`  ✔ ${name}`);
  else {
    failures++;
    console.log(`  ✘ ${name}${details ? ` — ${JSON.stringify(details)}` : ''}`);
  }
}

function startHarness() {
  const proc = spawn('dotnet', [HARNESS_DLL], { env: { ...process.env, VKHOTKEYS_PIPE: PIPE } });
  proc.stderr.on('data', (d) => process.stderr.write(`[harness] ${d}`));
  const rl = readline.createInterface({ input: proc.stdout });
  const queue = [];
  const waiters = [];
  rl.on('line', (line) => {
    let msg;
    try { msg = JSON.parse(line); } catch { return; }
    if (waiters.length) waiters.shift()(msg);
    else queue.push(msg);
  });
  const next = () => new Promise((resolve) => (queue.length ? resolve(queue.shift()) : waiters.push(resolve)));
  return {
    proc,
    next,
    async send(cmd) {
      proc.stdin.write(`${cmd}\n`);
      return next();
    },
  };
}

function setupNativeHost(userDataDir) {
  // Linux/macOS: Chromium ищет пользовательские хосты в <user-data-dir>/NativeMessagingHosts.
  const hostDir = path.join(userDataDir, 'NativeMessagingHosts');
  fs.mkdirSync(hostDir, { recursive: true });
  const wrapper = path.join(userDataDir, 'bridge.sh');
  fs.writeFileSync(wrapper, `#!/bin/sh\nexport VKHOTKEYS_PIPE='${PIPE}'\nexec dotnet '${BRIDGE_DLL}' "$@"\n`, { mode: 0o755 });
  fs.writeFileSync(path.join(hostDir, 'com.vkhotkeys.bridge.json'), JSON.stringify({
    name: 'com.vkhotkeys.bridge',
    description: 'VK Hotkeys bridge (e2e)',
    path: wrapper,
    type: 'stdio',
    allowed_origins: [`chrome-extension://${EXT_ID}/`],
  }));
}

async function main() {
  for (const f of [BRIDGE_DLL, HARNESS_DLL]) {
    if (!fs.existsSync(f)) throw new Error(`Не найден ${f}. Сначала: dotnet build tests/e2e/harness && dotnet build src/VkHotkeys.Bridge`);
  }

  const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vkhk-e2e-'));
  setupNativeHost(userDataDir);
  const harness = startHarness();
  await harness.next(); // { ready: true }

  const context = await chromium.launchPersistentContext(userDataDir, {
    headless: true,
    channel: 'chromium', // «новый» headless: поддерживает расширения
    args: [
      `--disable-extensions-except=${EXT}`,
      `--load-extension=${EXT}`,
      '--autoplay-policy=no-user-gesture-required',
    ],
  });

  const legacy = fs.readFileSync(path.join(__dirname, 'fixtures', 'vk-legacy.html'), 'utf8');
  const empty = fs.readFileSync(path.join(__dirname, 'fixtures', 'vk-empty.html'), 'utf8');
  await context.route(/https:\/\/(.*\.)?vk\.(com|ru)\/.*/, (route) => {
    const url = new URL(route.request().url());
    const body = url.pathname.startsWith('/audio') ? legacy : empty;
    route.fulfill({ status: 200, contentType: 'text/html; charset=utf-8', body });
  });

  try {
    console.log('\n[1] Браузер с расширением подключается к приложению');
    let s = await harness.send('wait-NoVkTab');
    check('статус «вкладка VK не открыта», пока VK не открыт', s.status === 'NoVkTab', s);
    let r = await harness.send('NextTrack');
    check('F10 без вкладки VK — честная ошибка', !r.ok && /не открыта/.test(r.message), r);

    console.log('\n[2] Страница VK без плеера');
    const feed = await context.newPage();
    await feed.goto('https://vk.com/feed');
    s = await harness.send('wait-PlayerNotFound');
    check('статус «плеер не найден»', s.status === 'PlayerNotFound', s);
    r = await harness.send('PlayPause');
    check('F8 без плеера — ошибка, а не ложный успех', !r.ok, r);
    await feed.close();

    for (const mode of ['full', 'ap', 'ms']) {
      console.log(`\n[3:${mode}] Плеер VK (${{ full: 'ap + Media Session', ap: 'только window.ap', ms: 'только Media Session' }[mode]})`);
      const page = await context.newPage();
      await page.goto(`https://vk.com/audio?mode=${mode}`);
      await page.evaluate(() => window.__start());
      s = await harness.send('wait-Ready');
      check('плеер найден, статус Ready', s.status === 'Ready', s);
      await page.waitForTimeout(400);
      s = await harness.send('status');
      check('название и исполнитель получены (HTML-сущности раскодированы)',
        s.title === 'First & Song' && s.artist === 'Artist One', s);
      check('воспроизведение определено', s.playing === true, s);
      check('браузер определён', !!s.browser, s);

      r = await harness.send('NextTrack');
      let st = await page.evaluate(() => window.__state());
      check('F10 → следующий трек', r.ok && st.idx === 1 && r.state.title === 'Second Song', { r, st });

      r = await harness.send('PreviousTrack');
      st = await page.evaluate(() => window.__state());
      check('F9 → предыдущий трек', r.ok && st.idx === 0, { r, st });

      r = await harness.send('PlayPause');
      st = await page.evaluate(() => window.__state());
      check('F8 → пауза', r.ok && st.paused && r.message === 'Пауза', { r, st });
      r = await harness.send('PlayPause');
      st = await page.evaluate(() => window.__state());
      check('F8 → продолжить', r.ok && !st.paused && r.message === 'Воспроизведение', { r, st });

      s = await harness.send('status');
      const before = s.volume;
      r = await harness.send('VolumeUp');
      st = await page.evaluate(() => window.__state());
      const expectUp = Math.min(100, before + 5);
      check(`Ctrl+Alt+Up → громкость ${before}% → ${expectUp}%`, r.ok && r.state.volume === expectUp, { r, st });
      r = await harness.send('VolumeDown');
      r = await harness.send('VolumeDown');
      check('Ctrl+Alt+Down ×2 → громкость −10%', r.ok && r.state.volume === expectUp - 10, r);

      const beforeMute = r.state.volume;
      r = await harness.send('ToggleMute');
      st = await page.evaluate(() => window.__state());
      check('Ctrl+Alt+M → звук выключен (громкость плеера 0)', r.ok && r.state.muted && r.state.volume === 0 && st.elVolume === 0, { r, st });
      r = await harness.send('ToggleMute');
      check(`Ctrl+Alt+M → громкость восстановлена (${beforeMute}%)`, r.ok && !r.state.muted && r.state.volume === beforeMute, r);

      const t0 = (await page.evaluate(() => window.__state())).time;
      r = await harness.send('SeekForward');
      st = await page.evaluate(() => window.__state());
      check('Ctrl+Alt+Shift+Right → +10 с', r.ok && st.time >= t0 + 9, { r, st, t0 });
      r = await harness.send('SeekBackward');
      st = await page.evaluate(() => window.__state());
      check('Ctrl+Alt+Shift+Left → −10 с', r.ok && st.time < t0 + 5, { r, st, t0 });

      st = await page.evaluate(() => window.__state());
      const usedMs = st.log.some((l) => l.startsWith('ms.'));
      if (mode === 'ap') check('без Media Session используется window.ap', !usedMs && st.log.includes('ap.playNext'), st.log);
      if (mode === 'full') check('при наличии Media Session используются её обработчики', st.log.includes('ms.next'), st.log);
      if (mode === 'ms') check('без window.ap используются обработчики Media Session', usedMs, st.log);

      await page.close();
      s = await harness.send('wait-NoVkTab');
      check('вкладка закрыта → статус обновился', s.status === 'NoVkTab', s);
    }

    console.log('\n[4] Устойчивость: перезапуск service worker');
    const page = await context.newPage();
    await page.goto('https://vk.com/audio?mode=full');
    await page.evaluate(() => window.__start());
    await harness.send('wait-Ready');
    const [sw] = context.serviceWorkers();
    if (sw) {
      // Имитируем остановку SW браузером: отключаем порт моста изнутри и даём расширению переподключиться.
      await sw.evaluate(() => { if (native) native.disconnect(); native = null; scheduleRetry(); });
      await page.waitForTimeout(300);
      s = await harness.send('wait-Ready');
      check('после разрыва Native Messaging связь восстановилась', s.status === 'Ready', s);
      r = await harness.send('NextTrack');
      check('команды снова работают', r.ok, r);
    } else {
      check('service worker найден', false);
    }
  } finally {
    await context.close();
    harness.proc.stdin.end();
    harness.proc.kill();
    fs.rmSync(userDataDir, { recursive: true, force: true });
  }

  console.log(failures ? `\nПРОВАЛЕНО проверок: ${failures}` : '\nВсе проверки пройдены');
  process.exit(failures ? 1 : 0);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
