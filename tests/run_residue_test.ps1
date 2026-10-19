# Build & run the close-residue test (ASCII only)
# Two scenarios: "hover" (hover a toolbar button, then close) and "startup-bookmark"
# (the user's exact steps: bookmark the startup page, dismiss the box, close the window).
$root = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $root "tools\roslyn\csc.exe"
if (-not (Test-Path $csc)) { Write-Output "ERROR: roslyn csc not found"; exit 1 }
$outDir = Join-Path $PSScriptRoot "_out_residue"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Copy-Item (Join-Path $root "libs\*.dll") $outDir -Force
$exe = Join-Path $outDir "CloseResidueTest.exe"
$log = Join-Path $PSScriptRoot "residue_test_log.txt"
$refs = @("System.Windows.Forms.dll","System.Drawing.dll","System.Web.Extensions.dll","System.Runtime.WindowsRuntime.dll","System.Runtime.Serialization.dll","System.Security.dll",
  (Join-Path $root "libs\Microsoft.Web.WebView2.Core.dll"), (Join-Path $root "libs\Microsoft.Web.WebView2.WinForms.dll"))
$src = @()
Get-ChildItem (Join-Path $root "src") -Filter *.cs | Where-Object { $_.Name -ne "Installer.cs" } | ForEach-Object { $src += $_.FullName }
$src += (Join-Path $PSScriptRoot "CloseResidueTest.cs")
$cmdArgs = @("/nologo","/target:exe","/codepage:65001","/langversion:latest","/main:LoongBrowser.CloseResidueTest",("/out:" + $exe))
foreach ($r in $refs) { $cmdArgs += ("/r:" + $r) }
foreach ($s in $src) { $cmdArgs += $s }
& $csc $cmdArgs 2>&1 | Out-File $log -Encoding utf8
if ($LASTEXITCODE -ne 0) { "COMPILE FAILED" | Out-File $log -Append -Encoding utf8; Write-Output "COMPILE FAILED - see tests\residue_test_log.txt"; exit 1 }

$code = 0
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
foreach ($m in @("hover","startup-bookmark")) {
    "##### scenario=$m #####" | Out-File $log -Append -Encoding utf8
    & $exe $m *>&1 | Out-File $log -Append -Encoding utf8
    if ($LASTEXITCODE -ne 0) { $code = 1; "SCENARIO FAILED: $m" | Out-File $log -Append -Encoding utf8 }
}
if ($code -ne 0) { "TEST FAILED" | Out-File $log -Append -Encoding utf8 }
Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Output ("DONE exit=" + $code + " log=" + $log)
exit $code
