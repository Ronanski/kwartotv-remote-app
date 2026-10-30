// Research helper: runs KwartoTV's TV navigation script (TvWebScript.cs) on bilibili.tv and prints where the
// highlight goes for each remote key (and the side menu and hold-OK back), with screenshots, so the TV layout can be checked without a Windows PC.
// Usage: node tools/probe/bilibili-tv.js [outDir]
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const out = process.argv[2] || 'probe-out';
fs.mkdirSync(out, { recursive: true });
const src = fs.readFileSync(path.join(__dirname, '../../src/KwartoTV.App/Services/TvWebScript.cs'), 'utf8');
const script = src.slice(src.indexOf('@"') + 2, src.lastIndexOf('";')).replace(/""/g, '"');

(async () => {
  const b = await chromium.launch();
  const ctx = await b.newContext({ viewport: { width: 1920, height: 1080 }, locale: 'en-US' });
  // WebView2's message channel, recorded instead of sent to the app
  await ctx.addInitScript(() => {
    window.__kwLog = [];
    window.chrome = window.chrome || {};
    window.chrome.webview = { postMessage: m => window.__kwLog.push(String(m)) };
  });
  await ctx.addInitScript(script);
  const p = await ctx.newPage();
  let shot = 0;
  async function log(label) {
    await p.waitForTimeout(900);
    const s = await p.evaluate(() => {
      const l = window.__kwLog.splice(0);
      const a = document.activeElement;
      // the KwartoTV side menu and hint (in the script's own shadow root)
      const ui = document.getElementById('kw-tv-ui'), sr = ui && ui.shadowRoot, m = sr && sr.querySelector('.menu');
      const sel = sr && sr.querySelector('.item.sel'), hint = sr && sr.querySelector('.hint');
      return { path: location.pathname, msgs: l, active: a ? a.tagName + '.' + String(a.className).slice(0, 60) : '',
        fullscreen: !!document.fullscreenElement, videos: document.querySelectorAll('video').length, paused: (document.querySelector('video') || {}).paused,
        menu: ui ? ui.getAttribute('data-menu') : null, menuOnScreen: !!m && m.getBoundingClientRect().right > 100,
        menuItem: sel ? sel.textContent : null, hint: hint ? getComputedStyle(hint).opacity : null,
        html: document.documentElement.className.split(' ').filter(c => /^kw-/.test(c)).join(' ') };
    });
    console.log(`##${label} ${JSON.stringify(s)}`);
    await p.screenshot({ path: path.join(out, `${String(++shot).padStart(2, '0')}-${label}.png`) });
  }
  async function key(k, n = 1, label = k) { for (let i = 0; i < n; i++) { await p.keyboard.press(k); await p.waitForTimeout(450); } await log(label); }
  // A notice pop-up may open first (region, cookies): Back closes it, as on the TV. Only pressed when one shows,
  // since Back on a normal page leaves it.
  async function closePopup(label) {
    const open = await p.evaluate(() => [...document.querySelectorAll('[role=dialog],[aria-modal=true],.dialog__close')]
      .some(e => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0; }));
    if (open) await key('Escape', 1, label); else console.log(`##${label} no pop-up`);
  }
  async function go(u, label) {
    try { await p.goto(u, { waitUntil: 'domcontentloaded', timeout: 45000 }); } catch (e) { console.log('nav: ' + e.message); }
    await p.waitForTimeout(6000);
    await log(label);
  }

  await go('https://www.bilibili.tv/en', 'home');
  await closePopup('close-popup');
  await key('ArrowDown', 2, 'down2');
  await key('ArrowRight', 6, 'right6');
  await key('ArrowRight', 6, 'right12');
  await key('ArrowDown', 3, 'down5');
  await key('ArrowUp', 6, 'up-to-top');
  // Row-level structure the script relies on: carousels and their arrows.
  console.log('##ROWS ' + JSON.stringify(await p.evaluate(() =>
    [...document.querySelectorAll('[class*=swiper i],[class*=carousel i],[class*=slider i],[class*=scroll i]')]
      .slice(0, 25).map(e => e.tagName + '.' + String(e.className).slice(0, 80)))));
  console.log('##ARROWS ' + JSON.stringify(await p.evaluate(() =>
    [...document.querySelectorAll('[class*=arrow i],[class*=next i],[class*=prev i]')]
      .slice(0, 25).map(e => e.tagName + '.' + String(e.className).slice(0, 80) + ' aria=' + (e.getAttribute('aria-label') || '')))));

  // The site's own top bar and footer (hidden by the script while the side menu replaces them).
  console.log('##HEADER ' + JSON.stringify(await p.evaluate(() =>
    [...document.querySelectorAll('header,footer,[class*=header i],[class*=footer i]')].filter(e => !e.parentElement.closest('header,footer,[class*=header i],[class*=footer i]'))
      .slice(0, 15).map(e => e.tagName + '.' + String(e.className).slice(0, 60) + ' display=' + getComputedStyle(e).display + ' h=' + Math.round(e.getBoundingClientRect().height)))));

  // Five-key control: ◀ at the left edge opens the side menu, ▼ to Anime, OK opens it, hold OK goes back.
  async function openMenu(label) {
    for (let i = 0; i < 10; i++) {
      if (await p.evaluate(() => { const u = document.getElementById('kw-tv-ui'); return !!u && u.getAttribute('data-menu') === 'open'; })) break;
      await p.keyboard.press('ArrowLeft'); await p.waitForTimeout(450);
    }
    await log(label);
  }
  async function menuTo(name, label) {
    for (let i = 0; i < 10; i++) {
      const at = await p.evaluate(() => { const s = document.getElementById('kw-tv-ui').shadowRoot.querySelector('.item.sel'); return s ? s.textContent : ''; });
      if (at.includes(name)) break;
      const items = await p.evaluate(() => [...document.getElementById('kw-tv-ui').shadowRoot.querySelectorAll('.item')].map(e => e.textContent));
      const k = items.findIndex(t => t.includes(name)) > items.findIndex(t => t === at) ? 'ArrowDown' : 'ArrowUp';
      await p.keyboard.press(k); await p.waitForTimeout(300);
    }
    await log(label);
  }
  async function holdOk(label) { await p.keyboard.down('Enter'); await p.waitForTimeout(700); await p.keyboard.up('Enter'); await p.waitForTimeout(2500); await log(label); }
  await go('https://www.bilibili.tv/en', 'home-again');
  await closePopup('home-again-popup');
  await openMenu('menu-open');
  await key('ArrowRight', 1, 'menu-close');
  await openMenu('menu-open-2');
  await menuTo('Anime', 'menu-anime');
  await key('Enter', 1, 'menu-ok-anime');
  await p.waitForTimeout(5000); await log('anime-page');
  await holdOk('hold-ok-back');
  await openMenu('menu-open-3');
  await menuTo('Search', 'menu-search');
  await key('Enter', 1, 'menu-ok-search');
  await p.waitForTimeout(3000); await log('search-page');
  console.log('##SEARCHBOX ' + JSON.stringify(await p.evaluate(() =>
    [...document.querySelectorAll('input')].slice(0, 8).map(e => e.type + ' ' + String(e.className).slice(0, 50) + ' ph=' + (e.placeholder || '') + ' w=' + Math.round(e.getBoundingClientRect().width)))));

  await go('https://www.bilibili.tv/en/play/34695', 'play-page');
  await closePopup('play-close-popup');
  await key('Enter', 1, 'ok-on-play');
  await key('ArrowRight', 1, 'skip');
  await key('Enter', 1, 'ok-pause');
  await holdOk('hold-ok-exit-fullscreen');
  await key('Escape', 1, 'back-2');
  console.log('##PLAYER ' + JSON.stringify(await p.evaluate(() =>
    [...document.querySelectorAll('[class*=player i] button,[class*=player i] [role=button],[class*=episode i] a,[class*=ep-item i]')]
      .slice(0, 30).map(e => e.tagName + '.' + String(e.className).slice(0, 70) + ' "' + (e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 30) + '"'))));
  await b.close();
})();
