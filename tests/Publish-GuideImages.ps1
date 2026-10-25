param([Parameter(Mandatory=$true)][string]$CaptureDirectory,[Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Guide-ImagePlan.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$images=[IO.Path]::GetFullPath((Join-Path $repo 'docs/images'))
$published=[IO.Path]::GetFullPath((Join-Path $images 'published'))
[IO.Directory]::CreateDirectory($published)|Out-Null
$documents=@('README.md','docs/Player-Guide.md','docs/Game-GUI.md')
$text=@{};foreach($document in $documents){$text[$document]=[IO.File]::ReadAllText((Join-Path $repo $document))}
$manifest=@('# Guide screenshot provenance','',('Captured from AO Nexus '+$Version+' using real WinForms controls and offline fixtures.'),'','One overview and eleven task-specific illustrations. Red boxes and numbers are guide annotations; crops omit unrelated controls. Accounts, paths, news and health/update results are examples.','','| Image | Source size | Crop (x, y, width, height) | Callouts | SHA-256 |','| --- | --- | --- | --- | --- |')
$keep=@()
foreach($name in $GuideImagePlan.Keys){
    $source=Join-Path $CaptureDirectory ('curated/'+$name)
    $hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    $stem=[IO.Path]::GetFileNameWithoutExtension($name)
    $newName=$stem+'-'+$hash.Substring(0,16)+'.png'
    Copy-Item -LiteralPath $source -Destination (Join-Path $published $newName) -Force
    $keep+=$newName
    $pattern='images/(?:'+[regex]::Escape($name)+'|published/'+[regex]::Escape($stem)+'-[a-f0-9]{16}\.png)(?:\?v=[a-f0-9]+)?(?=\))'
    $references=0
    foreach($document in $documents){
        $references += [regex]::Matches($text[$document],$pattern).Count
        $text[$document]=[regex]::Replace($text[$document],$pattern,('images/published/'+$newName))
    }
    if($references -ne 1){throw "Expected one guide placement for $name, found $references"}
    $plan=$GuideImagePlan[$name];$crop=if($plan.Crop){$plan.Crop -join ', '}else{'Full client area'}
    $manifest+=('| ['+$name+'](published/'+$newName+') | '+($plan.Size -join ' x ')+' | '+$crop+' | '+$plan.Boxes.Count+' | `'+$hash+'` |')
}
foreach($document in $documents){[IO.File]::WriteAllText((Join-Path $repo $document),$text[$document],[Text.UTF8Encoding]::new($false))}
[IO.File]::WriteAllText((Join-Path $images 'SCREENSHOTS.md'),(($manifest -join "`n")+"`n"),[Text.UTF8Encoding]::new($false))
# Only generated PNGs inside the exact documentation image directories are removed.
# Originals remain in ignored logs/guide-captures, not duplicated in the repository.
foreach($candidate in @(Get-ChildItem -LiteralPath $images -Filter '*.png')+@(Get-ChildItem -LiteralPath $published -Filter '*.png')){
    $absolute=[IO.Path]::GetFullPath($candidate.FullName)
    if(!$absolute.StartsWith($images+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected image path'}
    if($candidate.DirectoryName -eq $published -and $keep -contains $candidate.Name){continue}
    Remove-Item -LiteralPath $absolute
}
Write-Output 'Published 12 uniquely placed content-addressed screenshots.'
