/*
 * VK Hotkeys — content script (изолированный мир). Посредник между скриптом страницы (page.js)
 * и service worker расширения. Сам плеером не управляет.
 */
(() => {
  'use strict';
  if (window.__vkhkContentInstalled) return;
  window.__vkhkContentInstalled = true;

  const TAG = '__vkhk';
  let port = null;
  let alive = true;

  function toPage(msg) {
    window.postMessage(Object.assign({ [TAG]: 'cs' }, msg), window.location.origin);
  }

  function extensionAlive() {
    try {
      return !!(chrome.runtime && chrome.runtime.id);
    } catch (_) {
      return false;
    }
  }

  function connect() {
    if (!alive || port) return;
    if (!extensionAlive()) {
      alive = false; // расширение обновлено/удалено — этот экземпляр больше не нужен
      return;
    }
    try {
      port = chrome.runtime.connect({ name: 'vk-tab' });
    } catch (_) {
      port = null;
      setTimeout(connect, 2000);
      return;
    }

    port.onMessage.addListener((msg) => {
      if (!msg || typeof msg !== 'object') return;
      if (msg.type === 'command') toPage({ kind: 'command', id: msg.id, action: msg.action, value: msg.value });
      else if (msg.type === 'getState') toPage({ kind: 'getState' });
    });

    port.onDisconnect.addListener(() => {
      void chrome.runtime.lastError;
      port = null;
      // Service worker перезапустился — переподключаемся.
      setTimeout(connect, 1000);
    });

    toPage({ kind: 'getState' });
  }

  window.addEventListener('message', (e) => {
    if (e.source !== window) return;
    const d = e.data;
    if (!d || d[TAG] !== 'page' || !port) return;
    try {
      if (d.kind === 'state') port.postMessage({ type: 'state', state: d.state });
      else if (d.kind === 'result') {
        port.postMessage({ type: 'result', id: d.id, ok: d.ok, error: d.error, strategy: d.strategy, state: d.state });
      }
    } catch (_) {
      port = null;
      setTimeout(connect, 1000);
    }
  });

  // Возврат на страницу из bfcache: порт был закрыт браузером.
  window.addEventListener('pageshow', (e) => {
    if (e.persisted) connect();
  });

  connect();
})();
