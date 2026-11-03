param([string]$OutputDirectory=(Join-Path $PSScriptRoot '..\src\Launcher\obj\EmbeddedPluginHost'))
# Builds NexusPluginHost.exe (src/PluginHost). It references no AOSharp assembly at compile time;
# EasyHook and the bootstrapper are loaded at run time from the AOSharp pack folder.
# The source uses modern C# syntax, so this needs the Roslyn compiler: Visual Studio's (found with
# vswhere, as Build-Telemetry.ps1 does) or the .NET SDK's. The old .NET Framework csc.exe
# (C# 5) in Windows\Microsoft.NET cannot compile it.
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot '..\src\PluginHost\NexusPluginHost.cs'
$output=[IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output)|Out-Null
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$arguments=@('/nologo','/noconfig','/optimize+','/langversion:latest','/target:winexe','/platform:x86',"/out:$output\NexusPluginHost.exe",
  "/lib:$framework",'/r:mscorlib.dll','/r:System.dll','/r:System.Core.dll','/r:System.Web.Extensions.dll',$source)

$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$roslyn=if(Test-Path -LiteralPath $vswhere){& $vswhere -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1}
if($roslyn){
  & $roslyn @arguments
}else{
  $dotnet=Get-Command dotnet -ErrorAction SilentlyContinue
  if(!$dotnet){throw 'No Roslyn C# compiler found. Install Visual Studio (or Build Tools) or the .NET SDK.'}
  $sdk=(& $dotnet.Source --list-sdks | Select-Object -Last 1) -replace '^(\S+)\s+\[(.+)\]$','$2\$1'
  $csc=Join-Path $sdk 'Roslyn\bincore\csc.dll'
  if(!(Test-Path -LiteralPath $csc)){throw "The .NET SDK's C# compiler was not found at $csc."}
  # The SDK compiler needs the .NET Framework 4.8 reference assemblies (Developer Pack).
  $reference=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
  if(!(Test-Path -LiteralPath $reference)){throw 'Install the .NET Framework 4.8 Developer Pack (reference assemblies) or Visual Studio Build Tools.'}
  & $dotnet.Source $csc ($arguments -replace [regex]::Escape("/lib:$framework"),"/lib:$reference") '/nostdlib+'
}
if($LASTEXITCODE){throw 'Plugin host build failed.'}
Write-Host "Plugin host: $output\NexusPluginHost.exe"
