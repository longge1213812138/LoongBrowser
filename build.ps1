# Build script for LoongBrowser (ASCII only)
# Usage: powershell -File build.ps1
#
# Compiler: a vendored Roslyn (Microsoft.Net.Compilers.Toolset, net472 build) at
# tools/roslyn/csc.exe. Roslyn understands modern C# (string interpolation $"..." etc.)
# AND compiles against .NET Framework 4.x, so the existing bare-name references
# (System.Windows.Forms.dll, ...) keep resolving exactly as before. The compiled
# LoongBrowser.exe still has ZERO runtime dependencies.
$ErrorActionPreference = "Continue"
$root = $PSScriptRoot

# ---------- resolve a modern C# compiler (Roslyn, C# 6+) ----------
function Ensure-Roslyn {
    param($root)
    $dir = Join-Path $root "tools\roslyn"
    $exe = Join-Path $dir "csc.exe"
    if (Test-Path $exe) { return $exe }

    # First-run bootstrap: fetch the toolset once (needs network), then retry.
    $pkg = "microsoft.net.compilers.toolset"
    $ver = "4.8.0"
    $url = "https://api.nuget.org/v3-flatcontainer/$pkg/$ver/$pkg.$ver.nupkg"
    $tmp = Join-Path $env:TEMP "roslyn-toolset.nupkg"
    try {
        (New-Object System.Net.WebClient).DownloadFile($url, $tmp)
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($tmp)
        foreach ($e in $zip.Entries) {
            if ($e.FullName -like "tasks/net472/*") {
                $dest = Join-Path $dir ($e.FullName -replace "tasks/net472/","")
                $parent = Split-Path $dest
                if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $dest, $true)
            }
        }
        $zip.Dispose()
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Output "WARN: could not auto-fetch Roslyn compiler ($_)"
    }
    if (Test-Path $exe) { return $exe } else { return $null }
}

$csc = Ensure-Roslyn $root
$useRoslyn = $false
if (-not $csc) {
    # Fallback to the legacy Framework csc (C# 5 only). $"..." interpolation will FAIL here.
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
    if (-not (Test-Path $csc)) { Write-Output "ERROR: no C# compiler found"; exit 1 }
    Write-Output "WARN: using legacy csc (C# 5) — string interpolation is NOT supported. Run the Roslyn bootstrap to upgrade."
} else {
    $useRoslyn = $true
    Write-Output "Using Roslyn compiler: $csc"
}

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
if ($useRoslyn) { $appArgs += "/langversion:latest" }
# Application icon: Win32 resource baked into the exe by csc itself (/win32icon).
# No .resx / .rc / resource files are introduced, so the "zero resource files"
# and "no third-party dependency" constraints still hold.
$appIcon = Join-Path $root "icons\LoongBrowser.ico"
if (Test-Path $appIcon) {
    $appArgs += ("/win32icon:" + $appIcon)
} else {
    Write-Output "WARN: icons\LoongBrowser.ico not found - the exe will fall back to the default icon"
}
foreach ($r in $appRefs) { $appArgs += ("/r:" + $r) }
foreach ($s in $appSrc) { $appArgs += $s }

& $csc $appArgs 2>&1 | Out-File $log -Append -Encoding utf8
if ($LASTEXITCODE -ne 0) {
    "APP BUILD FAILED" | Out-File $log -Append -Encoding utf8
    Write-Output "APP BUILD FAILED - see build_log.txt"
    exit 1
}

# ---------- 2) installer ----------
# The installer links FileAssociations.cs too: file/URL registration must have ONE
# implementation, or installer and app drift apart and "Open with" ends up with
# duplicate LoongBrowser entries coming from two different code paths.
$insSrc = @(
    (Join-Path $root "src\Installer.cs"),
    (Join-Path $root "src\FileAssociations.cs")
)
$insRefs = @("System.Windows.Forms.dll", "System.Drawing.dll", "Microsoft.CSharp.dll")
$insArgs = @("/nologo", "/target:winexe", "/codepage:65001", "/optimize+", ("/out:" + (Join-Path $dist "LoongBrowserSetup.exe")))
if ($useRoslyn) { $insArgs += "/langversion:latest" }
# The installer gets the same icon: file managers and Add/Remove Programs show
# LoongBrowserSetup.exe's own icon, and the installer also writes DisplayIcon.
if (Test-Path $appIcon) { $insArgs += ("/win32icon:" + $appIcon) }
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
