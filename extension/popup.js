'use strict';

const ERRORS = {
  no_vk_tab: 'Вкладка VK не открыта',
  player_not_found: 'Плеер VK не найден на странице',
  no_track: 'Трек не выбран — запустите музыку в VK',
  no_effect: 'Плеер VK не отреагировал',
  autoplay_blocked: 'Браузер запретил автозапуск — кликните один раз на странице VK',
  tab_disconnected: 'Вкладка VK закрыта',
  timeout: 'Вкладка не ответила',
};

const $ = (id) => document.getElementById(id);

function setDot(el, cls) {
  el.className = `dot ${cls || ''}`;
}

function render(s) {
  if (!s) return;
  if (s.appConnected) {
    setDot($('dotApp'), 'ok');
    $('txtApp').textContent = 'Приложение VK Hotkeys подключено';
  } else if (s.nativeConnected) {
    setDot($('dotApp'), 'warn');
    $('txtApp').textContent = 'Мост работает, приложение не запущено';
  } else {
    setDot($('dotApp'), 'err');
    $('txtApp').textContent = /not found/i.test(s.nativeError || '')
      ? 'Мост не зарегистрирован — запустите VK Hotkeys'
      : 'Нет связи с приложением';
  }

  const st = s.state || {};
  if (!st.vkTabs) {
    setDot($('dotVk'), 'err');
    $('txtVk').textContent = 'Вкладка VK не открыта';
  } else if (!st.playerFound) {
    setDot($('dotVk'), 'warn');
    $('txtVk').textContent = 'Плеер VK не найден';
  } else {
    setDot($('dotVk'), 'ok');
    $('txtVk').textContent = st.playing ? 'Играет' : 'Пауза';
  }

  $('title').textContent = st.title || '—';
  $('artist').textContent = st.artist || '';
  const disabled = !st.playerFound;
  for (const id of ['btnPrev', 'btnToggle', 'btnNext']) $(id).disabled = disabled;
}

function refresh() {
  chrome.runtime.sendMessage({ type: 'popup:get' }, (r) => {
    void chrome.runtime.lastError;
    render(r);
  });
}

function command(action) {
  $('hint').textContent = '';
  chrome.runtime.sendMessage({ type: 'popup:cmd', action }, (r) => {
    void chrome.runtime.lastError;
    if (r && !r.ok) $('hint').textContent = ERRORS[r.error] || `Ошибка: ${r.error}`;
    refresh();
  });
}

$('btnPrev').addEventListener('click', () => command('prev'));
$('btnToggle').addEventListener('click', () => command('toggle'));
$('btnNext').addEventListener('click', () => command('next'));
$('btnOpenVk').addEventListener('click', () => chrome.tabs.create({ url: 'https://vk.com/audio' }));
$('btnReconnect').addEventListener('click', () => {
  chrome.runtime.sendMessage({ type: 'popup:reconnect' }, () => {
    void chrome.runtime.lastError;
    setTimeout(refresh, 500);
  });
});

refresh();
setInterval(refresh, 1000);
