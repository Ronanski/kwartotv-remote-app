# Starts the installed KwartoTV on a Windows desktop, checks the remote server, and takes screenshots.
# Screenshots are saved as JPEG files and also printed as base64 between ##SHOT markers.
param([Parameter(Mandatory)][string]$Exe, [string]$OutDir = "qa-shots")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$fail = 0
function Check($ok, $name) { if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:fail++ } }
# Not yet required (still being made to work on CI): reported, but does not stop the release.
function Warn($ok, $name) { if ($ok) { Write-Host "PASS $name" } else { Write-Host "WARN $name" } }

# Seeds a local playlist with two public HLS test streams (made for testing players) and one dead address,
# plus a website tile. No channel lists from anywhere else are bundled.
$data = Join-Path $env:APPDATA "KwartoTV"
New-Item -ItemType Directory -Force -Path $data | Out-Null
$m3u = Join-Path $data "demo.m3u"
$lines = @(
  "#EXTM3U",
  '#EXTINF:-1 group-title="Test",Test Stream 1',
  "https://devstreaming-cdn.apple.com/videos/streaming/examples/bipbop_4x3/bipbop_4x3_variant.m3u8",
  '#EXTINF:-1 group-title="Test",Test Stream 2',
  "https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8",
  '#EXTINF:-1 group-title="Test",Dead Stream',
  "https://example.invalid/dead.m3u8")
Set-Content -Path $m3u -Value $lines -Encoding UTF8
# plus a YouTube live channel, and the user's Philippines list shown as its own space
$settings = @{ playlists = @(
    @{ id = "demo"; name = "Test streams"; source = $m3u },
    @{ id = "yt"; name = "YouTube live test"; source = "https://www.youtube.com/@NASA/live"; isStream = $true },
    @{ id = "phspace"; name = "PH TV"; source = "https://raw.githubusercontent.com/IPTV-Philippines/IPTV-Philippines/main/ph.m3u"; showAsSpace = $true }); webTiles = @(@{ id = "ex"; name = "Example"; url = "https://example.com" }); libraryFolders = @(); port = 8765; screensaverMinutes = 0 } | ConvertTo-Json -Depth 5
Set-Content -Path (Join-Path $data "settings.json") -Value $settings -Encoding UTF8
Remove-Item (Join-Path $data "log.txt") -ErrorAction SilentlyContinue

function Shot($name) {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
    $w = 800; $h = [int]($b.Height * $w / $b.Width)
    $small = New-Object System.Drawing.Bitmap $bmp, $w, $h
    $path = Join-Path $OutDir "$name.jpg"
    $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq "image/jpeg"
    $ep = New-Object System.Drawing.Imaging.EncoderParameters 1
    $ep.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), 55L
    $small.Save($path, $codec, $ep)
    Write-Host "##SHOT $name"
    Write-Host ([Convert]::ToBase64String([IO.File]::ReadAllBytes($path)))
    Write-Host "##ENDSHOT"
    $g.Dispose(); $bmp.Dispose(); $small.Dispose()
}
function Keys($k) { [System.Windows.Forms.SendKeys]::SendWait($k); Start-Sleep -Milliseconds 900 }

# Take port 8765 first, the way Hyper-V/WSL or another program can on a real PC. KwartoTV must move to the next port.
$blocker = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, 8765)
$blocker.Start()

# Start KwartoTV with a normal-user (non-admin) token, like on a real PC. CI runs as Administrator,
# which once hid a bug where the remote server only worked for admins.
& runas.exe /trustlevel:0x20000 "`"$Exe`""
$p = $null
for ($i = 0; $i -lt 20 -and -not $p; $i++) { Start-Sleep -Seconds 1; $p = Get-Process KwartoTV -ErrorAction SilentlyContinue | Select-Object -First 1 }
Start-Sleep -Seconds 8
Check ($null -ne $p -and -not $p.HasExited) "app is running as a normal (non-admin) user"
$task = Get-ScheduledTask -TaskName "KwartoTV Update" -ErrorAction SilentlyContinue
# Per-user installs update with no permission prompt, so the old admin update task is not needed (and not made).
Check ($null -eq $task) "no admin update task: updates of a per-user install need no permission"
if (-not $p) { throw "KwartoTV did not start" }

$base = $null
foreach ($port in 8765, 8766, 8767, 8768, 8769, 18765) {
    try { $i = Invoke-RestMethod "http://127.0.0.1:$port/info" -TimeoutSec 2; if ($i.app -eq "KwartoTV") { $base = "http://127.0.0.1:$port"; break } } catch { }
}
Check ($base -and $base -ne "http://127.0.0.1:8765") "port 8765 was taken, KwartoTV moved to $base"
if (-not $base) { $base = "http://127.0.0.1:8766" }
try { $info = Invoke-RestMethod "$base/info"; Check ($info.app -eq "KwartoTV") "remote /info answers ($($info.name))" } catch { Check $false "remote /info answers: $_" }
try { $page = Invoke-WebRequest "$base/" -UseBasicParsing; Check ($page.Content -match "KwartoTV Remote") "browser remote page is served" } catch { Check $false "browser remote page: $_" }
try { Invoke-WebRequest "$base/pair" -Method Post -Body '{"pin":"000000","name":"x"}' -ContentType "application/json" -UseBasicParsing | Out-Null; Check $false "wrong pin is refused" }
catch { Check ($_.Exception.Response.StatusCode.value__ -eq 403) "wrong pin is refused (403)" }
# The endpoint list can lag behind the app by a few seconds, so look several times.
$mdns = $null
for ($try = 0; $try -lt 8 -and -not $mdns; $try++) {
    Start-Sleep -Seconds 2
    $eps = @(Get-NetUDPEndpoint -LocalPort 5353 -ErrorAction SilentlyContinue)
    $mdns = $eps | Where-Object { (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName -eq "KwartoTV" }
}
$eps | ForEach-Object { Write-Host ("udp 5353 owner: {0} ({1})" -f $_.OwningProcess, (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName) }
$logText = if (Test-Path (Join-Path $data "log.txt")) { Get-Content (Join-Path $data "log.txt") -Raw } else { "" }
Check (($null -ne $mdns) -or ($logText -match "mdns: advertising")) "mDNS responder started (socket bound or logged as advertising)"

Shot "00-first-run-pairing"
Keys "{ESC}"
Shot "01-home"
# Only the d-pad (arrow keys + OK), like the phone remote: up to the menu, right to Settings, OK.
Keys "{UP}"
Keys "{RIGHT}{RIGHT}{RIGHT}"
Keys "{ENTER}"
Start-Sleep -Seconds 1
Shot "01b-settings-by-dpad"
Check ((Get-Content (Join-Path $data "log.txt") -Raw) -match "page: Settings") "d-pad: up, right, OK reaches Settings"
# Settings must be scrollable to the end with the d-pad
Keys "{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}"
Shot "01c-settings-bottom-by-dpad"
Keys "{ESC}"
# down from the top of Home (apps, then the scan row) into the channel rows, OK plays the channel
Keys "{DOWN}{DOWN}{DOWN}"
Shot "01d-channel-row-by-dpad"
Keys "{ENTER}"
Start-Sleep -Seconds 6
Check ((Get-Content (Join-Path $data "log.txt") -Raw) -match "player: opening ") "d-pad: down to a channel row and OK plays it"
Keys "{ESC}"
Start-Sleep -Seconds 1
Keys "{DOWN}{DOWN}"
Shot "02-home-scrolled"
Keys "{F4}"
Shot "03-settings"
Keys "{DOWN}{DOWN}{DOWN}"
Shot "04-settings-scrolled"
Keys "{F3}"
Shot "05-library"
# iptv-org: are the public links the app builds real, and does picking one fill the channel list end to end?
foreach ($u in "https://iptv-org.github.io/iptv/countries/ph.m3u", "https://iptv-org.github.io/api/countries.json") {
    try { $text = Invoke-RestMethod $u -TimeoutSec 30 | Out-String; $n = ([regex]::Matches($text, "#EXTINF")).Count; Check ($text.Length -gt 100) "iptv-org reachable: $u ($($text.Length) chars, $n channels)" }
    catch { Check $false "iptv-org reachable: $u ($_)" }
}
# Add channels from iptv-org (Philippines), then choose them in the channel picker
Keys "{F9}"
Shot "10-iptv-org-picker"
Keys "phil"
Shot "11-iptv-org-filtered"
Keys "{ENTER}"
Start-Sleep -Seconds 25
Shot "12-channel-picker"
Keys "{RIGHT}{RIGHT}{ENTER}"
Shot "12b-picker-all-selected"
Keys "{ESC}"
Start-Sleep -Seconds 2
Shot "12c-settings-after-pick"
$cfg = Get-Content (Join-Path $data "settings.json") -Raw | ConvertFrom-Json
$ph = $cfg.playlists | Where-Object { $_.source -match "countries/ph.m3u" }
Check ($null -ne $ph -and @($ph.selected).Count -gt 0) "iptv-org Philippines was downloaded and its channels were chosen in the picker ($(@($ph.selected).Count) selected)"
# on-screen keyboard: OK on the empty search box opens it, then type with it
Keys "{F9}"
Keys "{ENTER}"
Shot "14-on-screen-keyboard"
Keys "{ENTER}{ENTER}"
Shot "15-keyboard-typed"
Keys "{ESC}"
Keys "{ESC}"
Keys "{F8}"
Shot "16-power-menu"
Keys "{ESC}"
Keys "{F4}"
Keys "{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}"
Shot "17-settings-updates"
Keys "{F1}"
Keys "{F5}"
Start-Sleep -Seconds 25
Shot "09-youtube-in-app"
# Into YouTube's TV home (signed out), to see the real screen with thumbnails, icons and text over pictures.
Keys "{ENTER}"; Start-Sleep -Seconds 8
Shot "09b-youtube-after-get-started"
# "Watch as guest" is the last choice: the home screen with thumbnails, icons and text over pictures.
Keys "{DOWN}"; Keys "{DOWN}"; Keys "{DOWN}"; Keys "{ENTER}"; Start-Sleep -Seconds 12
Shot "09c-youtube-guest-home"
Keys "{DOWN}"; Start-Sleep -Seconds 4
Shot "09d-youtube-guest-row"
Keys "{LEFT}"; Start-Sleep -Seconds 3
Shot "09e-youtube-guest-menu"
Keys "{F1}"
Start-Sleep -Seconds 2
Keys "{F6}"
Start-Sleep -Seconds 25
Shot "13-netflix-in-app"
Keys "{F1}"
Start-Sleep -Seconds 2
# the PH list has its own space: a tile in Apps (right of Live TV) opens it with the channel list on that space
$phLine = (Get-Content (Join-Path $data "log.txt") | Where-Object { $_ -like "*playlist: PH TV:*" } | Select-Object -First 1)
Check ($null -ne $phLine) "the PH playlist downloaded: $phLine"
Keys "{F1}"
Start-Sleep -Seconds 2
Shot "19-home-with-ph-space"
Keys "{DOWN}"
Keys "{RIGHT}"
Keys "{ENTER}"
Start-Sleep -Seconds 8
Shot "20-ph-space-channel-list"
Keys "{ESC}"
Keys "{ESC}"
Keys "{F1}"
# a YouTube live link in the lineup opens as a fullscreen video inside the app, and channel up still works.
# Numbers follow the playlists: Test Stream 1, 2, Dead Stream (hidden, keeps 3), so the YouTube link is channel 4.
Keys "4"
Start-Sleep -Seconds 25
Shot "21-youtube-channel-video-only"
Keys "{UP}"
Start-Sleep -Seconds 6
$logText = Get-Content (Join-Path $data "log.txt") -Raw
$ytAt = $logText.IndexOf("youtube channel: YouTube live test")
Check ($ytAt -ge 0) "the YouTube live channel opened inside the app"
Check ($ytAt -ge 0 -and $logText.IndexOf("player: opening ", $ytAt) -gt $ytAt) "channel up from the YouTube channel goes to the next channel"
Keys "{ESC}"
Keys "{F1}"
# channel 1 is a real public HLS test stream: libmpv must actually play it
Keys "1"
Start-Sleep -Seconds 18
Shot "06-live-tv-player"
$logText = Get-Content (Join-Path $data "log.txt") -Raw
Check ($logText -match "player: started Test Stream 1") "channel 1 (public HLS test stream) was opened and played by libmpv"
Keys "{ENTER}"
Shot "07-mini-guide"
Keys "{ESC}"
# volume rocker: + on the number pad raises the Windows volume and shows the bar
Keys "{ADD}"
Shot "18-volume-rocker"
Keys "{ENTER}"
Keys "{RIGHT}"
Shot "07b-guide-next-category"
Keys "{ESC}"
# the dead test address must have been found by the channel check and left out of the list
$logText = Get-Content (Join-Path $data "log.txt") -Raw
$deadLine = ($logText -split "`n" | Where-Object { $_ -like "*health: Dead Stream: dead*" } | Select-Object -First 1)
Check ($null -ne $deadLine) "the channel check found the dead address: $deadLine"
Check ($logText -notmatch "health: Test Stream 1: dead") "the working test stream was not marked as dead"
Keys "{F1}"
Keys "{F2}"
Start-Sleep -Seconds 3
Keys "{ENTER}"
Shot "06b-channel-list-without-dead"
Keys "{ESC}"
Keys "{ESC}"
Start-Sleep -Seconds 1
Shot "08-back-home"
# Free channel pack: one press from the Add channels menu, then wait for the check
Keys "{F1}"
Keys "{F7}"
Keys "{ENTER}"
Start-Sleep -Seconds 240
Keys "{F1}"
Start-Sleep -Seconds 3
Shot "22-free-pack-home"
Keys "{DOWN}{DOWN}{DOWN}{DOWN}{DOWN}"
Shot "23-free-pack-home-scrolled"
Keys "{F2}"
Start-Sleep -Seconds 4
Keys "{ENTER}"
Keys "{RIGHT}{RIGHT}{RIGHT}"
Shot "24-free-pack-channel-list"
Keys "{ESC}{ESC}"
$logText = Get-Content (Join-Path $data "log.txt") -Raw
$packLines = ($logText -split "`n" | Where-Object { $_ -match "playlist: (Philippines|Filipino channels|Tagalog channels|English channels):|health: checked" })
$packLines | ForEach-Object { Write-Host "pack: $_" }
Check ($logText -match "playlist: English channels: \d+ channels") "free channel pack downloaded"
Check (-not $p.HasExited) "app survived navigation and the player"

# Netflix inside KwartoTV: the remote's arrow keys must move the TV highlight on the real netflix.com (signed-out pages here).
Keys "{F6}"
Start-Sleep -Seconds 15
Shot "30-netflix-opened"
Keys "{DOWN}"; Keys "{DOWN}"; Keys "{RIGHT}"
Start-Sleep -Seconds 1
Shot "31-netflix-after-arrows"
$hl = @(Get-Content (Join-Path $data "log.txt") | Where-Object { $_ -match "web highlight:" })
$hl | Select-Object -First 12 | ForEach-Object { Write-Host "netflix: $_" }
Check ($hl.Count -ge 3) "Netflix: arrow keys move the TV highlight ($($hl.Count) moves)"
Keys "{F1}"
Start-Sleep -Seconds 2

# Bilibili as KwartoTV's own TV screen: rows of posters from bilibili.tv's lists; OK plays only the video; Back returns.
Keys "{F11}"
Start-Sleep -Seconds 12
Shot "32-bilibili-screen"
$bl = @(Get-Content (Join-Path $data "log.txt") | Where-Object { $_ -match "bilibili: " })
$bl | Select-Object -Last 6 | ForEach-Object { Write-Host "bilibili log: $_" }
$rowsLine = $bl | Where-Object { $_ -match "bilibili: (\d+) rows, (\d+) shows" } | Select-Object -Last 1
Check ($rowsLine -match "bilibili: ([1-9]\d*) rows, ([1-9]\d*) shows") "Bilibili screen: rows of shows load ($rowsLine)"
Keys "{DOWN}"
Keys "{ENTER}"
Start-Sleep -Seconds 25
Shot "33-bilibili-playing"
$bl2 = @(Get-Content (Join-Path $data "log.txt") | Where-Object { $_ -match "bilibili: play|web player:" })
$bl2 | Select-Object -Last 4 | ForEach-Object { Write-Host "bilibili log: $_" }
Check (@($bl2 | Where-Object { $_ -match "bilibili: play" }).Count -ge 1) "Bilibili screen: OK on a show opens it"
Warn (@($bl2 | Where-Object { $_ -match "web player:" }).Count -ge 1) "Bilibili: the episode's video player was found and made fullscreen"
Keys "{ESC}"
Start-Sleep -Seconds 2
Shot "33b-bilibili-back"
Keys "{F1}"
Start-Sleep -Seconds 2

# Lock screen: Ctrl+L shows the clock and locks the TV; keys on the PC must not get past it (only the phone unlocks).
Keys "^l"
Start-Sleep -Seconds 3
Shot "34-lock-screen-clock"
Keys "{ESC}"; Keys "{F1}"; Keys "{ENTER}"
Start-Sleep -Seconds 1
Shot "35-lock-screen-after-keys"
$lockLog = @(Get-Content (Join-Path $data "log.txt"))
$lockAt = [array]::LastIndexOf($lockLog, ($lockLog | Where-Object { $_ -match "screen: locked" } | Select-Object -Last 1))
Check ($lockAt -ge 0) "Ctrl+L locks the TV with the clock"
Check (-not ($lockLog | Select-Object -Skip ($lockAt + 1) | Where-Object { $_ -match "page: |screen: unlocked" })) "keys on the PC do not get past the lock screen"

$log = Join-Path $data "log.txt"
if (Test-Path $log) { Write-Host "---- app log ----"; Get-Content $log; Check (-not (Select-String -Path $log -Pattern "UI error|Fatal|Task error" -Quiet)) "no errors in the app log" } else { Check $true "no errors in the app log (no log file)" }

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Navigation check: on every page and the main dialogs, the arrow keys (the remote) must reach every button.
$navReport = Join-Path $OutDir "nav-audit.txt"
Remove-Item $navReport -ErrorAction SilentlyContinue
$na = Start-Process -FilePath $Exe -ArgumentList "--nav-audit", "`"$navReport`"" -PassThru
for ($i = 0; $i -lt 90 -and -not (Test-Path $navReport); $i++) { Start-Sleep -Seconds 1 }
if (Test-Path $navReport) { Get-Content $navReport | ForEach-Object { Write-Host "nav: $_" } }
else { Write-Host "nav: no report; app log:"; Get-Content (Join-Path $data "log.txt") -Tail 30 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "nav log: $_" } }
Check ((Test-Path $navReport) -and (Select-String -Path $navReport -Pattern "RESULT: every control" -Quiet)) "the remote's arrows reach every control on every page and dialog"
Stop-Process -Id $na.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# YouTube Music from the phone: a paired phone locks the TV, searches a song, plays it; the lock screen shows it.
$token = "smoke-music-token"
$hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($token)))
Set-Content -Path (Join-Path $data "devices.json") -Value ("[{""Id"":""smoke001"",""Name"":""Smoke phone"",""TokenHash"":""$hash"",""PairedAt"":""2026-01-01T00:00:00+00:00"",""Blocked"":false}]") -Encoding UTF8
$logBefore = @(Get-Content (Join-Path $data "log.txt") -ErrorAction SilentlyContinue).Count
$mp = Start-Process -FilePath $Exe -PassThru
$port = $null
for ($i = 0; $i -lt 40 -and -not $port; $i++) {
    Start-Sleep -Seconds 1
    $l = @(Get-Content (Join-Path $data "log.txt") -ErrorAction SilentlyContinue) | Select-Object -Skip $logBefore | Where-Object { $_ -match "remote: listening on port (\d+)" } | Select-Object -Last 1
    if ($l -match "port (\d+)") { $port = $Matches[1] }
}
$musicResults = $null
try {
    $ws = [System.Net.WebSockets.ClientWebSocket]::new()
    $ws.ConnectAsync([Uri]"ws://127.0.0.1:$port/ws?device=smoke001&token=$token", [Threading.CancellationToken]::None).Wait(10000) | Out-Null
    function WsSend($cmd, $arg) {
        $json = @{ id = [guid]::NewGuid().ToString("N").Substring(0, 6); cmd = $cmd; arg = $arg } | ConvertTo-Json -Compress
        $bytes = [Text.Encoding]::UTF8.GetBytes($json)
        $ws.SendAsync([ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).Wait(5000) | Out-Null
    }
    # One receive at a time: a receive that timed out stays pending and is picked up by the next call
    # (starting a second one would fail and lose every answer after it).
    $script:recvTask = $null; $script:recvBuf = New-Object byte[] 65536; $script:recvMs = New-Object IO.MemoryStream
    function WsReceiveUntil($type, $seconds) {
        $deadline = (Get-Date).AddSeconds($seconds)
        while ((Get-Date) -lt $deadline -and $ws.State -eq "Open") {
            if ($null -eq $script:recvTask) { $script:recvTask = $ws.ReceiveAsync([ArraySegment[byte]]::new($script:recvBuf), [Threading.CancellationToken]::None) }
            $left = [int][Math]::Max(100, ($deadline - (Get-Date)).TotalMilliseconds)
            if (-not $script:recvTask.Wait($left)) { return $null }
            $r = $script:recvTask.Result; $script:recvTask = $null
            $script:recvMs.Write($script:recvBuf, 0, $r.Count)
            if (-not $r.EndOfMessage) { continue }
            $text = [Text.Encoding]::UTF8.GetString($script:recvMs.ToArray()); $script:recvMs.SetLength(0)
            try { $o = $text | ConvertFrom-Json } catch { continue }
            if ($o.type -eq $type) { return $o }
        }
        return $null
    }
    Check ($ws.State -eq "Open") "a paired phone connects (port $port)"
    WsSend "power.screenoff" $null
    Start-Sleep -Seconds 2
    WsSend "music.search" "Ben&Ben Maybe the Night"
    $musicResults = WsReceiveUntil "music.results" 45
    $count = if ($musicResults) { @($musicResults.items).Count } else { 0 }
    if ($musicResults) { @($musicResults.items) | Select-Object -First 5 | ForEach-Object { Write-Host "music result: $($_.id) $($_.title) - $($_.artist)" } }
    Check ($count -gt 0) "music: a song search from the phone finds songs on the TV ($count)"
    if ($count -gt 0) {
        WsSend "music.play" (@($musicResults.items)[0].id)
        $playing = $null
        $deadline = (Get-Date).AddSeconds(40)
        while ((Get-Date) -lt $deadline -and -not $playing) { $m = WsReceiveUntil "music" 10; if ($m -and $m.playing -and $m.title) { $playing = $m } }
        if ($playing) { Write-Host "music now: $($playing.title) - $($playing.artist)" }
        Warn ($null -ne $playing) "music: the picked song plays on the TV and the phone sees its title"
        Start-Sleep -Seconds 2
        Shot "36-lock-screen-with-music"
        WsSend "music.next" $null
        $next = $null
        $deadline = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $deadline -and -not $next) { $m = WsReceiveUntil "music" 10; if ($m -and $m.title -and $playing -and $m.title -ne $playing.title) { $next = $m } }
        Warn ($null -ne $next) "music: next song from the phone ($($next.title))"
        WsSend "music.playpause" $null
        $paused = $null
        $deadline = (Get-Date).AddSeconds(15)
        while ((Get-Date) -lt $deadline -and -not $paused) { $m = WsReceiveUntil "music" 5; if ($m -and -not $m.playing -and $m.on) { $paused = $m } }
        Warn ($null -ne $paused) "music: pause from the phone, while the TV is locked"
    }
    WsSend "music.stop" $null
    WsSend "power.screenon" $null
    Start-Sleep -Seconds 1

    # ---- Full control from the phone: every screen opened, used and left with phone buttons only (no PC keys).
    function WsCmd($cmd, $arg) {
        $id = [guid]::NewGuid().ToString("N").Substring(0, 8)
        $json = @{ id = $id; cmd = $cmd; arg = $arg } | ConvertTo-Json -Compress
        $bytes = [Text.Encoding]::UTF8.GetBytes($json)
        $ws.SendAsync([ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).Wait(5000) | Out-Null
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline) {
            $o = WsReceiveUntil "ack" ([Math]::Max(1, ($deadline - (Get-Date)).TotalSeconds))
            if ($null -eq $o) { break }
            if ($o.id -eq $id) { return $o }
        }
        return $null
    }
    $logPath = Join-Path $data "log.txt"
    function LogCount { @(Get-Content $logPath -ErrorAction SilentlyContinue).Count }
    function WaitLog($from, $pattern, $seconds) {
        $deadline = (Get-Date).AddSeconds($seconds)
        while ((Get-Date) -lt $deadline) {
            $hit = @(Get-Content $logPath -ErrorAction SilentlyContinue) | Select-Object -Skip $from | Where-Object { $_ -match $pattern } | Select-Object -First 1
            if ($hit) { return $hit }
            Start-Sleep -Milliseconds 500
        }
        return $null
    }
    function Phone($cmd, $arg, $expect, $seconds, $name, $shot) {
        $from = LogCount
        $ack = WsCmd $cmd $arg
        $hit = if ($expect) { WaitLog $from $expect $seconds } else { "ok" }
        if ($shot) { Start-Sleep -Seconds 2; Shot $shot }
        Check ($null -ne $ack -and $ack.ok -and $null -ne $hit) "phone: $name (ack=$(if ($ack) { $ack.ok } else { 'none' }) $(if ($ack -and $ack.error) { $ack.error }) log=$hit)"
    }
    Phone "nav.home" $null "home" 10 "Home button goes to Home" $null
    Phone "app.livetv" $null "player: opening" 25 "Live TV plays a channel" "40-phone-live-tv"
    Phone "channel.next" $null "player: opening" 25 "channel up changes the channel" $null
    Phone "nav.home" $null "home" 10 "Home button leaves the player" $null
    Phone "app.ytmusic" $null "page: Music" 10 "YouTube Music opens its TV screen" "41-phone-youtube-music"
    Phone "nav.down" $null $null 5 "arrows work on the YouTube Music screen" $null
    Phone "app.bilibili" $null "page: Bilibili" 10 "Bilibili opens its TV screen" "42-phone-bilibili"
    Start-Sleep -Seconds 8
    # The phone's keyboard: Search, OK on the box (the TV keyboard opens), type on the phone, Enter, then Search.
    Phone "nav.ok" $null $null 5 "OK on Bilibili's Search" $null
    Start-Sleep -Seconds 1
    Phone "nav.ok" $null "keyboard: box waiting" 8 "OK on the search box opens the TV keyboard" $null
    Phone "key.text" "hello" "keyboard: typed 5 into the box" 5 "letters typed on the phone go into the TV's box" "42b-phone-typed"
    Phone "key.backspace" $null $null 5 "phone backspace" $null
    Phone "key.text" "o" "keyboard: typed 1 into the box" 5 "more letters after a backspace" $null
    Phone "key.enter" $null "keyboard: enter into the box" 5 "phone Enter finishes the box" $null
    Start-Sleep -Seconds 1
    Phone "nav.ok" $null 'bilibili: search "hello"' 15 "the typed text is searched" "42c-phone-search"
    Phone "nav.home" $null "home" 10 "Home after the search" $null
    Phone "app.bilibili" $null "page: Bilibili" 10 "Bilibili again" $null
    Start-Sleep -Seconds 3
    Phone "nav.down" $null $null 5 "arrow down to the Bilibili rows" $null
    Phone "nav.ok" $null "bilibili: play" 15 "OK on a Bilibili show plays it" "43-phone-bilibili-play"
    Phone "nav.back" $null "web: closed web-bilibili" 15 "Back from a Bilibili show returns to the rows" "44-phone-bilibili-back"
    Phone "app.youtube" $null "web: open YouTube" 10 "YouTube opens" "45-phone-youtube"
    Start-Sleep -Seconds 10
    Phone "nav.down" $null $null 5 "arrows inside YouTube" $null
    Phone "nav.home" $null "web: closed youtube" 10 "Home button leaves YouTube" $null
    Phone "app.netflix" $null "web: open Netflix" 10 "Netflix opens" $null
    Start-Sleep -Seconds 6
    Phone "netflix.search" "stranger things" "netflix: search" 10 "Netflix search from the phone" "45b-phone-netflix-search"
    Phone "netflix.tracks" $null "netflix: tracks" 10 "Audio & Subtitles asked from the phone (answers even with no video)" $null
    Phone "netflix.track" "s:0" "netflix: subtitles 0" 10 "a subtitle pick from the phone reaches Netflix" $null
    Phone "netflix.episodes" $null "netflix: episodes" 10 "Episodes asked from the phone (answers even with no show)" $null
    Phone "netflix.speed" "1.5" "netflix: speed\(1.5\)" 10 "speed from the phone reaches Netflix" $null
    Phone "netflix.seekto" "60" "netflix: seekTo\(60\)" 10 "jump to a time from the phone reaches Netflix" $null
    Phone "netflix.continue" $null "netflix: cont\(\)" 10 "Continue watching from the phone reaches Netflix" $null
    Phone "nav.home" $null "web: closed netflix" 10 "Home button leaves Netflix" $null
    Phone "app.open" "primevideo" "app: Prime Video" 10 "More apps: Prime Video opens" $null
    Phone "nav.home" $null "home" 10 "Home button leaves Prime Video" $null
    Phone "app.settings" $null "page: Settings" 10 "Settings opens" "46-phone-settings"
    Phone "nav.down" $null $null 5 "arrows in Settings" $null
    Phone "nav.back" $null "page: Home|home" 10 "Back from Settings goes Home" $null
    # Voice search (the words come from the phone's speech recognizer): each app opens on its results.
    Phone "app.search" "youtube|lofi hip hop" "web: open YouTube" 10 "voice search opens YouTube on the results" $null
    Start-Sleep -Seconds 10; Shot "47c-phone-voice-youtube"
    Phone "nav.home" $null "web: closed youtube" 10 "Home after the YouTube search" $null
    Phone "app.search" "bilibili|naruto" 'bilibili: search "naruto"' 25 "voice search on Bilibili" "47d-phone-voice-bilibili"
    Phone "app.search" "music|Ben&Ben Maybe the Night" 'music: search "Ben&Ben Maybe the Night"' 25 "voice search on YouTube Music" "47e-phone-voice-music"
    Phone "nav.home" $null "home" 10 "Home after the voice searches" $null
    Phone "music.play" "dQw4w9WgXcQ" "page: Music" 10 "a song picked on the phone shows the TV's YouTube Music screen" "47b-phone-music-screen"
    $fgMsg = WsReceiveUntil "fg" 5
    Warn ($null -ne $fgMsg -and $fgMsg.app -eq "Music") "phone: the phone is told the TV shows YouTube Music ($(if ($fgMsg) { $fgMsg.app }))"
    Phone "music.stop" $null "music: stopped" 10 "music stops from the phone" $null
    # CI machines have no sound card, so volume is reported, not required.
    $v = WsCmd "media.volup" $null; Warn ($null -ne $v -and $v.ok) "phone: volume up ($(if ($v) { $v.error }))"
    $v = WsCmd "media.voldown" $null; Warn ($null -ne $v -and $v.ok) "phone: volume down ($(if ($v) { $v.error }))"
    Phone "power.timer" "30" "sleep timer: 30 min" 5 "sleep timer on" $null
    Phone "power.timer" "0" "sleep timer: off" 5 "sleep timer off" $null
    Phone "power.screenoff" $null "screen: locked" 10 "Lock shows the clock" $null
    $locked = WsCmd "nav.ok" $null
    Check ($null -ne $locked -and -not $locked.ok) "phone: other buttons do nothing while locked ($(if ($locked) { $locked.error }))"
    Phone "power.screenon" $null "screen: unlocked" 10 "Unlock returns to the TV" "47-phone-unlocked"
    # ---- Every button of the phone, app by app: the command must do its job on the TV (seen in the log), not only be accepted.
    # Effect: like Phone, but a missing effect is only a warning when the CI machine itself cannot show it (no sign-in, no sound, bot checks).
    function Effect($cmd, $arg, $expect, $seconds, $name, [switch]$soft) {
        $from = LogCount
        $ack = WsCmd $cmd $arg
        $hit = if ($expect) { WaitLog $from $expect $seconds } else { "ok" }
        $ok = $null -ne $ack -and $ack.ok -and $null -ne $hit
        $line = "button: $name ($cmd $arg) ack=$(if ($ack) { $ack.ok } else { 'none' }) $(if ($ack -and $ack.error) { $ack.error }) effect=$hit"
        if ($soft) { Warn $ok $line } else { Check $ok $line }
    }
    $focus = "focus: |web highlight: "
    # Home and KwartoTV's own screens: the arrows move the highlight, OK opens, Back returns.
    Effect "nav.home" $null "home" 10 "Home"
    foreach ($k in "nav.right", "nav.down", "nav.left", "nav.up") { Effect $k $null $focus 5 "arrow on Home" }
    Effect "app.settings" $null "page: Settings" 10 "Settings"
    foreach ($k in "nav.down", "nav.down", "nav.up") { Effect $k $null $focus 5 "arrow in Settings" }
    Effect "nav.back" $null "page: Home|home" 10 "Back from Settings"
    Effect "app.ytmusic" $null "page: Music" 10 "YouTube Music screen"
    foreach ($k in "nav.down", "nav.right", "nav.left") { Effect $k $null $focus 5 "arrow on the YouTube Music screen" }
    # Live TV: play/pause, channels up/down/last/number, typed digits, the channel list, favorite.
    Effect "app.livetv" $null "player: opening" 25 "Live TV"
    Start-Sleep -Seconds 4
    Effect "media.playpause" $null "player: paused" 10 "pause Live TV"
    Effect "media.playpause" $null "player: playing" 10 "play Live TV"
    Effect "channel.next" $null "player: opening" 20 "channel up"
    Effect "channel.prev" $null "player: opening" 20 "channel down"
    Effect "channel.next" $null "player: opening" 20 "channel up again"
    Effect "channel.last" $null "player: opening" 20 "last channel"
    Effect "channel.number" "2" "player: opening" 20 "channel by number (list on the phone)"
    $null = WsCmd "channel.digit" "1"
    Effect "channel.commit" $null "player: opening" 20 "typed digit 1 + Go"
    Effect "channel.favorite" $null $null 5 "favorite channel"
    $null = WsCmd "channel.favorite" $null
    Effect "channel.list" $null "guide: open" 10 "channel list on the TV"
    Effect "nav.down" $null "player key: Down" 5 "down in the channel list"
    Effect "nav.up" $null "player key: Up" 5 "up in the channel list"
    Effect "nav.back" $null $null 5 "Back closes the channel list"
    Shot "50-buttons-live"
    Effect "nav.home" $null "home" 10 "Home from Live TV"
    # A Bilibili episode: OK plays, play/pause, ±10 s from the phone, ◀ ▶ on the D-pad, Back.
    Effect "app.bilibili" $null "page: Bilibili" 10 "Bilibili screen"
    Start-Sleep -Seconds 6
    foreach ($k in "nav.down", "nav.right", "nav.left") { Effect $k $null $focus 5 "arrow on the Bilibili screen" }
    Effect "nav.ok" $null "bilibili: play" 15 "OK plays a show"
    Start-Sleep -Seconds 12
    Effect "media.playpause" $null "web player: (pause|play) at" 10 "play/pause the episode" -soft
    Effect "media.playpause" $null "web player: (pause|play) at" 10 "play/pause the episode again" -soft
    Effect "media.seek" "10" "web player: seeked at" 10 "+10 s" -soft
    Effect "media.seek" "-10" "web player: seeked at" 10 "-10 s" -soft
    Effect "nav.right" $null "web player: seeked at" 10 "▶ on the D-pad skips 10 s" -soft
    Effect "nav.left" $null "web player: seeked at" 10 "◀ on the D-pad goes back 10 s" -soft
    Effect "nav.ok" $null "web player: (pause|play) at" 10 "OK on the D-pad pauses" -soft
    Shot "51-buttons-bilibili-video"
    Effect "nav.back" $null "web: closed web-bilibili" 15 "Back leaves the episode"
    # Typing a whole text at once (the phone's type box) into a TV box, then searching it.
    Effect "nav.home" $null "home" 10 "Home"
    Effect "app.bilibili" $null "page: Bilibili" 10 "Bilibili for the type box"
    Start-Sleep -Seconds 3
    for ($i = 0; $i -lt 6; $i++) { $null = WsCmd "nav.up" $null; Start-Sleep -Milliseconds 300 }
    for ($i = 0; $i -lt 3; $i++) { $null = WsCmd "nav.left" $null; Start-Sleep -Milliseconds 300 }
    Effect "nav.ok" $null $null 5 "OK on Search"
    Start-Sleep -Seconds 1
    Effect "nav.ok" $null "keyboard: box waiting" 8 "OK on the box"
    Effect "text.set" "1:kimetsu" $null 5 "the type box sends the whole text"
    Start-Sleep -Seconds 1
    Effect "nav.ok" $null 'bilibili: search "kimetsu"' 15 "the whole text is searched"
    Effect "nav.home" $null "home" 10 "Home"
    # YouTube: on the results of a search, OK plays, then play/pause, ±10 s, next video (CI machines may get YouTube's bot check).
    Effect "app.search" "youtube|lofi hip hop" "web: open YouTube" 10 "YouTube search"
    Start-Sleep -Seconds 10
    Effect "nav.ok" $null "web player: play at" 25 "OK plays the first result" -soft
    Start-Sleep -Seconds 5
    Effect "media.playpause" $null "web player: (pause|play) at" 10 "YouTube play/pause" -soft
    Effect "media.seek" "10" "web player: seeked at" 10 "YouTube +10 s" -soft
    Effect "media.seek" "-10" "web player: seeked at" 10 "YouTube -10 s" -soft
    Shot "52-buttons-youtube"
    Effect "nav.back" $null $null 5 "Back in YouTube"
    Effect "nav.home" $null "web: closed youtube" 10 "Home from YouTube"
    # Netflix (not signed in on CI): the arrows move on its page; a sign-in made on the phone reaches it.
    Effect "app.netflix" $null "web: open Netflix" 10 "Netflix"
    Start-Sleep -Seconds 8
    foreach ($k in "nav.down", "nav.right", "nav.down", "nav.up") { Effect $k $null "web highlight: " 6 "arrow on Netflix" }
    Effect "web.signin" "netflix|NetflixId=test; SecureNetflixId=test" "signin: Netflix from the phone" 10 "sign-in from the phone"
    Start-Sleep -Seconds 4
    Shot "53-buttons-netflix"
    Effect "nav.home" $null "web: closed netflix" 10 "Home from Netflix"
    # The mouse pad: the pointer moves, clicks and scrolls.
    Add-Type -AssemblyName System.Windows.Forms
    $p0 = [System.Windows.Forms.Cursor]::Position
    Effect "mouse.move" "60,40" $null 3 "mouse move"
    Start-Sleep -Milliseconds 300
    $p1 = [System.Windows.Forms.Cursor]::Position
    Check ($p1.X -ne $p0.X -or $p1.Y -ne $p0.Y) "button: the pointer moved ($($p0.X),$($p0.Y) -> $($p1.X),$($p1.Y))"
    Effect "mouse.scroll" "2" $null 3 "mouse scroll"
    Effect "mouse.click" $null $null 3 "mouse click"
    Effect "nav.home" $null "home" 10 "Home"
    # More apps: each opens (its Windows app, or its website inside KwartoTV).
    foreach ($a in "primevideo", "disneyplus", "hbomax", "viu", "iwanttfc", "crunchyroll", "spotify", "twitch", "appletv", "plex") {
        Effect "app.open" $a "app: " 10 "open $a"
        Effect "nav.home" $null "home" 10 "Home after $a"
    }
    # Power: the PC's own sleep / restart / shut down are not pressed here (they would end the test machine); the phone
    # asks first, and the TV accepts them (Core tests).
    $phoneErrors = @(Get-Content $logPath -ErrorAction SilentlyContinue) | Select-Object -Skip $logBefore | Where-Object { $_ -match "UI error|Fatal|Task error" }
    $phoneErrors | Select-Object -First 5 | ForEach-Object { Write-Host "phone error: $_" }
    Check ($phoneErrors.Count -eq 0) "phone: no app errors while the phone drove every screen ($($phoneErrors.Count))"
}
catch { Write-Host "music check error: $_"; Check $false "music check ran" }
@(Get-Content (Join-Path $data "log.txt") -ErrorAction SilentlyContinue) | Select-Object -Skip $logBefore | Where-Object { $_ -match "music|screen:" } | ForEach-Object { Write-Host "music log: $_" }
Stop-Process -Id $mp.Id -Force -ErrorAction SilentlyContinue
$blocker.Stop()
if ($fail -gt 0) { throw "$fail smoke check(s) failed" }
Write-Host "Smoke test passed"
