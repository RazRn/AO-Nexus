param([Parameter(Mandatory=$true)][string]$PackDirectory, [string]$OutputDirectory=(Join-Path $PSScriptRoot '..\src\Launcher\obj\EmbeddedAOSharp'))
# LOCAL BUILDS ONLY. Packs AOSharp and its plugins from a folder laid out like AO#\dist
# (AOSharp\ and Plugins\) so AONexus.exe can carry them. AOSharp and the plugins belong to
# their authors; do not publish an EXE built with this pack without their permission.
# Same allowlist as build/make_aosharp_pack.py: AOSharp runtime files, plugin DLLs/configs/data.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$source=[IO.Path]::GetFullPath($PackDirectory)
$destination=[IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($destination)|Out-Null
$core=@('AOSharp.Bootstrap.dll','AOSharp.Bootstrap.dll.config','AOSharp.Common.dll','AOSharp.Core.dll','AOSharp.Core.dll.config',
 'EasyHook.dll','EasyHook32.dll','EasyHook32Svc.exe','EasyLoad32.dll','HtmlAgilityPack.dll','Newtonsoft.Json.dll',
 'Serilog.dll','Serilog.Sinks.Console.dll','Serilog.Sinks.Debug.dll','Serilog.Sinks.File.dll','System.Buffers.dll',
 'System.Diagnostics.DiagnosticSource.dll','System.Memory.dll','System.Numerics.Vectors.dll',
 'System.Runtime.CompilerServices.Unsafe.dll','System.Threading.Channels.dll','System.Threading.Tasks.Extensions.dll')
$items=New-Object System.Collections.Generic.List[object]
foreach($name in $core){
 $path=Join-Path $source "AOSharp\$name"
 if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing AOSharp file $name in $source\AOSharp"}
 $items.Add([pscustomobject]@{Path="AOSharp/$name";Source=$path})
}
$plugins=Join-Path $source 'Plugins'
# Plugin windows are .xml files (AOSP\UI\...); the game crashes when a plugin opens a window whose
# file is missing. Pack every .xml except library documentation (an .xml next to a same-named .dll).
Get-ChildItem -LiteralPath $plugins -Recurse -File | Where-Object {
 ($_.Extension -in '.dll','.config','.json','.nav','.navmesh') -or
 ($_.Extension -eq '.xml' -and -not (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($_.FullName,'.dll')) -PathType Leaf))
} | ForEach-Object {
 $relative=$_.FullName.Substring($plugins.Length+1).Replace('\','/')
 $items.Add([pscustomobject]@{Path="Plugins/$relative";Source=$_.FullName})
}
# Every window file a plugin names (for example "\UI\DocSettingsView.xml") must be in the pack.
$packed=@{}; foreach($item in $items){ $packed[$item.Path.ToLowerInvariant()]=$true }
$missingViews=New-Object System.Collections.Generic.SortedSet[string]
foreach($item in @($items | Where-Object { $_.Path -like 'Plugins/*.dll' })){
 $bytes=[IO.File]::ReadAllBytes($item.Source)
 $text=[Text.Encoding]::Unicode.GetString($bytes)+"`n"+[Text.Encoding]::Unicode.GetString($bytes,1,$bytes.Length-1)
 $folder=$item.Path.Substring(0,$item.Path.LastIndexOf('/'))
 foreach($match in [regex]::Matches($text,'(?:[A-Za-z]:)?[\\/]?[A-Za-z0-9_\- \\/]+\.xml')){
  if($match.Value -match '^[A-Za-z]:'){continue}
  $ref=$match.Value.Replace('\','/').TrimStart('/')
  if(-not $ref.Contains('/')){continue}
  if(-not $packed.ContainsKey(("$folder/$ref").ToLowerInvariant())){[void]$missingViews.Add("$($item.Path) -> $ref")}
 }
}
if($missingViews.Count -gt 0){throw ("Plugin window files missing from pack:`n  "+($missingViews -join "`n  "))}
$catalog=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'aosharp-pack\plugins.json') | ConvertFrom-Json
$listed=@()
foreach($group in $catalog.groups.PSObject.Properties){
 foreach($id in $group.Value){
  $relative=if($id.Contains('.')){"Plugins/AOSP/$id.dll"}else{"Plugins/$id/$id.dll"}
  if(!($items | Where-Object Path -eq $relative)){throw "Catalog plugin missing from pack: $relative"}
  $listed+=[ordered]@{Id=$id;Group=$group.Name;Path=$relative;Suite=$(if($id.Contains('.')){'AOSP'}else{'Classic'})}
 }
}
# Extra catalogs beside plugins.json (Z suite, Nexus Shortcuts) list full entries with their paths.
# Their plugins must already be in the source folder (build_z_suite.py / build-local-pack.sh).
foreach($extra in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'aosharp-pack') -Filter '*.json' | Where-Object Name -ne 'plugins.json' | Sort-Object Name){
 foreach($entry in (Get-Content -Raw -LiteralPath $extra.FullName | ConvertFrom-Json).Plugins){
  if(!($items | Where-Object Path -eq $entry.Path)){throw "Catalog plugin missing from pack: $($entry.Path)"}
  $listed+=$entry
 }
}
$meta=Join-Path $destination 'plugins.json'
[IO.File]::WriteAllText($meta,(@{Plugins=$listed}|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$items.Add([pscustomobject]@{Path='plugins.json';Source=$meta})
$revisions=Join-Path $source 'SOURCE-REVISIONS.json'
if(Test-Path -LiteralPath $revisions){$items.Add([pscustomobject]@{Path='SOURCE-REVISIONS.json';Source=$revisions})}
$files=@($items | Sort-Object Path | ForEach-Object {
 $info=Get-Item -LiteralPath $_.Source
 [ordered]@{Path=$_.Path;Length=$info.Length;Sha256=(Get-FileHash -LiteralPath $_.Source -Algorithm SHA256).Hash.ToLowerInvariant();Source=$_.Source}
})
$temporary=Join-Path $destination ('AOSharpPack-'+[Guid]::NewGuid().ToString('N')+'.zip')
$zip=[IO.Compression.ZipFile]::Open($temporary,[IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($file in $files){
  $entry=$zip.CreateEntry($file.Path,[IO.Compression.CompressionLevel]::Optimal)
  $entry.LastWriteTime=[DateTimeOffset]::new(1980,1,1,0,0,0,[TimeSpan]::Zero)
  $input=[IO.File]::OpenRead($file.Source);$output=$entry.Open()
  try{$input.CopyTo($output)}finally{$output.Dispose();$input.Dispose()}
 }
}finally{$zip.Dispose()}
$manifest=[ordered]@{PayloadSha256=(Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant();Files=@($files|ForEach-Object{[ordered]@{Path=$_.Path;Length=$_.Length;Sha256=$_.Sha256}})}
Move-Item -LiteralPath $temporary -Destination (Join-Path $destination 'AOSharpPack.zip') -Force
[IO.File]::WriteAllText((Join-Path $destination 'AOSharpPack.manifest.json'),($manifest|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
Write-Host "AOSharp pack: $($files.Count) files, $($listed.Count) plugins (local build only)."
