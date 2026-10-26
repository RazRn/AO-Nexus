param([string]$AssemblyPath=(Join-Path $PSScriptRoot '..\dist\AONexus.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Web.Extensions
$root=Join-Path ([IO.Path]::GetTempPath()) ('AONexus-MonitoringTest-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
$checks=0
function Assert($ok,$reason){if(!$ok){throw $reason};$script:checks++}
try{
 # Simulate the updater: only the executable survives, with no sidecar folder.
 $portable=Join-Path $root 'AONexus.exe'
 Copy-Item -LiteralPath $AssemblyPath -Destination $portable
 $a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($portable))
 $type=$a.GetType('AOLauncher.EmbeddedMonitoring',$true)
 $extract=$type.GetMethod('Extract',[Reflection.BindingFlags]'Static,NonPublic')
 $cache=Join-Path $root 'cache'
 $arguments=New-Object object[] 1
 $arguments[0]=[string]$cache
 $folder=$extract.Invoke($null,$arguments)
 Assert ($folder.StartsWith($cache+'\',[StringComparison]::OrdinalIgnoreCase)) 'Companion extracts inside chosen cache'
 $names=@('NexusHealthHost.exe','NexusHealthPlugin.dll','NexusTelemetry.Protocol.dll','AOSharp.Common.dll','Newtonsoft.Json.dll','README.txt')
 Assert (@(Get-ChildItem -LiteralPath $folder -File).Count -eq $names.Count) 'Only allowlisted companion files extract'
 $stream=$a.GetManifestResourceStream('AOLauncher.Monitoring.manifest.json')
 $reader=[IO.StreamReader]::new($stream)
 try{$manifest=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
 Assert ($manifest.Files.Count -eq $names.Count) 'Manifest covers every companion file'
 foreach($name in $names){
  $path=Join-Path $folder $name
  Assert (Test-Path -LiteralPath $path -PathType Leaf) "Portable updater retains $name"
  $expected=@($manifest.Files|Where-Object {$_.Path -eq $name})
  Assert ($expected.Count -eq 1 -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $expected[0].Sha256) "Extracted hash matches $name"
 }
 Assert ($extract.Invoke($null,$arguments) -eq $folder) 'Unchanged payload reuses verified cache'
 $target=Join-Path $folder 'README.txt';[IO.File]::AppendAllText($target,'tampered')
 $rejected=$false;try{$extract.Invoke($null,$arguments)|Out-Null}catch{$rejected=$true}
 Assert $rejected 'Modified cached companion is rejected, never silently trusted'
 Write-Output "PASS: $checks embedded monitoring and single-file upgrade checks; no helpers or games launched."
}finally{
 $full=[IO.Path]::GetFullPath($root);$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$full.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($full) -notlike 'AONexus-MonitoringTest-*'){throw 'Unexpected cleanup path'}
 Remove-Item -LiteralPath $full -Recurse -Force
}
