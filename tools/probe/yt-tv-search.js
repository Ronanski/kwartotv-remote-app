// Research: which address opens YouTube's TV app (youtube.com/tv) straight on search results for a query.
const { chromium } = require('playwright');
const UA = process.argv[2];
(async () => {
  const browser = await chromium.launch();
  const tries = [
    'https://www.youtube.com/tv#/search?q=lofi%20hip%20hop',
    'https://www.youtube.com/tv#/search?query=lofi%20hip%20hop',
    'https://www.youtube.com/tv#/results?search_query=lofi%20hip%20hop',
    'https://www.youtube.com/tv?q=lofi%20hip%20hop#/search',
  ];
  for (const url of tries) {
    const ctx = await browser.newContext({ userAgent: UA, viewport: { width: 1280, height: 720 } });
    const p = await ctx.newPage();
    try {
      await p.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 });
      await p.waitForTimeout(15000);
      const info = await p.evaluate(() => ({ href: location.href, text: (document.body.innerText || '').replace(/\s+/g, ' ').slice(0, 400) }));
      console.log('URL ' + url + '\n  now: ' + info.href + '\n  text: ' + info.text + '\n  hasQuery: ' + /lofi/i.test(info.text));
      const shot = await p.screenshot({ type: 'jpeg', quality: 40 });
      console.log('##SHOT ' + url + '\n' + shot.toString('base64'));
    } catch (e) { console.log('URL ' + url + ' error ' + e.message); }
    await ctx.close();
  }
  await browser.close();
})();
