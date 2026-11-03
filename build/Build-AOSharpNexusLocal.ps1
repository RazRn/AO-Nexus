param(
 [Parameter(Mandatory=$true)][string]$AOSharpSource,
 [string]$BasePack=(Join-Path $PSScriptRoot '..\runtime\Local13Pack'),
 [string]$OutputDirectory=(Join-Path $PSScriptRoot '..\dist\local-only\AOSharp-Nexus')
)
$ErrorActionPreference='Stop'
$source=[IO.Path]::GetFullPath($AOSharpSource)
$base=[IO.Path]::GetFullPath($BasePack)
$output=[IO.Path]::GetFullPath($OutputDirectory)
if($output -eq $base -or $output.StartsWith($base.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){
 throw 'Use a separate output folder; the existing installation must be preserved.'
}
if(Test-Path -LiteralPath $output){throw 'Output already exists. Choose a new output folder to preserve existing files.'}
foreach($file in @('AOSharp.Common\IPC\AuthenticatedPacket.cs','AOSharp.Common\PluginRuntimeStatus.cs')){
 if(!(Test-Path -LiteralPath (Join-Path $source $file))){throw 'Apply AOSharp-Nexus-Local.patch to the local AOSharp source first. See docs/development/log/Optional-Components.txt.'}
}
if(!(Test-Path -LiteralPath (Join-Path $base 'AOSharp\AOSharp.Core.dll'))){throw 'BasePack must contain AOSharp and Plugins folders.'}
$msbuild=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
if(!$msbuild){throw 'Visual Studio MSBuild was not found.'}
& $msbuild (Join-Path $source 'AOSharp.Core\AOSharp.Core.csproj') /p:Configuration=Release /p:Platform=AnyCPU /m:1 /nologo /verbosity:minimal
if($LASTEXITCODE){throw 'Local AOSharp build failed.'}
[IO.Directory]::CreateDirectory($output)|Out-Null
Get-ChildItem -LiteralPath $base -Force | Copy-Item -Destination $output -Recurse -Force
foreach($name in @('AOSharp.Common','AOSharp.Bootstrap','AOSharp.Core')){
 Copy-Item -LiteralPath (Join-Path $source "$name\bin\Release\$name.dll") -Destination (Join-Path $output "AOSharp\$name.dll") -Force
}
$record=[ordered]@{
 Purpose='LOCAL ONLY: redistribution permission has not been established.'
 UpstreamBase='d55eb12b5a763e5ed65851e69c50343de6c6d73c'
 Source=$source
 BuiltUtc=[DateTime]::UtcNow.ToString('o')
 Changes=@('Existing Pet type fix','Initialization acknowledgements','Command and combat-handler conflict reports','Authenticated same-user, same-logon-session IPC')
 Files=@(foreach($name in @('AOSharp.Common','AOSharp.Bootstrap','AOSharp.Core')){
  @{Path="AOSharp/$name.dll";Sha256=(Get-FileHash -LiteralPath (Join-Path $output "AOSharp\$name.dll")).Hash}
 })
}
[IO.File]::WriteAllText((Join-Path $output 'LOCAL-ONLY-BUILD.json'),($record|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
Write-Host "Local-only AOSharp pack ready: $output"
