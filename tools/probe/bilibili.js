// Research helper: opens bilibili.tv pages and prints the data requests the site makes (to build the native TV app).
const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage();
  const seen = new Set();
  p.on('response', async r => {
    const u = r.url();
    if (!/api\.bilibili\.tv|bilibili\.tv\/.*\/(api|gateway)/.test(u) || seen.has(u.split('?')[0])) return;
    seen.add(u.split('?')[0]);
    let body = '';
    try { body = (await r.text()).slice(0, 2500); } catch (e) { body = String(e); }
    console.log('##API ' + r.status() + ' ' + u + '\n' + body + '\n');
  });
  const pages = ['https://www.bilibili.tv/en', 'https://www.bilibili.tv/en/anime', 'https://www.bilibili.tv/en/search-result?q=one%20piece'];
  for (const u of pages) { console.log('##PAGE ' + u); try { await p.goto(u, { waitUntil: 'networkidle', timeout: 45000 }); } catch (e) { console.log('nav: ' + e.message); } await p.waitForTimeout(3000); }
  // open the first title and the first episode
  const link = await p.evaluate(() => { const a = [...document.querySelectorAll('a[href*="/play/"],a[href*="/video/"]')][0]; return a && a.href; });
  console.log('##PAGE ' + link);
  if (link) { await p.goto(link, { waitUntil: 'networkidle', timeout: 45000 }).catch(e => console.log(e.message)); await p.waitForTimeout(5000); }
  await b.close();
})();
