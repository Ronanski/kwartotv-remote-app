// End-to-end check of the browser remote against the real server (tools/DevHost).
// Usage: node tools/qa/web-remote.js http://127.0.0.1:8791 <pin> <host-log-file> <out-dir>
const { chromium } = require('playwright');
const fs = require('fs');
const [base, pin, logFile, outDir] = process.argv.slice(2);
let failed = 0;
const check = (ok, name) => { console.log((ok ? 'PASS ' : 'FAIL ') + name); if (!ok) failed++; };
const hostLog = () => fs.readFileSync(logFile, 'utf8');
const wait = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 412, height: 915 }, deviceScaleFactor: 2, hasTouch: true, isMobile: true });
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(String(e)));
  page.on('console', m => { if (m.type() === 'error' && !m.text().includes('403')) errors.push(m.text()); });

  await page.goto(base);
  await page.screenshot({ path: `${outDir}/web-1-pair.png` });
  check(await page.locator('#pairView').isVisible(), 'pairing screen shown first');

  await page.fill('#pin', '000000'); await page.click('#pairBtn');
  await page.waitForFunction(() => document.querySelector('#pairMsg').textContent.length > 0);
  check((await page.textContent('#pairMsg')).includes('Wrong or expired'), 'wrong code shows a clear message');

  await page.fill('#pin', pin); await page.click('#pairBtn');
  await page.waitForSelector('#remoteView:not(.hidden)');
  await page.waitForFunction(() => document.querySelector('#status').textContent === 'connected');
  check(true, 'paired and connected');
  check((await page.textContent('#chName')) === 'Demo Channel 1', 'shows current channel from the TV');
  await page.screenshot({ path: `${outDir}/web-2-remote.png` });

  const before = hostLog().length;
  await page.click('[data-cmd="nav.ok"]'); await page.click('[data-cmd="nav.up"]'); await page.click('[data-cmd="nav.back"]'); await page.click('[data-cmd="nav.home"]');
  await wait(400);
  const log = hostLog().slice(before);
  check(['HOST nav.ok', 'HOST nav.up', 'HOST nav.back', 'HOST nav.home'].every(s => log.includes(s)), 'd-pad commands reach the host');

  await page.click('#modePad'); await page.screenshot({ path: `${outDir}/web-3-touchpad.png` });
  const pad = await page.locator('#padBox').boundingBox();
  const b2 = hostLog().length;
  await page.touchscreen.tap(pad.x + pad.width / 2, pad.y + pad.height / 2); await wait(300);
  check(hostLog().slice(b2).includes('HOST nav.ok'), 'touchpad tap = OK');
  // mouse mode: drag moves the pointer, tap clicks, scroll buttons scroll
  await page.click('#modeMouse'); await page.screenshot({ path: `${outDir}/web-3b-mouse.png` });
  const mp = await page.locator('#mousePad').boundingBox();
  const bm = hostLog().length;
  await page.touchscreen.tap(mp.x + 50, mp.y + 50); await wait(300);
  check(hostLog().slice(bm).includes('HOST mouse.click'), 'mouse pad tap = click');
  await page.mouse.move(mp.x + 60, mp.y + 60); await page.mouse.down(); await page.mouse.move(mp.x + 160, mp.y + 100, { steps: 6 }); await page.mouse.up(); await wait(300);
  check(/HOST mouse\.move -?\d+,-?\d+/.test(hostLog().slice(bm)), 'mouse pad drag moves the pointer');
  await page.click('#t-nav [data-cmd="mouse.scroll"][data-arg="2"]'); await wait(300);
  check(hostLog().slice(bm).includes('HOST mouse.scroll 2'), 'mouse scroll down');
  await page.click('#modeDpad');
  const bv = hostLog().length;
  await page.click('#t-nav [data-cmd="media.volup"]'); await page.click('#t-nav [data-cmd="media.voldown"]'); await wait(300);
  check(hostLog().slice(bv).includes('HOST media.volup') && hostLog().slice(bv).includes('HOST media.voldown'), 'volume buttons on the main Remote tab');

  await page.click('[data-tab="media"]'); await page.screenshot({ path: `${outDir}/web-4-media.png` });
  const b3 = hostLog().length;
  await page.click('[data-cmd="media.playpause"]'); await page.click('#t-media [data-cmd="media.volup"]'); await page.click('#t-media [data-cmd="media.mute"]');
  await page.click('[data-cmd="media.seek"][data-arg="-10"]'); await wait(400);
  const l3 = hostLog().slice(b3);
  check(l3.includes('HOST media.playpause') && l3.includes('HOST media.volup') && l3.includes('HOST media.mute') && l3.includes('HOST media.seek -10'), 'media, volume and seek commands');

  await page.click('[data-tab="chan"]'); await page.screenshot({ path: `${outDir}/web-5-channels.png` });
  await page.click('[data-cmd="channel.digit"][data-arg="1"]'); await page.click('[data-cmd="channel.digit"][data-arg="2"]'); await page.click('[data-cmd="channel.commit"]');
  await page.waitForFunction(() => document.querySelector('#chNum').textContent === '12');
  check((await page.textContent('#chName')) === 'Demo Channel 12', 'typing 1 2 + Go tunes channel 12 and the screen follows');
  await page.click('[data-cmd="channel.last"]');
  await page.waitForFunction(() => document.querySelector('#chNum').textContent === '1');
  check(true, 'Last returns to channel 1');
  await page.click('[data-cmd="channel.next"]');
  await page.waitForFunction(() => document.querySelector('#chNum').textContent === '2');
  check(true, 'CH up goes to channel 2');
  await page.click('[data-cmd="channel.digit"][data-arg="9"]'); await page.click('[data-cmd="channel.digit"][data-arg="9"]'); await page.click('[data-cmd="channel.digit"][data-arg="9"]'); await page.click('[data-cmd="channel.commit"]'); await wait(300);
  check((await page.textContent('#chNum')) === '2', 'a channel that does not exist leaves the TV on channel 2');

  await page.click('[data-tab="kb"]');
  await page.click('#kbText'); const b4 = hostLog().length;
  await page.keyboard.type('hi'); await page.keyboard.press('Backspace'); await page.keyboard.press('Enter'); await wait(400);
  const l4 = hostLog().slice(b4);
  check(l4.includes('HOST key.text h') && l4.includes('HOST key.text i') && l4.includes('HOST key.backspace') && l4.includes('HOST key.enter'), 'keyboard typing, backspace and enter');
  await page.screenshot({ path: `${outDir}/web-6-keyboard.png` });
  const bl = hostLog().length;
  await page.fill('#linkText', 'https://raw.githubusercontent.com/IPTV-Philippines/IPTV-Philippines/main/ph.m3u');
  await page.click('#sendLink'); await wait(400);
  check(hostLog().slice(bl).includes('HOST playlist.add https://raw.githubusercontent.com/IPTV-Philippines/IPTV-Philippines/main/ph.m3u'), 'a long playlist link is sent to the TV');

  await page.click('[data-tab="power"]'); await page.screenshot({ path: `${outDir}/web-7-power.png` });
  const b5 = hostLog().length;
  await page.click('[data-cmd="power.shutdown"]'); await wait(300);
  check(await page.locator('#dlg').evaluate(d => d.open), 'shutdown asks for confirmation');
  await page.screenshot({ path: `${outDir}/web-8-confirm.png` });
  await page.click('#dlgNo'); await wait(300);
  check(!hostLog().slice(b5).includes('power.shutdown'), 'cancel does not shut down');
  await page.click('[data-cmd="power.screenoff"]'); await wait(300);
  check(hostLog().slice(b5).includes('HOST power.screenoff'), 'screen off needs no confirmation');

  // reload keeps the pairing
  await page.reload(); await page.waitForFunction(() => document.querySelector('#status').textContent === 'connected');
  check(await page.locator('#remoteView').isVisible(), 'stays paired after reload');
  check(errors.length === 0, 'no console errors ' + errors.join(' | '));
  await browser.close();
  console.log(failed ? `${failed} FAILED` : 'ALL PASSED');
  process.exit(failed ? 1 : 0);
})();
