param([string]$ReleaseTag='components-1.3.0-preview.1')
$ErrorActionPreference='Stop'
if($ReleaseTag -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$'){throw 'Invalid component release tag.'}
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $root "dist\components\$ReleaseTag"
$descriptors=Join-Path $root 'config\components'
[IO.Directory]::CreateDirectory($output)|Out-Null
[IO.Directory]::CreateDirectory($descriptors)|Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Write-Descriptor($id,$archive,$manifest,$required){
 $bytes=(Get-Item -LiteralPath $archive).Length
 if($bytes -gt 128MB){throw 'Component package exceeds launcher download limit.'}
 # This recipe still pins the retired 0.7.1 renderer. Do not silently offer it
 # again when rebuilding Nexus. A new reviewed package needs a new immutable tag.
 $record=[ordered]@{Id=$id;Retired=$true;Version=$ReleaseTag;Url="https://github.com/RazRn/AO-Nexus/releases/download/$ReleaseTag/AO-Nexus-$id.zip";Size=$bytes;RequiredFile=$required;Manifest=$manifest}
 $json=$record|ConvertTo-Json -Depth 4
 [IO.File]::WriteAllText((Join-Path $descriptors "$id.json"),$json,[Text.UTF8Encoding]::new($false))
 [IO.File]::WriteAllText((Join-Path $output "$id.json"),$json,[Text.UTF8Encoding]::new($false))
}
# Public renderer packages must not require or include the private Rebirth patch.
& (Join-Path $PSScriptRoot 'Build-AovkPayload.ps1') -RendererOnly
$payload=Join-Path $root 'src\Launcher\obj\EmbeddedAOVK'
$sources=[ordered]@{}
foreach($name in @('randy31.dll','randy31-ui.dll')){$sources[$name]=Join-Path $payload $name}
$sources['Licenses/ao-vk.txt']=Join-Path $root 'src\Launcher\Assets\ao-vk-LICENSE.txt'
$sources['Licenses/imgui.txt']=Join-Path $root 'src\Launcher\Assets\imgui-LICENSE.txt'
$sources['Licenses/fsr2.txt']=Join-Path $root 'src\Launcher\Assets\fsr2-LICENSE.txt'
$sources['Licenses/ffx-sdk.txt']=Join-Path $root 'src\Launcher\Assets\ffx-sdk-LICENSE.txt'
$archive=Join-Path $output 'AO-Nexus-aovk.zip'
$temporary=Join-Path $output ([Guid]::NewGuid().ToString('N')+'.zip')
$zip=[IO.Compression.ZipFile]::Open($temporary,[IO.Compression.ZipArchiveMode]::Create)
$files=@()
try{
 foreach($name in $sources.Keys){
  $entry=$zip.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
  $entry.LastWriteTime=[DateTimeOffset]::new(1980,1,1,0,0,0,[TimeSpan]::Zero)
  $input=[IO.File]::OpenRead($sources[$name]);$stream=$entry.Open()
  try{$input.CopyTo($stream)}finally{$stream.Dispose();$input.Dispose()}
  $files+= [ordered]@{Path=$name;Length=(Get-Item -LiteralPath $sources[$name]).Length;Sha256=(Get-FileHash -LiteralPath $sources[$name]).Hash.ToLowerInvariant()}
 }
}finally{$zip.Dispose()}
Move-Item -LiteralPath $temporary -Destination $archive -Force
$manifest=@{PayloadSha256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant();Files=$files}|ConvertTo-Json -Depth 4
Write-Descriptor 'aovk' $archive $manifest 'randy31.dll'
[IO.File]::WriteAllText((Join-Path $output 'PUBLISHING.txt'),"Prepared locally. No assets have been uploaded.`r`nPublish assets under GitHub release tag $ReleaseTag, without marking this component release as the latest app release.`r`nDO NOT PUBLISH: this recipe still builds the retired AO-VK 0.7.1 package. A replacement requires a new verified bundle, a new immutable tag, updated descriptor and release checks. Files marked LOCAL-ONLY must never be uploaded.`r`nKeep releases immutable: changed binaries require a new tag and a rebuilt Nexus descriptor.`r`n",[Text.UTF8Encoding]::new($false))
Write-Host "Optional component assets prepared: $output"
