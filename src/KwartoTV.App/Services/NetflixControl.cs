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
  window.__kwNetflix = {
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
