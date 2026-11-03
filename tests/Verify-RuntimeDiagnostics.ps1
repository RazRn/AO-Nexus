param([Parameter(Mandatory=$true)][string]$OldRuntime,[Parameter(Mandatory=$true)][string]$NewRuntime,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
[IO.Directory]::CreateDirectory($OutputDirectory)|Out-Null
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
foreach($name in @('Good','Second','Bad')){
 $args=@('/nologo','/target:library','/platform:x86',"/r:$OldRuntime\AOSharp.Core.dll","/r:$OldRuntime\AOSharp.Common.dll","/out:$OutputDirectory\$name.dll",(Join-Path $PSScriptRoot 'RuntimeFixturePlugin.cs'))
 if($name -eq 'Bad'){$args+='/define:FAIL'}
 & $compiler @args
 if($LASTEXITCODE){throw 'Old-runtime fixture failed to compile.'}
}
Get-ChildItem -LiteralPath $NewRuntime -Filter '*.dll' | Copy-Item -Destination $OutputDirectory -Force
$exe=Join-Path $OutputDirectory 'RuntimeDiagnostics.Tests.exe'
& $compiler /nologo /target:exe /platform:x86 /langversion:7.3 "/out:$exe" "/r:$OutputDirectory\AOSharp.Bootstrap.dll" "/r:$OutputDirectory\AOSharp.Common.dll" "/r:$OutputDirectory\Newtonsoft.Json.dll" (Join-Path $PSScriptRoot 'RuntimeDiagnostics.Tests.cs')
if($LASTEXITCODE){throw 'Runtime diagnostic test compilation failed.'}
& $exe
if($LASTEXITCODE){throw 'Runtime diagnostic tests failed.'}
