// The on-screen keyboard on web pages (OskWindow.PageScript): it opens only for a box the user chose.
const { chromium } = require('playwright');
const fs = require('fs'), path = require('path');
let fail = 0;
function check(ok, name) { console.log((ok ? 'PASS ' : 'FAIL ') + name); if (!ok) fail++; }
(async () => {
  const cs = fs.readFileSync(path.join(__dirname, '../../src/KwartoTV.App/Ui/OskWindow.cs'), 'utf8');
  const script = cs.match(/public const string PageScript = @"([\s\S]*?)";\s*\n/)[1].replace(/""/g, '"');
  const PAGE = '<form onsubmit="event.preventDefault(); document.getElementById(\'pw\').focus()"><input id="em" type="email" autofocus><input id="pw" type="password"></form><button id="b">x</button>';
  const browser = await chromium.launch();
  const p = await browser.newPage();
  await p.addInitScript(() => { window.__msgs = []; window.chrome = { webview: { postMessage: m => window.__msgs.push(String(m)) } }; });
  await p.addInitScript(script);
  await p.route('https://kw.test/**', r => r.fulfill({ contentType: 'text/html', body: PAGE }));
  await p.goto('https://kw.test/');
  // onsubmit="event.preventDefault(); document.getElementById(\'pw\').focus()"><input id="em" type="email" autofocus><input id="pw" type="password"></form><button id="b">x</button>');
  await p.waitForTimeout(400);
  await p.evaluate(() => document.getElementById('em').focus());
  const asks = () => p.evaluate(() => window.__msgs.filter(m => m.startsWith('textfocus')));
  check((await asks()).length === 0, 'a box the page focuses by itself does not open the keyboard');
  // OK on the box (the TV script: Enter key down, then focus + click)
  await p.keyboard.down('Enter'); await p.keyboard.up('Enter');
  await p.evaluate(() => { const e = document.getElementById('em'); e.focus(); e.click(); });
  await p.waitForTimeout(100);
  check((await asks()).join() === 'textfocus:email', `OK on the box opens the keyboard (${await asks()})`);
  // the keyboard closes and the window gives the box its focus back: no second keyboard
  await p.evaluate(() => { document.getElementById('b').focus(); document.getElementById('em').focus(); });
  await p.waitForTimeout(100);
  check((await asks()).length === 1, `the box getting its focus back does not open the keyboard again (${(await asks()).length})`);
  // Go from the phone: the page moves on to the password box, which opens the keyboard (the next step)
  await p.evaluate(() => { window.__kw.insert('me@x.com'); window.__kw.submit(); });
  await p.waitForTimeout(100);
  check((await asks()).slice(-1)[0] === 'textfocus:password', `after Go, the next box (password) opens the keyboard (${await asks()})`);
  // a click on the box later opens it again
  await p.evaluate(() => document.getElementById('b').focus());
  await p.waitForTimeout(1700);
  await p.click('#em');
  await p.waitForTimeout(100);
  check((await asks()).slice(-1)[0] === 'textfocus:email', 'a click on a box opens the keyboard');
  await browser.close();
  console.log(fail ? `${fail} FAILED` : 'ALL PASSED');
  process.exit(fail ? 1 : 0);
})();
