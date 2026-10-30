// Research helper: which free XMLTV guides cover Philippine channels (for the Live TV "now playing" line).
// Prints, per source: HTTP status, size, channel count, programme count and the PH-looking channels it has.
const zlib = require('zlib');
const sources = [
  'https://epg.pw/xmltv/epg_PH.xml',
  'https://epg.pw/xmltv/epg_PH.xml.gz',
  'https://www.open-epg.com/files/philippines1.xml',
  'https://www.open-epg.com/files/philippines2.xml',
  'https://www.open-epg.com/files/philippines3.xml',
  'https://iptv-org.github.io/epg/guides/ph.xml',
  'https://raw.githubusercontent.com/iptv-org/epg/master/sites/clickthecity.com/clickthecity.com.channels.xml',
  'https://i.mjh.nz/SamsungTVPlus/all.xml.gz',
  'https://i.mjh.nz/PlutoTV/all.xml.gz',
];
const want = /gma|tv5|kapamilya|abs|a2z|ptv|ibc|net ?25|gtv|cnn ph|one ph|one news|anc|cinema ?one|jeepney|knowledge|tvn|cignal|kapatid|true fm|bilyonaryo|pbo|viva/i;
(async () => {
  for (const u of sources) {
    try {
      const r = await fetch(u, { redirect: 'follow', signal: AbortSignal.timeout(60000) });
      let buf = Buffer.from(await r.arrayBuffer());
      if (buf[0] === 0x1f && buf[1] === 0x8b) buf = zlib.gunzipSync(buf);
      const t = buf.toString('utf8');
      const chans = [...t.matchAll(/<channel id="([^"]+)"[^>]*>[\s\S]*?<display-name[^>]*>([^<]+)</g)].map(m => [m[1], m[2]]);
      const progs = (t.match(/<programme /g) || []).length;
      const stops = [...t.matchAll(/<programme [^>]*start="(\d{12})/g)].map(m => m[1]).sort();
      const ph = chans.filter(([id, n]) => want.test(n) || /\.ph\b/i.test(id));
      console.log(`##SRC ${u} status=${r.status} bytes=${buf.length} channels=${chans.length} programmes=${progs} range=${stops[0] || '-'}..${stops[stops.length - 1] || '-'}`);
      console.log(`##PH ${JSON.stringify(ph.slice(0, 60))}`);
      console.log(`##SAMPLE ${JSON.stringify(chans.slice(0, 8))}`);
    } catch (e) { console.log(`##SRC ${u} error=${e.message}`); }
  }
  // tvg-id values in the Philippine playlist, to see how guide ids would have to be matched
  try {
    const t = await (await fetch('https://iptv-org.github.io/iptv/countries/ph.m3u')).text();
    const head = t.split('\n')[0];
    const ids = [...t.matchAll(/tvg-id="([^"]*)"[^,]*,(.*)/g)].map(m => [m[1], m[2].trim()]);
    console.log(`##PHM3U header=${head} channels=${ids.length}`);
    console.log(`##PHIDS ${JSON.stringify(ids.slice(0, 80))}`);
  } catch (e) { console.log('##PHM3U error=' + e.message); }
})();
