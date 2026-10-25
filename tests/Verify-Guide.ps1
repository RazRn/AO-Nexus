$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$checks=0
function Assert($condition,$message){if(!$condition){throw $message};$script:checks++}
function Anchors([string]$body){
    $seen=@{};$result=@()
    foreach($heading in [regex]::Matches($body,'(?m)^#{1,6}\s+(.+?)\s*\r?$')){
        $slug=$heading.Groups[1].Value.ToLowerInvariant() -replace '[^\p{L}\p{N}\s_-]','' -replace '\s','-'
        $anchor=$slug
        if($seen.ContainsKey($slug)){$seen[$slug]++;$anchor+='-'+$seen[$slug]}else{$seen[$slug]=0}
        $result+=$anchor
    }
    return $result
}
$documents=@('README.md','docs/Player-Guide.md','docs/Game-GUI.md','docs/Building.md','docs/Privacy-and-Security.md','docs/Server-Health.md','docs/images/README.md','docs/images/SCREENSHOTS.md')
$narrative=@('README.md','docs/Player-Guide.md','docs/Game-GUI.md')
$imageRefs=@()
foreach($document in $documents){
    $path=Join-Path $repo $document
    $body=[IO.File]::ReadAllText($path)
    Assert ($body -notmatch '(?m)^#{1,6}[^\r\n]+\r?\n[^\r\n]') "Heading missing blank line: $document"
    foreach($link in [regex]::Matches($body,'!?\[[^\]\r\n]*\]\(([^)\r\n]+)\)')){
        $target=$link.Groups[1].Value
        if($target -match '^[a-z]+:'){continue}
        $parts=$target -split '#',2
        $relative=[Uri]::UnescapeDataString($parts[0])
        $destination=if(!$relative){$path}else{[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($path)) $relative))}
        Assert (Test-Path -LiteralPath $destination) "Broken link $target in $document"
        if($parts.Count -gt 1 -and $parts[1]){
            Assert ((Anchors ([IO.File]::ReadAllText($destination))) -contains $parts[1]) "Missing anchor $target in $document"
        }
        if($narrative -contains $document -and $link.Value.StartsWith('!')){
            Assert ($target -match 'images/published/.+-[a-f0-9]{16}\.png$') "Unversioned guide image: $target"
            $imageRefs+=$destination
        }
    }
}
Assert ($imageRefs.Count -eq 11) 'Guide should have one overview and ten distinct task images'
Assert (@($imageRefs | Select-Object -Unique).Count -eq 11) 'A screenshot is repeated in the guide'
$readme=[IO.File]::ReadAllText((Join-Path $repo 'README.md'))
Assert ([regex]::Matches($readme,'!\[').Count -eq 1) 'README should contain only the overview image'
Assert (($readme -split "`n").Count -le 100) 'README has grown beyond the quick-start overview'
$manifest=[IO.File]::ReadAllText((Join-Path $repo 'docs/images/SCREENSHOTS.md'))
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'Guide-ImagePlan.ps1')
foreach($image in $imageRefs){
    $hash=(Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert ([IO.Path]::GetFileName($image).Contains($hash.Substring(0,16))) "Image cache key differs from contents: $image"
    Assert ($manifest.Contains($hash)) "Image missing from provenance: $image"
    $name=([IO.Path]::GetFileName($image) -replace '-[a-f0-9]{16}\.png$','.png')
    $plan=$GuideImagePlan[$name]
    Assert ($null -ne $plan) "Image not in capture plan: $name"
    $bitmap=[Drawing.Bitmap]::FromFile($image)
    try {
        $size=if($plan.Crop){@($plan.Crop[2],$plan.Crop[3])}else{$plan.Size}
        Assert ($bitmap.Width -eq $size[0] -and $bitmap.Height -eq $size[1]) "Image size differs from plan: $name"
        foreach($box in $plan.Boxes){
            $x=$box[0];$y=$box[1];if($plan.Crop){$x-=$plan.Crop[0];$y-=$plan.Crop[1]}
            $color=$bitmap.GetPixel($x+2,$y)
            Assert ($color.R -gt 220 -and $color.G -lt 120 -and $color.B -lt 120) "Missing red callout: $name"
        }
    } finally {$bitmap.Dispose()}
}
$pngs=@(Get-ChildItem -LiteralPath (Join-Path $repo 'docs/images') -Recurse -Filter '*.png')
Assert ($pngs.Count -eq 11) 'Stale/duplicate screenshot files remain in docs/images'
Write-Output "PASS: $checks documentation link, anchor, image, hash, callout and structure checks."
