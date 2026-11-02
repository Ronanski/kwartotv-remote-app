// Checks the TV-style highlight (TvWebScript) on a page full of traps: hidden, empty, tiny, covered and doubled
// elements must be skipped, and the arrows must move like a TV: along the row with Left/Right, to the same column with
// Up/Down. Usage: node tools/qa/web-nav.js   (needs Playwright's Chromium)
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const cs = fs.readFileSync(path.join(__dirname, '../../src/KwartoTV.App/Services/TvWebScript.cs'), 'utf8');
const m = cs.match(/public const string Script = @"([\s\S]*?)";\s*\n/);
if (!m) { console.log('FAIL could not find the script'); process.exit(1); }
const script = m[1].replace(/""/g, '"');

const card = (row, i) => `<a class="card" href="#r${row}c${i}" style="left:${40 + i * 240}px"><img alt="" src="data:image/gif;base64,R0lGODlhAQABAAAAACw="><span>Row ${row} item ${i}</span><button class="overlay" aria-label="play r${row}c${i}"></button></a>`;
const page = `<!doctype html><html><head><style>
body{margin:0;background:#111;color:#eee;font:16px sans-serif;height:2000px}
header{position:fixed;top:0;left:0;right:0;height:60px;background:#222;z-index:5}
header a{display:inline-block;margin:18px 12px;color:#fff}
.row{position:absolute;left:0;right:0;height:140px}
.card{position:absolute;top:0;width:200px;height:120px;background:#333;display:block;color:#fff}
.card img{width:200px;height:90px;display:block}
.overlay{position:absolute;inset:0;background:transparent;border:0}
.hidden{display:none}.tiny{position:absolute;left:5px;top:300px;width:4px;height:4px}
.under{position:absolute;top:10px;left:900px;width:80px;height:30px}
</style></head><body>
<header><a href="#home">Home</a><a href="#anime">Anime</a><a href="#search">Search</a></header>
<button class="under">under the bar</button>
<button class="hidden">hidden</button>
<a class="tiny" href="#tiny"></a>
<div tabindex="0" style="position:absolute;top:80px;left:0;width:100%;height:900px"></div>
<button aria-hidden="true" style="position:absolute;top:500px;left:40px">aria hidden</button>
<span tabindex="0" style="position:absolute;top:520px;left:300px;width:40px;height:30px"></span>
<div class="row" style="top:100px">${[0,1,2,3].map(i => card(1, i)).join('')}</div>
<div class="row" style="top:300px">${[0,1,2,3].map(i => card(2, i)).join('')}</div>
</body></html>`;

let fail = 0;
const check = (ok, name) => { console.log((ok ? 'PASS ' : 'FAIL ') + name); if (!ok) fail++; };

(async () => {
  const browser = await chromium.launch();
  const p = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  await p.setContent(page);
  await p.evaluate(() => { window.__moves = []; window.chrome = { webview: { postMessage: m => window.__moves.push(String(m)) } }; });
  await p.evaluate(script);
  const key = async k => { await p.keyboard.press(k); await p.waitForTimeout(120); };
  const last = () => p.evaluate(() => window.__moves.filter(m => m.startsWith('tvfocus:')).slice(-1)[0] || '');

  // Starting: the first highlight goes somewhere real.
  await key('ArrowDown');
  const first = await last();
  check(/Home|Row 1 item 0/.test(first), `first stop is a real item (${first})`);

  // Walk every stop with lots of arrow presses and collect what was highlighted.
  const seen = new Set();
  for (const k of ['ArrowRight', 'ArrowRight', 'ArrowRight', 'ArrowDown', 'ArrowDown', 'ArrowLeft', 'ArrowLeft', 'ArrowLeft', 'ArrowUp', 'ArrowUp', 'ArrowUp', 'ArrowRight', 'ArrowRight']) { await key(k); seen.add(await last()); }
  const all = [...seen].join(' | ');
  check(!/hidden|under the bar|tiny|aria hidden|play r/i.test(all) && !/^tvfocus:(DIV|SPAN) *$/m.test([...seen].join('\n')), `no junk stops (${all})`);

  // Along a row with Right, in order.
  await p.evaluate(() => { window.__moves = []; });
  await p.keyboard.press('Escape').catch(() => {});
  await p.evaluate(() => window.scrollTo(0, 0));
  // put the highlight on row 1 item 0 by clicking it, then walk right
  await p.mouse.click(100, 150); await p.waitForTimeout(150);
  const row = [];
  for (let i = 0; i < 3; i++) { await key('ArrowRight'); row.push(await last()); }
  check(row.join(',').includes('Row 1 item 1') && row.join(',').includes('Row 1 item 2') && row.join(',').includes('Row 1 item 3'), `right walks the row in order (${row.join(' , ')})`);

  // Down from row 1 item 2 goes to row 2 item 2 (same column), not to a far corner.
  await p.mouse.click(40 + 2 * 240 + 100, 150); await p.waitForTimeout(150);
  await key('ArrowDown');
  check((await last()).includes('Row 2 item 2'), `down keeps the column (${await last()})`);
  await key('ArrowUp');
  check((await last()).includes('Row 1 item 2'), `up keeps the column (${await last()})`);

  // Back never returns to a sign-in page: after signing in (Google → YouTube Music), Back goes to KwartoTV.
  const ctx = await browser.newContext();
  await ctx.route('https://accounts.google.com/**', r => r.fulfill({ contentType: 'text/html', body: '<html><body><h1>Sign in</h1></body></html>' }));
  await ctx.route('https://music.youtube.com/**', r => r.fulfill({ contentType: 'text/html', body: '<html><body><a href="#x">Song</a><a href="#y">Song 2</a></body></html>' }));
  const s = await ctx.newPage();
  const arm = async () => { await s.evaluate(() => { window.__moves = []; window.chrome = { webview: { postMessage: m => window.__moves.push(String(m)) } }; }); await s.evaluate(script); };
  await s.goto('https://accounts.google.com/ServiceLogin?continue=https://music.youtube.com/');
  await s.goto('https://music.youtube.com/explore', { referer: 'https://accounts.google.com/ServiceLogin?continue=https://music.youtube.com/' });
  await arm();
  await s.keyboard.press('Escape'); await s.waitForTimeout(400);
  const msgs = await s.evaluate(() => window.__moves).catch(() => []);
  check(s.url().startsWith('https://music.youtube.com/') && msgs.includes('home'), `after signing in, Back goes to KwartoTV, not back to the sign-in (${s.url()}, ${msgs.filter(m => m === 'home').length} home)`);
  // Control: an ordinary deeper page still goes back one page.
  await s.goto('https://music.youtube.com/');
  await s.goto('https://music.youtube.com/explore', { referer: 'https://music.youtube.com/' });
  await arm();
  await s.keyboard.press('Escape'); await s.waitForTimeout(600);
  check(s.url() === 'https://music.youtube.com/', `on an ordinary page Back still goes back one page (${s.url()})`);

  // Netflix through its own player (NetflixControl): play/pause, seek, skip intro by itself, next episode, search.
  const ncs = fs.readFileSync(path.join(__dirname, '../../src/KwartoTV.App/Services/NetflixControl.cs'), 'utf8');
  const nm = ncs.match(/public const string Script = @"([\s\S]*?)";\s*\n/);
  check(!!nm, 'netflix: control script found');
  const nscript = nm[1].replace(/""/g, '"');
  const n = await browser.newPage();
  await n.route('https://www.netflix.com/**', r => r.fulfill({ contentType: 'text/html', body: '<html><body>search page</body></html>' }));
  await n.goto('https://www.netflix.com/watch/81234567');
  await n.evaluate(() => {
    window.__msgs = []; window.chrome = { webview: { postMessage: m => window.__msgs.push(String(m)) } };
    const calls = window.__calls = [];
    const p = { paused: false, t: 600000,
      isPaused() { return this.paused; }, play() { this.paused = false; calls.push('play'); }, pause() { this.paused = true; calls.push('pause'); },
      getCurrentTime() { return this.t; }, seek(ms) { this.t = ms; calls.push('seek ' + ms); } };
    window.netflix = { appContext: { state: { playerApp: { getAPI: () => ({ videoPlayer: {
      getAllPlayerSessionIds: () => ['motion-billboard-1', 'watch-81234567'], getVideoPlayerBySessionId: id => id.indexOf('watch') >= 0 ? p : null } }) } } } };
    document.body.innerHTML = '<button data-uia="player-skip-intro" onclick="window.__calls.push(\'skip intro clicked\'); this.remove()">Skip Intro</button>' +
      '<button data-uia="next-episode-seamless-button" onclick="window.__calls.push(\'next clicked\')">Next Episode</button>';
  });
  await n.evaluate(nscript);
  await n.waitForTimeout(1500);
  check(!(await n.evaluate(() => window.__calls)).includes('skip intro clicked'), 'netflix: the intro is never skipped by itself');
  const rs = await n.evaluate(() => window.__kwNetflix.skip());
  const r1 = await n.evaluate(() => window.__kwNetflix.toggle());
  const r2 = await n.evaluate(() => window.__kwNetflix.toggle());
  const r3 = await n.evaluate(() => window.__kwNetflix.seek(10));
  const r4 = await n.evaluate(() => window.__kwNetflix.seek(-30));
  await n.waitForTimeout(1500);
  const r5 = await n.evaluate(() => window.__kwNetflix.next());
  const calls = await n.evaluate(() => window.__calls);
  const nmsgs = await n.evaluate(() => window.__msgs);
  check(r1 === 'player' && r2 === 'player' && calls.filter(c => c === 'pause' || c === 'play').join() === 'pause,play', `netflix: play/pause goes to Netflix's own player (${calls.filter(c => c === 'pause' || c === 'play')})`);
  check(r3 === 'player' && calls.includes('seek 610000') && calls.includes('seek 580000'), `netflix: +10 s / -30 s seek the player, never the video element (${calls.filter(c => c.startsWith('seek'))})`);
  check(rs === 'clicked' && calls.includes('skip intro clicked'), 'netflix: Skip intro from the phone skips it');
  check(r5 === 'clicked' && calls.includes('next clicked'), 'netflix: next episode');
  // Audio & Subtitles, like the Netflix TV app's menu: from the player when it has the lists, else from Netflix's own menu.
  const t = await browser.newPage();
  await t.route('https://www.netflix.com/**', r => r.fulfill({ contentType: 'text/html', body: '<html><body></body></html>' }));
  await t.goto('https://www.netflix.com/watch/81234567');
  await t.evaluate(() => {
    const A = [{ trackId: 'a1', displayName: 'English [Original]' }, { trackId: 'a2', displayName: 'Japanese' }];
    const T = [{ trackId: 'n', isNoneTrack: true, displayName: 'Off' }, { trackId: 't1', displayName: 'English' }, { trackId: 't2', displayName: 'Filipino' }];
    const p = window.__tp = { a: A[0], t: T[0], getAudioTrackList: () => A, getTextTrackList: () => T, getAudioTrack() { return this.a; }, getTextTrack() { return this.t; },
      setAudioTrack(x) { this.a = x; }, setTextTrack(x) { this.t = x; } };
    window.netflix = { appContext: { state: { playerApp: { getAPI: () => ({ videoPlayer: { getAllPlayerSessionIds: () => ['watch-1'], getVideoPlayerBySessionId: () => p } }) } } } };
  });
  await t.evaluate(nscript);
  const tr1 = JSON.parse(await t.evaluate(() => window.__kwNetflix.tracks()));
  check(tr1.via === 'player' && tr1.audio.map(x => x.n).join() === 'English [Original],Japanese' && tr1.audio[0].on && tr1.subs.map(x => x.n).join() === 'Off,English,Filipino' && tr1.subs[0].on,
    `netflix: audio and subtitle lists from the player (${JSON.stringify(tr1)})`);
  await t.evaluate(() => { window.__kwNetflix.setTrack('s', 2); window.__kwNetflix.setTrack('a', 1); });
  const tr2 = JSON.parse(await t.evaluate(() => window.__kwNetflix.tracks()));
  check(tr2.subs[2].on && !tr2.subs[0].on && tr2.audio[1].on, `netflix: picking Filipino subtitles and Japanese audio sets them (${JSON.stringify(tr2)})`);
  const m = await browser.newPage();
  await m.route('https://www.netflix.com/**', r => r.fulfill({ contentType: 'text/html', body: '<html><body></body></html>' }));
  await m.goto('https://www.netflix.com/watch/81234567');
  await m.evaluate(() => {
    window.__picked = [];
    document.body.innerHTML = '<button data-uia="control-audio-subtitle" id="menu">AS</button><div id="panel"></div>';
    document.getElementById('menu').onclick = () => { document.getElementById('panel').innerHTML =
      '<li data-uia="audio-item-selected">English</li><li data-uia="audio-item">Spanish</li>' +
      '<li data-uia="subtitle-item-selected">Off</li><li data-uia="subtitle-item">English (CC)</li>';
      document.querySelectorAll('#panel li').forEach(li => li.onclick = () => window.__picked.push(li.textContent)); };
  });
  await m.evaluate(nscript);
  const tm = JSON.parse(await m.evaluate(() => window.__kwNetflix.tracks()));
  check(tm.via === 'menu' && tm.audio.map(x => x.n).join() === 'English,Spanish' && tm.audio[0].on && tm.subs.map(x => x.n).join() === 'Off,English (CC)' && tm.subs[0].on,
    `netflix: without the player lists, Netflix's own Audio & Subtitles menu is read (${JSON.stringify(tm)})`);
  await m.evaluate(() => window.__kwNetflix.setTrack('s', 1));
  check((await m.evaluate(() => window.__picked)).join() === 'English (CC)', 'netflix: picking from the menu clicks Netflix\'s own item');

  // Watching from the phone: what plays (title, time), seek to a time, speed, episodes, "Are you still watching?".
  const w = await browser.newPage();
  await w.route('https://www.netflix.com/**', r => {
    if (r.request().url().includes('/metadata?movieid=')) return r.fulfill({ contentType: 'application/json', body: JSON.stringify({ video: { title: 'Dark', currentEpisode: 80100173,
      seasons: [{ seq: 1, title: 'Season 1', episodes: [{ episodeId: 80100172, seq: 1, title: 'Secrets' }, { episodeId: 80100173, seq: 2, title: 'Lies' }] },
                { seq: 2, title: 'Season 2', episodes: [{ episodeId: 80200001, seq: 1, title: 'Beginnings and Endings' }] }] } }) });
    return r.fulfill({ contentType: 'text/html', body: '<html><body></body></html>' });
  });
  await w.goto('https://www.netflix.com/watch/80100173');
  await w.evaluate(() => {
    window.__msgs = []; window.chrome = { webview: { postMessage: m => window.__msgs.push(String(m)) } };
    const p = window.__wp = { t: 125000, paused: false, getCurrentTime() { return this.t; }, getDuration: () => 3000000, isPaused() { return this.paused; }, seek(ms) { this.t = ms; }, play() {}, pause() {} };
    window.netflix = { appContext: { state: { playerApp: { getAPI: () => ({ videoPlayer: { getAllPlayerSessionIds: () => ['watch-1'], getVideoPlayerBySessionId: () => p } }) } } } };
    document.body.innerHTML = '<video></video><div data-uia="video-title"><h4>Dark</h4><span>S1:E2</span><span>Lies</span></div>';
  });
  await w.evaluate(nscript);
  await w.waitForTimeout(1300);
  const nowOf = async () => { const m = (await w.evaluate(() => window.__msgs)).filter(x => x.startsWith('netflix:now:')).pop(); return m ? JSON.parse(m.slice(12)) : null; };
  let now1 = await nowOf();
  check(now1 && now1.t === 'Dark' && now1.e === 'S1:E2 · Lies' && Math.round(now1.pos) === 125 && now1.dur === 3000 && !now1.paused && !now1.still,
    `netflix: the phone is told what plays and where (${JSON.stringify(now1)})`);
  await w.evaluate(() => window.__kwNetflix.seekTo(1800));
  check(await w.evaluate(() => window.__wp.t) === 1800000, 'netflix: the phone\'s progress bar jumps to a time (30:00)');
  await w.evaluate(() => window.__kwNetflix.speed(1.5));
  check(await w.evaluate(() => document.querySelector('video').playbackRate) === 1.5, 'netflix: speed 1.5x from the phone');
  await w.evaluate(() => window.__kwNetflix.episodes());
  await w.waitForTimeout(800);
  const epm = (await w.evaluate(() => window.__msgs)).filter(x => x.startsWith('netflix:episodes:')).pop();
  const ep = epm ? JSON.parse(epm.slice(17)) : null;
  check(ep && ep.show === 'Dark' && ep.seasons.length === 2 && ep.seasons[0].eps.map(e => e.n).join() === '1. Secrets,2. Lies' && ep.seasons[0].eps[1].on && ep.seasons[1].eps[0].id === '80200001',
    `netflix: the episodes and seasons go to the phone (${epm})`);
  await w.evaluate(() => { document.body.insertAdjacentHTML('beforeend', '<button data-uia="interrupt-autoplay-continue" onclick="window.__cont = 1; this.remove()">Continue Watching</button>'); });
  await w.waitForTimeout(1300);
  check((await nowOf()).still, 'netflix: "Are you still watching?" shows on the phone');
  check(await w.evaluate(() => window.__kwNetflix.cont()) === 'clicked' && await w.evaluate(() => window.__cont) === 1, 'netflix: Continue watching from the phone');
  await w.evaluate(() => window.__kwNetflix.playEpisode('80200001'));
  await w.waitForTimeout(800);
  check(w.url() === 'https://www.netflix.com/watch/80200001', `netflix: an episode picked on the phone plays (${w.url()})`);

  await n.evaluate(() => window.__kwNetflix.search('stranger things'));
  await n.waitForTimeout(800);
  check(n.url() === 'https://www.netflix.com/search?q=stranger%20things', `netflix: search from the phone opens the results (${n.url()})`);
  const none = await (await browser.newPage()).evaluate(nscript + '; window.__kwNetflix.seek(10)');
  check(none === 'none', 'netflix: without a Netflix player nothing is seeked');

  await browser.close();
  console.log(fail ? `${fail} FAILED` : 'ALL PASSED');
  process.exit(fail ? 1 : 0);
})();
