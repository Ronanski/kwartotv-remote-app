namespace KwartoTV.App.Services;

/// <summary>
/// Makes a website made for a mouse (Netflix, Bilibili, any website tile) work like a TV app: the arrow keys move a big highlight
/// between titles and buttons, OK opens, Back goes back, the rows scroll by themselves,
/// and the mouse pointer and scroll bars are hidden.
/// The highlight is a separate frame drawn over the page (the page's own styles and layout are not touched),
/// and styles are added as a constructed style sheet, which the site's security policy does not block.
/// </summary>
public static class TvWebScript
{
    public const string Script = @"
(function () {
  if (window.__kwTv || window.top !== window) return; window.__kwTv = true;
  var css =
    'html{scrollbar-width:none!important} ::-webkit-scrollbar{display:none!important}' +
    'html.kw-nocursor, html.kw-nocursor *{cursor:none!important}' +
    'footer,.member-footer,.site-footer-wrapper{display:none!important}';
  var sheet = null;
  function addCss() {
    try {
      if (!sheet) { sheet = new CSSStyleSheet(); sheet.replaceSync(css); }
      if (document.adoptedStyleSheets.indexOf(sheet) < 0) document.adoptedStyleSheets = document.adoptedStyleSheets.concat([sheet]);
    } catch (x) {
      if (document.getElementById('kw-tv-css')) return;
      var s = document.createElement('style'); s.id = 'kw-tv-css'; s.textContent = css; (document.head || document.documentElement).appendChild(s);
    }
  }

  // The highlight frame
  var ring = null;
  function makeRing() {
    if (ring && document.documentElement.contains(ring)) return ring;
    ring = document.createElement('div');
    var st = ring.style;
    st.position = 'fixed'; st.pointerEvents = 'none'; st.zIndex = '2147483647'; st.display = 'none';
    st.border = '4px solid #fff'; st.borderRadius = '8px'; st.boxSizing = 'border-box';
    st.boxShadow = '0 0 0 4px rgba(229,9,20,.9), 0 0 28px 6px rgba(0,0,0,.75)';
    st.transition = 'left .14s ease-out, top .14s ease-out, width .14s ease-out, height .14s ease-out';
    document.documentElement.appendChild(ring);
    return ring;
  }

  var SEL = 'a[href],button,input,select,textarea,[role=button],[role=link],[role=tab],[role=menuitem],[role=option],[role=checkbox],video,[tabindex]:not([tabindex=""-1""])';
  // What to highlight first on each screen (the main Play button, the first profile, the first title).
  var PREFER = ['[data-uia=""play-button""]', '[data-uia*=""play-button""]', 'button.color-primary', '.profile-link', '[data-uia=""action-select-profile+primary""]',
    '.title-card-container a', '.slider-refocus', '[data-uia=""login-submit-button""]'];
  var cur = null, lastRect = null;

  function rectOf(el) {
    var r = el.getBoundingClientRect();
    // a link that wraps a picture can measure 0 x 0; use its first child with a size
    if ((r.width < 2 || r.height < 2) && el.firstElementChild) { var c = el.firstElementChild.getBoundingClientRect(); if (c.width > 2 && c.height > 2) return c; }
    return r;
  }
  function visible(el) {
    if (!el || el.disabled || el === ring) return false;
    var r = rectOf(el);
    if (r.width < 8 || r.height < 8) return false;
    if (r.bottom < -window.innerHeight || r.top > window.innerHeight * 2) return false;
    if (r.right < 0 || r.left > window.innerWidth) return false;
    var st = getComputedStyle(el);
    return st.visibility !== 'hidden' && st.display !== 'none' && parseFloat(st.opacity || '1') > 0.05;
  }
  // When a pop-up is open (title details, menus) only move inside it.
  function scope() {
    var m = document.querySelectorAll('[role=dialog],[aria-modal=true],.previewModal--container.detail-modal,.detail-modal-container');
    for (var i = m.length - 1; i >= 0; i--) if (visible(m[i]) && m[i].getBoundingClientRect().width > window.innerWidth * 0.3) return m[i];
    // A pop-up without those marks (a notice, a sign-in box): whatever covers the middle of the screen in a fixed layer.
    var el = document.elementFromPoint(window.innerWidth / 2, window.innerHeight / 2);
    for (; el && el !== document.body && el !== document.documentElement; el = el.parentElement) {
      var st = getComputedStyle(el);
      if (st.position === 'fixed' && el.getBoundingClientRect().width > window.innerWidth * 0.2 && el.querySelector(SEL)) {
        // the page's own fixed frame (a full-page app shell) is not a pop-up
        if (el.querySelectorAll(SEL).length > 60) break;
        return el;
      }
    }
    return document;
  }
  function candidates() {
    var all = scope().querySelectorAll(SEL), out = [];
    for (var i = 0; i < all.length; i++) {
      var el = all[i];
      // a row's own scroll arrows are pressed for you at the end of the row
      if (visible(el) && !el.closest('.handle,.handleNext,.handlePrev')) out.push(el);
    }
    // keep the outermost of nested candidates (a card link and the picture link inside it)
    return out.filter(function (el) { return !out.some(function (o) { return o !== el && o.contains(el); }); });
  }
  function onScreen(el) { var r = rectOf(el); return r.top >= 0 && r.top < window.innerHeight && r.left < window.innerWidth; }
  function preferred() {
    var s = scope();
    for (var i = 0; i < PREFER.length; i++) {
      var list = s.querySelectorAll(PREFER[i]);
      for (var j = 0; j < list.length; j++) if (visible(list[j]) && onScreen(list[j])) return list[j];
    }
    // A page built around a video (a Bilibili episode): start on the video, so OK plays it instead of a header link.
    var vids = s.querySelectorAll('video');
    for (var k = 0; k < vids.length; k++) if (visible(vids[k]) && onScreen(vids[k]) && rectOf(vids[k]).width > window.innerWidth * 0.3) return vids[k];
    var box = s.querySelector('input');
    if (box && visible(box) && onScreen(box)) return box;
    var c = candidates().filter(onScreen);
    c.sort(function (a, b) { var ra = rectOf(a), rb = rectOf(b); return (ra.top * 3 + ra.left) - (rb.top * 3 + rb.left); });
    return c[0] || null;
  }
  function center(r) { return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; }
  function pick(dir) {
    var list = candidates();
    var a = cur && document.contains(cur) && visible(cur) ? rectOf(cur) : lastRect;
    if (!a) return preferred();
    var ca = center(a), best = null, bs = 1e12;
    list.forEach(function (el) {
      if (el === cur || (cur && (el.contains(cur) || cur.contains(el)))) return;
      var r = rectOf(el), c = center(r), dx = c.x - ca.x, dy = c.y - ca.y, main, side;
      if (dir === 'right') { if (r.left < a.right - 4 && c.x <= ca.x + 4) return; if (r.bottom <= a.top || r.top >= a.bottom) return; main = Math.max(0, r.left - a.right); side = Math.abs(dy); }
      else if (dir === 'left') { if (r.right > a.left + 4 && c.x >= ca.x - 4) return; if (r.bottom <= a.top || r.top >= a.bottom) return; main = Math.max(0, a.left - r.right); side = Math.abs(dy); }
      else if (dir === 'down') { if (r.top < a.bottom - 4 && c.y <= ca.y + 4) return; main = Math.max(0, r.top - a.bottom); side = Math.abs(dx); }
      else { if (r.bottom > a.top + 4 && c.y >= ca.y - 4) return; main = Math.max(0, a.top - r.bottom); side = Math.abs(dx); }
      var s = main + side * (dir === 'up' || dir === 'down' ? 0.5 : 3);
      if (s < bs) { bs = s; best = el; }
    });
    return best;
  }
  function focus(el) {
    if (!el) return false;
    cur = el; lastRect = rectOf(el);
    // a text box only takes the keyboard when OK is pressed on it (then the on-screen keyboard opens)
    if (typing(el)) { if (typing(document.activeElement) && document.activeElement !== el) document.activeElement.blur(); }
    else { try { el.focus({ preventScroll: true }); } catch (x) { } }
    try { window.chrome.webview.postMessage('tvfocus:' + el.tagName + ' ' + ((el.getAttribute('aria-label') || el.textContent || el.getAttribute('href') || '').trim().slice(0, 40))); } catch (x) { }
    var r = rectOf(el);
    if (r.top < 80 || r.bottom > window.innerHeight - 40) { try { el.scrollIntoView({ block: 'center', inline: 'nearest', behavior: 'smooth' }); } catch (x) { el.scrollIntoView(); } }
    draw();
    return true;
  }
  function draw() {
    var g = makeRing();
    if (!cur || !document.contains(cur) || !visible(cur) || watching()) { g.style.display = 'none'; return; }
    var r = rectOf(cur); lastRect = r;
    g.style.display = 'block';
    g.style.left = (r.left - 6) + 'px'; g.style.top = (r.top - 6) + 'px';
    g.style.width = (r.width + 12) + 'px'; g.style.height = (r.height + 12) + 'px';
  }
  (function loop() { try { draw(); } catch (x) { } requestAnimationFrame(loop); })();

  // At the end of a row, press the row's own next/previous arrow, then highlight the first new title.
  function pageRow(dir) {
    if (!cur) return false;
    var row = cur.closest('.lolomoRow,.rowContainer,[data-list-context],.slider,.scroll-wrap');
    if (!row) return false;
    var h = row.querySelector(dir === 'right' ? '.handleNext,.handle.next,.scroll-wrap__btn.next,[aria-label*=""next"" i],[aria-label*=""more titles"" i]' : '.handlePrev,.handle.prev,.scroll-wrap__btn.prev,[aria-label*=""previous"" i]');
    if (!h) return false;
    h.click();
    var was = cur;
    setTimeout(function () {
      if (cur !== was) return; // the highlight was moved meanwhile
      var best = null, bx = dir === 'right' ? 1e9 : -1e9;
      candidates().forEach(function (el) {
        if (!row.contains(el)) return; var r = rectOf(el);
        if (r.left < 0 || r.right > window.innerWidth) return;
        if (dir === 'right' ? r.left < bx : r.left > bx) { bx = r.left; best = el; }
      });
      if (best) focus(best);
    }, 750);
    return true;
  }
  function typing(el) { return el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable); }
  function netflixWatch() { return /netflix\./.test(location.host) && /\/watch\//.test(location.pathname); }
  function fullVideo() { var f = document.fullscreenElement; return !!f && (f.tagName === 'VIDEO' || !!f.querySelector('video')); }
  function watching() { return netflixWatch() || fullVideo(); }
  function theVideo() { var f = document.fullscreenElement, v = (f && (f.tagName === 'VIDEO' ? f : f.querySelector('video'))) || document.querySelector('video'); return v; }
  // A site's home page (/, /browse, /en, /en-us/ ...): Back from here returns to KwartoTV.
  function atRoot() { return /^\/(browse|[a-z]{2}(-[a-z]{2})?)?\/?$/i.test(location.pathname); }
  // OK on a video: play it fullscreen (the site's own player box, so its subtitles stay).
  function playFull(v) {
    var box = v.closest('[class*=player i],[id*=player i]') || v;
    if (box.getBoundingClientRect().width < v.getBoundingClientRect().width * 0.9) box = v;
    try { var r = box.requestFullscreen(); if (r && r.catch) r.catch(function () { }); } catch (x) { }
    if (v.paused) v.play().catch(function () { });
  }
  function back() {
    var m = scope();
    if (m !== document) {
      var close = m.querySelector('[aria-label*=""close"" i],.previewModal-close,[data-uia*=""close""]');
      if (!close) close = Array.prototype.find.call(m.querySelectorAll('[class*=""close"" i],' + SEL), function (b) { return /^\s*[x×✕✖]\s*$/i.test(b.textContent || '') || /close/i.test(String(b.className)); });
      if (close) { close.click(); cur = null; return; }
    }
    if (fullVideo()) { document.exitFullscreen().catch(function () { }); return; }
    if (netflixWatch() || (history.length > 1 && !atRoot())) { history.back(); cur = null; return; }
    window.chrome.webview.postMessage('home');
  }

  // Highlight something as soon as a screen or a pop-up appears, so the remote works right away.
  var lastScope = null, lastPath = '', stash = null;
  setInterval(function () {
    try {
      addCss();
      if (watching() || typing(document.activeElement)) return;
      var s = scope();
      var lost = !cur || !document.contains(cur) || !visible(cur) || (s !== document && !s.contains(cur));
      if (lost || s !== lastScope || location.pathname !== lastPath) {
        if (s !== lastScope && s !== document) stash = cur; // remember the title the pop-up was opened from
        var back2 = s === document && s !== lastScope && stash && document.contains(stash) && visible(stash) ? stash : null;
        if (back2) { focus(back2); stash = null; }
        else if (lost || s !== lastScope) { var p = preferred(); if (p) focus(p); }
        lastScope = s; lastPath = location.pathname;
      }
    } catch (x) { }
  }, 400);

  // The pointer hides while the remote's keys are used, and comes back when the mouse (or the phone's Mouse pad) moves.
  document.addEventListener('mousemove', function () { document.documentElement.classList.remove('kw-nocursor'); }, true);
  window.addEventListener('keydown', function (e) {
    if (e.key === 'F1') return;
    document.documentElement.classList.add('kw-nocursor');
    // While a video plays: OK pauses/plays, ◀ ▶ skip 10 s, Back leaves. On Netflix its own player keys also work.
    if (watching()) {
      var v = theVideo();
      if (e.key === 'Escape' || e.key === 'BrowserBack' || e.key === 'Backspace') { e.preventDefault(); e.stopPropagation(); back(); }
      else if (e.key === 'Enter' || e.key === ' ') { if (v) { e.preventDefault(); e.stopPropagation(); if (v.paused) v.play(); else v.pause(); } }
      else if (!netflixWatch() && v && (e.key === 'ArrowLeft' || e.key === 'ArrowRight')) {
        e.preventDefault(); e.stopImmediatePropagation();
        try { v.currentTime = Math.max(0, v.currentTime + (e.key === 'ArrowRight' ? 10 : -10)); } catch (x) { }
      }
      return;
    }
    var t = document.activeElement, dir = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right' }[e.key];
    if (dir) {
      if (typing(t) && (dir === 'left' || dir === 'right')) return;
      e.preventDefault(); e.stopImmediatePropagation();
      var n = pick(dir);
      if (n) focus(n);
      else if (dir === 'left' || dir === 'right') pageRow(dir);
      else window.scrollBy({ top: (dir === 'down' ? 1 : -1) * window.innerHeight * 0.5, behavior: 'smooth' });
    } else if (e.key === 'Enter') {
      if (typing(t)) return;
      var el = cur && document.contains(cur) ? cur : null;
      if (el) { e.preventDefault(); e.stopImmediatePropagation(); if (el.tagName === 'VIDEO') playFull(el); else { if (typing(el)) el.focus(); el.click(); } }
    } else if (e.key === 'Escape' || e.key === 'BrowserBack' || (e.key === 'Backspace' && !typing(t))) {
      e.preventDefault(); e.stopImmediatePropagation(); back();
    }
  }, true);
  // a real mouse click moves the highlight too
  document.addEventListener('pointerdown', function (e) { var el = e.target.closest && e.target.closest(SEL); if (el) { cur = el; draw(); } }, true);
  addCss();
})();";
}
