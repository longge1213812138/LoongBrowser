# Dump WebView2 favicon API names (ASCII only)
$root = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { Write-Output "ERROR: csc.exe not found"; exit 1 }

$tmp = Join-Path $root "temp"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$exe = Join-Path $tmp "FaviconApiDump.exe"
$log = Join-Path $tmp "favicon_api.txt"

& $csc /nologo /out:$exe (Join-Path $PSScriptRoot "FaviconApiDump.cs") 2>&1 | Out-File $log -Encoding utf8
if ($LASTEXITCODE -ne 0) {
    "COMPILE FAILED" | Out-File $log -Append -Encoding utf8
    Write-Output "COMPILE FAILED"
    exit 1
}

[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
& $exe *>&1 | Out-File $log -Append -Encoding utf8
Write-Output ("DONE log=" + $log)
exit 0
