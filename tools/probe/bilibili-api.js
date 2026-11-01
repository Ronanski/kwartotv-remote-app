// Research: which JSON APIs bilibili.tv's own pages use (home rows, search, anime page, episode play),
// so KwartoTV can show Bilibili as a real TV screen (rows of posters) instead of the website.
const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch();
  const ctx = await b.newContext({ locale: 'en-US', viewport: { width: 1280, height: 800 } });
  const p = await ctx.newPage();
  const seen = new Set();
  p.on('response', async (r) => {
    const u = r.url();
    if (!/api\.bilibili\.tv|bilibili\.tv\/.*\/(api|gateway)/.test(u) || seen.has(u.split('?')[0])) return;
    seen.add(u.split('?')[0]);
    let body = '';
    try { body = (await r.text()).replace(/\s+/g, ' ').slice(0, 1400); } catch (e) { body = 'ERR ' + e; }
    console.log('\n### ' + r.status() + ' ' + u.slice(0, 400) + '\n' + body);
  });
  async function visit(url, wait) {
    console.log('\n======== ' + url);
    try { await p.goto(url, { waitUntil: 'domcontentloaded', timeout: 45000 }); } catch (e) { console.log('goto: ' + e.message); }
    await p.waitForTimeout(wait);
    for (let i = 0; i < 4; i++) { await p.mouse.wheel(0, 1500); await p.waitForTimeout(1200); }
  }
  await visit('https://www.bilibili.tv/en', 8000);
  await visit('https://www.bilibili.tv/en/anime', 6000);
  await visit('https://www.bilibili.tv/en/search-result?q=one%20piece', 6000);
  // the first anime link found on the anime page, then its first episode
  const links = await p.$$eval('a[href*="/play/"]', as => as.map(a => a.href)).catch(() => []);
  console.log('\nplay links: ' + links.slice(0, 5).join(' '));
  if (links[0]) await visit(links[0], 12000);
  await b.close();
})();
