# KwartoTV core (Live TV engine + remote link)

What is here (cross-platform, no external packages):

- `M3uParser` - reads M3U/M3U8 playlists you supply (http/https only, dedupes, keeps logos and groups).
- `PlaylistLoader` - downloads a playlist link added in Settings (https only, size-capped) and parses it.
- `ChannelLineup` - cable-style numbered channels: up/down with wrap, last channel, number entry with timeout, favorites, groups, save/restore.
- `PairingService` - 6-digit PIN pairing, one token per device, many devices at once, block/revoke, tokens stored hashed.
- `CommandRouter` - whitelist + argument checks for remote commands; channel commands handled here, the rest go to `IHostActions` (implemented by the Windows shell).
- `AppLaunchPlans` - launch commands for the YouTube tile (fullscreen Edge/Chrome app window, own profile for the Premium sign-in) and the Netflix tile (Microsoft Store app). Any `https` website can be added as a tile.
- `RemoteServer` - `POST /pair` and `GET /ws` over the local network.

No channel sources are bundled. Add playlists/links you are entitled to use.

Run the tests: `dotnet run --project tests/KwartoTV.Tests`

Not included yet (needs Windows / Android toolchains): TV shell UI (Home, Player with libmpv),
Windows `IHostActions` (key/media/volume/power), mDNS discovery, Android remote app.

To verify on the Windows PC: the Netflix app id (`Get-StartApps | Where Name -like '*Netflix*'`) and that `youtube.com/tv` navigates well with arrow keys in the chosen browser.
