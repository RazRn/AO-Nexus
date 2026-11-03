param([string]$InputDirectory = (Join-Path $PSScriptRoot '..\runtime\AOVKPackage'), [switch]$RendererOnly)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$files=@{
    'randy31.dll'='60759C61C7383EFA39AE4F09981ED3E42D7A25CE7C9D093E6791A5C4A98040C6'
    'version-rebirth.dll'='BB7581048F77D3BD89D95F983BD472B5ABB7FDA8D0B920D2825E499998A25B2A'
    'randy31-ui.dll'='BAF83111E172FA8BB833F2615B02D1162C0F6125CF6836421F22C714445825EE'
    'version-rebirth-ui.dll'='BB7581048F77D3BD89D95F983BD472B5ABB7FDA8D0B920D2825E499998A25B2A'
}
$destination=Join-Path $root 'src\Launcher\obj\EmbeddedAOVK'
[IO.Directory]::CreateDirectory($destination)|Out-Null
foreach($name in $files.Keys){
    if($RendererOnly -and $name.StartsWith('version-')){continue}
    $source=Join-Path $InputDirectory $name
    if(!(Test-Path -LiteralPath $source) -or (Get-FileHash -LiteralPath $source).Hash -ne $files[$name]){throw "Missing or unverified AO-VK payload: $name. See docs/development/log/AOVK-Managed-Install.txt."}
    Copy-Item -LiteralPath $source -Destination (Join-Path $destination $name) -Force
}
$license=Join-Path $root 'dist\Licenses\ao-vk'
[IO.Directory]::CreateDirectory($license)|Out-Null
Copy-Item -LiteralPath (Join-Path $root 'src\Launcher\Assets\ao-vk-LICENSE.txt') -Destination (Join-Path $license 'LICENSE.txt') -Force
$imguiLicense=Join-Path $root 'dist\Licenses\imgui'
[IO.Directory]::CreateDirectory($imguiLicense)|Out-Null
Copy-Item -LiteralPath (Join-Path $root 'src\Launcher\Assets\imgui-LICENSE.txt') -Destination (Join-Path $imguiLicense 'LICENSE.txt') -Force
if($RendererOnly){Write-Host 'AO-VK 0.7.1 renderer hashes verified.'}
else{Write-Host 'AO-VK 0.7.1 and local Rebirth compatibility patch: hashes verified.'}
