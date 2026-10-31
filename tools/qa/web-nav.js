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

  await browser.close();
  console.log(fail ? `${fail} FAILED` : 'ALL PASSED');
  process.exit(fail ? 1 : 0);
})();
