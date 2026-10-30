# Runs the in-app update the way the Update dialog does (KwartoTV --apply-update uses the same code):
# the app starts the helper and closes, the helper installs silently and opens KwartoTV again.
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference = "Stop"
$dir = Join-Path ([IO.Path]::GetTempPath()) "KwartoTV-update"
Remove-Item (Join-Path $dir "status.txt"), (Join-Path $dir "install.log") -ErrorAction SilentlyContinue
Get-Process KwartoTV -ErrorAction SilentlyContinue | Stop-Process -Force
$before = (Get-Item $Exe).LastWriteTimeUtc
$first = Start-Process -FilePath $Exe -ArgumentList "--apply-update", (Resolve-Path $Installer).Path -PassThru
Write-Host "KwartoTV started (pid $($first.Id)); waiting for it to close and update"
$ok = $false
for ($i = 0; $i -lt 150; $i++) {
    Start-Sleep -Seconds 2
    $status = Get-Content (Join-Path $dir "status.txt") -Raw -ErrorAction SilentlyContinue
    $again = Get-Process KwartoTV -ErrorAction SilentlyContinue | Where-Object Id -ne $first.Id
    if ($status -match "opened again" -and $again) { $ok = $true; break }
}
Write-Host "---- helper status ----"; Write-Host $status
$log = Join-Path $dir "install.log"
if (Test-Path $log) { Write-Host "---- installer log (end) ----"; Get-Content $log -Tail 15 }
if (-not $first.HasExited) { throw "The old KwartoTV did not close" }
if ($status -notmatch "installer finished with code 0") { throw "The installer did not finish with code 0" }
if (-not (Select-String -Path $log -Pattern "Installation process succeeded" -Quiet)) { throw "The installer log does not say the installation succeeded" }
if (-not $ok) { throw "KwartoTV did not open again after the update" }
Write-Host "PASS in-app update: app closed, installed silently, opened again (exe written $before -> $((Get-Item $Exe).LastWriteTimeUtc))"
Get-Process KwartoTV -ErrorAction SilentlyContinue | Stop-Process -Force
