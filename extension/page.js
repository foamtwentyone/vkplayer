/*
 * VK Hotkeys — скрипт страницы (выполняется в MAIN world на vk.com / vk.ru).
 *
 * Задача: найти музыкальный плеер VK и управлять им через интерфейсы самой страницы:
 *   1. обработчики Media Session API, которые VK регистрирует для мультимедийных клавиш
 *      (nexttrack / previoustrack / play / pause / seekto) — самый стабильный «публичный» контракт;
 *   2. внутренний объект плеера VK window.ap (playNext, playPrev, setVolume, ...), если он есть;
 *   3. HTML-элемент <audio>, через который реально идёт звук (громкость, перемотка);
 *   4. кнопки плеера в DOM (element.click(), без эмуляции мыши) — последний запасной вариант.
 *
 * Каждая команда проверяется: успех возвращается, только если состояние плеера действительно
 * изменилось. Если VK поменяет вёрстку и плеер не найдётся — вернётся ошибка, а не исключение.
 */
(() => {
  'use strict';

  if (window.__vkhkPageInstalled) return;
  try {
    Object.defineProperty(window, '__vkhkPageInstalled', { value: true });
  } catch (_) {
    return;
  }

  const TAG = '__vkhk';
  const media = new Set();
  const handlers = Object.create(null);
  let primary = null;
  let lastPlayingAt = 0;
  let lastPlayBlockedAt = 0;
  let desiredVolume = null;

  // Селекторы кнопок плеера: старая и новая вёрстка VK. Используются только как запасной вариант.
  const DOM_BUTTONS = {
    next: ['.top_audio_player_next', '.audio_page_player_next', '[data-testid="audio-player-controls-forward-button"]',
      '[data-testid="audioplayer-next"]', 'button[aria-label="Следующий трек"]', 'button[aria-label="Next track"]'],
    prev: ['.top_audio_player_prev', '.audio_page_player_prev', '[data-testid="audio-player-controls-backward-button"]',
      '[data-testid="audioplayer-prev"]', 'button[aria-label="Предыдущий трек"]', 'button[aria-label="Previous track"]'],
    play: ['.top_audio_player_play', '.audio_page_player_play', '[data-testid="audio-player-controls-state-button"]',
      '[data-testid="audioplayer-play"]'],
  };
  const DOM_TITLE = ['.top_audio_player_title', '[data-testid="audio-player-track-title"]', '.audio_page_player_title_song'];
  const DOM_ARTIST = ['[data-testid="audio-player-track-artist"]', '.audio_page_player_title_performer'];

  // ------------------------------------------------------------------ перехват media-элементов

  function isCandidate(el) {
    // Аудио — всегда; видео — только «невидимое» (VK может проигрывать HLS-аудио через скрытый <video>).
    return el instanceof HTMLAudioElement || (el instanceof HTMLVideoElement && !el.isConnected);
  }

  const MEDIA_EVENTS = ['play', 'playing', 'pause', 'ended', 'volumechange', 'loadedmetadata', 'durationchange', 'emptied', 'seeked'];

  function trackElement(el) {
    if (!isCandidate(el) || media.has(el)) return;
    media.add(el);
    for (const type of MEDIA_EVENTS) el.addEventListener(type, onMediaEvent, { passive: true });
    if (desiredVolume !== null && !apVolumeApi()) {
      try { el.volume = desiredVolume; } catch (_) { /* ignore */ }
    }
    prune();
  }

  function onMediaEvent(e) {
    const el = e.currentTarget;
    if (e.type === 'play' || e.type === 'playing') {
      primary = el;
      lastPlayingAt = Date.now();
    }
    scheduleEmit();
  }

  function prune() {
    if (media.size <= 8) return;
    for (const el of media) {
      if (el !== primary && !el.isConnected && !el.currentSrc && media.size > 8) media.delete(el);
    }
  }

  try {
    const proto = HTMLMediaElement.prototype;
    const originalPlay = proto.play;
    proto.play = function play() {
      try { trackElement(this); } catch (_) { /* ignore */ }
      const result = originalPlay.apply(this, arguments);
      if (result && typeof result.catch === 'function') {
        result.catch((err) => {
          if (err && err.name === 'NotAllowedError') lastPlayBlockedAt = Date.now();
        });
      }
      return result;
    };
  } catch (_) { /* ignore */ }

  document.addEventListener('play', (e) => {
    try { trackElement(e.target); onMediaEvent({ type: 'play', currentTarget: e.target }); } catch (_) { /* ignore */ }
  }, true);

  // ------------------------------------------------------------------ перехват Media Session

  try {
    if (typeof MediaSession !== 'undefined' && MediaSession.prototype.setActionHandler) {
      const originalSet = MediaSession.prototype.setActionHandler;
      MediaSession.prototype.setActionHandler = function setActionHandler(action, handler) {
        try {
          if (typeof handler === 'function') handlers[action] = handler;
          else delete handlers[action];
          scheduleEmit();
        } catch (_) { /* ignore */ }
        return originalSet.apply(this, arguments);
      };

      const desc = Object.getOwnPropertyDescriptor(MediaSession.prototype, 'metadata');
      if (desc && desc.set && desc.get && desc.configurable) {
        Object.defineProperty(MediaSession.prototype, 'metadata', {
          configurable: true,
          enumerable: desc.enumerable,
          get() { return desc.get.call(this); },
          set(v) { desc.set.call(this, v); scheduleEmit(); },
        });
      }
    }
  } catch (_) { /* ignore */ }

  // ------------------------------------------------------------------ доступ к плееру

  function getAp() {
    try {
      const ap = window.ap;
      if (ap && typeof ap === 'object' && (typeof ap.playNext === 'function' || typeof ap.play === 'function')) return ap;
    } catch (_) { /* ignore */ }
    return null;
  }

  function apFn(name) {
    const ap = getAp();
    return ap && typeof ap[name] === 'function' ? (...args) => ap[name](...args) : null;
  }

  function apVolumeApi() {
    return !!(apFn('setVolume') && apFn('getVolume'));
  }

  function getElement() {
    let fallback = null;
    for (const el of media) {
      if (!el.paused && !el.ended) return el;
      if (el === primary) fallback = el;
    }
    if (fallback) return fallback;
    for (const el of media) if (el.currentSrc) return el;
    return null;
  }

  function domButton(kind) {
    for (const sel of DOM_BUTTONS[kind] || []) {
      try {
        const el = document.querySelector(sel);
        if (el) return el;
      } catch (_) { /* ignore */ }
    }
    return null;
  }

  function domText(selectors) {
    for (const sel of selectors) {
      try {
        const el = document.querySelector(sel);
        const t = el && clean(el.textContent);
        if (t) return t;
      } catch (_) { /* ignore */ }
    }
    return null;
  }

  function clean(s) {
    if (s === null || s === undefined) return null;
    s = String(s);
    if (/[<&]/.test(s)) {
      try {
        s = new DOMParser().parseFromString(s, 'text/html').documentElement.textContent || '';
      } catch (_) { /* ignore */ }
    }
    s = s.replace(/\s+/g, ' ').trim();
    return s || null;
  }

  function apAudio() {
    const get = apFn('getCurrentAudio');
    if (!get) return null;
    try {
      const a = get();
      if (!a) return null;
      if (Array.isArray(a)) return { title: clean(a[3]), artist: clean(a[4]), duration: Number(a[5]) || null };
      if (typeof a === 'object') {
        return { title: clean(a.title), artist: clean(a.performer || a.artist), duration: Number(a.duration) || null };
      }
    } catch (_) { /* ignore */ }
    return null;
  }

  function metadata() {
    try {
      const m = navigator.mediaSession && navigator.mediaSession.metadata;
      if (m && m.title) return { title: clean(m.title), artist: clean(m.artist) };
    } catch (_) { /* ignore */ }
    const a = apAudio();
    if (a && a.title) return { title: a.title, artist: a.artist };
    const t = domText(DOM_TITLE);
    if (t) return { title: t, artist: domText(DOM_ARTIST) };
    return { title: null, artist: null };
  }

  function isPlaying() {
    const el = getElement();
    if (el) return !el.paused && !el.ended;
    const fn = apFn('isPlaying');
    if (fn) {
      try { return !!fn(); } catch (_) { /* ignore */ }
    }
    try {
      return navigator.mediaSession && navigator.mediaSession.playbackState === 'playing';
    } catch (_) {
      return false;
    }
  }

  function getVolume() {
    const fn = apFn('getVolume');
    if (fn) {
      try {
        const v = Number(fn());
        if (Number.isFinite(v)) return clamp(v > 1 ? v / 100 : v, 0, 1);
      } catch (_) { /* ignore */ }
    }
    const el = getElement();
    if (el) return el.muted ? 0 : el.volume;
    return null;
  }

  function getDuration() {
    const el = getElement();
    if (el && Number.isFinite(el.duration) && el.duration > 0) return el.duration;
    const a = apAudio();
    return a && a.duration ? a.duration : null;
  }

  function getPosition() {
    const el = getElement();
    if (el && Number.isFinite(el.currentTime)) return el.currentTime;
    const fn = apFn('getCurrentProgress');
    const d = getDuration();
    if (fn && d) {
      try {
        const p = Number(fn());
        if (Number.isFinite(p)) return clamp(p, 0, 1) * d;
      } catch (_) { /* ignore */ }
    }
    return null;
  }

  function trackKey() {
    const m = metadata();
    const el = getElement();
    return `${m.title || ''}\u0001${m.artist || ''}\u0001${el ? el.currentSrc : ''}`;
  }

  function hasTrack() {
    const m = metadata();
    if (m.title) return true;
    const el = getElement();
    return !!(el && el.currentSrc);
  }

  function playerFound() {
    return !!getAp() || media.size > 0 || Object.keys(handlers).length > 0 || !!domButton('next');
  }

  function strategyName() {
    const parts = [];
    if (Object.keys(handlers).length) parts.push('mediaSession');
    if (getAp()) parts.push('ap');
    if (media.size) parts.push('media');
    if (!parts.length && domButton('next')) parts.push('dom');
    return parts.join('+') || 'none';
  }

  function snapshot() {
    const m = metadata();
    const pos = getPosition();
    const dur = getDuration();
    const vol = getVolume();
    return {
      playerFound: playerFound(),
      hasTrack: hasTrack(),
      playing: isPlaying(),
      title: m.title,
      artist: m.artist,
      volume: vol === null ? null : Math.round(vol * 1000) / 1000,
      position: pos === null ? null : Math.round(pos * 10) / 10,
      duration: dur === null ? null : Math.round(dur * 10) / 10,
      canSeek: dur !== null && (!!handlers.seekto || !!apFn('seek') || !!getElement()),
      strategy: strategyName(),
      lastPlayingAt,
    };
  }

  // ------------------------------------------------------------------ команды

  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

  async function waitFor(predicate, timeoutMs) {
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
      try { if (predicate()) return true; } catch (_) { /* ignore */ }
      await sleep(50);
    }
    try { return !!predicate(); } catch (_) { return false; }
  }

  /**
   * Выполняет первую доступную стратегию. Следующая пробуется только если предыдущая недоступна
   * или выбросила исключение — так одно нажатие никогда не переключит два трека.
   */
  function runFirst(strategies) {
    for (const [name, available, exec] of strategies) {
      let ok = false;
      try { ok = !!available(); } catch (_) { ok = false; }
      if (!ok) continue;
      try {
        exec();
        return name;
      } catch (_) { /* пробуем следующую */ }
    }
    return null;
  }

  const callHandler = (action, extra) => () => handlers[action](Object.assign({ action }, extra || {}));

  async function switchTrack(direction) {
    if (!playerFound()) return { ok: false, error: 'player_not_found' };
    if (!hasTrack()) return { ok: false, error: 'no_track' };
    const before = trackKey();
    const posBefore = getPosition();
    const el = getElement();
    const msAction = direction === 'next' ? 'nexttrack' : 'previoustrack';
    const apName = direction === 'next' ? 'playNext' : 'playPrev';

    const used = runFirst([
      ['mediaSession', () => handlers[msAction], callHandler(msAction)],
      ['ap', () => apFn(apName), () => apFn(apName)()],
      ['dom', () => domButton(direction), () => domButton(direction).click()],
    ]);
    if (!used) return { ok: false, error: 'player_not_found' };

    const changed = await waitFor(() => {
      if (trackKey() !== before) return true;
      // «Назад» в середине трека в VK перезапускает текущий трек — это тоже корректная реакция.
      const p = getPosition();
      if (direction === 'prev' && posBefore !== null && posBefore > 3 && p !== null && p < 2) return true;
      const cur = getElement();
      return cur !== el && !!cur;
    }, 2000);
    return changed ? { ok: true, strategy: used } : { ok: false, error: 'no_effect', strategy: used };
  }

  async function setPlaying(play) {
    if (!playerFound()) return { ok: false, error: 'player_not_found' };
    if (play && !hasTrack() && !getAp()) return { ok: false, error: 'no_track' };
    const el = getElement();
    const blockedBefore = lastPlayBlockedAt;
    const used = runFirst(play
      ? [
        ['mediaSession', () => handlers.play, callHandler('play')],
        ['ap', () => apFn('play'), () => apFn('play')()],
        ['media', () => el, () => { el.play(); }],
        ['dom', () => domButton('play'), () => domButton('play').click()],
      ]
      : [
        ['mediaSession', () => handlers.pause, callHandler('pause')],
        ['ap', () => apFn('pause'), () => apFn('pause')()],
        ['media', () => el, () => el.pause()],
        ['dom', () => domButton('play'), () => domButton('play').click()],
      ]);
    if (!used) return { ok: false, error: 'player_not_found' };

    const changed = await waitFor(() => isPlaying() === play, 1500);
    if (changed) return { ok: true, strategy: used };
    if (play && lastPlayBlockedAt !== blockedBefore) return { ok: false, error: 'autoplay_blocked', strategy: used };
    if (play && !hasTrack()) return { ok: false, error: 'no_track', strategy: used };
    return { ok: false, error: 'no_effect', strategy: used };
  }

  async function setVolume(value) {
    const v = clamp(Number(value), 0, 1);
    if (!Number.isFinite(v)) return { ok: false, error: 'internal' };
    if (!playerFound()) return { ok: false, error: 'player_not_found' };

    const close = () => {
      const cur = getVolume();
      return cur !== null && Math.abs(cur - v) < 0.02;
    };

    const setApi = apFn('setVolume');
    if (setApi) {
      try {
        setApi(v);
        if (await waitFor(close, 300)) return { ok: true, strategy: 'ap' };
      } catch (_) { /* пробуем элемент */ }
    }

    const el = getElement();
    if (el) {
      desiredVolume = v;
      try {
        el.volume = v;
        if (v > 0 && el.muted) el.muted = false;
      } catch (_) { /* ignore */ }
      if (Math.abs(el.volume - v) < 0.02) return { ok: true, strategy: 'media' };
    }
    return { ok: false, error: 'volume_unavailable' };
  }

  async function seek(delta) {
    const d = Number(delta);
    if (!Number.isFinite(d) || d === 0) return { ok: false, error: 'internal' };
    if (!playerFound()) return { ok: false, error: 'player_not_found' };
    if (!hasTrack()) return { ok: false, error: 'no_track' };

    const pos = getPosition();
    const dur = getDuration();
    if (pos === null || dur === null) return { ok: false, error: 'seek_unavailable' };
    const target = clamp(pos + d, 0, Math.max(0, dur - 1));
    const el = getElement();
    const apSeek = apFn('seek');

    const used = runFirst([
      ['mediaSession', () => handlers.seekto, callHandler('seekto', { seekTime: target, fastSeek: false })],
      ['ap', () => apSeek, () => apSeek(target / dur)],
      ['media', () => el && Number.isFinite(el.duration), () => { el.currentTime = target; }],
      ['mediaSession', () => handlers[d > 0 ? 'seekforward' : 'seekbackward'],
        callHandler(d > 0 ? 'seekforward' : 'seekbackward', { seekOffset: Math.abs(d) })],
    ]);
    if (!used) return { ok: false, error: 'seek_unavailable' };

    const moved = await waitFor(() => {
      const p = getPosition();
      return p !== null && Math.abs(p - target) < 2.5;
    }, 1000);
    return moved ? { ok: true, strategy: used } : { ok: false, error: 'no_effect', strategy: used };
  }

  async function execute(action, value) {
    switch (action) {
      case 'next': return switchTrack('next');
      case 'prev': return switchTrack('prev');
      case 'toggle': return setPlaying(!isPlaying());
      case 'play': return setPlaying(true);
      case 'pause': return setPlaying(false);
      case 'setVolume': return setVolume(value);
      case 'seek': return seek(value);
      default: return { ok: false, error: 'internal' };
    }
  }

  // ------------------------------------------------------------------ связь с content script

  function post(msg) {
    try {
      window.postMessage(Object.assign({ [TAG]: 'page' }, msg), window.location.origin);
    } catch (_) { /* ignore */ }
  }

  let lastKey = '';
  let lastSent = 0;
  let emitTimer = null;

  function emit(force) {
    let state;
    try {
      state = snapshot();
    } catch (_) {
      state = { playerFound: false, hasTrack: false, playing: false, strategy: 'error', lastPlayingAt };
    }
    const key = JSON.stringify(Object.assign({}, state, { position: null }));
    const now = Date.now();
    if (force || key !== lastKey || (state.playing && now - lastSent >= 1000)) {
      lastKey = key;
      lastSent = now;
      post({ kind: 'state', state });
    }
  }

  function scheduleEmit() {
    if (emitTimer) return;
    emitTimer = setTimeout(() => {
      emitTimer = null;
      emit(false);
    }, 50);
  }

  window.addEventListener('message', (e) => {
    if (e.source !== window) return;
    const d = e.data;
    if (!d || d[TAG] !== 'cs') return;
    if (d.kind === 'getState') {
      emit(true);
    } else if (d.kind === 'command') {
      Promise.resolve()
        .then(() => execute(d.action, d.value))
        .catch(() => ({ ok: false, error: 'internal' }))
        .then((r) => {
          let state = null;
          try { state = snapshot(); } catch (_) { /* ignore */ }
          post({ kind: 'result', id: d.id, ok: !!r.ok, error: r.error || null, strategy: r.strategy || null, state });
          emit(true);
        });
    }
  });

  setInterval(() => emit(false), 1000);
  emit(true);
})();
