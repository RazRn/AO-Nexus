param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference='Stop'
if([IntPtr]::Size -ne 4){throw 'Run with x86 Windows PowerShell.'}
Add-Type -AssemblyName System.Web.Extensions
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$engine=$assembly.GetType('AOLauncher.GuiLayouts',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Public,Static'
$checks=0
function Assert($condition,$message){if(!$condition){throw $message};$script:checks++}
function Call($method,[object[]]$arguments){for($i=0;$i -lt $arguments.Length;$i++){if($null -ne $arguments[$i]){$arguments[$i]=$arguments[$i].psobject.BaseObject}};$engine.GetMethod($method,$flags).Invoke($null,$arguments)}
function Write-Fixture($path,$text){$null=[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path));[IO.File]::WriteAllText($path,$text,[Text.UTF8Encoding]::new($false))}
function Snapshot($path){$result=@{};foreach($file in Get-ChildItem -LiteralPath $path -Recurse -File){$result[$file.FullName.Substring($path.Length)]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash};return $result}
$root=Join-Path ([IO.Path]::GetTempPath()) ('AONexus-StarterTest-'+[guid]::NewGuid().ToString('N'))
$null=[IO.Directory]::CreateDirectory($root)
try{
 foreach($size in @(@(1280,720),@(1600,900),@(1701,1038),@(1920,1080))){
    $width=$size[0];$height=$size[1];$store=Join-Path $root ('Library-'+$width+'x'+$height)
    $generated=@(& (Join-Path $PSScriptRoot '../tools/New-StarterGuiLayouts.ps1') -AssemblyPath $AssemblyPath -StoreRoot $store -Width $width -Height $height)
    Assert ($generated.Count -eq 5) 'Each canvas has five generated layouts'
    foreach($design in $generated){for($i=0;$i -lt $design.Panels.Count;$i++){for($j=$i+1;$j -lt $design.Panels.Count;$j++){
        $left=$design.Panels[$i];$right=$design.Panels[$j]
        Assert (!($left.X -lt $right.X+$right.W -and $left.X+$left.W -gt $right.X -and $left.Y -lt $right.Y+$right.H -and $left.Y+$left.H -gt $right.Y)) 'Generated solid rectangles do not overlap'
    }}}
    $templates=@(Call 'ListTemplates' @($store))
    Assert ($templates.Count -eq 5) 'All five are visible to the real library engine'
    Assert ((@($templates.Id|Select-Object -Unique)).Count -eq 5) 'Stable IDs are unique'
    $beforeIds=($templates.Id|Sort-Object)-join ','
    & (Join-Path $PSScriptRoot '../tools/New-StarterGuiLayouts.ps1') -AssemblyPath $AssemblyPath -StoreRoot $store -Width $width -Height $height | Out-Null
    Assert ((@(Call 'ListTemplates' @($store)).Id|Sort-Object)-join ',' -eq $beforeIds) 'Repeated generation does not add or replace templates'
    $index=0
    foreach($template in $templates){
        Assert ([int]$template.Components -eq 1) 'Only Layout is captured'
        Assert ($template.SourceCharacter -ceq 'AO Nexus original starter layout') 'No real source character or account identity is packaged'
        Assert ($template.Name -match ($width.ToString()+'.'+$height.ToString()+'$') -and $template.Name -notmatch 'Ã|Â') 'Name identifies logical canvas without encoding corruption'
        $templatePath=Join-Path $store ('Templates/'+$template.Id+'/Files')
        foreach($file in Get-ChildItem -LiteralPath $templatePath -Recurse -File){
            $text=[IO.File]::ReadAllText($file.FullName);[xml]$xml=$text
            Assert ($text -notmatch 'KeyBindings|position_map|PetName[1-6]|Identity\(|selected_group|selected_bar|NumHotbars|Login|Password') 'Template contains no actions, user items, pet names, bindings, chat routing or credentials'
            foreach($rect in $xml.SelectNodes('//Rect[@name="WindowFrame"]')){
                Assert ($rect.value -match '^Rect\((\d+\.\d+),(\d+\.\d+),(\d+\.\d+),(\d+\.\d+)\)$') 'WindowFrame uses observed AO Rect syntax'
                $x1=[double]::Parse($Matches[1],[Globalization.CultureInfo]::InvariantCulture);$y1=[double]::Parse($Matches[2],[Globalization.CultureInfo]::InvariantCulture)
                $x2=[double]::Parse($Matches[3],[Globalization.CultureInfo]::InvariantCulture);$y2=[double]::Parse($Matches[4],[Globalization.CultureInfo]::InvariantCulture)
                Assert ($x1 -ge 0 -and $y1 -ge 0 -and $x2 -gt $x1 -and $y2 -gt $y1 -and $x2 -le $width -and $y2 -le $height) 'Every generated rectangle fits the requested logical canvas'
            }
            foreach($dockConfig in $xml.SelectNodes('//Archive[@name="dock_config"]')){
                Assert ($dockConfig.SelectNodes('Array[@name="dock_node_configs"]/Archive').Count -eq $dockConfig.SelectNodes('Array[@name="docked_view_identities"]/String').Count) 'Dock members and page configurations match'
            }
        }
        $target=Join-Path $root ('Targets-'+$width+'/Char'+(100+$index));$index++
        Write-Fixture (Join-Path $target 'Prefs.xml') '<Root><Value name="PetName1" value="My original pet"/><Value name="NumHotbars" value="9"/><Value name="camera_mode" value="KEEP"/><Archive name="KeyBindings" code="0"><String name="private" value="KEEP"/></Archive></Root>'
        Write-Fixture (Join-Path $target 'Containers/ShortcutBar_0.xml') '<Archive code="0"><Rect name="WindowFrame" value="Rect(1,2,403,39)"/><Int32 name="selected_bar" value="7"/><Archive name="position_map" code="0"><Array name="item_id"><Identity value="Identity(104,64)"/></Array><Array name="item_pos"><IPoint value="IPoint(9,0)"/></Array></Archive></Archive>'
        Write-Fixture (Join-Path $target 'Containers/ShortcutBar_9.xml') '<Archive code="0"><Rect name="WindowFrame" value="Rect(10,20,412,57)"/></Archive>'
        Write-Fixture (Join-Path $target 'Chat/Windows/Window1/Config.xml') '<Archive code="0"><String name="name" value="My private chat"/><Array name="selected_group_names"><String value="Private group"/></Array><Archive name="chat_window_config" code="0"><Rect name="WindowFrame" value="Rect(1,2,301,202)"/></Archive></Archive>'
        Write-Fixture (Join-Path $target 'DockAreas/RollupArea.xml') '<Archive code="0"><Archive name="dock_config" code="0"><Array name="dock_node_configs"><Archive code="0"><Float name="page_height" value="123"/></Archive></Array><Array name="docked_view_identities"><String value="&quot;Container_51017x99&quot;"/></Array></Archive></Archive>'
        Write-Fixture (Join-Path $target 'Macros.bin') 'unrelated-macro-file'
        $before=Snapshot $target
        Call 'PreflightApply' @($store,$template.Id,$target)
        $backup=Call 'Apply' @($store,$template.Id,$target)
        Assert ($backup.Id -and $backup.State -eq 'Applied') 'Template applies with a completed recoverable backup'
        [xml]$prefs=[IO.File]::ReadAllText((Join-Path $target 'Prefs.xml'))
        Assert ($prefs.SelectSingleNode('/Root/Value[@name="PetName1"]').value -eq 'My original pet') 'Pet names remain unchanged'
        Assert ($prefs.SelectSingleNode('/Root/Value[@name="NumHotbars"]').value -eq '9') 'Existing enabled hotbar count is preserved'
        Assert ($prefs.SelectSingleNode('/Root/Archive[@name="KeyBindings"]/String').value -eq 'KEEP') 'Key bindings remain unchanged'
        Assert ($prefs.SelectSingleNode('/Root/Value[@name="camera_mode"]').value -eq 'KEEP') 'Unrelated character settings remain unchanged'
        [xml]$bar=[IO.File]::ReadAllText((Join-Path $target 'Containers/ShortcutBar_0.xml'))
        Assert ($bar.SelectSingleNode('//Int32[@name="selected_bar"]').value -eq '7' -and $bar.SelectSingleNode('//Identity').value -eq 'Identity(104,64)') 'Hotbar page and action identity are preserved'
        Assert ((Get-FileHash -LiteralPath (Join-Path $target 'Containers/ShortcutBar_9.xml')).Hash -eq $before['\Containers\ShortcutBar_9.xml']) 'Additional hotbars remain byte-identical'
        [xml]$chat=[IO.File]::ReadAllText((Join-Path $target 'Chat/Windows/Window1/Config.xml'))
        Assert ($chat.SelectSingleNode('/Archive/String[@name="name"]').value -eq 'My private chat' -and $chat.SelectSingleNode('//Array[@name="selected_group_names"]/String').value -eq 'Private group') 'Chat identity and subscriptions remain unchanged'
        [xml]$dock=[IO.File]::ReadAllText((Join-Path $target 'DockAreas/RollupArea.xml'))
        Assert ($dock.OuterXml.Contains('Container_51017x99')) 'Existing backpack docking survives'
        Call 'Restore' @($store,$backup.Id,$target)
        $after=Snapshot $target
        Assert ($after.Count -eq $before.Count) 'Restore removes only newly created layout files'
        foreach($key in $before.Keys){Assert ($after[$key] -eq $before[$key]) 'Restore returns every original file byte-for-byte'}
    }
 }
 Write-Output "PASS: $checks starter layout schema, bounds, isolation, idempotence, apply, preservation and exact restore checks across four canvases. No live Prefs touched."
}finally{
 $resolved=[IO.Path]::GetFullPath($root);$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($resolved).StartsWith('AONexus-StarterTest-')){throw 'Unsafe test cleanup path'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
