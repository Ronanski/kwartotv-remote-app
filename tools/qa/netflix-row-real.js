// Netflix's real row (as its pages build it, and as the NetflixController extension describes it): titles in view are
// ".slider-item-N" (N = place in view; a half-shown title at each edge), titles out of view have no number, and each
// slide REBUILDS the titles (new elements, numbered again). Right must walk every title in order, Left all the way back.
const fs = require('fs'); const path = require('path'); const { chromium } = require('playwright');
const cs = fs.readFileSync(path.join(__dirname, '../../src/KwartoTV.App/Services/TvWebScript.cs'), 'utf8');
const script = cs.match(/public const string Script = @"([\s\S]*?)";\s*\n/)[1].replace(/""/g, '"');
const page = `<!doctype html><html><body style="margin:0;background:#141414;color:#fff;font:14px sans-serif;width:1280px;overflow-x:hidden">
<div class="lolomo"><div class="lolomoRow" style="position:relative;top:120px"><h2 class="rowHeader">Trending Now</h2>
<div class="rowContainer" id="row-0"><div class="slider" style="position:relative;width:1280px;height:150px;overflow:hidden">
<span class="handle handlePrev" style="position:absolute;left:0;top:0;width:60px;height:150px;z-index:3"></span>
<div class="sliderMask"><div class="sliderContent" id="sc" style="position:relative;height:150px"></div></div>
<span class="handle handleNext active" style="position:absolute;right:0;top:0;width:60px;height:150px;z-index:3"></span>
</div></div></div></div>
<script>
var N = 20, st = 0, W = 232;
function card(i, cls, x) { return '<div class="slider-item ' + cls + '" style="position:absolute;top:0;left:' + x + 'px;width:' + (W - 8) + 'px;height:140px">' +
  '<div class="title-card-container"><div class="title-card"><a href="/watch/' + i + '" aria-label="Title ' + i + '" class="slider-refocus" tabindex="0">' +
  '<div class="boxart-container" style="width:' + (W - 8) + 'px;height:130px;background:#333">Title ' + i + '</div></a></div></div></div>'; }
function render() {
  var h = '', x0 = 60 - (st > 0 ? W / 2 : 0) - (st > 0 ? 0 : 0), shifted = st > 0;
  // place: shifted rows show a half title on the left (number 0); the first page starts at number 0 fully shown
  var first = shifted ? st - 1 : 0, n = 0;
  for (var i = Math.max(0, first - 6); i < first; i++) h += card(i, 'slider-item-', -2000);
  for (var i = first; i < Math.min(N, first + 6 + (shifted ? 1 : 0)); i++) { h += card(i, 'slider-item-' + n, (shifted ? 60 - W / 2 : 60) + n * W); n++; }
  for (var j = i; j < Math.min(N, i + 6); j++) h += card(j, 'slider-item-', 4000);
  document.getElementById('sc').innerHTML = h;
}
render();
document.querySelector('.handleNext').addEventListener('click', function () { setTimeout(function () { st = st === 0 ? 5 : Math.min(N - 5, st + 5); render(); }, 300); });
document.querySelector('.handlePrev').addEventListener('click', function () { setTimeout(function () { st = Math.max(0, st - 5); render(); }, 300); });
</script></body></html>`;
let fail = 0; const check = (ok, n) => { console.log((ok ? 'PASS ' : 'FAIL ') + n); if (!ok) fail++; };
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1280, height: 720 } });
  await p.route('https://www.netflix.com/**', r => r.fulfill({ contentType: 'text/html', body: page }));
  await p.addInitScript(() => { window.__m = []; window.chrome = { webview: { postMessage: m => window.__m.push(String(m)) } }; });
  await p.goto('https://www.netflix.com/browse');
  await p.evaluate(script);
  await p.waitForTimeout(600);
  const last = () => p.evaluate(() => (window.__m.filter(m => m.startsWith('tvfocus:')).slice(-1)[0] || '').replace(/^tvfocus:\S+ /, '').trim());
  await p.keyboard.press('ArrowDown'); await p.waitForTimeout(300);
  const seen = [await last()];
  for (let i = 0; i < 19; i++) { await p.keyboard.press('ArrowRight'); await p.waitForTimeout(1300); seen.push(await last()); }
  console.log('right: ' + seen.join(' | '));
  check(seen.every((t, i) => t === 'Title ' + i), 'Right walks every title in order, sliding the row (rebuilt by the page) at the edge');
  const back = [];
  for (let i = 0; i < 19; i++) { await p.keyboard.press('ArrowLeft'); await p.waitForTimeout(1300); back.push(await last()); }
  console.log('left: ' + back.join(' | '));
  check(back.every((t, i) => t === 'Title ' + (18 - i)), 'Left walks back one title at a time to the first');
  await b.close(); console.log(fail ? `${fail} FAILED` : 'ALL PASSED'); process.exit(fail ? 1 : 0);
})();
