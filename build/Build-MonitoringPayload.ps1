param([string]$InputDirectory=(Join-Path $PSScriptRoot '..\dist\Monitoring'), [string]$OutputDirectory=(Join-Path $PSScriptRoot '..\src\Launcher\obj\EmbeddedMonitoring'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$source=[IO.Path]::GetFullPath($InputDirectory)
$destination=[IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($destination)|Out-Null
# Never enumerate a user's AOSharp installation. Only these companion files ship.
$names=@('NexusHealthHost.exe','NexusHealthPlugin.dll','NexusTelemetry.Protocol.dll','AOSharp.Common.dll','Newtonsoft.Json.dll','README.txt')
$files=@(foreach($name in $names){
 $path=Join-Path $source $name
 if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing monitoring input $name. Run Build-Telemetry.ps1 first."}
 $item=Get-Item -LiteralPath $path
 if($item.Length -eq 0 -or $item.Length -gt 16MB){throw "Unexpected monitoring input size: $name"}
 [ordered]@{Path=$name;Length=$item.Length;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$temporary=Join-Path $destination ('Monitoring-'+[Guid]::NewGuid().ToString('N')+'.zip')
$zip=[IO.Compression.ZipFile]::Open($temporary,[IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($file in $files){
  $entry=$zip.CreateEntry($file.Path,[IO.Compression.CompressionLevel]::Optimal)
  $entry.LastWriteTime=[DateTimeOffset]::new(1980,1,1,0,0,0,[TimeSpan]::Zero)
  $input=[IO.File]::OpenRead((Join-Path $source $file.Path));$output=$entry.Open()
  try{$input.CopyTo($output)}finally{$output.Dispose();$input.Dispose()}
 }
}finally{$zip.Dispose()}
$manifest=[ordered]@{PayloadSha256=(Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant();Files=$files}
Move-Item -LiteralPath $temporary -Destination (Join-Path $destination 'Monitoring.zip') -Force
[IO.File]::WriteAllText((Join-Path $destination 'Monitoring.manifest.json'),($manifest|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
Write-Host "Monitoring payload: $($files.Count) allowlisted files."
