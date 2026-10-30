// Research helper: which official Philippine TV and news YouTube channels have a live stream right now
// (for a free "Pinoy Live" channel pack; KwartoTV plays YouTube live links as fullscreen channels).
const handles = [
  // live in the first probe
  'ABSCBNEntertainment', 'TV5Philippines', 'gmanetwork', 'gmanews', 'NET25TV', 'UNTVNewsandRescue',
  // official channels that exist but were not live in the first probe
  'ABSCBNNews', 'ANCalerts', 'ABSCBNNewsChannel', 'gtvph', 'Kapatid', 'ptvph', 'INCTV', 'DZRH', 'SMNI', 'AbanteTV',
  'inquirerdotnet', 'AllTVPH', 'brigadanews', 'radyopilipinas',
  // more guesses: networks, news, government, radio-TV, music, sports, religious
  'onenewsph', 'OneNewsPH', 'OnePHtv', 'news5', 'TV5News', 'RPTVph', 'RPTVofficial', 'BilyonaryoChannel', 'BilyonaryoNews',
  'TeleRadyo', 'DZMMTeleRadyo', 'teleradyoserbisyoofficial', 'PBSphilippines', 'PTVNews', 'PTVph4', 'IBC13official', 'ibctv13',
  'EagleNewsPH', 'EagleNewsService', 'CLTV36', 'MYXphilippines', 'myxph', 'KnowledgeChannelOrg', 'KnowledgeChannel',
  'DWIZ882', 'DWIZNewsTV', 'DZXL558', 'RMNNetworks', 'BomboRadyoPhilippines', 'dzbbsuperradyo', 'DZBBofficial',
  'RTVMalacanang', 'PCOgovph', 'SenatePH', 'SenateofthePhilippines', 'HouseofRepresentativesPH', 'CongressPH',
  'SolarSportsPH', 'OneSportsPH', 'PBAOfficial', 'UAAPsports', 'NCAAPhilippines',
  'TheMediaHubPH', 'IntercontinentalBroadcastingCorporation', 'PinoyTVOnline', 'GMAPinoyTV', 'Kapuso', 'KapusoTV',
  'ABSCBNStarCinema', 'CinemaOnePH', 'JeepneyTV', 'MetroChannel', 'TFCglobal', 'iWantTFC', 'ALLTVNews', 'A2ZNews',
  'CBCPNews', 'TVMariaPH', 'TVMaria', 'Radyo Veritas'.replace(' ', ''), 'RadyoVeritas846', 'LightTVPH', 'SonshineMediaNetwork',
  'DZRJ810', 'RadyoBandido', 'NewsWatchPlus', 'BusinessWorldTV', 'PhilstarTV', 'RapplerTV', 'Rappler', 'VeraFilesPH',
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
