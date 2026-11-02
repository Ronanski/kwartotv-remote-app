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
  function name(x, i) { return (x && (x.displayName || x.language || x.bcp47)) || ('Track ' + (i + 1)); }
  function lists() {
    var p = player();
    if (p && p.getAudioTrackList && p.getTextTrackList) {
      try {
        var a = p.getAudioTrackList() || [], t = p.getTextTrackList() || [], ca = p.getAudioTrack && p.getAudioTrack(), ct = p.getTextTrack && p.getTextTrack();
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
  window.__kwRate = 1;
  function report() {
    var o = { t: '', e: '', pos: 0, dur: 0, paused: true, rate: 1, still: false };
    if (watchId()) {
      var p = player(), v = document.querySelector('video'), tt = titles();
      o.t = tt[0]; o.e = tt[1];
      try { if (p) { o.pos = p.getCurrentTime() / 1000; o.dur = p.getDuration() / 1000; o.paused = p.isPaused(); } else if (v) { o.pos = v.currentTime; o.dur = v.duration || 0; o.paused = v.paused; } } catch (x) { }
      if (v) { if (window.__kwRate !== 1 && Math.abs(v.playbackRate - window.__kwRate) > 0.01) v.playbackRate = window.__kwRate; o.rate = v.playbackRate || 1; }
      o.still = !!document.querySelector(STILL);
    }
    var k = JSON.stringify(o);
    if (k !== window.__kwLastNow) { window.__kwLastNow = k; post('now:' + k); }
  }
  setInterval(report, 1000);
  function episodes() {
    var id = watchId(); if (!id) { post('episodes:{}'); return 'none'; }
    var urls = ['/nq/website/memberapi/release/metadata?movieid=' + id];
    try { var bid = window.netflix.reactContext.models.serverDefs.data.BUILD_IDENTIFIER; if (bid) urls.push('/api/shakti/' + bid + '/metadata?movieid=' + id); } catch (x) { }
    function next(i) {
      if (i >= urls.length) { click('[data-uia=""control-episodes""]'); post('episodes:{""seasons"":[],""onTv"":true}'); return; }
      fetch(urls[i], { credentials: 'include' }).then(function (r) { return r.json(); }).then(function (j) {
        var v = j && j.video;
        if (!v || !v.seasons || !v.seasons.length) throw new Error('no seasons');
        var cur = String(v.currentEpisode || id);
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
    cont: function () {
      if (click(STILL)) return 'clicked';
      var b = Array.prototype.filter.call(document.querySelectorAll('button'), function (x) { return /continue watching|continue playing/i.test(x.innerText || ''); })[0];
      if (b) { b.click(); return 'clicked'; }
      return 'none';
    },
    tracks: function () { return JSON.stringify(lists()); },
    setTrack: function (kind, i) {
      var p = player();
      if (p && p.getAudioTrackList && p.getTextTrackList) {
        try { if (kind === 'a') p.setAudioTrack(p.getAudioTrackList()[i]); else p.setTextTrack(p.getTextTrackList()[i]); return 'player'; } catch (e) { }
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
})();";
}
