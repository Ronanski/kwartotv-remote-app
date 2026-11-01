// Research: YouTube Music's own search and "up next" (radio) answers to a plain request, so the TV can search and
// queue songs itself and play them through the signed-in YouTube app.
const ctx = { client: { clientName: 'WEB_REMIX', clientVersion: '1.20241023.01.00', hl: 'en', gl: 'PH' } };
const headers = { 'Content-Type': 'application/json', Origin: 'https://music.youtube.com', Referer: 'https://music.youtube.com/', 'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36' };
async function call(name, body) {
  const r = await fetch(`https://music.youtube.com/youtubei/v1/${name}?prettyPrint=false`, { method: 'POST', headers, body: JSON.stringify({ context: ctx, ...body }) });
  const t = await r.text();
  console.log(`\n### ${name} ${r.status} ${t.length} chars`);
  return t;
}
function find(o, key, out, max = 6) {
  if (!o || typeof o !== 'object' || out.length >= max) return;
  if (Array.isArray(o)) { o.forEach(x => find(x, key, out, max)); return; }
  if (o[key]) { out.push(o[key]); return; }
  for (const k in o) find(o[k], key, out, max);
}
(async () => {
  const s = await call('search', { query: 'Ben&Ben Maybe the Night', params: 'EgWKAQIIAWoKEAkQBRAKEAMQBA==' });
  const sj = JSON.parse(s); const items = []; find(sj, 'musicResponsiveListItemRenderer', items, 2);
  console.log('search item sample:', JSON.stringify(items[0]).slice(0, 2500));
  const id = (items[0] && items[0].playlistItemData && items[0].playlistItemData.videoId) || 'hJhVURhdLEg';
  const n = await call('next', { videoId: id, playlistId: 'RDAMVM' + id, isAudioOnly: true });
  const nj = JSON.parse(n); const vids = []; find(nj, 'playlistPanelVideoRenderer', vids, 3);
  console.log('next count sample:', vids.length, JSON.stringify(vids[1] || vids[0]).slice(0, 2500));
})();

// Home rows (Quick picks, charts, moods) without an account.
(async () => {
  await new Promise(r => setTimeout(r, 3000));
  const h = await call('browse', { browseId: 'FEmusic_home' });
  const hj = JSON.parse(h);
  const shelves = []; find(hj, 'musicCarouselShelfRenderer', shelves, 12);
  console.log('home shelves:', shelves.length);
  for (const s of shelves) {
    const title = JSON.stringify(s.header && s.header.musicCarouselShelfBasicHeaderRenderer && s.header.musicCarouselShelfBasicHeaderRenderer.title);
    const kinds = (s.contents || []).map(c => Object.keys(c)[0]);
    console.log('shelf', title, kinds.length, [...new Set(kinds)].join(','));
  }
  const two = []; find(hj, 'musicTwoRowItemRenderer', two, 2);
  console.log('twoRow sample:', JSON.stringify(two[0]).slice(0, 2500));
  const rows = []; find(hj, 'musicResponsiveListItemRenderer', rows, 1);
  console.log('listItem sample:', JSON.stringify(rows[0] || null).slice(0, 1500));
})();
