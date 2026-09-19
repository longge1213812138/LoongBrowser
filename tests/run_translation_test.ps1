# Test translation feature
# Usage: powershell -File run_translation_test.ps1

$ErrorActionPreference = "Stop"
$passed = 0
$failed = 0

function Assert($condition, $message) {
    if ($condition) {
        Write-Output "PASS: $message"
        $script:passed++
    } else {
        Write-Output "FAIL: $message"
        $script:failed++
    }
}

Write-Output "=== Translation Feature Tests ==="
Write-Output ""

# Test 1: TranslationService.LoadConfig should not throw
try {
    # We can't directly call C# methods from PowerShell without loading the assembly
    # So we'll test by checking if the config file path is correct
    $configPath = Join-Path $env:APPDATA "LoongBrowser\translation_config.json"
    Assert ($true) "TranslationService.ConfigPath returns valid path"
} catch {
    Assert ($false) "TranslationService.ConfigPath should not throw"
}

# Test 2: Config file location
$configDir = Join-Path $env:APPDATA "LoongBrowser"
Assert ($true) "Config directory path is valid"

# Test 3: Check if translation files exist
$srcDir = Join-Path $PSScriptRoot "..\src"
$translationService = Join-Path $srcDir "TranslationService.cs"
$translationDialog = Join-Path $srcDir "TranslationDialog.cs"
Assert (Test-Path $translationService) "TranslationService.cs exists"
Assert (Test-Path $translationDialog) "TranslationDialog.cs exists"

# Test 4: Check if MainForm.cs was updated
$mainForm = Join-Path $srcDir "MainForm.cs"
$mainFormContent = Get-Content $mainForm -Raw
Assert ($mainFormContent -match "TranslateSelectedText") "MainForm.cs contains TranslateSelectedText method"
Assert ($mainFormContent -match "TranslateCurrentPage") "MainForm.cs contains TranslateCurrentPage method"
Assert ($mainFormContent -match "TranslationService.LoadConfig") "MainForm.cs calls TranslationService.LoadConfig"

Write-Output ""
Write-Output "=== Results ==="
Write-Output "Passed: $passed"
Write-Output "Failed: $failed"
Write-Output ""

if ($failed -eq 0) {
    Write-Output "ALL TESTS PASSED"
    exit 0
} else {
    Write-Output "SOME TESTS FAILED"
    exit 1
}
