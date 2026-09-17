# Build script for LoongBrowser (ASCII only)
# Usage: powershell -File build.ps1
$ErrorActionPreference = "Continue"
$root = $PSScriptRoot

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { Write-Output "ERROR: csc.exe not found"; exit 1 }

$dist = Join-Path $root "dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$log = Join-Path $root "build_log.txt"
("Build started " + (Get-Date)) | Out-File $log -Encoding utf8

# ---------- 1) main app ----------
$appRefs = @(
    "System.Windows.Forms.dll",
    "System.Drawing.dll",
    "System.Web.Extensions.dll",
    "System.Runtime.WindowsRuntime.dll",
    "System.Runtime.Serialization.dll",
    "System.Security.dll",
    (Join-Path $root "libs\Microsoft.Web.WebView2.Core.dll"),
    (Join-Path $root "libs\Microsoft.Web.WebView2.WinForms.dll")
)
$appSrc = Get-ChildItem (Join-Path $root "src") -Filter *.cs |
    Where-Object { $_.Name -ne "Installer.cs" } |
    ForEach-Object { $_.FullName }

$appArgs = @("/nologo", "/target:winexe", "/codepage:65001", "/optimize+", ("/out:" + (Join-Path $dist "LoongBrowser.exe")))
foreach ($r in $appRefs) { $appArgs += ("/r:" + $r) }
foreach ($s in $appSrc) { $appArgs += $s }

& $csc $appArgs 2>&1 | Out-File $log -Append -Encoding utf8
if ($LASTEXITCODE -ne 0) {
    "APP BUILD FAILED" | Out-File $log -Append -Encoding utf8
    Write-Output "APP BUILD FAILED - see build_log.txt"
    exit 1
}

# ---------- 2) installer ----------
$insSrc = Join-Path $root "src\Installer.cs"
$insRefs = @("System.Windows.Forms.dll", "System.Drawing.dll", "Microsoft.CSharp.dll")
$insArgs = @("/nologo", "/target:winexe", "/codepage:65001", "/optimize+", ("/out:" + (Join-Path $dist "LoongBrowserSetup.exe")))
foreach ($r in $insRefs) { $insArgs += ("/r:" + $r) }
$insArgs += $insSrc

& $csc $insArgs 2>&1 | Out-File $log -Append -Encoding utf8
if ($LASTEXITCODE -ne 0) {
    "INSTALLER BUILD FAILED" | Out-File $log -Append -Encoding utf8
    Write-Output "INSTALLER BUILD FAILED - see build_log.txt"
    exit 1
}

# ---------- 3) dependencies next to exe (dist folder = installer package) ----------
Copy-Item (Join-Path $root "libs\*.dll") $dist -Force

"BUILD OK" | Out-File $log -Append -Encoding utf8
Write-Output "BUILD OK"
Get-ChildItem $dist | ForEach-Object { Write-Output ($_.Name + "  " + [math]::Round($_.Length / 1KB) + " KB") }
exit 0
