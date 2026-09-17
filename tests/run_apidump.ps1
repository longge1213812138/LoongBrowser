# Dump WebView2 API names (ASCII only)
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
$exe = Join-Path $env:TEMP "ApiDump.exe"
$log = Join-Path $root "apidump_log.txt"
& $csc /nologo /out:$exe (Join-Path $root "ApiDump.cs") 2>&1 | Out-File $log -Encoding utf8
if ($LASTEXITCODE -ne 0) { Write-Output "COMPILE FAILED"; exit 1 }
& $exe *>&1 | Out-File $log -Append -Encoding utf8
Write-Output "DONE"
exit 0
