// Research helper: opens bilibili.tv pages and prints the data requests the site makes (to build the native TV app).
const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1366, height: 800 } });
  const seen = new Set();
  p.on('response', async r => {
    const u = r.url();
    if (!/api\.bilibili\.tv|passport|bilibili\.tv\/.*(api|gateway)/.test(u)) return;
    const key = u.split('?')[0];
    if (seen.has(key) || /dm\/web|reply\/web|popups|notifycard|vip\/benefits|log|report|heartbeat/.test(key)) return;
    seen.add(key);
    let body = '';
    try { body = (await r.text()).slice(0, 1800); } catch (e) { body = String(e); }
    console.log('##API ' + r.status() + ' ' + r.request().method() + ' ' + u + '\n' + body + '\n');
  });
  async function go(u) { console.log('##PAGE ' + u); try { await p.goto(u, { waitUntil: 'networkidle', timeout: 45000 }); } catch (e) { console.log('nav: ' + e.message); } await p.waitForTimeout(2500); }
  async function state() {
    const s = await p.evaluate(() => { const k = Object.keys(window).filter(k => /^__/.test(k)); const o = {}; k.forEach(x => { try { o[x] = JSON.stringify(window[x]).slice(0, 1500); } catch (e) { } }); return o; });
    for (const k in s) console.log('##STATE ' + k + ' ' + s[k]);
  }
  await go('https://www.bilibili.tv/en/anime');
  await state();
  await p.mouse.wheel(0, 3000); await p.waitForTimeout(3000);
  const play = await p.evaluate(() => [...document.querySelectorAll('a[href*="/play/"]')].map(a => a.href).slice(0, 3));
  console.log('##LINKS ' + play.join(' '));
  await go('https://www.bilibili.tv/en/search-result?q=one%20piece'); await state();
  await go('https://www.bilibili.tv/en/timeline');
  if (play[0]) { await go(play[0]); await state(); }
  await go('https://www.bilibili.tv/en/play/34695');
  // sign-in: open the login box and choose the QR code
  await go('https://www.bilibili.tv/en');
  try {
    await p.getByText('Sign In', { exact: true }).first().click(); await p.waitForTimeout(2500);
    const qr = p.getByText(/QR/i).first(); if (await qr.count()) { await qr.click(); }
    await p.waitForTimeout(6000);
  } catch (e) { console.log('login: ' + e.message); }
  await b.close();
})();
