# Run this ON THE TV PC (PowerShell). It checks the two things that differ between PCs:
#   1. the Netflix Store app id, 2. whether youtube.com/tv answers the arrow keys in your browser.
# Usage:  powershell -ExecutionPolicy Bypass -File verify-windows.ps1
$ErrorActionPreference = "Continue"
Write-Host "== 1. Netflix app id ==" -ForegroundColor Cyan
$nf = Get-StartApps | Where-Object Name -like "*Netflix*"
if ($nf) {
    $nf | Format-Table Name, AppID -AutoSize
    Write-Host "KwartoTV looks this id up by itself at run time. Opening Netflix now to prove it launches..."
    Start-Process explorer.exe "shell:AppsFolder\$(@($nf)[0].AppID)"
    Write-Host "Did Netflix open? (it should be on screen in a few seconds)"
} else {
    Write-Host "Netflix app NOT installed. KwartoTV will open netflix.com in a fullscreen browser window instead." -ForegroundColor Yellow
    Write-Host "Install it from the Microsoft Store (search: Netflix) for the best result."
}

Write-Host "`n== 2. YouTube TV with arrow keys ==" -ForegroundColor Cyan
$browsers = @(
    "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    "C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    "C:\Program Files\Google\Chrome\Application\chrome.exe",
    "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe")
$exe = $browsers | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exe) { Write-Host "No Edge or Chrome found. Install one." -ForegroundColor Red; exit 1 }
Write-Host "Browser: $exe"
$profile = Join-Path $env:TEMP "kwartotv-verify-profile"
Start-Process $exe @("--app=https://www.youtube.com/tv", "--start-fullscreen", "--user-data-dir=$profile", "--no-first-run", "--no-default-browser-check")
Write-Host "YouTube TV is opening. Waiting 15 s, then sending: Down, Down, Right, Right, Enter ..."
Start-Sleep -Seconds 15
Add-Type -AssemblyName System.Windows.Forms
foreach ($k in "{DOWN}", "{DOWN}", "{RIGHT}", "{RIGHT}") { [System.Windows.Forms.SendKeys]::SendWait($k); Start-Sleep -Milliseconds 700 }
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
Write-Host "`nLook at the screen. PASS if the highlight moved around the tiles and Enter opened something."
Write-Host "(You may see the sign-in page first. Sign in once with the same keys. KwartoTV keeps the login in its own browser profile.)"
Write-Host "Press Esc on the keyboard to go Back. Close the window with Alt+F4 when done."
