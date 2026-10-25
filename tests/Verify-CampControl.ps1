param([string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$install = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -property installationPath
$dev = Join-Path $install 'Common7\Tools\VsDevCmd.bat'
$fixture = Join-Path $PSScriptRoot 'CampControl.Tests.cpp'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Nexus-CampFixture-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$exe = Join-Path $testRoot 'AnarchyOnline.exe'
$obj = Join-Path $testRoot 'CampFixture.obj'
$line = '"' + $dev + '" -arch=x86 -host_arch=x64 >nul && cl.exe /nologo /EHsc /std:c++17 /Fe:"' + $exe + '" /Fo:"' + $obj + '" "' + $fixture + '" user32.lib'
$process = $null
try {
    & cmd.exe /d /s /c $line
    if ($LASTEXITCODE -ne 0) { throw 'Camp fixture compilation failed' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Native camp tests failed' }
    # Launch only the binary compiled above; no production client or registry is used.
    $token = [Guid]::NewGuid().ToString('N')
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = [Diagnostics.ProcessStartInfo]::new($exe, $token)
    $process.StartInfo.UseShellExecute = $false; $process.StartInfo.CreateNoWindow = $true
    [void]$process.Start()
    $a = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
    $type = $a.GetType('AOLauncher.PresetCamping'); $flags = [Reflection.BindingFlags]'Static,NonPublic'
    $client = [Activator]::CreateInstance($a.GetType('AOLauncher.PresetCamping+Client'), $true)
    $client.ProcessId = $process.Id; $client.StartTimeUtcTicks = $process.StartTime.ToUniversalTime().Ticks
    $client.GamePath = $testRoot; $client.CharacterName = 'Fixture'; $client.CharacterId = 17; $client.CampToken = $token
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 50; $process.Refresh() } while ($process.MainWindowTitle -ne 'Anarchy Online - Fixture' -and $watch.Elapsed.TotalSeconds -lt 5)
    if (!$type.GetMethod('IsSameProcess', $flags).Invoke($null, @($client))) { throw 'Fixture identity validation failed' }
    $client.StartTimeUtcTicks++
    if ($type.GetMethod('SendCamp', $flags).Invoke($null, @($client)) -notlike 'Client changed*') { throw 'PID reuse guard failed' }
    $client.StartTimeUtcTicks--
    $result = $type.GetMethod('SendCamp', $flags).Invoke($null, @($client))
    if ($result -notlike '/camp accepted*') { throw "Native bridge rejected valid fixture: $result" }
    $result = $type.GetMethod('SendCamp', $flags).Invoke($null, @($client))
    if ($result -notlike 'The client declined*') { throw 'Native repeat guard failed' }
    Write-Host 'PASS: end-to-end managed identity check -> named events -> native UI-thread camp -> acknowledgement; stale identity and repeat rejection.'
} finally {
    if ($process) {
        if (!$process.HasExited) { [void]$process.CloseMainWindow(); [void]$process.WaitForExit(20000) }
        $process.Dispose()
    }
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'Nexus-CampFixture-*') { throw 'Unsafe cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
