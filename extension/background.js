/*
 * VK Hotkeys — service worker.
 *
 * Держит соединение Native Messaging с мостом VkHotkeys.Bridge.exe (а тот — с Windows-приложением),
 * собирает состояние плеера со всех вкладок VK и пересылает команды в нужную вкладку.
 * Открытый порт connectNative не даёт браузеру усыпить service worker.
 */
'use strict';

const HOST_NAME = 'com.vkhotkeys.bridge';
const VK_URLS = ['https://vk.com/*', 'https://*.vk.com/*', 'https://vk.ru/*', 'https://*.vk.ru/*'];
const TAB_TIMEOUT_MS = 3500;

/** @type {Map<number, {id:number, port:chrome.runtime.Port, state:any, updatedAt:number}>} */
const tabs = new Map();
/** @type {Map<string, {tabId:number, resolve:Function, timer:number}>} */
const pending = new Map();

let native = null;
let nativeConnected = false;
let appConnected = false;
let nativeError = null;
let retryDelay = 1000;
let retryTimer = null;
let lastStateKey = '';
let seq = 0;

// ------------------------------------------------------------------ Native Messaging

function browserName() {
  const brands = ((navigator.userAgentData && navigator.userAgentData.brands) || []).map((b) => b.brand);
  const ua = navigator.userAgent;
  const has = (re) => brands.some((b) => re.test(b));
  if (has(/Edge/i) || /Edg\//.test(ua)) return 'Edge';
  if (has(/Yandex/i) || /YaBrowser/.test(ua)) return 'Yandex';
  if (has(/Opera/i) || /OPR\//.test(ua)) return 'Opera';
  if (has(/Brave/i)) return 'Brave';
  if (/Vivaldi/.test(ua)) return 'Vivaldi';
  if (has(/Google Chrome/i)) return 'Chrome';
  return 'Chromium';
}

function postNative(msg) {
  if (!native) return false;
  try {
    native.postMessage(msg);
    return true;
  } catch (_) {
    return false;
  }
}

function sendHello() {
  postNative({ type: 'hello', browser: browserName(), version: chrome.runtime.getManifest().version });
}

function connectNative() {
  if (native) return;
  if (retryTimer) {
    clearTimeout(retryTimer);
    retryTimer = null;
  }
  let port;
  try {
    port = chrome.runtime.connectNative(HOST_NAME);
  } catch (e) {
    nativeError = String((e && e.message) || e);
    scheduleRetry();
    return;
  }
  native = port;

  port.onMessage.addListener((msg) => {
    nativeConnected = true;
    nativeError = null;
    retryDelay = 1000;
    onNativeMessage(msg);
  });

  port.onDisconnect.addListener(() => {
    nativeError = (chrome.runtime.lastError && chrome.runtime.lastError.message) || 'Мост отключился';
    if (native === port) native = null;
    nativeConnected = false;
    appConnected = false;
    scheduleRetry();
  });

  sendHello();
  sendState(true);
}

function scheduleRetry() {
  if (retryTimer) return;
  retryTimer = setTimeout(() => {
    retryTimer = null;
    connectNative();
  }, retryDelay);
  retryDelay = Math.min(retryDelay * 2, 30000);
}

function onNativeMessage(msg) {
  if (!msg || typeof msg !== 'object') return;
  switch (msg.type) {
    case 'appStatus':
      appConnected = !!msg.connected;
      if (appConnected) {
        sendHello();
        sendState(true);
      }
      break;
    case 'command':
      dispatch(msg.action, msg.value).then((r) => {
        postNative({ type: 'result', id: msg.id, ok: r.ok, error: r.error || null, state: r.state || aggregate() });
      });
      break;
    case 'ping':
      postNative({ type: 'pong' });
      break;
    default:
      break;
  }
}

// ------------------------------------------------------------------ вкладки VK

function score(t) {
  const s = t.state || {};
  return [s.playerFound && s.playing ? 1 : 0, s.playerFound && s.hasTrack ? 1 : 0, s.lastPlayingAt || 0,
    s.playerFound ? 1 : 0, t.updatedAt];
}

function compare(a, b) {
  const sa = score(a);
  const sb = score(b);
  for (let i = 0; i < sa.length; i++) if (sa[i] !== sb[i]) return sb[i] - sa[i];
  return 0;
}

/** Вкладка, которой управляем: играющая > с треком > недавно игравшая > с плеером > любая. */
function selectTab() {
  if (tabs.size === 0) return null;
  return [...tabs.values()].sort(compare)[0];
}

function aggregate() {
  const t = selectTab();
  const s = (t && t.state) || {};
  return {
    vkTabs: tabs.size,
    tabId: t ? t.id : null,
    playerFound: !!s.playerFound,
    hasTrack: !!s.hasTrack,
    playing: !!s.playing,
    title: s.title || null,
    artist: s.artist || null,
    volume: typeof s.volume === 'number' ? s.volume : null,
    position: typeof s.position === 'number' ? s.position : null,
    duration: typeof s.duration === 'number' ? s.duration : null,
    canSeek: !!s.canSeek,
    strategy: s.strategy || null,
    lastPlayingAt: s.lastPlayingAt || 0,
  };
}

function sendState(force) {
  const state = aggregate();
  const key = JSON.stringify(state);
  if (!force && key === lastStateKey) return;
  lastStateKey = key;
  postNative({ type: 'state', state });
}

function dispatch(action, value) {
  return new Promise((resolve) => {
    const tab = selectTab();
    if (!tab) {
      resolve({ ok: false, error: 'no_vk_tab' });
      return;
    }
    const id = `c${++seq}`;
    const timer = setTimeout(() => {
      pending.delete(id);
      resolve({ ok: false, error: 'timeout' });
    }, TAB_TIMEOUT_MS);
    pending.set(id, { tabId: tab.id, resolve, timer });
    try {
      tab.port.postMessage({ type: 'command', id, action, value });
    } catch (_) {
      clearTimeout(timer);
      pending.delete(id);
      resolve({ ok: false, error: 'tab_disconnected' });
    }
  });
}

chrome.runtime.onConnect.addListener((port) => {
  if (port.name !== 'vk-tab' || !port.sender || !port.sender.tab) return;
  const tabId = port.sender.tab.id;
  const prev = tabs.get(tabId);
  const entry = { id: tabId, port, state: prev ? prev.state : null, updatedAt: Date.now() };
  tabs.set(tabId, entry);

  port.onMessage.addListener((msg) => {
    if (!msg || typeof msg !== 'object') return;
    if (msg.state && typeof msg.state === 'object') {
      entry.state = msg.state;
      entry.updatedAt = Date.now();
    }
    if (msg.type === 'result') {
      const p = pending.get(msg.id);
      if (p) {
        clearTimeout(p.timer);
        pending.delete(msg.id);
        p.resolve({ ok: !!msg.ok, error: msg.error || null, state: aggregate() });
      }
    }
    sendState(false);
  });

  port.onDisconnect.addListener(() => {
    void chrome.runtime.lastError;
    if (tabs.get(tabId) && tabs.get(tabId).port === port) tabs.delete(tabId);
    for (const [id, p] of pending) {
      if (p.tabId === tabId) {
        clearTimeout(p.timer);
        pending.delete(id);
        p.resolve({ ok: false, error: 'tab_disconnected' });
      }
    }
    sendState(false);
  });

  connectNative();
  sendState(false);
});

// ------------------------------------------------------------------ popup

chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
  if (!msg || typeof msg !== 'object') return false;
  if (msg.type === 'popup:get') {
    sendResponse({ nativeConnected, appConnected, nativeError, state: aggregate() });
    return false;
  }
  if (msg.type === 'popup:cmd') {
    dispatch(msg.action, msg.value).then(sendResponse);
    return true;
  }
  if (msg.type === 'popup:reconnect') {
    retryDelay = 1000;
    connectNative();
    sendResponse({ ok: true });
    return false;
  }
  return false;
});

// ------------------------------------------------------------------ жизненный цикл

/** После установки/обновления расширения подключаемся к уже открытым вкладкам VK без перезагрузки. */
async function injectIntoOpenTabs() {
  let list = [];
  try {
    list = await chrome.tabs.query({ url: VK_URLS });
  } catch (_) {
    return;
  }
  for (const tab of list) {
    if (tab.id === undefined || tab.discarded) continue;
    try {
      await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ['page.js'], world: 'MAIN' });
      await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ['content.js'] });
    } catch (_) {
      /* вкладка недоступна — подключится при следующей загрузке */
    }
  }
}

chrome.runtime.onInstalled.addListener(() => {
  injectIntoOpenTabs();
});

chrome.runtime.onStartup.addListener(() => connectNative());

chrome.alarms.create('vkhk-keepalive', { periodInMinutes: 1 });
chrome.alarms.onAlarm.addListener((a) => {
  if (a.name === 'vkhk-keepalive') connectNative();
});

connectNative();
