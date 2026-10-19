# Build & run the startup page test (ASCII only)
# Usage: powershell -File tests\run_startup_test.ps1
$root = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $root "tools\roslyn\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { Write-Output "ERROR: csc.exe not found"; exit 1 }
$langArgs = @()
if ($csc -like "*roslyn*") { $langArgs += "/langversion:latest" }

$outDir = Join-Path $PSScriptRoot "_out_startup"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Copy-Item (Join-Path $root "libs\*.dll") $outDir -Force

$exe = Join-Path $outDir "StartupPageTest.exe"
$log = Join-Path $PSScriptRoot "startup_test_log.txt"

$refs = @(
    "System.Windows.Forms.dll",
    "System.Drawing.dll",
    "System.Web.Extensions.dll",
    "System.Runtime.WindowsRuntime.dll",
    "System.Runtime.Serialization.dll",
    "System.Security.dll",
    (Join-Path $root "libs\Microsoft.Web.WebView2.Core.dll"),
    (Join-Path $root "libs\Microsoft.Web.WebView2.WinForms.dll")
)

$src = @()
Get-ChildItem (Join-Path $root "src") -Filter *.cs |
    Where-Object { $_.Name -ne "Installer.cs" } |
    ForEach-Object { $src += $_.FullName }
$src += (Join-Path $PSScriptRoot "StartupPageTest.cs")

$cmdArgs = @("/nologo", "/target:exe", "/codepage:65001", "/main:LoongBrowser.StartupPageTest") + $langArgs + @("/out:" + $exe)
foreach ($r in $refs) { $cmdArgs += ("/r:" + $r) }
foreach ($s in $src) { $cmdArgs += $s }

& $csc $cmdArgs 2>&1 | Out-File $log -Encoding utf8
if ($LASTEXITCODE -ne 0) {
    "COMPILE FAILED" | Out-File $log -Append -Encoding utf8
    Write-Output "COMPILE FAILED - see tests\startup_test_log.txt"
    exit 1
}

$code = 0
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
& $exe *>&1 | Out-File $log -Append -Encoding utf8
if ($LASTEXITCODE -ne 0) { $code = 1; "TEST FAILED" | Out-File $log -Append -Encoding utf8 }

Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Output ("DONE exit=" + $code + " log=" + $log)
exit $code
