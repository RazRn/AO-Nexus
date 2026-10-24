param([string]$DotNetPath = (Join-Path $env:LOCALAPPDATA 'AOSharpBuildTools\dotnet\dotnet.exe'))
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('AOLauncher-TrackedLimits-' + [Guid]::NewGuid().ToString('N'))
$output = Join-Path $testRoot 'output'
$game = Join-Path $testRoot 'game fixture'
[IO.Directory]::CreateDirectory($game) | Out-Null
[IO.Directory]::CreateDirectory($output) | Out-Null
try {
    $sources = @(
        (Join-Path $PSScriptRoot 'AccountLimits.Tests.cs'),
        (Join-Path $repo 'src\Backend\AOQuickLauncher\LaunchQueue.cs'),
        (Join-Path $repo 'src\Backend\AOQuickLauncher\ClientSessions.cs'),
        (Join-Path $repo 'src\Backend\AOQuickLauncher\CreationMonitor.cs'),
        (Join-Path $repo 'src\Common\ServerAccountPolicy.cs'),
        (Join-Path $repo 'src\Common\Texts.cs')
    )
    $compile = ($sources | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join [Environment]::NewLine
    $project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><StartupObject>AOQuickLauncher.AccountLimitsTests</StartupObject></PropertyGroup><ItemGroup>' + $compile + '</ItemGroup></Project>'
    $projectPath = Join-Path $testRoot 'TrackedLimits.csproj'
    [IO.File]::WriteAllText($projectPath, $project)
    & $DotNetPath build $projectPath --configuration Release --output $output --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Tracked account limit test harness failed to compile.' }
    [IO.File]::WriteAllBytes((Join-Path $output 'LoginHandler.dll'), [byte[]]@())
    [IO.File]::WriteAllBytes((Join-Path $game 'AnarchyOnline.exe'), [byte[]]@())
    & $DotNetPath (Join-Path $output 'TrackedLimits.dll') $game
    if ($LASTEXITCODE -ne 0) { throw 'Tracked account limit fixture tests failed.' }
} finally {
    # This fresh GUID folder contains only this harness and inert fixture files.
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'AOLauncher-TrackedLimits-*') { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
