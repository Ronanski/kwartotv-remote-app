# KwartoTV QA report

What was actually run, where, and what could not be checked from the build environment.

## Automated, run on every push (GitHub Actions)

| Check | Where | Result |
|---|---|---|
| Core tests: playlist parser, lineup, pairing, router, server, launch plans, settings, library, mDNS packets, key map, web remote | Linux | 125 pass |
| Android unit tests (pair link, host parsing, protocol JSON) | Linux | pass |
| Android lint + signed release APK + `aapt2`/`apksigner` verification | Linux | pass |
| Windows self-contained publish (WPF app, libmpv bundled) | Windows Server 2025 | pass |
| Inno Setup installer `KwartoTV.exe` | Windows | pass |
| **Silent install as a user**, URL reservation for port 8765, firewall rules created | Windows | pass |
| **Start the installed app on a real desktop**: remote `/info`, browser remote page, wrong PIN gives 403, mDNS socket open on UDP 5353 | Windows | pass |
| Navigation by keyboard: Home, Settings, Library, number entry to Live TV, mini guide, back (screenshots below) | Windows | pass |
| libmpv starts inside the player window and shows "Can't play this channel" for an unreachable stream (placeholder URLs) | Windows | pass |
| No errors in the app log during the run | Windows | pass |
| Uninstall removes the URL reservation | Windows | pass |

## Browser remote end-to-end (real Chromium, 412x915 phone viewport, real server)
`node tools/qa/web-remote.js` (16 checks): wrong PIN message, pairing, current channel shown, d-pad, touchpad tap, media/volume/seek, number pad + Go (TV follows), Last, CH up, nonexistent channel, keyboard typing/backspace/enter, shutdown confirmation and cancel, stays paired after reload, no console errors. All pass. Screenshots: `docs/qa/web-*.png`.

## Found and fixed during QA
- Bringing a window to the front shrank maximized windows back to normal size (player and Home were not fullscreen). Fixed.
- An Alt-key trick used to take focus opened the system menu on the TV. Replaced with thread-input attach.
- Guide list overlapped the error message. Fixed.
- Windows build lacked `System.IO`/`System.Net.Http` implicit usings. Fixed.

## UI/UX review
- Everything on the TV is reachable with arrow keys, Enter and Esc. The focused item is outlined and lifted, so it is readable from a sofa. Text on the TV is 16 pt or larger and mostly 20 pt+.
- The phone remote fires on press, repeats while held (arrows, volume), gives a small vibration, has labelled touch targets of at least 56 dp, and asks for confirmation before sleep, restart and shutdown.
- Errors say what to do next ("Wrong or expired code. Start pairing again on the TV.").
- Colour contrast: dark text on the orange accent and light text on near-black, both above 4.5:1.

## NOT verified here (needs your hardware) — honest list
1. **Netflix app id and YouTube TV arrow keys on your laptop** (item 6). I could not run on your laptop. The app looks the Netflix id up at run time (`Get-StartApps`), and falls back to netflix.com in a fullscreen browser window if the Store app is missing. To confirm on your PC: run `tools/verify-windows.ps1`.
2. **Android app on a real Samsung A56**: it builds, is signed and passes unit tests/lint, but I could not run it on a phone or emulator. Things to look at first: QR scanner permission prompt, mDNS discovery of the TV (needs Wi-Fi with client isolation off), touchpad feel.
3. **Playing real channels**: no channel sources are bundled, so playback of real streams was not tested, only that libmpv starts and reports failures.
4. Multiple monitors, HDR, and sleep/wake of your specific PC. "Wake" from a phone is not possible (a sleeping PC has no network listener): use the power button or wake-on-LAN separately.
5. Windows SmartScreen may warn about the unsigned installer. Click "More info" then "Run anyway". Code signing needs a paid certificate.
