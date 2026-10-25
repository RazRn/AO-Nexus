param([switch]$LiveNews)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $projectRoot 'tests'
$logRoot = Join-Path $projectRoot 'logs'
$launcher = Join-Path $projectRoot 'dist\AONexus.exe'
[IO.Directory]::CreateDirectory($logRoot) | Out-Null
$x86 = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
$compiler = & (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe') -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
foreach ($test in @('Verify-Launcher', 'Verify-ServerAccountLimits', 'Verify-CharacterSlots', 'Verify-CharacterCreation', 'Verify-CreatedCharacters', 'Verify-ProfileImport', 'Verify-NewsPanel', 'Verify-CompactDetails', 'Verify-SlimScrolling', 'Verify-DialogTheme', 'Verify-PasswordPreferences', 'Verify-SavedPasswordUpdate', 'Verify-Tray', 'Verify-PresetHotkeys')) {
    & $x86 -NoProfile -ExecutionPolicy Bypass -STA -File (Join-Path $testRoot ($test + '.ps1')) -AssemblyPath $launcher *> (Join-Path $logRoot ($test + '.txt'))
    if ($LASTEXITCODE -ne 0) { throw "Failed: $test. See logs." }
    Write-Host "PASS: $test"
}
foreach ($test in @('Verify-AccountLaunchLimits', 'Verify-TrackedAccountLimits', 'Verify-CreationMonitor')) {
    & (Join-Path $testRoot ($test + '.ps1')) *> (Join-Path $logRoot ($test + '.txt'))
    Write-Host "PASS: $test"
}
& (Join-Path $testRoot 'Verify-BackendRunner.ps1') -Compiler $compiler *> (Join-Path $logRoot 'Verify-BackendRunner.txt')
& (Join-Path $testRoot 'Verify-DesktopLifetime.ps1') -Compiler $compiler *> (Join-Path $logRoot 'Verify-DesktopLifetime.txt')
& $x86 -NoProfile -ExecutionPolicy Bypass -STA -File (Join-Path $testRoot 'Verify-CampControl.ps1') -AssemblyPath $launcher *> (Join-Path $logRoot 'Verify-CampControl.txt')
if ($LASTEXITCODE -ne 0) { throw 'Native camp control verification failed. See logs.' }
& (Join-Path $testRoot 'Verify-EmbeddedBackend.ps1') -Compiler $compiler -LauncherPath $launcher *> (Join-Path $logRoot 'Verify-EmbeddedBackend.txt')
& (Join-Path $testRoot 'Verify-News.ps1') -Live:$LiveNews *> (Join-Path $logRoot 'Verify-News.txt')
& (Join-Path $PSScriptRoot 'Build-Payload.ps1') -ValidateOnly -VerifyExecutable $launcher
Write-Host 'All checks passed. Tests use synthetic accounts, isolated stores, and inert game fixtures.'
Write-Host 'No real game login or injection is performed by this test suite.'
