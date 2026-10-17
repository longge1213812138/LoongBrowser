# Build & run the chrome (Vista glass shell) preview test - renders PNG screenshots.
# Usage: powershell -File tests\run_chrome_preview.ps1
# Compiles with the vendored Roslyn compiler (tools\roslyn\csc.exe), same as build.ps1.
$root = Split-Path $PSScriptRoot -Parent

$csc = Join-Path $root "tools\roslyn\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
}
if (-not (Test-Path $csc)) { Write-Output "ERROR: csc.exe not found"; exit 1 }

$outDir = Join-Path $PSScriptRoot "_out_chrome"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Copy-Item (Join-Path $root "libs\*.dll") $outDir -Force

$exe = Join-Path $outDir "ChromePreviewTest.exe"
$log = Join-Path $PSScriptRoot "chrome_preview_log.txt"

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
$src += (Join-Path $PSScriptRoot "ChromePreviewTest.cs")

# /main: src\Program.cs also declares a Main, so pick our entry point explicitly.
$cmdArgs = @("/nologo", "/target:exe", "/codepage:65001", "/langversion:latest",
             "/main:LoongBrowser.ChromePreviewTest", ("/out:" + $exe))
foreach ($r in $refs) { $cmdArgs += ("/r:" + $r) }
foreach ($s in $src) { $cmdArgs += $s }

& $csc $cmdArgs 2>&1 | Out-File $log -Encoding utf8
if ($LASTEXITCODE -ne 0) {
    "COMPILE FAILED" | Out-File $log -Append -Encoding utf8
    Write-Output "COMPILE FAILED - see tests\chrome_preview_log.txt"
    exit 1
}

$code = 0
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
& $exe *>&1 | Out-File $log -Append -Encoding utf8
if ($LASTEXITCODE -ne 0) { $code = 1; "TEST FAILED" | Out-File $log -Append -Encoding utf8 }

# copy the rendered PNGs out before the temp dir gets removed
$shots = @("chrome_preview.png", "chrome_preview_raw.png", "chrome_preview_zoom.png", "chrome_preview_text_lab.png")
foreach ($name in $shots) {
    $from = Join-Path $outDir $name
    if (Test-Path $from) {
        Copy-Item $from (Join-Path $PSScriptRoot $name) -Force
        Write-Output ("shot: " + (Join-Path $PSScriptRoot $name))
    }
}

Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Output ("DONE exit=" + $code + " log=" + $log)
exit $code
