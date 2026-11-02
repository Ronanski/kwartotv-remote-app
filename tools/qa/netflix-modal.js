// A title's details on Netflix (its pop-up over the rows, as Netflix builds it: a clickable box around the picture and
// its buttons, a shade over the picture, a long episode list that scrolls inside the pop-up). The TV highlight must
// start on Resume, come back to it with Up from the episodes, and the phone's Resume (netflix.play) must press it;
// the page reports the title so the phone can show it.
const fs = require('fs'); const path = require('path'); const { chromium } = require('playwright');
const grab = (f) => fs.readFileSync(path.join(__dirname, '../../src/KwartoTV.App/Services/' + f), 'utf8')
  .match(/public const string Script = @"([\s\S]*?)";\s*\n/)[1].replace(/""/g, '"');
const tv = grab('TvWebScript.cs'), nf = grab('NetflixControl.cs');
const eps = Array.from({ length: 10 }, (_, i) => `<div class="titleCardList--container episode-item" tabindex="0" role="button" aria-label="Episode ${i + 1}" style="height:120px;margin:8px 0;background:#222">Episode ${i + 1}</div>`).join('');
const page = `<!doctype html><html><body style="margin:0;background:#141414;color:#fff;font:14px sans-serif">
<div class="lolomoRow" style="padding-top:200px"><div class="rowContainer" id="row-0"><div class="slider-item slider-item-0" style="width:240px">
<div class="title-card-container"><a href="/watch/81234" class="slider-refocus" aria-label="Frieren" tabindex="0" onclick="event.preventDefault(); openModal()"><div style="width:230px;height:130px;background:#333">Frieren</div></a></div></div></div></div>
<script>
function openModal() {
  history.pushState({}, '', '/browse?jbv=81234');
  document.body.insertAdjacentHTML('beforeend', '<div class="previewModal--wrapper" style="position:fixed;inset:0;overflow-y:auto;background:rgba(0,0,0,.7)">' +
   '<div class="previewModal--container detail-modal" role="dialog" aria-modal="true" style="width:850px;margin:30px auto;background:#181818">' +
   '<div class="previewModal--player_container" tabindex="0" role="button" style="position:relative;height:480px;background:#456">' +
   '<img class="previewModal--player-titleTreatment-logo" alt="Frieren: Beyond Journey&#39;s End" style="width:300px;height:80px;position:absolute;left:40px;top:200px">' +
   '<div class="shade" style="position:absolute;inset:0;background:linear-gradient(transparent,rgba(0,0,0,.4))"></div>' +
   '<div class="buttonControls--container" style="position:absolute;left:40px;top:320px;display:flex;gap:10px">' +
   '<button data-uia="play-button" class="color-primary hasLabel" onclick="window.__clicked.push(\\'Resume\\')" style="width:140px;height:50px"><span>Resume</span></button>' +
   '<button aria-label="Add to My List" style="width:50px;height:50px">+</button><button aria-label="Rate" style="width:50px;height:50px">R</button></div></div>' +
   '<div class="episodeSelector" style="padding:20px 40px">${eps}</div></div></div>');
}
</script></body></html>`;
let fail = 0; const check = (ok, n) => { console.log((ok ? 'PASS ' : 'FAIL ') + n); if (!ok) fail++; };
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1280, height: 720 } });
  await p.route('https://www.netflix.com/**', r => r.fulfill({ contentType: 'text/html', body: page }));
  await p.addInitScript(() => { window.__m = []; window.__clicked = []; window.chrome = { webview: { postMessage: m => window.__m.push(String(m)) } }; });
  await p.goto('https://www.netflix.com/browse');
  await p.evaluate(nf); await p.evaluate(tv);
  await p.waitForTimeout(700);
  const last = () => p.evaluate(() => (window.__m.filter(m => m.startsWith('tvfocus:')).slice(-1)[0] || '').replace(/^tvfocus:\S+ /, '').trim());
  check(/Frieren/.test(await last()), `the title is highlighted on the rows (${await last()})`);
  await p.keyboard.press('Enter'); await p.waitForTimeout(1200);
  check(await last() === 'Resume', `the title's pop-up opens with the highlight on Resume (${await last()})`);
  for (let i = 0; i < 6; i++) { await p.keyboard.press('ArrowDown'); await p.waitForTimeout(350); }
  const down = await last();
  check(/Episode/.test(down), `Down goes into the episodes (${down})`);
  let reached = false;
  for (let i = 0; i < 14 && !reached; i++) { await p.keyboard.press('ArrowUp'); await p.waitForTimeout(350); reached = (await last()) === 'Resume'; }
  check(reached, `Up from the episodes comes back to Resume (${await last()})`);
  await p.waitForTimeout(1200);
  const now = await p.evaluate(() => window.__m.filter(m => m.startsWith('netflix:now:')).map(m => JSON.parse(m.slice(12))).filter(o => o.modal).slice(-1)[0]);
  check(now && now.modal.n === "Frieren: Beyond Journey's End" && now.modal.p === 'Resume' && now.modal.id === '81234',
    `the page reports the open title for the phone (${JSON.stringify(now && now.modal)})`);
  const r = await p.evaluate(() => window.__kwNetflix.play());
  check(r === 'clicked' && (await p.evaluate(() => window.__clicked)).includes('Resume'), `the phone's Resume presses the title's Resume (${r})`);
  await b.close(); console.log(fail ? `${fail} FAILED` : 'ALL PASSED'); process.exit(fail ? 1 : 0);
})();
