// Netflix's player with the TV remote (TvWebScript + NetflixControl on a Netflix-like /watch page): OK plays/pauses
// through Netflix's player, ▼ shows the controls with the highlight on Audio & Subtitles, ◀ ▶ walk the buttons, OK
// presses one, Back hides the controls and stays in the video, OK answers "Are you still watching?".
const fs = require('fs'); const path = require('path'); const { chromium } = require('playwright');
const grab = (f) => fs.readFileSync(path.join(__dirname, '../../src/KwartoTV.App/Services/' + f), 'utf8')
  .match(/public const string Script = @"([\s\S]*?)";\s*\n/)[1].replace(/""/g, '"');
const tv = grab('TvWebScript.cs'), nf = grab('NetflixControl.cs');
const btn = (uia, label) => `<button data-uia="${uia}" aria-label="${label}" onclick="window.__clicked.push('${label}')" style="width:60px;height:60px;margin:6px">${label}</button>`;
const page = `<!doctype html><html><body style="margin:0;background:#000;color:#fff">
<div class="watch-video" style="position:fixed;inset:0"><video style="width:100%;height:80%"></video>
<div class="controls" style="position:absolute;bottom:20px;left:20px;display:flex">
${btn('control-play-pause-pause', 'Pause')}${btn('control-back10', 'Back 10')}${btn('control-forward10', 'Forward 10')}${btn('control-volume-high', 'Volume')}
${btn('control-next', 'Next Episode')}${btn('control-episodes', 'Episodes')}${btn('control-audio-subtitle', 'Audio & Subtitles')}${btn('control-speed', 'Speed')}
</div></div></body></html>`;
let fail = 0; const check = (ok, n) => { console.log((ok ? 'PASS ' : 'FAIL ') + n); if (!ok) fail++; };
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1280, height: 720 } });
  await p.route('https://www.netflix.com/**', r => r.fulfill({ contentType: 'text/html', body: page }));
  await p.addInitScript(() => {
    window.__m = []; window.__clicked = []; window.__calls = [];
    window.chrome = { webview: { postMessage: m => window.__m.push(String(m)) } };
    const pl = { paused: false, isPaused() { return this.paused; }, play() { this.paused = false; window.__calls.push('play'); }, pause() { this.paused = true; window.__calls.push('pause'); },
      getCurrentTime: () => 5000, getDuration: () => 60000, seek() {} };
    window.netflix = { appContext: { state: { playerApp: { getAPI: () => ({ videoPlayer: { getAllPlayerSessionIds: () => ['watch-1'], getVideoPlayerBySessionId: () => pl } }) } } } };
  });
  await p.goto('https://www.netflix.com/watch/81234567');
  await p.evaluate(nf); await p.evaluate(tv);
  await p.waitForTimeout(500);
  const last = () => p.evaluate(() => (window.__m.filter(m => m.startsWith('tvfocus:')).slice(-1)[0] || '').replace(/^tvfocus:\S+ /, '').trim());
  await p.keyboard.press('Enter'); await p.waitForTimeout(200);
  check((await p.evaluate(() => window.__calls)).join() === 'pause', `OK while watching pauses through Netflix's player (${await p.evaluate(() => window.__calls)})`);
  await p.keyboard.press('ArrowDown'); await p.waitForTimeout(500);
  check(await last() === 'Audio & Subtitles', `▼ shows the controls, on Audio & Subtitles (${await last()})`);
  await p.keyboard.press('ArrowRight'); await p.waitForTimeout(300);
  check(await last() === 'Speed', `▶ moves to the next button (${await last()})`);
  await p.keyboard.press('ArrowLeft'); await p.waitForTimeout(300);
  await p.keyboard.press('ArrowLeft'); await p.waitForTimeout(300);
  check(await last() === 'Episodes', `◀ moves back (${await last()})`);
  await p.keyboard.press('Enter'); await p.waitForTimeout(300);
  check((await p.evaluate(() => window.__clicked)).includes('Episodes'), 'OK presses the highlighted button (Episodes)');
  await p.keyboard.press('Escape'); await p.waitForTimeout(400);
  check(p.url() === 'https://www.netflix.com/watch/81234567', `Back hides the controls and stays in the video (${p.url()})`);
  await p.keyboard.press('Enter'); await p.waitForTimeout(200);
  check((await p.evaluate(() => window.__calls)).join() === 'pause,play', `after that, OK plays again (${await p.evaluate(() => window.__calls)})`);
  await p.evaluate(() => document.body.insertAdjacentHTML('beforeend', '<button data-uia="interrupt-autoplay-continue" onclick="window.__clicked.push(\'Continue\'); this.remove()">Continue Watching</button>'));
  await p.keyboard.press('Enter'); await p.waitForTimeout(200);
  check((await p.evaluate(() => window.__clicked)).includes('Continue') && (await p.evaluate(() => window.__calls)).length === 2, 'OK answers "Are you still watching?" (and does not pause)');
  await b.close(); console.log(fail ? `${fail} FAILED` : 'ALL PASSED'); process.exit(fail ? 1 : 0);
})();
