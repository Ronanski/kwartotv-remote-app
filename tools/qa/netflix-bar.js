// KwartoTV's own Netflix bar while a video plays (NetflixControl + TvWebScript on a Netflix-like /watch page with
// Netflix's player API, subtitles as "timed text" tracks): ▼ opens it on Audio & Subtitles, ◀ ▶ choose, OK presses,
// lists for Audio & Subtitles / Speed / Episodes, Back closes the list then the bar, and the video never leaves.
const fs = require('fs'); const path = require('path'); const { chromium } = require('playwright');
const grab = (f) => fs.readFileSync(path.join(__dirname, '../../src/KwartoTV.App/Services/' + f), 'utf8')
  .match(/public const string Script = @"([\s\S]*?)";\s*\n/)[1].replace(/""/g, '"');
const tv = grab('TvWebScript.cs'), nf = grab('NetflixControl.cs');
let fail = 0; const check = (ok, n) => { console.log((ok ? 'PASS ' : 'FAIL ') + n); if (!ok) fail++; };
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1280, height: 720 } });
  await p.route('https://www.netflix.com/**', r => {
    if (r.request().url().includes('/metadata?movieid=')) return r.fulfill({ contentType: 'application/json', body: JSON.stringify({ video: { title: 'Dark', currentEpisode: 81234567,
      seasons: [{ seq: 1, title: 'Season 1', episodes: [{ episodeId: 81234567, seq: 1, title: 'Secrets' }, { episodeId: 81234568, seq: 2, title: 'Lies' }] }] } }) });
    return r.fulfill({ contentType: 'text/html', body: '<!doctype html><html><body style="margin:0;background:#000"><div class="watch-video"><video style="width:100%;height:90vh"></video><div data-uia="video-title"><h4>Dark</h4><span>S1:E1</span><span>Secrets</span></div></div></body></html>' });
  });
  await p.addInitScript(() => {
    window.__m = []; window.__calls = [];
    window.chrome = { webview: { postMessage: m => window.__m.push(String(m)) } };
    const A = [{ trackId: 'a1', displayName: 'English [Original]' }, { trackId: 'a2', displayName: 'Japanese' }];
    const T = [{ trackId: 'n', isNoneTrack: true, displayName: 'Off' }, { trackId: 't1', displayName: 'English' }, { trackId: 't2', displayName: 'Filipino' }];
    const pl = { paused: false, t: 300000, a: A[0], s: T[0], isPaused() { return this.paused; }, play() { this.paused = false; window.__calls.push('play'); }, pause() { this.paused = true; window.__calls.push('pause'); },
      getCurrentTime() { return this.t; }, getDuration: () => 3000000, seek(ms) { this.t = ms; window.__calls.push('seek ' + ms); },
      getAudioTrackList: () => A, getAudioTrack() { return this.a; }, setAudioTrack(x) { this.a = x; window.__calls.push('audio ' + x.displayName); },
      getTimedTextTrackList: () => T, getTimedTextTrack() { return this.s; }, setTimedTextTrack(x) { this.s = x; window.__calls.push('subs ' + x.displayName); } };
    window.__pl = pl;
    window.netflix = { appContext: { state: { playerApp: { getAPI: () => ({ videoPlayer: { getAllPlayerSessionIds: () => ['watch-1'], getVideoPlayerBySessionId: () => pl } }) } } } };
  });
  await p.goto('https://www.netflix.com/watch/81234567');
  await p.evaluate(nf); await p.evaluate(tv);
  await p.waitForTimeout(1300);
  const last = () => p.evaluate(() => (window.__m.filter(m => m.startsWith('tvfocus:NFBAR')).slice(-1)[0] || '').replace(/^tvfocus:NFBAR ?/, '').trim());
  const calls = () => p.evaluate(() => window.__calls.slice());
  const key = async (k, w = 250) => { await p.keyboard.press(k); await p.waitForTimeout(w); };
  await key('ArrowDown', 400);
  check(await last() === 'Audio & Subtitles', `▼ opens the bar on Audio & Subtitles (${await last()})`);
  check(await p.evaluate(() => !!document.getElementById('kw-nfbar') && window.__kwNfBar.isOpen()), 'the bar is on screen');
  await key('Enter', 400);
  check(await last() === 'English [Original]', `OK opens the audio list on the current audio (${await last()})`);
  await key('ArrowRight'); await key('ArrowDown'); await key('ArrowDown');
  check(await last() === 'Filipino', `→ goes to the subtitles, ↓↓ to Filipino (${await last()})`);
  await key('Enter', 700);
  check((await calls()).includes('subs Filipino'), `OK sets Filipino subtitles through Netflix's player (timed text) (${await calls()})`);
  await key('ArrowLeft'); await key('ArrowDown'); await key('Enter', 700);
  check((await calls()).includes('audio Japanese'), `audio Japanese (${await calls()})`);
  await key('Escape');
  check(await p.evaluate(() => window.__kwNfBar.isOpen()) && await last() === 'Audio & Subtitles', `Back closes the list and stays on the bar (${await last()})`);
  // ⏪ 10 is three to the left of Audio & Subtitles
  await key('ArrowLeft'); await key('ArrowLeft'); await key('ArrowLeft');
  check(await last() === '⏪ 10', `◀ walks to ⏪ 10 (${await last()})`);
  await key('Enter');
  check((await calls()).includes('seek 290000'), `⏪ 10 goes back 10 s (${await calls()})`);
  await key('ArrowRight'); await key('Enter');
  check((await calls()).includes('pause'), `the pause button pauses (${await calls()})`);
  // Speed
  for (let i = 0; i < 4; i++) await key('ArrowRight');
  check(await last() === 'Speed', `→ to Speed (${await last()})`);
  await key('Enter'); await key('ArrowDown'); await key('ArrowDown'); await key('ArrowDown'); await key('Enter', 600);
  check(await p.evaluate(() => document.querySelector('video').playbackRate) === 1.5, 'Speed 1.5× from the bar');
  // Episodes (the bar closed after the speed: open it again)
  await key('ArrowDown', 400);
  await key('ArrowRight'); await key('Enter', 900);
  check(await last() === '1. Secrets', `Episodes opens on the episode playing (${await last()})`);
  await key('ArrowDown'); await key('Enter', 800);
  check(p.url() === 'https://www.netflix.com/watch/81234568', `OK plays episode 2 (${p.url()})`);
  await b.close(); console.log(fail ? `${fail} FAILED` : 'ALL PASSED'); process.exit(fail ? 1 : 0);
})();
