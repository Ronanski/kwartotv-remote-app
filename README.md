# KwartoTV

A living-room TV for a Windows PC, controlled from your phone. **No channels or streams are included**: you add playlists (.m3u / .m3u8) you are allowed to use.

| Part | What it is |
|---|---|
| `KwartoTV.exe` | Windows installer for the TV app (fullscreen kiosk, .NET 8, libmpv player) |
| `KwartoTV-Remote.apk` | Android remote (Kotlin, built for a Samsung A56) |
| Browser remote | `http://<tv-address>:8765` in any phone or PC browser, no install |

Download the newest version from the **Releases** page (marked **Latest**), or from the artifacts of the newest workflow run.

### Version numbers

Every push to `main` that passes the tests becomes a release `vMAJOR.MINOR.PATCH` (for example `v1.1.0`, `v1.1.1`, ...):

- `MAJOR.MINOR` comes from the `VERSION` file. Change it (for example to `1.2` or `2.0`) for a bigger update; PATCH starts again at 0.
- `PATCH` goes up by one with each release.
- The build number (Android `versionCode`, the last part of the Windows file version) only ever goes up, and is what the in-app updaters compare.

## Using it

1. Run `KwartoTV.exe` on the TV PC. It sets up the Wi-Fi remote (URL reservation, firewall) for you.
2. In **Settings → Playlists → Add playlist**, paste an https link to a playlist or choose a file.
3. **Settings → Paired devices → Pair new device** shows a 6-digit code and a QR code.
4. On the phone: open **KwartoTV Remote**, scan the QR (or type the code). The TV is also found automatically on the Wi-Fi (mDNS).
5. Several phones can be paired and connected at the same time. Block or revoke any of them in Settings.

### TV app
- **Home**: Netflix-style rows (apps, favorites, channel groups). YouTube opens inside KwartoTV (Edge WebView2, TV layout: sign in with the code from your phone, and cast from the phone's YouTube app). Netflix also opens inside KwartoTV (the website; use the phone remote's **Mouse** pad), or switch to the Microsoft Store app in Settings. Add any https website as a tile (also opens inside the app). F1 returns Home.
- **Channels**: Home and Settings have **Add from iptv-org**: pick a country or category from the public lists at github.com/iptv-org/iptv (nothing is bundled). Or add any .m3u link or file.
- **Live TV**: cable-style. Number keys, ▲▼ channel up/down, Back/Tab = last channel, `F` = favorite, OK/◀ = mini guide.
- **Library**: videos and music from folders you add.
- **Settings**: playlists, website tiles, paired devices, library folders, start with Windows.
- Keyboard: arrows, Enter, Esc/Back, digits. The phone remote sends the same keys.

### Android remote
**Channels → Find channels**: the TV checks every channel of every playlist from its own internet connection (thousands are fine, up to 32 at a time, with progress on the phone). Only the channels that work are listed, by category. Tap one to put it on the TV (or take it off), tap ☆ to make it a favorite, or add a whole category at once. Favorites show under ★ Favorites on the phone and on the TV.

Pairing by PIN or QR · D-pad and touchpad · media and volume · channel pad · live keyboard · power (screen on/off, sleep, restart, shutdown) · several TVs.

## Repository layout
```
src/KwartoTV.Core     playlist parser, lineup, pairing, command router, remote server, mDNS, web remote page (cross-platform)
src/KwartoTV.App      Windows TV app (WPF) and Windows IHostActions
tests/KwartoTV.Tests  core tests: dotnet run --project tests/KwartoTV.Tests
android/              Android remote (Gradle), unit tests in app/src/test
installer/            Inno Setup script for KwartoTV.exe
tools/                libmpv download, Windows smoke test, DevHost + web remote e2e test, icon generator
docs/QA.md            QA report and checklist
```

## Build and test
```
dotnet run --project tests/KwartoTV.Tests                         # any OS
dotnet run --project tools/DevHost 8791                           # fake TV to try a remote against
node tools/qa/web-remote.js http://127.0.0.1:8791 <pin> <log> docs/qa
cd android && ./gradlew testDebugUnitTest assembleRelease        # Android
```
The Windows app, installer and smoke test build on GitHub Actions (`.github/workflows/build.yml`) on a Windows runner.

## Security notes
- Pairing needs a short-lived 6-digit code (5 tries). Each phone gets its own token, stored only as a hash on the TV.
- The remote speaks plain HTTP/WebSocket on your home network only (the installer opens port 8765 on all network profiles, because Windows often marks home Wi-Fi as Public; pairing still needs the on-screen code). Do not expose port 8765 to the internet.
- Blocking or revoking a phone disconnects it immediately.
- The Android APK is signed with a fixed sideload key committed in `android/keystore` so updates install over older builds. It is not a store key.
