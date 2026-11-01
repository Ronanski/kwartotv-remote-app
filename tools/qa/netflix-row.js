// Netflix-like row: 10 titles, 5 in view, a "next" handle that slides the row (as on netflix.com). Right must walk
// every title in order (sliding the row when the highlight reaches its end), Left must walk back.
const fs = require('fs'); const path = require('path'); const { chromium } = require('playwright');
const cs = fs.readFileSync(path.join(__dirname, '../../src/KwartoTV.App/Services/TvWebScript.cs'), 'utf8');
const script = cs.match(/public const string Script = @"([\s\S]*?)";\s*\n/)[1].replace(/""/g, '"');
const items = [...Array(10).keys()].map(i => `<div class="slider-item"><a class="title-card" href="#t${i}" aria-label="Title ${i}"><img alt="" src="data:image/gif;base64,R0lGODlhAQABAAAAACw=" style="width:220px;height:124px"><span>Title ${i}</span></a></div>`).join('');
const page = `<!doctype html><html><body style="margin:0;background:#141414;color:#fff;font:14px sans-serif">
<div class="lolomoRow" style="position:relative;top:120px"><h2>Trending</h2>
<div class="slider" style="position:relative;overflow:hidden;width:1280px;height:150px">
<span class="handle handlePrev" style="position:absolute;left:0;top:0;width:50px;height:150px;z-index:3;background:rgba(0,0,0,.5)" onclick="slide(-1)"></span>
<div class="sliderContent" style="display:flex;gap:10px;padding-left:60px;transition:none">${items}</div>
<span class="handle handleNext" style="position:absolute;right:0;top:0;width:50px;height:150px;z-index:3;background:rgba(0,0,0,.5)" onclick="slide(1)"></span>
</div></div>
<script>var page=0;function slide(d){page=Math.max(0,Math.min(1,page+d));document.querySelector('.sliderContent').style.transform='translateX('+(-page*1150)+'px)';}</script>
</body></html>`;
let fail = 0; const check = (ok, n) => { console.log((ok ? 'PASS ' : 'FAIL ') + n); if (!ok) fail++; };
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1280, height: 720 } });
  await p.setContent(page);
  await p.evaluate(() => { window.__m = []; window.chrome = { webview: { postMessage: m => window.__m.push(String(m)) } }; });
  await p.evaluate(script);
  const last = () => p.evaluate(() => (window.__m.filter(m => m.startsWith('tvfocus:')).slice(-1)[0] || '').replace(/^tvfocus:\S+ /, '').trim());
  await p.keyboard.press('ArrowDown'); await p.waitForTimeout(150);
  const seen = [await last()];
  for (let i = 0; i < 9; i++) { await p.keyboard.press('ArrowRight'); await p.waitForTimeout(900); seen.push(await last()); }
  console.log('right: ' + seen.join(' | '));
  check(seen[9] && /Title 9/.test(seen[9]) && new Set(seen).size >= 9, 'Right walks the whole row, sliding it at the end');
  const back = [];
  for (let i = 0; i < 9; i++) { await p.keyboard.press('ArrowLeft'); await p.waitForTimeout(900); back.push(await last()); }
  console.log('left: ' + back.join(' | '));
  check(back.some(t => /Title 0/.test(t)), 'Left walks back to the start');
  await b.close(); console.log(fail ? `${fail} FAILED` : 'ALL PASSED'); process.exit(fail ? 1 : 0);
})();
