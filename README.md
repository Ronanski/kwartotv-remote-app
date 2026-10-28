# KwartoTV

A living-room TV for a Windows PC, controlled from your phone. **No channels or streams are included**: you add playlists (.m3u / .m3u8) you are allowed to use.

| Part | What it is |
|---|---|
| `KwartoTV.exe` | Windows installer for the TV app (fullscreen kiosk, .NET 8, libmpv player) |
| `KwartoTV-Remote.apk` | Android remote (Kotlin, built for a Samsung A56) |
| Browser remote | `http://<tv-address>:8765` in any phone or PC browser, no install |

Download the latest build from the **Releases** page (`latest`), or from the artifacts of the newest workflow run.

## Using it

1. Run `KwartoTV.exe` on the TV PC. It sets up the Wi-Fi remote (URL reservation, firewall) for you.
2. In **Settings → Playlists → Add playlist**, paste an https link to a playlist or choose a file.
3. **Settings → Paired devices → Pair new device** shows a 6-digit code and a QR code.
4. On the phone: open **KwartoTV Remote**, scan the QR (or type the code). The TV is also found automatically on the Wi-Fi (mDNS).
5. Several phones can be paired and connected at the same time. Block or revoke any of them in Settings.

### TV app
- **Home**: Netflix-style rows (apps, favorites, channel groups). YouTube and Netflix tiles; add any https website as a tile.
- **Live TV**: cable-style. Number keys, ▲▼ channel up/down, Back/Tab = last channel, `F` = favorite, OK/◀ = mini guide.
- **Library**: videos and music from folders you add.
- **Settings**: playlists, website tiles, paired devices, library folders, start with Windows.
- Keyboard: arrows, Enter, Esc/Back, digits. The phone remote sends the same keys.

### Android remote
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
