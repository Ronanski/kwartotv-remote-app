# The only part of KwartoTV that needs administrator rights, run once by the installer (one Windows permission prompt):
#  - opens the phone remote's ports in Windows Firewall (by port, so later updates never need this again),
#  - moves an older KwartoTV out of Program Files (those versions needed a permission prompt for every update),
#  - removes the old "KwartoTV Update" scheduled task and the old http.sys reservation.
# With -Remove (uninstall) it only removes the firewall rules.
param([string]$App, [string]$OldUninstaller, [switch]$Remove)

function Netsh([string]$arguments) {
    Start-Process -FilePath "$env:SystemRoot\System32\netsh.exe" -ArgumentList $arguments -WindowStyle Hidden -Wait
}

if (-not $Remove -and $OldUninstaller -and (Test-Path $OldUninstaller)) {
    # The old uninstaller also deletes the firewall rules, so it runs before the rules are added below.
    Start-Process -FilePath $OldUninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
    # The uninstaller hands over to a copy of itself and returns at once; it is done when it has removed itself.
    for ($i = 0; $i -lt 120 -and (Test-Path $OldUninstaller); $i++) { Start-Sleep -Milliseconds 500 }
    Start-Sleep -Seconds 1
}
Unregister-ScheduledTask -TaskName 'KwartoTV Update' -Confirm:$false -ErrorAction SilentlyContinue
Netsh 'http delete urlacl url=http://+:8765/'

foreach ($name in 'KwartoTV app', 'KwartoTV remote', 'KwartoTV discovery') { Netsh "advfirewall firewall delete rule name=`"$name`"" }
if ($Remove) { exit 0 }

Netsh "advfirewall firewall add rule name=`"KwartoTV app`" dir=in action=allow program=`"$App\KwartoTV.exe`" profile=any"
Netsh 'advfirewall firewall add rule name="KwartoTV remote" dir=in action=allow protocol=TCP localport=8765,8766,8767,8768,8769,18765,28765,38765,48765,58765 profile=any'
Netsh 'advfirewall firewall add rule name="KwartoTV discovery" dir=in action=allow protocol=UDP localport=5353 profile=any'
exit 0
