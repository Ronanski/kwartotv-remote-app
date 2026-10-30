// Research helper: runs KwartoTV's TV navigation script (TvWebScript.cs) on bilibili.tv and prints where the
// highlight goes for each remote key, with screenshots, so the TV layout can be checked without a Windows PC.
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
      return { path: location.pathname, msgs: l, active: a ? a.tagName + '.' + String(a.className).slice(0, 60) : '',
        fullscreen: !!document.fullscreenElement, videos: document.querySelectorAll('video').length };
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

  await go('https://www.bilibili.tv/en/play/34695', 'play-page');
  await closePopup('play-close-popup');
  await key('Enter', 1, 'ok-on-play');
  await key('ArrowRight', 1, 'skip');
  await key('Escape', 1, 'back-1');
  await key('Escape', 1, 'back-2');
  console.log('##PLAYER ' + JSON.stringify(await p.evaluate(() =>
    [...document.querySelectorAll('[class*=player i] button,[class*=player i] [role=button],[class*=episode i] a,[class*=ep-item i]')]
      .slice(0, 30).map(e => e.tagName + '.' + String(e.className).slice(0, 70) + ' "' + (e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 30) + '"'))));
  await b.close();
})();
