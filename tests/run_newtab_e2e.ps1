# Build & run the new tab page end-to-end test (ASCII only)
# Usage: powershell -File tests\run_newtab_e2e.ps1
$root = Split-Path $PSScriptRoot -Parent

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { Write-Output "ERROR: csc.exe not found"; exit 1 }

$outDir = Join-Path $PSScriptRoot "_out_newtab_e2e"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Copy-Item (Join-Path $root "libs\*.dll") $outDir -Force

$exe = Join-Path $outDir "NewTabE2ETest.exe"
$log = Join-Path $PSScriptRoot "newtab_e2e_log.txt"

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
$src += (Join-Path $PSScriptRoot "NewTabE2ETest.cs")

$cmdArgs = @("/nologo", "/target:exe", "/codepage:65001", "/main:LoongBrowser.NewTabE2ETest", ("/out:" + $exe))
foreach ($r in $refs) { $cmdArgs += ("/r:" + $r) }
foreach ($s in $src) { $cmdArgs += $s }

& $csc $cmdArgs 2>&1 | Out-File $log -Encoding utf8
if ($LASTEXITCODE -ne 0) {
    "COMPILE FAILED" | Out-File $log -Append -Encoding utf8
    Write-Output "COMPILE FAILED - see tests\newtab_e2e_log.txt"
    exit 1
}

$code = 0
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
& $exe *>&1 | Out-File $log -Append -Encoding utf8
if ($LASTEXITCODE -ne 0) { $code = 1; "E2E TEST FAILED" | Out-File $log -Append -Encoding utf8 }

Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Output ("DONE exit=" + $code + " log=" + $log)
exit $code
