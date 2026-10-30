namespace KwartoTV.App.Services;

/// <summary>
/// Makes a website made for a mouse (Netflix) work like a TV app: the arrow keys move a big highlight
/// between titles and buttons, OK opens, Back goes back, the rows scroll by themselves,
/// and the mouse pointer and scroll bars are hidden.
/// </summary>
public static class TvWebScript
{
    public const string Script = @"
(function () {
  if (window.__kwTv) return; window.__kwTv = true;
  var css =
    'html{scrollbar-width:none!important} ::-webkit-scrollbar{display:none!important}' +
    'html.kw-nocursor, html.kw-nocursor *{cursor:none!important}' +
    '.kw-focus{outline:4px solid #fff!important;outline-offset:3px!important;border-radius:6px!important;' +
      'box-shadow:0 0 0 7px rgba(229,9,20,.85),0 12px 40px rgba(0,0,0,.7)!important;transform:scale(1.06);' +
      'transition:transform .15s ease, box-shadow .15s ease!important;z-index:10!important;position:relative}' +
    '.kw-tv footer,.kw-tv .member-footer{display:none!important}';
  function addCss() {
    if (document.getElementById('kw-tv-css')) return;
    var s = document.createElement('style'); s.id = 'kw-tv-css'; s.textContent = css;
    (document.head || document.documentElement).appendChild(s);
  }
  function mark() { document.documentElement.classList.add('kw-tv'); addCss(); }
  mark(); document.addEventListener('DOMContentLoaded', mark);

  var SEL = 'a[href],button,input,select,textarea,[role=button],[role=link],[role=tab],[role=menuitem],[role=option],[tabindex]:not([tabindex=""-1""])';
  var cur = null;

  function visible(el) {
    if (!el || el.disabled) return false;
    var r = el.getBoundingClientRect();
    if (r.width < 8 || r.height < 8) return false;
    if (r.bottom < -window.innerHeight || r.top > window.innerHeight * 2) return false;
    if (r.right < 0 || r.left > window.innerWidth) return false;
    var st = getComputedStyle(el);
    return st.visibility !== 'hidden' && st.display !== 'none' && parseFloat(st.opacity || '1') > 0.05;
  }
  // When a modal is open (title details, menus) only move inside it.
  function scope() {
    var m = document.querySelectorAll('[role=dialog],[aria-modal=true],.previewModal--container.detail-modal');
    for (var i = m.length - 1; i >= 0; i--) if (visible(m[i])) return m[i];
    return document;
  }
  function candidates() {
    var all = scope().querySelectorAll(SEL), out = [];
    for (var i = 0; i < all.length; i++) {
      var el = all[i];
      // a row's own scroll arrows are pressed for you at the end of the row
      if (visible(el) && !el.closest('.handle,.handleNext,.handlePrev')) out.push(el);
    }
    return out;
  }
  function center(r) { return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; }
  function pick(dir) {
    var list = candidates();
    if (!cur || !document.contains(cur) || !visible(cur)) {
      // first press: the first thing on screen, top-left first
      var best = null, bs = 1e9;
      list.forEach(function (el) { var r = el.getBoundingClientRect(); if (r.top < 0 || r.top > window.innerHeight) return; var s = r.top * 3 + r.left; if (s < bs) { bs = s; best = el; } });
      return best || list[0] || null;
    }
    var a = cur.getBoundingClientRect(), ca = center(a), best = null, bs = 1e12;
    list.forEach(function (el) {
      if (el === cur || el.contains(cur) || cur.contains(el)) return;
      var r = el.getBoundingClientRect(), c = center(r), dx = c.x - ca.x, dy = c.y - ca.y, main, side;
      if (dir === 'right') { if (r.left < a.right - 4 && c.x <= ca.x + 4) return; main = Math.max(0, r.left - a.right); side = Math.abs(dy); if (r.bottom <= a.top || r.top >= a.bottom) return; }
      else if (dir === 'left') { if (r.right > a.left + 4 && c.x >= ca.x - 4) return; main = Math.max(0, a.left - r.right); side = Math.abs(dy); if (r.bottom <= a.top || r.top >= a.bottom) return; }
      else if (dir === 'down') { if (r.top < a.bottom - 4 && c.y <= ca.y + 4) return; main = Math.max(0, r.top - a.bottom); side = Math.abs(dx); }
      else { if (r.bottom > a.top + 4 && c.y >= ca.y - 4) return; main = Math.max(0, a.top - r.bottom); side = Math.abs(dx); }
      var s = main + side * (dir === 'up' || dir === 'down' ? 0.5 : 3);
      if (s < bs) { bs = s; best = el; }
    });
    return best;
  }
  function focus(el) {
    if (!el) return false;
    if (cur) cur.classList.remove('kw-focus');
    cur = el; el.classList.add('kw-focus');
    try { el.focus({ preventScroll: true }); } catch (x) { }
    try { el.scrollIntoView({ block: 'center', inline: 'nearest', behavior: 'smooth' }); } catch (x) { el.scrollIntoView(); }
    return true;
  }
  // At the end of a row, press the row's own next/previous arrow, then highlight the new title.
  function pageRow(dir) {
    if (!cur) return false;
    var row = cur.closest('.lolomoRow,.rowContainer,[data-list-context],.slider');
    if (!row) return false;
    var h = row.querySelector(dir === 'right' ? '.handleNext,.handle.next,[aria-label*=""next"" i],[aria-label*=""more titles"" i]' : '.handlePrev,.handle.prev,[aria-label*=""previous"" i]');
    if (!h) return false;
    h.click();
    var was = cur;
    setTimeout(function () {
      if (cur !== was) return; // the highlight was moved meanwhile
      // the first new title on the right (or the last one on the left) of the same row
      var best = null, bx = dir === 'right' ? 1e9 : -1e9;
      candidates().forEach(function (el) {
        if (!row.contains(el)) return; var r = el.getBoundingClientRect();
        if (r.left < 0 || r.right > window.innerWidth) return;
        if (dir === 'right' ? r.left < bx : r.left > bx) { bx = r.left; best = el; }
      });
      if (best) focus(best);
    }, 700);
    return true;
  }
  function typing(el) {
    return el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable);
  }
  function watching() { return /\/watch\//.test(location.pathname); }
  function back() {
    var m = scope();
    if (m !== document) {
      var close = m.querySelector('[aria-label*=""close"" i],.previewModal-close,[data-uia*=""close""]');
      if (close) { close.click(); return; }
    }
    if (watching() || (history.length > 1 && !/^\/(browse)?\/?$/.test(location.pathname))) { history.back(); return; }
    window.chrome.webview.postMessage('home');
  }
  // The pointer hides while the remote's keys are used, and comes back when the mouse (or the phone's Mouse pad) moves.
  document.addEventListener('mousemove', function () { document.documentElement.classList.remove('kw-nocursor'); }, true);
  window.addEventListener('keydown', function (e) {
    if (e.key === 'F1') return;
    document.documentElement.classList.add('kw-nocursor');
    // While a video plays, Netflix's own player keys work (space, ←/→ skip, ↑/↓ volume); only Back is ours.
    if (watching()) {
      if (e.key === 'Escape' || e.key === 'BrowserBack') { e.preventDefault(); e.stopPropagation(); back(); }
      else if (e.key === 'Enter') { var v = document.querySelector('video'); if (v) { e.preventDefault(); e.stopPropagation(); if (v.paused) v.play(); else v.pause(); } }
      return;
    }
    var t = document.activeElement, dir = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right' }[e.key];
    if (dir) {
      if (typing(t) && (dir === 'left' || dir === 'right')) return;
      e.preventDefault(); e.stopPropagation();
      if (!focus(pick(dir)) && (dir === 'left' || dir === 'right')) pageRow(dir);
      else if (!cur && dir === 'down') window.scrollBy({ top: window.innerHeight * 0.6, behavior: 'smooth' });
    } else if (e.key === 'Enter') {
      if (typing(t)) return;
      var el = cur && document.contains(cur) ? cur : null;
      if (el) { e.preventDefault(); e.stopPropagation(); el.click(); }
    } else if (e.key === 'Escape' || e.key === 'BrowserBack' || (e.key === 'Backspace' && !typing(t))) {
      e.preventDefault(); e.stopPropagation(); back();
    }
  }, true);
  // a real mouse click moves the highlight too
  document.addEventListener('pointerdown', function (e) { var el = e.target.closest && e.target.closest(SEL); if (el) { if (cur) cur.classList.remove('kw-focus'); cur = el; el.classList.add('kw-focus'); } }, true);
})();";
}
