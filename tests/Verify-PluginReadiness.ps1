$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$out=Join-Path $root 'logs/plugin-readiness'
[IO.Directory]::CreateDirectory($out)|Out-Null
$vs=& "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products '*' -property installationPath
$compiler=Join-Path $vs 'MSBuild/Current/Bin/Roslyn/csc.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$out/PluginReadiness.Tests.exe" (Join-Path $PSScriptRoot 'PluginReadiness.Tests.cs')
if($LASTEXITCODE){throw 'Managed fixture compilation failed'}
& "$out/PluginReadiness.Tests.exe" "$root/dist/AONexus.exe"
if($LASTEXITCODE){throw 'Managed readiness fixture failed'}
$dev=Join-Path $vs 'Common7/Tools/VsDevCmd.bat'
$command='"'+$dev+'" -arch=x86 -host_arch=x64 >nul && cl.exe /nologo /O2 /MT /EHsc /Fe:"'+$out+'/NativeReadiness.exe" /Fo:"'+$out+'/NativeReadiness.obj" "'+$PSScriptRoot+'/PluginReadiness.Tests.cpp"'
& $env:ComSpec /d /s /c $command
if($LASTEXITCODE){throw 'Native fixture compilation failed'}
& "$out/NativeReadiness.exe"
if($LASTEXITCODE){throw 'Native readiness fixture failed'}
