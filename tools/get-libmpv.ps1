# Downloads libmpv-2.dll (the video engine) into src\KwartoTV.App\libs so it is copied next to KwartoTV.exe.
# Source: https://github.com/shinchiro/mpv-winbuild-cmake (the libmpv "dev" builds linked from mpv.io).
param([string]$Dest = "$PSScriptRoot\..\src\KwartoTV.App\libs")
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $Dest | Out-Null
$headers = @{ "User-Agent" = "KwartoTV-build" }
if ($env:GITHUB_TOKEN) { $headers["Authorization"] = "Bearer $env:GITHUB_TOKEN" }
$rel = Invoke-RestMethod -Uri "https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest" -Headers $headers
$asset = $rel.assets | Where-Object { $_.name -match '^mpv-dev-x86_64-\d{8}-git-.*\.7z$' } | Select-Object -First 1
if (-not $asset) { throw "No mpv-dev-x86_64 asset found in release $($rel.tag_name)" }
Write-Host "libmpv from $($rel.tag_name): $($asset.name)"
$tmp = Join-Path ([IO.Path]::GetTempPath()) "libmpv-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$archive = Join-Path $tmp $asset.name
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $archive -Headers @{ "User-Agent" = "KwartoTV-build" }
& 7z x $archive "-o$tmp\x" -y | Out-Null
$dll = Get-ChildItem -Path "$tmp\x" -Recurse -Filter "libmpv-2.dll" | Select-Object -First 1
if (-not $dll) { throw "libmpv-2.dll not found inside $($asset.name)" }
Copy-Item $dll.FullName (Join-Path $Dest "libmpv-2.dll") -Force
Write-Host ("libmpv-2.dll: {0:N1} MB" -f ((Get-Item (Join-Path $Dest "libmpv-2.dll")).Length / 1MB))
