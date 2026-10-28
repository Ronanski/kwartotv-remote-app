# Why do no channels load? Run on the TV PC (normal PowerShell is fine):
#   irm https://raw.githubusercontent.com/Ronanski/game-dev-era/ccr-a4b86001-rhkx9c/tools/check-iptv.ps1 | iex
$ErrorActionPreference = "Continue"
$ProgressPreference = "SilentlyContinue"
function Say($c, $t) { Write-Host $t -ForegroundColor $c }
$hosts = @(
  @{ Name = "iptv-org.github.io";        Url = "https://iptv-org.github.io/iptv/countries/ph.m3u" },
  @{ Name = "cdn.jsdelivr.net";          Url = "https://cdn.jsdelivr.net/gh/iptv-org/iptv@gh-pages/countries/ph.m3u" },
  @{ Name = "raw.githubusercontent.com"; Url = "https://raw.githubusercontent.com/iptv-org/iptv/gh-pages/countries/ph.m3u" }
)

Say Cyan "== Proxy / VPN =="
$ie = Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings" -ErrorAction SilentlyContinue
Say White ("Proxy enabled: {0}  Server: {1}" -f $ie.ProxyEnable, $ie.ProxyServer)
$vpn = Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq "Up" -and $_.InterfaceDescription -match "VPN|TAP|WireGuard|Tailscale|ZeroTier|Hamachi" }
if ($vpn) { Say Yellow ("VPN-like adapter is up: " + (($vpn | ForEach-Object Name) -join ", ")) } else { Say Green "No VPN adapter." }

$anyOk = $false
foreach ($h in $hosts) {
    Say Cyan ("`n== " + $h.Name + " ==")
    try {
        $ips = Resolve-DnsName $h.Name -Type A -ErrorAction Stop | Where-Object { $_.Type -eq "A" } | Select-Object -ExpandProperty IPAddress
        Say Green ("DNS (IPv4): " + ($ips -join ", "))
    } catch { Say Red ("DNS FAILED: " + $_.Exception.Message + "  -> try another DNS (for example 1.1.1.1 or 8.8.8.8) in the Wi-Fi settings") ; continue }
    $t = Test-NetConnection $h.Name -Port 443 -WarningAction SilentlyContinue
    if ($t.TcpTestSucceeded) { Say Green "Port 443 connects" } else { Say Red "Port 443 does NOT connect (blocked by router, ISP, firewall or antivirus)"; continue }
    try {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $text = Invoke-RestMethod $h.Url -TimeoutSec 40 | Out-String
        $n = ([regex]::Matches($text, "#EXTINF")).Count
        Say Green ("Downloaded in {0:N1}s: {1} channels" -f $sw.Elapsed.TotalSeconds, $n)
        if ($n -gt 0) { $anyOk = $true }
    } catch { Say Red ("Download FAILED: " + $_.Exception.Message) }
}

Say Cyan "`n== Verdict =="
if ($anyOk) { Say Green "This PC can download the list. Update KwartoTV.exe to the latest and use Settings > Reload channels." }
else { Say Red "This PC cannot download any iptv-org list. Fix DNS / proxy / VPN / antivirus above, or download the .m3u on another device, copy it to this PC, and use Settings > Add playlist link/file." }
