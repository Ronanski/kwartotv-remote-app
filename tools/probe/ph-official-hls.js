// Research helper: finds the free live streams (HLS .m3u8) that Philippine broadcasters play on their own official websites,
// and checks each one (answers, is a real HLS playlist, no DRM). Only official sites are opened.
// Usage: node tools/probe/ph-official-hls.js
const { chromium } = require('playwright');
const pages = [
  ['PTV', 'https://ptvnews.ph/live/'], ['PTV', 'https://ptvnews.ph/'],
  ['UNTV', 'https://www.untvweb.com/live/'], ['UNTV', 'https://untvweb.com/'],
  ['NET25', 'https://www.net25.com/live'], ['NET25', 'https://www.net25.com/'],
  ['SMNI', 'https://www.smninewschannel.com/live/'], ['SMNI', 'https://www.smninewschannel.com/'],
  ['DZRH', 'https://www.dzrh.com.ph/live'], ['DZRH', 'https://dzrh.com.ph/'],
  ['Light TV', 'https://www.lighttv.com.ph/live'], ['Light TV', 'https://lighttv.com.ph/'],
  ['INC TV', 'https://incmedia.org/live/'], ['INC TV', 'https://www.inctv.ph/'],
  ['Knowledge Channel', 'https://knowledgechannel.org/'],
  ['GMA News', 'https://www.gmanetwork.com/news/livestream/'],
  ['TV5 / News5', 'https://www.tv5.com.ph/live'], ['News5', 'https://news.tv5.com.ph/live'],
  ['ABS-CBN News', 'https://news.abs-cbn.com/live'], ['Kapamilya Online', 'https://www.abs-cbn.com/kapamilya-online-live'],
  ['One News', 'https://www.onenews.ph/live'], ['Bilyonaryo', 'https://bilyonaryo.com/live/'],
  ['CLTV36', 'https://cltv36.tv/live/'], ['CLTV36', 'https://www.cltv36.tv/'],
  ['Abante TV', 'https://www.abante.com.ph/abante-tv/'], ['Radyo Pilipinas', 'https://radyopilipinas.gov.ph/'], ['PBS', 'https://pbs.gov.ph/'],
  ['Brigada', 'https://www.brigadanews.ph/live/'], ['Bombo Radyo', 'https://www.bomboradyo.com/live/'], ['DWIZ', 'https://www.dwiz.ph/'],
  ['RMN', 'https://rmn.ph/live/'], ['IBC 13', 'https://www.ibc.gov.ph/'], ['ALLTV', 'https://www.alltv.ph/'],
  ['Senate', 'https://web.senate.gov.ph/'], ['DZBB', 'https://www.gmanetwork.com/radio/dzbb/'], ['Super Radyo', 'https://www.gmanetwork.com/news/superradyo/'],
  ['TeleRadyo', 'https://teleradyo.abs-cbn.com/'], ['Teleradyo Serbisyo', 'https://www.teleradyoserbisyo.ph/'],
  ['Sonshine', 'https://www.sonshinemedianetwork.com/'], ['TV Maria', 'https://tvmaria.com/'], ['Hope Channel PH', 'https://hopechannel.ph/live/'],
];
(async () => {
  const b = await chromium.launch();
  const found = new Map();   // url -> station
  for (const [station, url] of pages) {
    const ctx = await b.newContext({ viewport: { width: 1366, height: 800 }, locale: 'en-PH' });
    const p = await ctx.newPage();
    const hits = new Set();
    p.on('request', r => { const u = r.url(); if (/\.m3u8(\?|$)/i.test(u) || /\/manifest\(|\.mpd(\?|$)/i.test(u)) hits.add(u); });
    let status = '?';
    try {
      const resp = await p.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 });
      status = resp ? resp.status() : '?';
      await p.waitForTimeout(8000);
      // players often wait for a click
      for (const sel of ['video', '.vjs-big-play-button', '.jw-display-icon-container', '[aria-label*="play" i]', 'button[class*="play" i]', 'iframe']) {
        try { const el = await p.$(sel); if (el) { await el.click({ timeout: 2000 }); await p.waitForTimeout(3000); } } catch { }
      }
      await p.waitForTimeout(4000);
      const html = await p.content();
      for (const m of html.matchAll(/https?:\/\/[^"'\s<>]+?\.m3u8[^"'\s<>]*/gi)) hits.add(m[0].replace(/\\\//g, '/'));
      const yt = (html.match(/youtube\.com\/embed\/[A-Za-z0-9_-]+/g) || []).slice(0, 2);
      console.log(`##PAGE ${station} ${url} status=${status} streams=${hits.size} youtube=${yt.join(',')}`);
    } catch (e) { console.log(`##PAGE ${station} ${url} error=${e.message.split('\n')[0]}`); }
    for (const h of hits) if (!found.has(h)) found.set(h, station);
    await ctx.close();
  }
  await b.close();
  // Check each stream: answers, real HLS, DRM-free, and does a segment download
  for (const [u, station] of found) {
    try {
      const r = await fetch(u, { signal: AbortSignal.timeout(15000) });
      const t = r.ok ? await r.text() : '';
      const hls = t.startsWith('#EXTM3U');
      const drm = /#EXT-X-(SESSION-)?KEY:METHOD=(SAMPLE-AES|SAMPLE-AES-CTR)|KEYFORMAT="(com\.widevine|urn:uuid|com\.apple\.streamingkeydelivery)/i.test(t);
      const tokened = /[?&](token|hdnts|hdnea|expires|exp|signature|sig|st|e)=/i.test(u);
      console.log(`##STREAM ${station} status=${r.status} hls=${hls} drm=${drm} tokenInUrl=${tokened} ${u}`);
    } catch (e) { console.log(`##STREAM ${station} error=${e.message} ${u}`); }
  }
})();
