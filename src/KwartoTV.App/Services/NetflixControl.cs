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
  window.__kwNetflix = {
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
