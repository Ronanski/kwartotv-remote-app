namespace KwartoTV.App.Services;

/// <summary>
/// Netflix controlled through its own player (not by guessing on the page): play/pause, seek, skip intro/recap,
/// next episode and search, from the phone. The intro is skipped only when the phone's Skip intro is pressed.
/// </summary>
public static class NetflixControl
{
    public const string Script = @"
(function () {
  if (window.__kwNf) return; window.__kwNf = true;
  function post(t) { try { window.chrome.webview.postMessage('netflix:' + t); } catch (e) {} }
  function player() {
    try {
      var vp = window.netflix.appContext.state.playerApp.getAPI().videoPlayer;
      var ids = vp.getAllPlayerSessionIds();
      var id = ids.filter(function (x) { return x.indexOf('watch') >= 0; })[0] || ids[ids.length - 1];
      return id ? vp.getVideoPlayerBySessionId(id) : null;
    } catch (e) { return null; }
  }
  function click(sel) { var b = document.querySelector(sel); if (b) { b.click(); return true; } return false; }
  // Audio and subtitles: from the player when it offers them, otherwise from Netflix's own Audio & Subtitles menu.
  var MENU = '[data-uia=""control-audio-subtitle""]', AUDIO = '[data-uia^=""audio-item""]', SUBS = '[data-uia^=""subtitle-item""]';
  // Netflix's player calls subtitles ""timed text"" (getTimedTextTrackList / setTimedTextTrack); older builds ""text"".
  function subList(p) { var f = p.getTimedTextTrackList || p.getTextTrackList; return f ? f.bind(p) : null; }
  function subNow(p) { var f = p.getTimedTextTrack || p.getTextTrack; return f ? f.bind(p) : null; }
  function subSet(p) { var f = p.setTimedTextTrack || p.setTextTrack; return f ? f.bind(p) : null; }
  function name(x, i) { return (x && (x.displayName || x.language || x.bcp47)) || ('Track ' + (i + 1)); }
  function lists() {
    var p = player();
    if (p && p.getAudioTrackList && subList(p)) {
      try {
        var a = p.getAudioTrackList() || [], t = subList(p)() || [], ca = p.getAudioTrack && p.getAudioTrack(), ct = subNow(p) && subNow(p)();
        return { via: 'player',
          audio: a.map(function (x, i) { return { i: i, n: name(x, i), on: !!ca && ca.trackId === x.trackId }; }),
          subs: t.map(function (x, i) { return { i: i, n: x && x.isNoneTrack ? 'Off' : name(x, i), on: !!ct && ct.trackId === x.trackId }; }) };
      } catch (e) { }
    }
    if (!document.querySelector(AUDIO)) click(MENU);
    function rows(sel) { return Array.prototype.map.call(document.querySelectorAll(sel), function (el, i) {
      return { i: i, n: (el.innerText || el.textContent || '').trim().slice(0, 60) || ('Track ' + (i + 1)), on: /selected/i.test((el.getAttribute('data-uia') || '') + ' ' + el.className) }; }); }
    return { via: 'menu', audio: rows(AUDIO), subs: rows(SUBS) };
  }
  // What plays, for the phone (every second while a video is open): title, episode, position, length, paused, speed,
  // and whether Netflix asks ""Are you still watching?"". Times from Netflix's player (milliseconds), as Firefox's
  // Picture-in-Picture does for Netflix.
  var STILL = '[data-uia=""interrupt-autoplay-continue""]';
  function watchId() { var m = /^\/watch\/(\d+)/.exec(location.pathname); return m ? m[1] : null; }
  function titles() {
    var md = navigator.mediaSession && navigator.mediaSession.metadata, t = '', e = '';
    var el = document.querySelector('[data-uia=""video-title""]');
    if (el) {
      var h = el.querySelector('h4'), spans = el.querySelectorAll('span');
      t = ((h && h.innerText) || '').trim();
      e = Array.prototype.map.call(spans, function (x) { return (x.innerText || '').trim(); }).filter(Boolean).join(' · ');
      if (!t) t = (el.innerText || '').trim().split('\n')[0];
    }
    if (!t && md) { t = md.title || ''; e = e || md.artist || ''; }
    return [t, e];
  }
  // A title's details open (Netflix's pop-up over the rows, or its title page): its name, the label of its Play
  // button (Resume, Play, Play S1:E9) and its number, so the phone can show a Resume button for it.
  var MODAL = '.previewModal--container.detail-modal, .detail-modal-container, [data-uia*=""detail-modal""], [role=dialog][aria-modal=true]';
  var PLAYSEL = '[data-uia=""play-button""], a[data-uia*=""play-button""], button[data-uia*=""play-button""], [data-uia=""resume-button""], [data-uia*=""play-resume""]';
  function modalEl() {
    var l = document.querySelectorAll(MODAL);
    for (var i = l.length - 1; i >= 0; i--) { var r = l[i].getBoundingClientRect(); if (r.width > innerWidth * 0.3 && r.height > 100) return l[i]; }
    return /^\/title\/\d+/.test(location.pathname) ? document : null;
  }
  function playButton(root) {
    root = root || modalEl() || document;
    var l = root.querySelectorAll(PLAYSEL);
    for (var i = 0; i < l.length; i++) { var r = l[i].getBoundingClientRect(); if (r.width > 4 && r.height > 4) return l[i]; }
    var bs = root.querySelectorAll('button, a[role=button], a[href*=""/watch/""]');
    for (var j = 0; j < bs.length; j++) {
      var t = ((bs[j].innerText || '') + ' ' + (bs[j].getAttribute('aria-label') || '')).trim();
      if (/^(resume|play|continue|ituloy|i-play|panoorin)\b/i.test(t)) return bs[j];
    }
    return null;
  }
  function modalId() {
    var m = /[?&]jbv=(\d+)/.exec(location.search) || /^\/title\/(\d+)/.exec(location.pathname);
    return m ? m[1] : null;
  }
  function modalInfo() {
    var m = modalEl(); if (!m) return null;
    var name = '';
    var logo = m.querySelector('.previewModal--player-titleTreatment-logo, [data-uia*=""titleTreatment""] img, img.title-treatment, .title-logo');
    if (logo) name = (logo.getAttribute('alt') || logo.getAttribute('title') || '').trim();
    if (!name) { var h = m.querySelector('.about-header strong, .previewModal--section-header strong, h1, h3.previewModal--player-titleTreatment-title'); if (h) name = (h.innerText || '').trim(); }
    var b = playButton(m);
    var label = b ? ((b.innerText || b.getAttribute('aria-label') || '').trim().split('\n')[0]) : '';
    if (!name && !b) return null;
    return { n: name.slice(0, 120), p: label.slice(0, 40), id: modalId() || '' };
  }
  window.__kwRate = 1;
  function report() {
    var o = { t: '', e: '', pos: 0, dur: 0, paused: true, rate: 1, still: false };
    if (watchId()) {
      var p = player(), v = document.querySelector('video'), tt = titles();
      o.t = tt[0]; o.e = tt[1];
      try { if (p) { o.pos = p.getCurrentTime() / 1000; o.dur = p.getDuration() / 1000; o.paused = p.isPaused(); } else if (v) { o.pos = v.currentTime; o.dur = v.duration || 0; o.paused = v.paused; } } catch (x) { }
      if (v) { if (window.__kwRate !== 1 && Math.abs(v.playbackRate - window.__kwRate) > 0.01) v.playbackRate = window.__kwRate; o.rate = v.playbackRate || 1; }
      o.still = !!document.querySelector(STILL);
    } else { var mi = modalInfo(); if (mi) o.modal = mi; }
    var k = JSON.stringify(o);
    if (k !== window.__kwLastNow) { window.__kwLastNow = k; post('now:' + k); }
  }
  setInterval(report, 1000);
  function episodes() {
    var id = watchId() || modalId(); if (!id) { post('episodes:{}'); return 'none'; }
    var urls = ['/nq/website/memberapi/release/metadata?movieid=' + id];
    try { var bid = window.netflix.reactContext.models.serverDefs.data.BUILD_IDENTIFIER; if (bid) urls.push('/api/shakti/' + bid + '/metadata?movieid=' + id); } catch (x) { }
    function next(i) {
      if (i >= urls.length) { if (window.__kwEpsWanted) { window.__kwEpsWanted(null); window.__kwEpsWanted = null; } else click('[data-uia=""control-episodes""]'); post('episodes:{""seasons"":[],""onTv"":true}'); return; }
      fetch(urls[i], { credentials: 'include' }).then(function (r) { return r.json(); }).then(function (j) {
        var v = j && j.video;
        if (!v || !v.seasons || !v.seasons.length) throw new Error('no seasons');
        var cur = String(v.currentEpisode || id);
        window.__kwEps = { show: v.title || '', seasons: v.seasons.map(function (s, si) {
          return { n: s.title || s.longName || ('Season ' + (s.seq || si + 1)), eps: (s.episodes || []).map(function (e, ei) {
            var eid = String(e.episodeId || e.id || '');
            return { id: eid, n: (e.seq || ei + 1) + '. ' + (e.title || 'Episode'), on: eid === cur || eid === id };
          }) };
        }) };
        if (window.__kwEpsWanted) { window.__kwEpsWanted(window.__kwEps); window.__kwEpsWanted = null; }
        post('episodes:' + JSON.stringify({ show: v.title || '', seasons: v.seasons.map(function (s, si) {
          return { n: s.title || s.longName || ('Season ' + (s.seq || si + 1)), eps: (s.episodes || []).map(function (e, ei) {
            var eid = String(e.episodeId || e.id || '');
            return { id: eid, n: (e.seq || ei + 1) + '. ' + (e.title || 'Episode'), on: eid === cur || eid === id };
          }) };
        }) }));
      }).catch(function () { next(i + 1); });
    }
    next(0);
    return 'asked';
  }
  window.__kwNetflix = {
    episodes: episodes,
    playEpisode: function (eid) { if (!/^\d{1,12}$/.test(String(eid))) return 'none'; location.href = 'https://www.netflix.com/watch/' + eid; return 'ok'; },
    seekTo: function (sec) { var p = player(); if (!p) return 'none'; p.seek(Math.max(0, sec) * 1000); return 'player'; },
    speed: function (r) { var v = document.querySelector('video'); if (!v) return 'none'; window.__kwRate = r; v.playbackRate = r; return 'video'; },
    // The title's Play / Resume (its details open on the TV), as if OK was pressed on it.
    play: function () {
      var b = playButton();
      if (b) { b.click(); return 'clicked'; }
      var id = modalId();
      if (id) { location.href = 'https://www.netflix.com/watch/' + id; return 'watch'; }
      return 'none';
    },
    cont: function () {
      if (click(STILL)) return 'clicked';
      var b = Array.prototype.filter.call(document.querySelectorAll('button'), function (x) { return /continue watching|continue playing/i.test(x.innerText || ''); })[0];
      if (b) { b.click(); return 'clicked'; }
      return 'none';
    },
    tracks: function () { return JSON.stringify(lists()); },
    setTrack: function (kind, i) {
      var p = player();
      if (p && p.getAudioTrackList && subList(p)) {
        try { if (kind === 'a') p.setAudioTrack(p.getAudioTrackList()[i]); else subSet(p)(subList(p)()[i]); return 'player'; } catch (e) { }
      }
      var sel = kind === 'a' ? AUDIO : SUBS;
      if (!document.querySelector(sel)) click(MENU);
      var items = document.querySelectorAll(sel);
      if (!items[i]) return 'none';
      items[i].click();
      return 'menu';
    },
    toggle: function () {
      var p = player();
      if (p) { if (p.isPaused()) p.play(); else p.pause(); return 'player'; }
      var v = document.querySelector('video'); if (v) { if (v.paused) v.play(); else v.pause(); return 'video'; }
      return 'none';
    },
    seek: function (s) { var p = player(); if (!p) return 'none'; p.seek(Math.max(0, p.getCurrentTime() + s * 1000)); return 'player'; },
    skip: function () { return click('[data-uia=""player-skip-intro""],[data-uia=""player-skip-recap""],[data-uia=""player-skip-preplay""]') ? 'clicked' : 'none'; },
    next: function () { return click('[data-uia=""next-episode-seamless-button""],[data-uia=""next-episode-seamless-button-draining""],[data-uia=""control-next""]') ? 'clicked' : 'none'; },
    search: function (q) { location.href = 'https://www.netflix.com/search?q=' + encodeURIComponent(q); return 'ok'; }
  };

  // ---- KwartoTV's own Netflix bar on the TV (as on the Netflix TV app): ▲ or ▼ while a video plays shows it, ◀ ▶ choose,
  // OK presses, Back (or 8 s without a key) hides it. Its buttons use Netflix's own player, so they work whether or not
  // Netflix's mouse controls are showing. Audio & Subtitles, Speed and Episodes open a list on the TV.
  (function () {
    var host = null, root = null, open = false, sel = 0, idle = 0, list = null, items = [], li = 0, col = 0;
    var CSS = ':host{all:initial}' +
      '.bar{position:fixed;left:4%;right:4%;bottom:14%;z-index:2147483646;font:600 22px Segoe UI,Arial,sans-serif;color:#fff;background:rgba(10,10,10,.86);border-radius:18px;padding:18px 22px;box-shadow:0 10px 40px rgba(0,0,0,.6)}' +
      '.t{font-size:26px;margin-bottom:4px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.e{font:400 18px Segoe UI,Arial;color:#bbb;margin-bottom:10px}' +
      '.pr{height:6px;background:#444;border-radius:3px;margin:8px 0 6px}.pf{height:6px;background:#e50914;border-radius:3px}.tm{font:400 16px Segoe UI,Arial;color:#ccc;display:flex;justify-content:space-between;margin-bottom:12px}' +
      '.bs{display:flex;flex-wrap:wrap;gap:10px}.b{padding:12px 18px;border-radius:12px;background:#2a2a2a}.b.on{background:#fff;color:#111}' +
      '.ls{display:flex;gap:30px;margin-top:14px;max-height:46vh;overflow:hidden}.c{min-width:260px}.ch{color:#999;font:600 16px Segoe UI,Arial;margin-bottom:6px;text-transform:uppercase}' +
      '.i{padding:8px 14px;border-radius:10px}.i.cur{background:#fff;color:#111}.i.set::before{content:""✓  ""}';
    function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }
    function clock(s) { s = Math.max(0, Math.round(s || 0)); var h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), x = s % 60; return (h ? h + ':' + (m < 10 ? '0' : '') : '') + m + ':' + (x < 10 ? '0' : '') + x; }
    function now() { try { return JSON.parse(window.__kwLastNow || '{}'); } catch (x) { return {}; } }
    function buttons() {
      var b = [['⏪ 10', function () { window.__kwNetflix.seek(-10); }], [playing() ? '❚❚ Pause' : '▶ Play', function () { window.__kwNetflix.toggle(); }], ['10 ⏩', function () { window.__kwNetflix.seek(10); }],
        ['Audio & Subtitles', showTracks], ['Episodes', showEpisodes], ['Speed', showSpeed], ['Next episode', function () { window.__kwNetflix.next(); }]];
      if (document.querySelector('[data-uia=""player-skip-intro""],[data-uia=""player-skip-recap""]')) b.unshift(['Skip intro', function () { window.__kwNetflix.skip(); }]);
      if (document.querySelector(STILL)) b.unshift(['Continue watching', function () { window.__kwNetflix.cont(); }]);
      return b;
    }
    function playing() { var n = now(); return !n.paused; }
    function draw() {
      if (!root) return;
      while (root.lastChild && root.lastChild.tagName !== 'STYLE') root.removeChild(root.lastChild);
      if (!open) return;
      var n = now(), bar = el('div', 'bar');
      bar.appendChild(el('div', 't', n.t || 'Netflix'));
      if (n.e) bar.appendChild(el('div', 'e', n.e));
      var pr = el('div', 'pr'), pf = el('div', 'pf'); pf.style.width = (n.dur ? Math.min(100, 100 * n.pos / n.dur) : 0) + '%'; pr.appendChild(pf); bar.appendChild(pr);
      var tm = el('div', 'tm'); tm.appendChild(el('span', '', clock(n.pos))); tm.appendChild(el('span', '', (n.rate && n.rate !== 1 ? n.rate + '×   ' : '') + '-' + clock((n.dur || 0) - (n.pos || 0)))); bar.appendChild(tm);
      var bs = el('div', 'bs'), b = buttons(); sel = Math.max(0, Math.min(sel, b.length - 1));
      b.forEach(function (x, i) { bs.appendChild(el('div', 'b' + (i === sel && !list ? ' on' : ''), x[0])); });
      bar.appendChild(bs);
      if (list) {
        var ls = el('div', 'ls');
        list.cols.forEach(function (c, ci) {
          var cd = el('div', 'c'); cd.appendChild(el('div', 'ch', c.name));
          var start = ci === col ? Math.max(0, Math.min(li - 4, c.items.length - 9)) : 0;
          c.items.slice(start, start + 9).forEach(function (it, k) { cd.appendChild(el('div', 'i' + (ci === col && start + k === li ? ' cur' : '') + (it.on ? ' set' : ''), it.n)); });
          ls.appendChild(cd);
        });
        bar.appendChild(ls);
      }
      root.appendChild(bar);
      try { window.chrome.webview.postMessage('tvfocus:NFBAR ' + (list ? (list.cols[col] && list.cols[col].items[li] ? list.cols[col].items[li].n : '') : (b[sel] ? b[sel][0] : ''))); } catch (x) { }
    }
    function make() {
      if (host && document.contains(host)) return;
      host = document.createElement('div'); host.id = 'kw-nfbar'; (document.body || document.documentElement).appendChild(host);
      root = host.attachShadow({ mode: 'open' });
      var st = document.createElement('style'); st.textContent = CSS; root.appendChild(st);
    }
    function poke() { clearTimeout(idle); idle = setTimeout(close, 8000); }
    function show(what) { make(); open = true; list = null; sel = what === 'down' ? Math.max(0, buttons().findIndex(function (x) { return x[0] === 'Audio & Subtitles'; })) : 1; poke(); draw(); }
    function close() { open = false; list = null; clearTimeout(idle); draw(); }
    function showList(l) { list = l; col = 0; li = 0; l.cols.forEach(function (c, ci) { c.items.forEach(function (it, k) { if (it.on && ci === 0) li = k; }); }); draw(); }
    function showTracks() {
      var t = {}; try { t = JSON.parse(window.__kwNetflix.tracks()); } catch (x) { }
      var mk = function (arr, kind) { return (arr || []).map(function (x) { return { n: x.n, on: x.on, go: function () { window.__kwNetflix.setTrack(kind, x.i); setTimeout(showTracks, 500); } }; }); };
      showList({ cols: [{ name: 'Audio', items: mk(t.audio, 'a') }, { name: 'Subtitles', items: mk(t.subs, 's') }].filter(function (c) { return c.items.length; }) });
      if (!list.cols.length) { list = null; draw(); }
    }
    function showSpeed() {
      var r = now().rate || 1;
      showList({ cols: [{ name: 'Speed', items: [0.5, 0.75, 1, 1.25, 1.5].map(function (v) { return { n: v === 1 ? '1× (normal)' : v + '×', on: Math.abs(v - r) < 0.01, go: function () { window.__kwNetflix.speed(v); setTimeout(close, 300); } }; }) }] });
    }
    function showEpisodes() {
      var fill = function (e) {
        if (!e || !e.seasons || !e.seasons.length) { list = null; draw(); return; }
        showList({ cols: e.seasons.map(function (s) { return { name: s.n, items: s.eps.map(function (x) { return { n: x.n, on: x.on, go: function () { window.__kwNetflix.playEpisode(x.id); close(); } }; }) }; }) });
        list.cols.forEach(function (c, ci) { c.items.forEach(function (it, k) { if (it.on) { col = ci; li = k; } }); }); draw();
      };
      if (window.__kwEps) fill(window.__kwEps); else { window.__kwEpsWanted = fill; window.__kwNetflix.episodes(); }
    }
    function key(k) {
      if (!open) return false;
      poke();
      if (list) {
        var c = list.cols[col];
        if (k === 'ArrowDown') li = Math.min(li + 1, c.items.length - 1);
        else if (k === 'ArrowUp') li = Math.max(li - 1, 0);
        else if (k === 'ArrowRight' && col < list.cols.length - 1) { col++; li = Math.min(li, list.cols[col].items.length - 1); }
        else if (k === 'ArrowLeft' && col > 0) { col--; li = Math.min(li, list.cols[col].items.length - 1); }
        else if (k === 'Enter') { var it = c.items[li]; if (it) it.go(); return true; }
        else if (k === 'Escape' || k === 'BrowserBack' || k === 'Backspace') { list = null; }
        draw(); return true;
      }
      var b = buttons();
      if (k === 'ArrowRight') sel = Math.min(sel + 1, b.length - 1);
      else if (k === 'ArrowLeft') sel = Math.max(sel - 1, 0);
      else if (k === 'Enter') { if (b[sel]) b[sel][1](); draw(); return true; }
      else if (k === 'Escape' || k === 'BrowserBack' || k === 'Backspace' || k === 'ArrowUp') { close(); return true; }
      draw(); return true;
    }
    setInterval(function () { if (open) draw(); if (open && !watchId()) close(); }, 1000);
    window.__kwNfBar = { show: show, close: close, key: key, isOpen: function () { return open; } };
  })();
})();";
}
