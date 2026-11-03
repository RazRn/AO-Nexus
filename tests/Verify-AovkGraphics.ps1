param([string]$PreparedGame, [string]$PrkGame, [switch]$TransactionOnly, [switch]$SkipLegacyUi15, [switch]$ClientCompatibilityOnly, [switch]$Installed081Only)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compiler = & (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe') -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Visual Studio C# compiler not found.' }
$output = Join-Path $root 'logs/AovkGraphics.Tests.exe'
[IO.Directory]::CreateDirectory((Split-Path $output)) | Out-Null
if(!$TransactionOnly -and !$PreparedGame){throw 'Supply -PreparedGame or use -TransactionOnly.'}
$resources=@("/resource:$root\src\Launcher\Assets\AovkDefault.ini,AOLauncher.AOVK.defaults.ini")
if(!$TransactionOnly -and !$ClientCompatibilityOnly -and !$Installed081Only){
 & (Join-Path $root 'build/Build-AovkPayload.ps1')
 foreach($name in @('randy31.dll','version-rebirth.dll','randy31-ui.dll','version-rebirth-ui.dll')){
  $resources+="/resource:$root\runtime\AOVKPackage\$name,AOLauncher.AOVK.$name"
 }
}
. (Join-Path $PSScriptRoot 'CompilerInputs.ps1')
$nexusCompilerInputs = @(Get-NexusCompilerInputs)
& $compiler @nexusCompilerInputs /nologo /target:exe /platform:x86 /langversion:7.3 /r:System.Web.Extensions.dll "/out:$output" (Join-Path $root 'src/Launcher/AovkGraphics.cs') (Join-Path $PSScriptRoot 'AovkGraphics.Tests.cs') @resources
if ($LASTEXITCODE -ne 0) { throw 'Graphics test compilation failed.' }
$fixtures=@(if($TransactionOnly){'--transaction-only'}else{$PreparedGame}); if ($PrkGame -and !$TransactionOnly) { $fixtures+=$PrkGame }
if($SkipLegacyUi15){$fixtures+='--skip-legacy-ui15'}
if($ClientCompatibilityOnly){$fixtures+='--client-compatibility-only'}
if($Installed081Only){$fixtures+='--installed-081-only'}
& $output @fixtures
if ($LASTEXITCODE -ne 0) { throw 'Graphics tests failed.' }
