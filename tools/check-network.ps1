# KwartoTV network check. Run on the TV PC in PowerShell **as Administrator**:
#   irm https://raw.githubusercontent.com/Ronanski/game-dev-era/ccr-a4b86001-rhkx9c/tools/check-network.ps1 | iex
# It finds why a phone cannot reach the TV, fixes what it can, and prints a verdict.
$ErrorActionPreference = "SilentlyContinue"
$port = 8765
function Say($c, $t) { Write-Host $t -ForegroundColor $c }
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Say Red "Please run PowerShell as Administrator (right-click > Run as administrator) and run this again."; return }

Say Cyan "`n== 1. Is KwartoTV listening? =="
$ip = (Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq "Up" } | Select-Object -First 1).IPv4Address.IPAddress
Say White "This PC's LAN address: $ip"
$listening = $false
try { $r = Invoke-RestMethod "http://$($ip):$port/info" -TimeoutSec 5; $listening = $r.app -eq "KwartoTV"; Say Green "OK: KwartoTV answers on http://$($ip):$port ($($r.name))" }
catch {
    $running = Get-Process KwartoTV -ErrorAction SilentlyContinue
    if ($running) { Say Red "KwartoTV is open but its remote server is not answering. Update to the latest KwartoTV.exe (older versions had this bug). ($($_.Exception.Message))" }
    else { Say Red "KwartoTV is not running. Open it first. ($($_.Exception.Message))" }
}

Say Cyan "`n== 2. Network type (Public blocks phones) =="
Get-NetConnectionProfile | ForEach-Object {
    Say White ("{0}: {1}" -f $_.Name, $_.NetworkCategory)
    if ($_.NetworkCategory -eq "Public") { Set-NetConnectionProfile -InterfaceIndex $_.InterfaceIndex -NetworkCategory Private; Say Green "  -> changed to Private" }
}

Say Cyan "`n== 3. Windows Firewall =="
Get-NetFirewallProfile | ForEach-Object {
    Say White ("{0}: enabled={1} inbound={2} allowInboundRules={3}" -f $_.Name, $_.Enabled, $_.DefaultInboundAction, $_.AllowInboundRules)
    if ($_.AllowInboundRules -eq "False") { Set-NetFirewallProfile -Name $_.Name -AllowInboundRules True; Say Green "  -> 'Block all incoming connections' was ON, turned it off" }
}
Remove-NetFirewallRule -DisplayName "KwartoTV remote", "KwartoTV discovery" 2>$null
New-NetFirewallRule -DisplayName "KwartoTV remote" -Direction Inbound -Action Allow -Protocol TCP -LocalPort $port -Profile Any | Out-Null
Remove-NetFirewallRule -DisplayName "KwartoTV app" 2>$null
$exe = (Get-Process KwartoTV -ErrorAction SilentlyContinue | Select-Object -First 1).Path
if (-not $exe) { $exe = "$env:ProgramFiles\KwartoTV\KwartoTV.exe" }
if (Test-Path $exe) { New-NetFirewallRule -DisplayName "KwartoTV app" -Direction Inbound -Action Allow -Program $exe -Profile Any | Out-Null }
New-NetFirewallRule -DisplayName "KwartoTV discovery" -Direction Inbound -Action Allow -Protocol UDP -LocalPort 5353 -Profile Any | Out-Null
Say Green "Firewall rules for TCP $port and UDP 5353 re-created (all profiles)."
$blocks = Get-NetFirewallRule -Direction Inbound -Action Block -Enabled True | Where-Object { ($_ | Get-NetFirewallPortFilter).LocalPort -contains "$port" }
if ($blocks) { Say Yellow "A rule BLOCKS port $($port): $($blocks.DisplayName -join ', ')" }
netsh http delete urlacl url=http://+:$port/ | Out-Null

Say Cyan "`n== 4. Other security software (these ignore Windows Firewall rules) =="
$fw = Get-CimInstance -Namespace root/SecurityCenter2 -ClassName FirewallProduct
$av = Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct
$third = @($fw + $av | Where-Object { $_.displayName -notmatch "Windows Defender|Microsoft Defender" } | Select-Object -ExpandProperty displayName -Unique)
if ($third) { Say Yellow ("Found: " + ($third -join ", ") + ". Its own firewall must allow KwartoTV / port $port, or turn its firewall off for a test.") }
else { Say Green "None found (only Microsoft Defender)." }

Say Cyan "`n== Verdict =="
if (-not $listening) { Say Red "Install the latest KwartoTV.exe, open it, then run this again." }
elseif ($third) { Say Yellow "Try the phone again now. If it still times out, allow KwartoTV in: $($third -join ', ')." }
else {
    Say Green "The PC side is now open. Try pairing on the phone again with address $ip."
    Say White "If it STILL times out: your router keeps Wi-Fi devices apart ('AP isolation' / 'client isolation'),"
    Say White "or the phone is on a guest network / other band. Turn AP isolation off in the router settings,"
    Say White "or connect the PC and phone to the same (non-guest) Wi-Fi name."
}
