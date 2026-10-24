param(
    [string]$DotNetPath = (Join-Path $env:LOCALAPPDATA 'AOSharpBuildTools\dotnet\dotnet.exe'),
    [string]$NativeReporterPath
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('AONexus-CreationMonitor-' + [Guid]::NewGuid().ToString('N'))
$output = Join-Path $testRoot 'output'
[IO.Directory]::CreateDirectory($output) | Out-Null
try {
    $sources = @(
        (Join-Path $PSScriptRoot 'CreationMonitor.Tests.cs'),
        (Join-Path $PSScriptRoot 'AccountLimits.Tests.cs'),
        (Join-Path $repo 'src\Backend\AOQuickLauncher\LaunchQueue.cs'),
        (Join-Path $repo 'src\Backend\AOQuickLauncher\CreationMonitor.cs'),
        (Join-Path $repo 'src\Backend\AOQuickLauncher\ClientSessions.cs'),
        (Join-Path $repo 'src\Common\ServerAccountPolicy.cs'),
        (Join-Path $repo 'src\Common\Texts.cs')
    )
    $compile = ($sources | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join [Environment]::NewLine
    $project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><StartupObject>AOQuickLauncher.CreationMonitorTests</StartupObject></PropertyGroup><ItemGroup>' + $compile + '</ItemGroup></Project>'
    $projectPath = Join-Path $testRoot 'CreationMonitor.csproj'
    [IO.File]::WriteAllText($projectPath, $project)
    & $DotNetPath build $projectPath --configuration Release --output $output --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Creation-monitor test harness failed to compile.' }
    if ($NativeReporterPath) { & $DotNetPath (Join-Path $output 'CreationMonitor.dll') ([IO.Path]::GetFullPath($NativeReporterPath)) }
    else { & $DotNetPath (Join-Path $output 'CreationMonitor.dll') }
    if ($LASTEXITCODE -ne 0) { throw 'Creation-monitor tests failed.' }
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'AONexus-CreationMonitor-*') { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
