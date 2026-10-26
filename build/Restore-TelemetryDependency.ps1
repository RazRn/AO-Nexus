param([string]$CacheDirectory=(Join-Path $PSScriptRoot '..\runtime\AOSharpSource'))
$ErrorActionPreference='Stop'
$revision='d55eb12b5a763e5ed65851e69c50343de6c6d73c'
$source=[IO.Path]::GetFullPath($CacheDirectory)
if(!(Test-Path -LiteralPath (Join-Path $source '.git'))){
 if(Test-Path -LiteralPath $source){throw 'Telemetry source cache already exists without Git metadata. Choose a new CacheDirectory.'}
 git init --quiet $source
 if($LASTEXITCODE){throw 'Could not create telemetry source cache'}
 git -C $source remote add origin https://gitlab.com/never-knows-best/aosharp.git
 git -C $source fetch --quiet --depth 1 origin $revision
 if($LASTEXITCODE){throw 'Could not fetch pinned AOSharp source'}
 git -C $source checkout --quiet --detach $revision
 if($LASTEXITCODE){throw 'Could not check out pinned AOSharp source'}
}
if((git -C $source rev-parse HEAD) -ne $revision){throw 'Telemetry source cache is at an unexpected revision'}
if(git -C $source status --porcelain --untracked-files=no){throw 'Telemetry source cache contains tracked modifications'}
$msbuild=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
if(!$msbuild){throw 'Install Visual Studio Build Tools with the .NET Framework 4.8 targeting pack'}
$project=Join-Path $source 'AOSharp.Core\AOSharp.Core.csproj'
& $msbuild $project /t:Restore /p:RestorePackagesConfig=true "/p:SolutionDir=$source\" /p:Configuration=Release /m:1 /nologo /verbosity:minimal
if($LASTEXITCODE){throw 'AOSharp dependency restore failed'}
& $msbuild $project /t:Build /p:Configuration=Release /m:1 /nologo /verbosity:minimal
if($LASTEXITCODE){throw 'AOSharp reference build failed'}
$runtime=Join-Path $source 'AOSharp.Core\bin\Release'
foreach($name in @('AOSharp.Core.dll','AOSharp.Common.dll','Newtonsoft.Json.dll')){if(!(Test-Path -LiteralPath (Join-Path $runtime $name))){throw "Missing AOSharp build output: $name"}}
Write-Output $runtime
