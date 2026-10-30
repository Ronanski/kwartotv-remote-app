# KwartoTV in-app update, run by the "KwartoTV Update" scheduled task.
#
# The installer registers the task (-Register) for the user who installed KwartoTV, with the highest privileges, so an update
# needs no Windows permission prompt (which a TV remote cannot answer). This script lives in the install folder, which only
# administrators can change, and it downloads and checks the installer itself in that folder: nothing a normal program could
# swap is ever run with administrator rights.
#
# Flow when KwartoTV asks for an update: KwartoTV starts the task and closes; this script waits for it to close, checks the
# "latest" release, downloads and verifies the installer (address and SHA-256 from the update manifest), installs it silently,
# and opens KwartoTV again at the user's normal level.
param([switch]$Register, [switch]$Unregister, [string]$User)

$TaskName = 'KwartoTV Update'

if ($Register) {
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`""
    # Interactive logon: runs in the user's own session (so KwartoTV can be opened again) and needs no stored password.
    $principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 30)
    Register-ScheduledTask -TaskName $TaskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
    exit 0
}
if ($Unregister) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    exit 0
}

$app = $PSScriptRoot
$exe = Join-Path $app 'KwartoTV.exe'
$dir = Join-Path $app 'updates'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$log = Join-Path $dir 'update.log'
function Say($t) { "$(Get-Date -Format s) $t" | Out-File -FilePath $log -Append -Encoding utf8 }

try {
    # KwartoTV started this task and is closing.
    foreach ($i in 1..30) { if (-not (Get-Process -Name KwartoTV -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Seconds 1 }
    Get-Process -Name KwartoTV -ErrorAction SilentlyContinue | Stop-Process -Force

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $manifestUrl = 'https://github.com/Ronanski/game-dev-era/releases/download/latest/KwartoTV-update.json'
    $prefix = 'https://github.com/Ronanski/game-dev-era/releases/download/'
    $m = Invoke-RestMethod -Uri $manifestUrl -UseBasicParsing -TimeoutSec 60
    # Checked on the parsed address, the one actually downloaded ("../" cannot lead out of this project's releases).
    $url = ([Uri]$m.exe.url).AbsoluteUri
    if (-not $url.StartsWith($prefix, [StringComparison]::Ordinal)) { throw "update address not allowed: $url" }
    if ("$($m.exe.sha256)" -notmatch '^[0-9a-fA-F]{64}$') { throw 'the update manifest has no valid checksum' }

    $have = 0
    try { $have = [Version]::Parse((Get-Item $exe).VersionInfo.FileVersion).Build } catch { }
    if ([int]$m.build -le $have) { Say "already up to date (build $have)"; return }

    $installer = Join-Path $dir "KwartoTV-$([int]$m.build).exe"
    Say "downloading build $($m.build) (have $have)"
    Invoke-WebRequest -Uri $url -OutFile $installer -UseBasicParsing -TimeoutSec 900
    $hash = (Get-FileHash $installer -Algorithm SHA256).Hash
    if ($hash -ne "$($m.exe.sha256)".ToUpperInvariant()) { Remove-Item $installer -Force; throw "checksum does not match ($hash)" }

    Say 'installing'
    $p = Start-Process -FilePath $installer -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /LOG=`"$dir\install.log`"" -PassThru -Wait
    Say "installer finished with code $($p.ExitCode)"
    Remove-Item $installer -Force -ErrorAction SilentlyContinue
}
catch { Say "failed: $($_.Exception.Message)" }
finally {
    # Open KwartoTV again at the user's normal level (started through Explorer, not from this elevated task).
    if (-not (Get-Process -Name KwartoTV -ErrorAction SilentlyContinue)) {
        Start-Process -FilePath explorer.exe -ArgumentList "`"$exe`""
        Say 'KwartoTV opened again'
    }
}
