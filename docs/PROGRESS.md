# Progress

## v1.1.22 (build 100), released 2 Nov 2026

The phone design the owner approved is in `docs/design/phone-remote-design.html`. It is also published as an artifact: https://claude.ai/artifact/ESR2LSzFPU1k6W5d3x763q

### Phone
- One font (Figtree, `res/font`, OFL licence in `android/FONT-LICENSE-Figtree.txt`).
- Six text sizes (`ui/Theme.kt` object `T`).
- Equal sizes everywhere: `KEY` 56, `PILL` 44 and `CTA` 52.
- Shared pieces are in `ui/Kit.kt`: RoundKey, Pill, Cta, DPad, VolumeBar, WorkspaceBar, KSheet (bottom sheet) and OptionRow.
- Remote tab (`ui/RemoteTab.kt`):
  - Five app tiles, the D-pad in the middle, nothing beside the D-pad.
  - The key row: Back, Home, Play/Pause, Voice and Type.
  - The volume bar.
  - The swipe pad is chosen from the ⋮ menu.
- Netflix workspace (`ui/NetflixWorkspace.kt`):
  - It shows when the TV is on Netflix, and ‹ goes back to the usual remote.
  - Three states: watching, title open (Resume → `netflix.play`), and picking.
  - Sheets for Audio & Subs, Episodes and Speed.
- Lock tab (`ui/LockTab.kt`):
  - A live preview that uses the TV's own photos (`lock.photo`).
  - Every lock screen option (`lock.set key=value`).
  - The photo folder (`lock.folders`).
  - The sleep timer.
- TypeOnPhone is a bottom sheet that mirrors what the TV's text box shows. Voice "Search where?" is also a sheet.

### TV
- `LockOptions` (Core) checks every option. `LockScreenWindow.ApplySettings` applies them live.
  - Clock: classic, big or corner.
  - Color and dim.
  - Date, song and verse.
  - Background: photos, black or midnight.
- Netflix rows (`TvWebScript` `nfStep`):
  - Right/Left slide at a title cut off by the screen, or at the last title shown.
  - After the slide, the wanted title is found again by its id.
  - Test: `tools/qa/netflix-row-real.js`, a lean row variant that fails on the old script.
- Netflix title pop-up:
  - The highlight starts on Play/Resume, and Up from the episodes returns to it.
  - The page reports `modal` {n, p, id} to the phone.
  - `__kwNetflix.play()` presses only the pop-up's own button.
  - Test: `tools/qa/netflix-modal.js`.

### Tests at release
- Core: 394 passed.
- Phone UI (Robolectric): 9/9 passed, about 140 buttons pressed.
- Chromium QA: all passed.
- Windows smoke: 173 PASS, 0 FAIL, 16 WARN.

### Not testable here
- Real Netflix: no account, and netflix.com is blocked. Only faithful mocks were tested.
- Showing slideshow photos on the TV: the CI machine has no pictures.

### Notes for the next session
- Robolectric UI tests:
  - A screen change needs two `settle()` calls.
  - Press pop-up items (menus, dialogs) with `OnClick`.
  - On the long Lock tab, `performScrollTo` can stall, so press there with the semantics click.
- Never put `"` inside the C# verbatim `@"..."` scripts, not even in comments: it broke the Windows build once. Check by running the Python string-end check from the transcript.
