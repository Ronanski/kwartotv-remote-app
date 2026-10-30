// Research helper: which official Philippine TV and news YouTube channels have a live stream right now
// (for a free "Pinoy Live" channel pack; KwartoTV plays YouTube live links as fullscreen channels).
const handles = [
  'KapamilyaOnlineLive', 'ABSCBNNews', 'ABSCBNEntertainment', 'TeleRadyoSerbisyo', 'ANCalerts', 'ABSCBNNewsChannel',
  'gmanews', 'GMAPublicAffairs', 'gmanetwork', 'GMAIntegratedNews', 'gtvph', 'DZBB', 'superradyodzbb',
  'News5Everywhere', 'TV5Philippines', 'OnePH', 'RPTV', 'Kapatid',
  'PTVPhilippines', 'ptvph', 'IBC13', 'NET25TV', 'Net25Official', 'INCTV', 'INCTVnetwork',
  'UNTVNewsandRescue', 'UNTVLife', 'dzrhnewstv', 'DZRH', 'SMNINews', 'SMNI', 'BilyonaryoNewsChannel', 'AbanteTV',
  'rapplerdotcom', 'inquirerdotnet', 'philstar', 'manilabulletin', 'AllTVPH', 'A2ZChannel11', 'TheFilipinoChannel',
  'KnowledgeChannelPH', 'DepEdTVPH', 'PIAgovph', 'CNNPhilippines', 'brigadanews', 'BomboRadyoPH', 'radyopilipinas',
];
(async () => {
  for (const h of handles) {
    const u = `https://www.youtube.com/@${h}/live`;
    try {
      const r = await fetch(u, { headers: { 'accept-language': 'en-US', 'user-agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36' }, signal: AbortSignal.timeout(30000) });
      const t = await r.text();
      const live = /"isLiveNow":true|"isLive":true/.test(t);
      const canon = (t.match(/<link rel="canonical" href="([^"]+)"/) || [])[1] || '';
      const title = ((t.match(/<meta name="title" content="([^"]*)"/) || [])[1] || '').slice(0, 70);
      const chan = ((t.match(/"ownerChannelName":"([^"]*)"/) || t.match(/<meta property="og:title" content="([^"]*)"/) || [])[1] || '').slice(0, 40);
      console.log(`##YT @${h} status=${r.status} live=${live} channel="${chan}" title="${title}" canonical=${canon}`);
    } catch (e) { console.log(`##YT @${h} error=${e.message}`); }
  }
})();
