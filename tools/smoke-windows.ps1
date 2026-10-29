# Starts the installed KwartoTV on a Windows desktop, checks the remote server, and takes screenshots.
# Screenshots are saved as JPEG files and also printed as base64 between ##SHOT markers.
param([Parameter(Mandatory)][string]$Exe, [string]$OutDir = "qa-shots")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$fail = 0
function Check($ok, $name) { if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:fail++ } }

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
    @{ id = "phspace"; name = "PH TV"; source = "https://raw.githubusercontent.com/IPTV-Philippines/IPTV-Philippines/main/ph.m3u"; showAsSpace = $true }); webTiles = @(@{ id = "ex"; name = "Example"; url = "https://example.com" }); libraryFolders = @(); port = 8765 } | ConvertTo-Json -Depth 5
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
# down from the top of Home into the channel rows, OK plays the channel
Keys "{DOWN}{DOWN}"
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
# a YouTube live link in the lineup opens as a fullscreen video inside the app, and channel up still works
Keys "3"
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

$log = Join-Path $data "log.txt"
if (Test-Path $log) { Write-Host "---- app log ----"; Get-Content $log; Check (-not (Select-String -Path $log -Pattern "UI error|Fatal|Task error" -Quiet)) "no errors in the app log" } else { Check $true "no errors in the app log (no log file)" }

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
$blocker.Stop()
if ($fail -gt 0) { throw "$fail smoke check(s) failed" }
Write-Host "Smoke test passed"
