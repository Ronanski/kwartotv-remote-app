# Starts the installed KwartoTV on a Windows desktop, checks the remote server, and takes screenshots.
# Screenshots are saved as JPEG files and also printed as base64 between ##SHOT markers.
param([Parameter(Mandatory)][string]$Exe, [string]$OutDir = "qa-shots")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$fail = 0
function Check($ok, $name) { if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:fail++ } }

# Placeholder channels only (example.invalid never resolves). Seeds a local playlist and a website tile.
$data = Join-Path $env:APPDATA "KwartoTV"
New-Item -ItemType Directory -Force -Path $data | Out-Null
$m3u = Join-Path $data "demo.m3u"
$lines = @("#EXTM3U")
$groups = @("News", "Movies", "Kids")
for ($i = 1; $i -le 18; $i++) { $g = $groups[$i % 3]; $lines += "#EXTINF:-1 group-title=`"$g`",Demo Channel $i"; $lines += "https://example.invalid/demo$i.m3u8" }
Set-Content -Path $m3u -Value $lines -Encoding UTF8
$settings = @{ playlists = @(@{ id = "demo"; name = "Demo (placeholder)"; source = $m3u }); webTiles = @(@{ id = "ex"; name = "Example"; url = "https://example.com" }); libraryFolders = @(); port = 8765 } | ConvertTo-Json -Depth 5
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

$p = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 10
Check (-not $p.HasExited) "app is running"

$base = "http://127.0.0.1:8765"
try { $info = Invoke-RestMethod "$base/info"; Check ($info.app -eq "KwartoTV") "remote /info answers ($($info.name))" } catch { Check $false "remote /info answers: $_" }
try { $page = Invoke-WebRequest "$base/" -UseBasicParsing; Check ($page.Content -match "KwartoTV Remote") "browser remote page is served" } catch { Check $false "browser remote page: $_" }
try { Invoke-WebRequest "$base/pair" -Method Post -Body '{"pin":"000000","name":"x"}' -ContentType "application/json" -UseBasicParsing | Out-Null; Check $false "wrong pin is refused" }
catch { Check ($_.Exception.Response.StatusCode.value__ -eq 403) "wrong pin is refused (403)" }
$mdns = Get-NetUDPEndpoint -LocalPort 5353 -ErrorAction SilentlyContinue | Where-Object { (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName -eq "KwartoTV" }
Check ($null -ne $mdns) "mDNS responder is listening on UDP 5353"

Shot "01-home"
Keys "{UP}{RIGHT}{RIGHT}{RIGHT}{ENTER}"
Start-Sleep -Seconds 1
Shot "02-settings"
Keys "{ESC}"
Keys "{DOWN}{DOWN}"
Shot "03-home-scrolled"
Keys "1"
Start-Sleep -Seconds 6
Shot "04-live-tv-player"
Keys "{ENTER}"
Shot "05-mini-guide"
Keys "{ESC}{ESC}"
Start-Sleep -Seconds 1
Check (-not $p.HasExited) "app survived navigation and the player"

$log = Join-Path $data "log.txt"
if (Test-Path $log) { Write-Host "---- app log ----"; Get-Content $log; Check (-not (Select-String -Path $log -Pattern "UI error|Fatal|Task error" -Quiet)) "no errors in the app log" } else { Check $true "no errors in the app log (no log file)" }

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
if ($fail -gt 0) { throw "$fail smoke check(s) failed" }
Write-Host "Smoke test passed"
