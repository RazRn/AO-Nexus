param([string]$AssemblyPath=(Join-Path $PSScriptRoot '..\dist\AONexus.exe'), [string]$ArchivePath=(Join-Path $PSScriptRoot '..\dist\components\components-1.3.0-preview.1\AO-Nexus-aovk.zip'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Web.Extensions
Add-Type -AssemblyName System.IO.Compression.FileSystem
$assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes([IO.Path]::GetFullPath($AssemblyPath)))
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$type=$assembly.GetType('AOLauncher.OptionalComponents',$true)
$resource=$assembly.GetManifestResourceStream('AOLauncher.Components.aovk.json')
$reader=[IO.StreamReader]::new($resource)
try {$configured=$reader.ReadToEnd()|ConvertFrom-Json} finally {$reader.Dispose()}
if($configured.Retired) {
 $blocked=$false
 try {$type.GetMethod('Read',$flags).Invoke($null,@('aovk'))|Out-Null} catch {
  if($_.Exception.InnerException -is [IO.IOException]){$blocked=$true}else{throw}
 }
 if(!$blocked){throw 'The retired AO-VK package must not be offered for installation.'}
 Write-Output 'PASS: retired AO-VK package is unavailable; no archive downloaded or native binary executed.'
 return
}
$descriptor=$type.GetMethod('Read',$flags).Invoke($null,@('aovk'))
$archive=[IO.Path]::GetFullPath($ArchivePath)
$manifest=$descriptor.Manifest|ConvertFrom-Json
if((Get-Item -LiteralPath $archive).Length -ne $descriptor.Size -or (Get-FileHash -LiteralPath $archive).Hash -ne $manifest.PayloadSha256){throw 'Packaged archive differs from the launcher descriptor.'}
$expected=@('randy31.dll','randy31-ui.dll','Licenses/ao-vk.txt','Licenses/imgui.txt','Licenses/fsr2.txt','Licenses/ffx-sdk.txt')
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{if(Compare-Object @($zip.Entries.FullName|Sort-Object) @($expected|Sort-Object)){throw 'Unexpected downloadable package contents.'}}finally{$zip.Dispose()}
$cache=Join-Path ([IO.Path]::GetTempPath()) ('AONexus-OptionalDistribution-'+[Guid]::NewGuid().ToString('N'))
try{
 $folder=$type.GetMethod('InstallArchive',$flags).Invoke($null,@($descriptor,[string]$cache,[string]$archive,[Threading.CancellationToken]::None))
 foreach($entry in $manifest.Files){
  if((Get-FileHash -LiteralPath (Join-Path $folder $entry.Path)).Hash -ne $entry.Sha256){throw "Extracted hash differs: $($entry.Path)"}
 }
 $installed=$type.GetMethod('Installed',$flags,$null,@($descriptor.GetType(),[string]),$null).Invoke($null,@($descriptor,[string]$cache))
 if($folder -ne $installed){throw 'Installed cache was not recognized.'}
 $graphics=$assembly.GetType('AOLauncher.AovkGraphics',$true)
 $snapshotType=$graphics.GetNestedType('Snapshot',[Reflection.BindingFlags]'NonPublic')
 $snapshot=[Activator]::CreateInstance($snapshotType,$true)
 $instanceFlags=[Reflection.BindingFlags]'NonPublic,Instance'
 $values=New-Object 'System.Collections.Generic.Dictionary[string,string]'
 $values.Add('RVK_NexusUI','0')
 $snapshotType.GetField('Values',$instanceFlags).SetValue($snapshot,$values)
 $snapshotType.GetField('Enabled',$instanceFlags).SetValue($snapshot,$true)
 $renderer=[IO.File]::ReadAllBytes((Join-Path $folder 'randy31.dll'))
 $snapshotType.GetField('Renderer',$instanceFlags).SetValue($snapshot,$renderer)
 if($graphics.GetMethod('NeedsRendererDownload',$flags).Invoke($null,@($snapshot))){throw 'A verified existing renderer should not need a download.'}
 $reused=$graphics.GetMethod('RendererPayload',$flags).Invoke($null,@($snapshot))
 if(![object]::ReferenceEquals($renderer,$reused)){throw 'Existing renderer was not reused offline.'}
 Write-Output 'PASS: actual renderer archive matches embedded descriptor, contains only two renderers plus four notices, installs and verifies offline; no native binaries executed.'
}finally{
 $full=[IO.Path]::GetFullPath($cache)
 $temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$full.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($full) -notlike 'AONexus-OptionalDistribution-*'){throw 'Unexpected cleanup path.'}
 if(Test-Path -LiteralPath $full){Remove-Item -LiteralPath $full -Recurse -Force}
}
