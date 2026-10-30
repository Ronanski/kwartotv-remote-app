namespace KwartoTV.App.Services;

/// <summary>
/// Makes a website made for a mouse (Netflix, Bilibili, any website tile) work like a TV app: the arrow keys move a big highlight
/// between titles and buttons, OK opens, holding OK (or Back) goes back, the rows scroll by themselves,
/// and the mouse pointer and scroll bars are hidden. Only five keys are needed: ▲ ▼ ◀ ▶ and OK.
/// ◀ at the left edge opens a KwartoTV side menu (Search, Home, Movies ... for Netflix and Bilibili; Back and KwartoTV home everywhere),
/// which replaces the site's own top bar (hidden on Netflix and Bilibili).
/// The highlight is a separate frame drawn over the page (the page's own styles and layout are not touched),
/// and styles are added as a constructed style sheet, which the site's security policy does not block.
/// </summary>
public static class TvWebScript
{
    public const string Script = @"
(function () {
  if (window.__kwTv || window.top !== window) return; window.__kwTv = true;
  var NF = /(^|\.)netflix\.com$/i.test(location.host), BILI = /(^|\.)bilibili\.tv$/i.test(location.host);
  var css =
    'html{scrollbar-width:none!important} ::-webkit-scrollbar{display:none!important}' +
    'html.kw-nocursor, html.kw-nocursor *{cursor:none!important}' +
    'footer,.member-footer,.site-footer-wrapper{display:none!important}' +
    // the side menu replaces the site's top bar (shown again on search pages, and on Netflix's profile and sign-in pages)
    'html.kw-nf.kw-hidehead:not(.kw-search) .pinning-header, html.kw-nf.kw-hidehead:not(.kw-search) .main-header{display:none!important}' +
    'html.kw-bili.kw-hidehead:not(.kw-search) .bstar-header, html.kw-bili.kw-hidehead:not(.kw-search) header:not(main header):not(section header):not(article header):not([class*=""player"" i]){display:none!important}' +
    'html.kw-bili .bstar-footer{display:none!important}';
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
    // thick white frame, red edge and a soft glow, so the highlight reads from the couch
    st.border = '6px solid #fff'; st.borderRadius = '10px'; st.boxSizing = 'border-box';
    st.boxShadow = '0 0 0 3px rgba(229,9,20,.95), 0 0 36px 10px rgba(255,255,255,.45), 0 0 60px 16px rgba(0,0,0,.6)';
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
    if (menuOpen || !cur || !document.contains(cur) || !visible(cur) || watching()) { g.style.display = 'none'; return; }
    var r = rectOf(cur); lastRect = r;
    g.style.display = 'block';
    g.style.left = (r.left - 8) + 'px'; g.style.top = (r.top - 8) + 'px';
    g.style.width = (r.width + 16) + 'px'; g.style.height = (r.height + 16) + 'px';
  }
  (function loop() { try { draw(); } catch (x) { } requestAnimationFrame(loop); })();

  // At the end of a row, press the row's own next/previous arrow, then highlight the first new title.
  function pageRow(dir) {
    if (!cur) return false;
    var row = cur.closest('.lolomoRow,.rowContainer,[data-list-context],.slider,.scroll-wrap');
    if (!row) return false;
    var h = row.querySelector(dir === 'right' ? '.handleNext,.handle.next,.scroll-wrap__btn.next,[aria-label*=""next"" i],[aria-label*=""more titles"" i]' : '.handlePrev,.handle.prev,.scroll-wrap__btn.prev,[aria-label*=""previous"" i]');
    if (!h || h.disabled || h.getAttribute('aria-disabled') === 'true' || /disabled/i.test(String(h.className))) return false;
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

  // The KwartoTV side menu (◀ at the left edge opens it) and the first-load hint. They live in their own shadow root,
  // so the site's styles do not reach them, styled by a constructed style sheet; built without innerHTML (Trusted Types).
  var UICSS = ':host{all:initial}' +
    '.menu{position:fixed;left:0;top:0;bottom:0;width:500px;box-sizing:border-box;padding:56px 0 40px;display:flex;flex-direction:column;gap:6px;' +
    'background:linear-gradient(90deg,rgba(8,8,12,.95) 0,rgba(8,8,12,.9) 78%,rgba(8,8,12,0) 100%);color:#fff;font:600 30px/1.2 ""Segoe UI"",system-ui,sans-serif;' +
    'transform:translateX(-105%);transition:transform .18s ease-out;pointer-events:auto}' +
    '.menu.open{transform:none}' +
    '.brand{font-size:26px;font-weight:800;letter-spacing:.08em;color:#e50914;padding:0 46px 22px}' +
    '.item{display:flex;align-items:center;gap:22px;margin:0 40px 0 18px;padding:13px 22px;border-radius:14px;border-left:6px solid transparent;color:rgba(255,255,255,.8);transition:transform .12s,background .12s}' +
    '.item.gap{margin-top:22px}.item.here{border-left-color:#e50914;color:#fff}' +
    '.item.sel{background:#fff;color:#000;transform:scale(1.05);box-shadow:0 0 34px rgba(255,255,255,.35)}' +
    '.ic{width:46px;text-align:center;font-size:30px}' +
    '.hint{position:fixed;left:32px;bottom:32px;padding:14px 26px;border-radius:12px;background:rgba(0,0,0,.8);color:#fff;font:600 26px/1.2 ""Segoe UI"",system-ui,sans-serif;white-space:pre;transition:opacity .8s;pointer-events:none;z-index:1}' +
    '.hint.gone{opacity:0}';
  // [icon, label, what it does]: a path on the site, 'search:<fallback path>', 'back' or 'home'
  var ITEMS = (NF ? [['🔍', 'Search', 'search:/search'], ['🏠', 'Home', '/browse'], ['📺', 'TV Shows', '/browse/genre/83'], ['🎬', 'Movies', '/browse/genre/34399'],
      ['🔥', 'New & Popular', '/latest'], ['➕', 'My List', '/browse/my-list']]
    : BILI ? [['🔍', 'Search', 'search:/en/search-result'], ['🏠', 'Home', '/en'], ['✨', 'Anime', '/en/anime'], ['📅', 'Timeline', '/en/timeline']]
    : []).concat([['⟵', 'Back', 'back'], ['⌂', 'KwartoTV home', 'home']]);
  var ui = null, uiRoot = null, uiSheet = null, menu = null, menuOpen = false, sel = 0;
  function makeUi() {
    if (ui && document.documentElement.contains(ui)) return uiRoot;
    ui = document.createElement('div'); ui.id = 'kw-tv-ui';
    var st = ui.style; st.position = 'fixed'; st.left = '0'; st.top = '0'; st.width = '0'; st.height = '0'; st.zIndex = '2147483646'; st.pointerEvents = 'none';
    uiRoot = ui.attachShadow({ mode: 'open' }); menu = null;
    try { if (!uiSheet) { uiSheet = new CSSStyleSheet(); uiSheet.replaceSync(UICSS); } uiRoot.adoptedStyleSheets = [uiSheet]; }
    catch (x) { var s = document.createElement('style'); s.textContent = UICSS; uiRoot.appendChild(s); }
    document.documentElement.appendChild(ui);
    return uiRoot;
  }
  // the menu item for the screen you are on
  function hereIndex() {
    var p = location.pathname.replace(/\/$/, '') || '/';
    for (var i = 0; i < ITEMS.length; i++) if (ITEMS[i][2].replace(/^search:/, '') === p) return i;
    return -1;
  }
  function itemClass(i, here) { return 'item' + (ITEMS[i][2] === 'back' && i > 0 ? ' gap' : '') + (i === here ? ' here' : '') + (i === sel ? ' sel' : ''); }
  function renderMenu() {
    var root = makeUi();
    if (!menu) {
      menu = document.createElement('div'); menu.className = 'menu';
      var b = document.createElement('div'); b.className = 'brand'; b.textContent = 'KWARTOTV'; menu.appendChild(b);
      ITEMS.forEach(function (it, i) {
        var d = document.createElement('div'), ic = document.createElement('span'), l = document.createElement('span');
        d.className = 'item'; ic.className = 'ic'; ic.textContent = it[0]; l.textContent = it[1];
        d.appendChild(ic); d.appendChild(l);
        d.addEventListener('click', function () { sel = i; runItem(); });
        menu.appendChild(d);
      });
      root.appendChild(menu);
      menu.getBoundingClientRect(); // lay it out closed first, so opening slides in
    }
    var here = hereIndex(), items = menu.querySelectorAll('.item');
    for (var i = 0; i < items.length; i++) items[i].className = itemClass(i, here);
    menu.classList.toggle('open', menuOpen);
    ui.setAttribute('data-menu', menuOpen ? 'open' : 'closed');
  }
  function openMenu() { var h = hereIndex(); sel = h >= 0 ? h : 0; menuOpen = true; renderMenu(); try { window.chrome.webview.postMessage('tvfocus:MENU'); } catch (x) { } }
  function closeMenu() { if (!menuOpen) return; menuOpen = false; renderMenu(); draw(); }
  function moveMenu(d) { sel = Math.max(0, Math.min(ITEMS.length - 1, sel + d)); renderMenu(); }
  function runItem() {
    var a = ITEMS[sel][2];
    closeMenu();
    if (a === 'back') back();
    else if (a === 'home') window.chrome.webview.postMessage('home');
    else if (a.indexOf('search:') === 0) openSearch(a.slice(7));
    else if (location.pathname.replace(/\/$/, '') !== a) location.href = location.origin + a;
  }
  // Search: show the site's top bar again and put the keyboard in its search box (the on-screen keyboard opens);
  // when the page has no search box, open the site's search page.
  var SEARCH_IN = 'input[type=search],input[data-uia*=""search"" i],input[name*=""search"" i],input[placeholder*=""search"" i],input[aria-label*=""search"" i],input[class*=""search"" i]';
  var SEARCH_BTN = '.searchTab,[data-uia*=""search-box-launcher"" i],button[aria-label*=""search"" i],[role=button][aria-label*=""search"" i],[class*=""search"" i] button';
  function firstVisible(q) { var l = document.querySelectorAll(q); for (var i = 0; i < l.length; i++) if (visible(l[i])) return l[i]; return null; }
  function openSearch(url) {
    document.documentElement.classList.add('kw-search');
    setTimeout(function () {
      var box = firstVisible(SEARCH_IN), btn = box ? null : firstVisible(SEARCH_BTN);
      if (btn) btn.click();
      setTimeout(function () {
        box = box || firstVisible(SEARCH_IN);
        if (box) { focus(box); box.focus(); try { box.click(); } catch (x) { } }
        else location.href = location.origin + url;
      }, btn ? 500 : 0);
    }, 80);
  }
  // A short hint the first time a site opens: the menu, OK and hold OK.
  function hint() {
    try { if (sessionStorage.getItem('kwTvHint')) return; sessionStorage.setItem('kwTvHint', '1'); } catch (x) { }
    var h = document.createElement('div'); h.className = 'hint'; h.textContent = '◀ Menu   OK: Piliin   Hold OK: Bumalik';
    makeUi().appendChild(h);
    setTimeout(function () { h.classList.add('gone'); }, 4000);
    setTimeout(function () { if (h.parentNode) h.parentNode.removeChild(h); }, 5000);
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { setTimeout(hint, 800); }); else setTimeout(hint, 800);

  // Which site parts are hidden: Netflix's top bar except on its profile, sign-in and account pages; Bilibili's top bar;
  // both show it again on search pages.
  function siteClasses() {
    var h = document.documentElement, p = location.pathname;
    h.classList.toggle('kw-nf', NF); h.classList.toggle('kw-bili', BILI);
    var hide = BILI || (NF && !/^\/(login|signup|loginhelp|profiles|manageprofiles|youraccount|account)/i.test(p) && !document.querySelector('.profiles-gate-container,.list-profiles,[data-uia*=""profile-gate"" i]'));
    h.classList.toggle('kw-hidehead', hide && !/search/i.test(p));
  }

  // Highlight something as soon as a screen or a pop-up appears, so the remote works right away.
  var lastScope = null, lastPath = '', stash = null;
  setInterval(function () {
    try {
      addCss(); siteClasses();
      if (location.pathname !== lastPath && !/search/i.test(location.pathname)) document.documentElement.classList.remove('kw-search');
      if (menuOpen || watching() || typing(document.activeElement)) return;
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

  // OK: a short press (released within half a second) opens or plays; holding it goes back, once, while still held.
  var okAt = 0, okTimer = 0, okLong = false;
  function okDown(e) {
    e.preventDefault(); e.stopImmediatePropagation();
    if (e.repeat) return; // the key repeating while held
    clearTimeout(okTimer); okAt = Date.now(); okLong = false;
    okTimer = setTimeout(holdBack, 500);
  }
  function holdBack() { okLong = true; if (menuOpen) closeMenu(); back(); }
  function okShort() {
    if (menuOpen) { runItem(); return; }
    if (watching()) { var v = theVideo(); if (v) { if (v.paused) v.play(); else v.pause(); } return; }
    var el = cur && document.contains(cur) ? cur : (document.activeElement !== document.body ? document.activeElement : null);
    if (!el) return;
    if (el.tagName === 'VIDEO') playFull(el); else { if (typing(el)) el.focus(); el.click(); }
  }
  window.addEventListener('keyup', function (e) {
    if (e.key !== 'Enter' || !okAt) return;
    e.preventDefault(); e.stopImmediatePropagation();
    clearTimeout(okTimer);
    var held = Date.now() - okAt; okAt = 0;
    if (!okLong) { if (held >= 500) holdBack(); else okShort(); }
  }, true);

  // The pointer hides while the remote's keys are used, and comes back when the mouse (or the phone's Mouse pad) moves.
  document.addEventListener('mousemove', function () { document.documentElement.classList.remove('kw-nocursor'); }, true);
  var edgeEl = null, edgeAt = 0;
  window.addEventListener('keydown', function (e) {
    if (e.key === 'F1') return;
    document.documentElement.classList.add('kw-nocursor');
    var isBack = e.key === 'Escape' || e.key === 'BrowserBack';
    // The side menu: ▲ ▼ move, ▶ or Back closes it, OK opens the item, hold OK goes back.
    if (menuOpen) {
      if (e.key === 'Enter') { okDown(e); return; }
      e.preventDefault(); e.stopImmediatePropagation();
      if (e.key === 'ArrowUp' || e.key === 'ArrowDown') moveMenu(e.key === 'ArrowDown' ? 1 : -1);
      else if (e.key === 'ArrowRight' || isBack || e.key === 'Backspace') closeMenu();
      return;
    }
    // While a video plays: OK pauses/plays, hold OK or Back leaves, ◀ ▶ skip 10 s. On Netflix its own player keys also work.
    if (watching()) {
      var v = theVideo();
      if (isBack || e.key === 'Backspace') { e.preventDefault(); e.stopPropagation(); back(); }
      else if (e.key === 'Enter') okDown(e);
      else if (e.key === ' ') { if (v) { e.preventDefault(); e.stopPropagation(); if (v.paused) v.play(); else v.pause(); } }
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
      if (n) { focus(n); edgeEl = null; }
      else if (dir === 'left') {
        // nothing further left: page the row back if it can, otherwise (or when paging changed nothing) open the side menu
        if (edgeEl === cur && Date.now() - edgeAt > 900) { edgeEl = null; openMenu(); }
        else if (pageRow('left')) { if (edgeEl !== cur) { edgeEl = cur; edgeAt = Date.now(); } }
        else { edgeEl = null; openMenu(); }
      }
      else if (dir === 'right') pageRow(dir);
      else window.scrollBy({ top: (dir === 'down' ? 1 : -1) * window.innerHeight * 0.5, behavior: 'smooth' });
    } else if (e.key === 'Enter') {
      if (typing(t)) return; // in a text box OK belongs to the box (it sends the search)
      okDown(e);
    } else if (isBack || (e.key === 'Backspace' && !typing(t))) {
      e.preventDefault(); e.stopImmediatePropagation(); back();
    }
  }, true);
  // a real mouse click moves the highlight too (and a click on the page closes the side menu)
  document.addEventListener('pointerdown', function (e) {
    if (e.target === ui) return;
    closeMenu();
    var el = e.target.closest && e.target.closest(SEL); if (el) { cur = el; draw(); }
  }, true);
  addCss();
})();";
}
