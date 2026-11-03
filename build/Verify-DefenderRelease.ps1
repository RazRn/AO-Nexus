param(
    [Parameter(Mandatory=$true)][string[]]$ArtifactPath,
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DefenderRelease.Policy.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$ReportPath) { $ReportPath = Join-Path $root ('logs/defender-release-' + [guid]::NewGuid().ToString('N') + '.json') }
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
# Validate before entering the reporting finally block, which writes these paths.
foreach ($inputPath in $ArtifactPath) {
    $candidate = [IO.Path]::GetFullPath($inputPath)
    if ($candidate -eq $ReportPath -or $candidate.StartsWith($ReportPath + '.', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Scan reports cannot overwrite scanned artifacts.'
    }
}
[IO.Directory]::CreateDirectory((Split-Path -Parent $ReportPath)) | Out-Null
$report = [ordered]@{ Result='Blocked'; StartedUtc=[datetime]::UtcNow; CompletedUtc=$null; Defender=$null; Artifacts=@(); Error=$null }
try {
    $status = Get-MpComputerStatus -ErrorAction Stop
    Assert-DefenderReady $status (Get-MpPreference -ErrorAction Stop)
    $report.Defender = $status | Select-Object AMProductVersion,AMEngineVersion,AntivirusSignatureVersion,AntivirusSignatureLastUpdated
    $platform = Join-Path $env:ProgramData 'Microsoft/Windows Defender/Platform'
    $scanner = Get-ChildItem -LiteralPath $platform -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'MpCmdRun.exe' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (!$scanner) { $scanner = Join-Path $env:ProgramFiles 'Windows Defender/MpCmdRun.exe' }
    if (!(Test-Path -LiteralPath $scanner -PathType Leaf)) { throw 'Microsoft Defender command-line scanner is unavailable.' }
    foreach ($inputPath in $ArtifactPath) {
        $path = [IO.Path]::GetFullPath($inputPath)
        $entry = [ordered]@{ Path=$path; Sha256=$null; ExclusionExitCode=$null; ScanExitCode=$null; Detections=@(); Result='Blocked' }
        $report.Artifacts += $entry
        $active = @(Get-MpThreat -ErrorAction Stop | Where-Object IsActive | Select-Object -ExpandProperty ThreatID)
        $entry.Detections = @(Get-MpThreatDetection -ErrorAction Stop | Where-Object {
            $detection = $_
            $active -contains $detection.ThreatID -and @($detection.Resources | Where-Object { Test-DefenderResourcePath $_ $path }).Count -gt 0
        } | Select-Object ThreatID,InitialDetectionTime,Resources)
        if ($entry.Detections.Count -gt 0) { throw "Active Defender detection applies to $path." }
        $entry.Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        $exclusionOutput = & $scanner -CheckExclusion -Path $path 2>&1
        $entry.ExclusionExitCode = $LASTEXITCODE
        $exclusionOutput | Out-File -LiteralPath ($ReportPath + '.' + $report.Artifacts.Count + '.exclusion.txt') -Encoding utf8
        if ($entry.ExclusionExitCode -ne 1) { throw 'Artifact is excluded, or Defender could not verify its exclusion status. Run the release check in an elevated PowerShell.' }
        $scanStart = Get-Date
        # Normal remediation remains enabled; never execute the submitted artifact.
        $scanOutput = & $scanner -Scan -ScanType 3 -File $path 2>&1
        $entry.ScanExitCode = $LASTEXITCODE
        $scanOutput | Out-File -LiteralPath ($ReportPath + '.' + $report.Artifacts.Count + '.scan.txt') -Encoding utf8
        $active = @(Get-MpThreat -ErrorAction Stop | Where-Object IsActive | Select-Object -ExpandProperty ThreatID)
        $entry.Detections = @(Get-MpThreatDetection -ErrorAction Stop | Where-Object {
            $detection = $_
            ($active -contains $detection.ThreatID -or $detection.InitialDetectionTime -ge $scanStart -or $detection.LastThreatStatusChangeTime -ge $scanStart) -and
                @($detection.Resources | Where-Object { Test-DefenderResourcePath $_ $path }).Count -gt 0
        } | Select-Object ThreatID,InitialDetectionTime,Resources)
        $afterHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        Assert-DefenderScanResult $entry.ScanExitCode $entry.Sha256 $afterHash $entry.Detections
        Assert-DefenderReady (Get-MpComputerStatus -ErrorAction Stop) (Get-MpPreference -ErrorAction Stop)
        $entry.Result = 'NoDetectionObserved'
    }
    $report.Result = 'NoDetectionObserved'
} catch {
    $report.Error = $_.Exception.Message
    throw
} finally {
    $report.CompletedUtc = [datetime]::UtcNow
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
    Write-Host "Defender release check: $($report.Result). Report: $ReportPath"
}
