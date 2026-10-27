param([string]$CaptureDirectory=(Join-Path $PSScriptRoot '..\logs\game-gui-usability'),[string]$Version='1.2.0.0')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'Guide-ImagePlan.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$images=Join-Path $repo 'docs\images'
$provenance=Join-Path $images 'SCREENSHOTS.md'
$legacyRows=@([IO.File]::ReadAllLines($provenance)|Where-Object {$_ -match '^\| \[' -and $_ -notmatch '^\| \[gui-'})
if($legacyRows.Count -ne 9){throw 'Expected nine retained non-GUI provenance rows'}
$documents=@('README.md','docs/Player-Guide.md','docs/Game-GUI.md')
$body=($documents|ForEach-Object {[IO.File]::ReadAllText((Join-Path $repo $_))}) -join "`n"
$entries=@(foreach($name in $GuideImagePlan.Keys){
 $plan=$GuideImagePlan[$name];if(!$plan.Versioned){continue}
 $source=Join-Path $CaptureDirectory $plan.Source
 if(!(Test-Path -LiteralPath $source -PathType Leaf)){throw "Missing final QA capture: $source"}
 if([regex]::Matches($body,[regex]::Escape('images/'+$name)+ '\)').Count -ne 1){throw "Expected one guide placement for $name"}
 $bitmap=[Drawing.Bitmap]::FromFile($source)
 try{if($bitmap.Width -ne $plan.Size[0] -or $bitmap.Height -ne $plan.Size[1]){throw "Final capture size differs from plan: $name"}}finally{$bitmap.Dispose()}
 [pscustomobject]@{Name=$name;Source=$source;Plan=$plan;Hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()}
})
if($entries.Count -ne 5){throw 'Expected all five Game GUI captures'}
# Validate every input before replacing any public image. No capture is generated here.
foreach($entry in $entries){Copy-Item -LiteralPath $entry.Source -Destination (Join-Path $images $entry.Name) -Force}
$lines=@('# Guide screenshot provenance','','One overview and thirteen task-specific illustrations. All accounts, paths and results are synthetic examples; no personal profile or game client was opened.','','## Retained overview and launcher tasks','','These nine images are preserved from the previous full guide capture using real WinForms controls and offline fixtures. Red boxes and numbers are guide annotations; source sizes, crops, capture sources and content hashes are retained below.','','| Image | Source size | Crop (x, y, width, height) | Callouts | QA source | SHA-256 |','| --- | --- | --- | --- | --- | --- |')
$lines+=$legacyRows
$lines+=@('','## Game GUI 1.2','',('Captured from AO Nexus '+$Version+' by `tests/Verify-GameGuiUsability.ps1`, English at 100% scale and the default window size. Each image shows the full client area with no cropping, red boxes or other annotations. The fixture checks action visibility and uses isolated synthetic layouts, skins, scripts, macros and backups. Final captures must also be visually reviewed before this publishing step.'),'','| Image | Source size | Crop | Callouts | QA source in `logs/game-gui-usability` | SHA-256 |','| --- | --- | --- | --- | --- | --- |')
foreach($entry in $entries){$lines+=('| ['+$entry.Name+']('+$entry.Name+') | '+($entry.Plan.Size -join ' x ')+' | Full client area | 0 | `'+$entry.Plan.Source+'` | `'+$entry.Hash+'` |')}
[IO.File]::WriteAllText($provenance,($lines -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
# Remove only the obsolete three generated GUI images after ensuring no guide uses them.
$published=Join-Path $images 'published'
foreach($file in Get-ChildItem -LiteralPath $published -File -Filter '*.png'){
 if($file.Name -notmatch '^gui-(layouts|skins|backups)-[a-f0-9]{16}\.png$'){continue}
 if($body.Contains($file.Name)){throw "Obsolete image is still referenced: $($file.Name)"}
 $absolute=[IO.Path]::GetFullPath($file.FullName)
 if([IO.Path]::GetDirectoryName($absolute) -ne [IO.Path]::GetFullPath($published)){throw 'Unexpected obsolete image path'}
 Remove-Item -LiteralPath $absolute
}
Write-Output 'Published five approved Game GUI captures with SHA-256 provenance; nine existing images preserved.'
