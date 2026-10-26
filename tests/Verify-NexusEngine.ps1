param([string]$EngineDirectory = (Join-Path $PSScriptRoot '..\runtime\NexusEngine'))
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $EngineDirectory).Path
$engine = Join-Path $root 'NexusEngine.exe'
# Exercise the published entry point, without launching games or reading profiles.
$marker = 'SYNTHETIC_CLI_SECRET_MUST_NOT_BE_ECHOED'
foreach ($arguments in @('', ('fixture ' + $marker + ' 17'), '--help', '--check')) {
    $start = [Diagnostics.ProcessStartInfo]::new($engine, $arguments)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (!$process.Start()) { throw 'Engine did not start.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(10000)) { throw 'Engine argument rejection timed out.' }
        $text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 1 -or !$text.Contains('Start AONexus.exe.') -or $text.Contains($marker)) {
            throw 'Standalone CLI must reject arguments without echoing them.'
        }
    } finally { $process.Dispose() }
}
$deps = Get-Content -LiteralPath (Join-Path $root 'NexusEngine.deps.json') -Raw
if ($deps -match 'CommandLineParser') { throw 'Removed parser dependency was bundled.' }
if (Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $_.Name -match '^(AOQuickLauncher|CommandLine)\.' -or $_.Name -eq 'Login.bat' }) {
    throw 'Obsolete standalone launcher files were bundled.'
}
Write-Host 'PASS: Nexus-only worker rejects standalone commands without exposing arguments; removed launcher/parser artifacts are absent.'
