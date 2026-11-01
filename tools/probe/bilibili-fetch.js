// Research: what bilibili.tv's list APIs answer to a plain request (no browser), with and without browser-like headers.
const urls = [
  'https://api.bilibili.tv/intl/gateway/web/v2/home/recommend?s_locale=en_US&platform=web&pn=1&ps=40',
  'https://api.bilibili.tv/intl/gateway/web/v2/anime/page?s_locale=en_US&platform=web&page_type=4&pn=1',
  'https://api.bilibili.tv/intl/gateway/web/v2/anime/page?s_locale=en_US&platform=web&page_type=4&pn=2',
  'https://api.bilibili.tv/intl/gateway/web/v2/search_v2?s_locale=en_US&platform=web&keyword=one%20piece&highlight=0&pn=1&ps=30&qid=&sort=0&duration_type=0',
];
const heads = [
  {},
  { 'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36', Referer: 'https://www.bilibili.tv/', Origin: 'https://www.bilibili.tv' },
];
(async () => {
  for (const u of urls) for (const h of heads) {
    try {
      const r = await fetch(u, { headers: h });
      const t = await r.text();
      console.log(`\n### ${r.status} ${Object.keys(h).length ? 'browser-headers' : 'plain'} ${u}\n${t.replace(/\s+/g, ' ').slice(0, 700)}`);
    } catch (e) { console.log(`\n### ERR ${u} ${e}`); }
  }
})();
