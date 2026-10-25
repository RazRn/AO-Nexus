param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '../dist/AONexus.exe'),
    [string]$StoreRoot,
    [ValidateRange(1280,7680)][int]$Width = 1920,
    [ValidateRange(720,4320)][int]$Height = 1080
)
# Creates original, layout-only examples in the Nexus template library. Never applies them to a game.
# Coordinates use the game's logical client canvas, not desktop pixels or monitor resolution.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web.Extensions
if (!$StoreRoot) { $StoreRoot = Join-Path ([IO.Path]::GetTempPath()) ("AONexus-StarterLayouts-{0}x{1}" -f $Width,$Height) }
$StoreRoot = [IO.Path]::GetFullPath($StoreRoot)
$funcom = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Funcom')).TrimEnd('\') + '\'
if ($StoreRoot.StartsWith($funcom,[StringComparison]::OrdinalIgnoreCase) -or $StoreRoot -match '(?i)[\\/]Prefs([\\/]|$)') { throw 'Output must be a Nexus template library, never a game Prefs folder.' }
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$engine = $assembly.GetType('AOLauncher.GuiLayouts', $true)
$flags = [Reflection.BindingFlags]'NonPublic,Public,Static'
$capture = $engine.GetMethod('Capture', $flags)
function Require-SafePath([string]$path) { $null=$engine.GetMethod('SafePath',$flags).Invoke($null,[object[]]@($path.psobject.BaseObject)) }
function Require-Child([string]$parent,[string]$child) {
    $prefix=[IO.Path]::GetFullPath($parent).TrimEnd('\')+'\';$full=[IO.Path]::GetFullPath($child)
    if(!$full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Generated path escapes its intended directory.'}
    Require-SafePath $full
}
Require-SafePath $StoreRoot
$json = [Web.Script.Serialization.JavaScriptSerializer]::new()
$utf8 = [Text.UTF8Encoding]::new($false)
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('AONexus-StarterLayouts-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($scratch)
$null = [IO.Directory]::CreateDirectory($StoreRoot)
$previewRoot = Join-Path $StoreRoot 'StarterPreviews'
$null = [IO.Directory]::CreateDirectory($previewRoot)
function Write-Text($path, $text) { Require-SafePath $path; $null=[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)); [IO.File]::WriteAllText($path,$text,$utf8) }
function Esc($text) { [Security.SecurityElement]::Escape([string]$text) }
function Rect([int]$x,[int]$y,[int]$w,[int]$h) {
    if ($x -lt 0 -or $y -lt 0 -or $w -le 0 -or $h -le 0 -or $x+$w -gt $Width -or $y+$h -gt $Height) { throw 'Generated rectangle is outside its canvas.' }
    '<Rect name="WindowFrame" value="Rect({0}.000000,{1}.000000,{2}.000000,{3}.000000)" />' -f $x,$y,($x+$w),($y+$h)
}
function Panel($label,[int]$x,[int]$y,[int]$w,[int]$h) { [pscustomobject]@{Label=$label;X=$x;Y=$y;W=$w;H=$h} }
function Stable-Id($text) { $sha=[Security.Cryptography.SHA256]::Create(); try { ([BitConverter]::ToString($sha.ComputeHash($utf8.GetBytes($text)))).Replace('-','').ToLowerInvariant().Substring(0,32) } finally {$sha.Dispose()} }
function Dock-Xml($dockName,$view,$panel) {
    '<Archive code="0"><Archive name="dock_config" code="0"><Int32 name="selected_tab" value="0"/>' +
    (Rect $panel.X $panel.Y $panel.W $panel.H) + '<Bool name="WindowPinButtonState" value="false"/>' +
    '<Array name="dock_node_configs"><Archive code="0"/></Array><Array name="docked_view_identities"><String value="&quot;' + $view + '&quot;"/></Array></Archive>' +
    '<String name="dock_type" value="&quot;DockTabbedWindow&quot;"/><String name="dock_name" value="&quot;' + $dockName + '&quot;"/></Archive>'
}
function Preview($design,$panels,$side,$path) {
    $items=[Collections.Generic.List[string]]::new()
    foreach($p in $panels) {
        $items.Add(('<rect x="{0}" y="{1}" width="{2}" height="{3}" rx="4" fill="#172d38" stroke="#44b5c8"/><text x="{4}" y="{5}" font-size="18" fill="#d3edf2">{6}</text>' -f $p.X,$p.Y,$p.W,$p.H,($p.X+12),($p.Y+25),(Esc $p.Label)))
    }
    $items.Add(('<rect x="{0}" y="80" width="250" height="{1}" fill="#172d38" stroke="#44b5c8" stroke-dasharray="7 5"/><text x="{2}" y="106" font-size="17" fill="#d3edf2">Skin-managed sidepanel</text>' -f ($Width-265),([Math]::Min(530,$Height-220)),($Width-253)))
    $i=0; foreach($name in $side) { $items.Add(('<text x="{0}" y="{1}" font-size="17" fill="#94bbc7">{2}</text>' -f ($Width-253),(140+$i*28),(Esc $name))); $i++ }
    $svg='<svg xmlns="http://www.w3.org/2000/svg" width="'+$Width+'" height="'+$Height+'" viewBox="0 0 '+$Width+' '+$Height+'"><rect width="100%" height="100%" fill="#09131c"/><text x="20" y="30" font-family="sans-serif" font-size="21" fill="#4fc3d4">'+(Esc $design.Name)+' — '+$Width+'×'+$Height+'</text><text x="20" y="55" font-size="16" font-family="sans-serif" fill="#91a8b6">Schematic, not an in-game screenshot. Solid boxes use generated XML coordinates; dotted panel is skin-managed.</text><g font-family="sans-serif">'+($items -join '')+'</g><text x="20" y="'+($Height-8)+'" font-family="sans-serif" font-size="15" fill="#91a8b6">Hotbar actions/count, health bars, chat routing and unlisted windows remain yours.</text></svg>'
    Write-Text $path $svg
}
$guide='https://www.ao-universe.com/guides/classic-ao/gameplay-guides-6/basic-gui-guide'
$designs=@(
    @{Key='compact';Name='Compact Solo';Open=@('ncu_window');Order=@('ncu_window','team_view','nano_window','wear_window','mission_window','map_window');ChatWidth=600;ChatHeight=170;Description='Compact lower-left chat and a short NCU-first right sidepanel; team and utility panels start closed.';Source='https://www.mojeanarchy.cz/news/spusteni-hry1/'},
    @{Key='healer';Name='Team Healer';Open=@('team_view','ncu_window','nano_window');Order=@('ncu_window','nano_window','wear_window','mission_window','map_window');ChatWidth=650;ChatHeight=205;Description='A floating team panel at upper right, with NCU and nanos grouped in the right sidepanel. Team visibility takes priority.';Source='https://babaoroody.wordpress.com/2009/06/27/customize-your-ao-hud-gui/'},
    @{Key='pets';Name='Pet Commander';Open=@('pet_window','team_view','ncu_window','nano_window');Order=@('team_view','ncu_window','nano_window','wear_window','mission_window','map_window');ChatWidth=640;ChatHeight=170;Description='A visible pet panel above lower-left chat, with team, NCU and nanos in the right sidepanel. A pet must exist for the game to display the pet panel.';Source=$guide},
    @{Key='expedition';Name='Expedition Map';Open=@('team_view','map_window','mission_window');Order=@('team_view','ncu_window','nano_window','wear_window');ChatWidth=600;ChatHeight=170;Description='Local map at upper right, missions at upper left, compact chat below; the sidepanel keeps team information accessible.';Source=$guide},
    @{Key='combat';Name='Clean Combat';Open=@('team_view','ncu_window');Order=@('team_view','ncu_window','nano_window','wear_window','mission_window','map_window');ChatWidth=750;ChatHeight=130;Description='Short, wide lower-left chat and team/NCU panels on the right, leaving the center clear. Existing health bars and hotbars are preserved.';Source='https://athenpaladins.org/forums/viewtopic.php?t=342'}
)
$results=[Collections.Generic.List[object]]::new()
try {
    foreach($design in $designs) {
        $name=$design.Name+' · '+$Width+'×'+$Height
        $id=Stable-Id ('AO Nexus original starter layouts v1/'+$design.Key+'/'+$Width+'/'+$Height)
        $final=Join-Path $StoreRoot ('Templates/'+$id)
        $source=Join-Path $scratch 'Char1'
        # Each design has a fresh source. Only our synthetic directory is removed.
        if(Test-Path -LiteralPath $source){Require-Child $scratch $source; Remove-Item -LiteralPath $source -Recurse -Force}
        $null=[IO.Directory]::CreateDirectory($source)
        $panels=[Collections.Generic.List[object]]::new()
        $prefs=[Collections.Generic.List[string]]::new()
        foreach($visible in @('team_view','ncu_window','nano_window','wear_window','mission_window','map_window','pet_window')) {
            $prefs.Add('<Value name="'+$visible+'" value="'+([string]($design.Open -contains $visible)).ToLowerInvariant()+'"/>')
        }
        $prefs.Add('<Value name="cc_rollup_panel" value="true"/>')
        $configs=@{team_view='TeamViewConfig';ncu_window='NCUWindowConfig';nano_window='NanoViewConfig';wear_window='WearViewConfig';mission_window='MissionViewConfig';map_window='PFMapWindowConfig'}
        foreach($view in $configs.Keys) {
            $dock='RollupArea'
            if($design.Key -eq 'healer' -and $view -eq 'team_view'){$dock='DockArea90'}
            if($design.Key -eq 'expedition' -and $view -eq 'map_window'){$dock='DockArea90'}
            if($design.Key -eq 'expedition' -and $view -eq 'mission_window'){$dock='DockArea91'}
            $prefs.Add('<Archive name="'+$configs[$view]+'" code="0"><String name="DockableViewDockName" value="&quot;'+$dock+'&quot;"/></Archive>')
        }
        if($design.Key -eq 'healer') {
            $p=Panel 'Team' ($Width-545) 100 250 200; $panels.Add($p)
            Write-Text (Join-Path $source 'DockAreas/DockArea90.xml') (Dock-Xml 'DockArea90' 'team_view' $p)
        }
        if($design.Key -eq 'pets') {
            $p=Panel 'Pets (when summoned)' 20 ($Height-550) 310 170; $panels.Add($p)
            $prefs.Add('<Archive name="PetWindowConfig" code="0">'+(Rect $p.X $p.Y $p.W $p.H)+'<Bool name="WindowPinButtonState" value="false"/></Archive>')
        }
        if($design.Key -eq 'expedition') {
            $mapHeight=[Math]::Min(340,$Height-380)
            $p=Panel 'Local map' ($Width-610) 100 320 $mapHeight; $panels.Add($p)
            Write-Text (Join-Path $source 'DockAreas/DockArea90.xml') (Dock-Xml 'DockArea90' 'map_window' $p)
            $p=Panel 'Missions' 20 100 280 ([Math]::Min(300,$Height-380)); $panels.Add($p)
            Write-Text (Join-Path $source 'DockAreas/DockArea91.xml') (Dock-Xml 'DockArea91' 'mission_window' $p)
        }
        Write-Text (Join-Path $source 'Prefs.xml') ('<Root>'+($prefs -join '')+'</Root>')
        $pages=[Collections.Generic.List[string]]::new();$ids=[Collections.Generic.List[string]]::new();$side=[Collections.Generic.List[string]]::new()
        foreach($view in $design.Order) {
            $expanded=$design.Open -contains $view
            $pageHeight=if($view -eq 'team_view'){165}elseif($view -eq 'ncu_window'){150}elseif($view -eq 'nano_window'){180}else{220}
            $pages.Add('<Archive code="0"><Bool name="is_page_expanded" value="'+([string]$expanded).ToLowerInvariant()+'"/><Float name="page_height" value="'+$pageHeight+'.000000"/></Archive>')
            $ids.Add('<String value="&quot;'+$view+'&quot;"/>')
            $side.Add($view.Replace('_window','').Replace('_view','')+$(if($expanded){' (open)'}else{' (closed)'}))
        }
        $rollup='<Archive code="0"><Archive name="dock_config" code="0"><Array name="dock_node_configs">'+($pages -join '')+'</Array><Array name="docked_view_identities">'+($ids -join '')+'</Array><Float name="scroll_offset" value="0.000000"/></Archive><String name="dock_type" value="&quot;RollupController&quot;"/><String name="dock_name" value="&quot;RollupArea&quot;"/></Archive>'
        Write-Text (Join-Path $source 'DockAreas/RollupArea.xml') $rollup
        $chat=Panel 'Existing chat Window1' 20 ($Height-$design.ChatHeight-120) $design.ChatWidth $design.ChatHeight; $panels.Add($chat)
        # Do not set window_name, name, selected groups, input routing or log flags.
        Write-Text (Join-Path $source 'Chat/Windows/Window1/Config.xml') ('<Archive code="0"><Archive name="chat_window_config" code="0">'+(Rect $chat.X $chat.Y $chat.W $chat.H)+'<Bool name="WindowPinButtonState" value="false"/></Archive></Archive>')
        # Layout changes the frames only, never position_map actions, selected page or NumHotbars.
        # A character with fewer visible hotbars keeps that count; enable extra bars in-game if wanted.
        $barCount=if($design.Key -in @('compact','pets')){3}else{2}
        $barX=if($design.Key -in @('compact','pets','combat')){$Width-430}else{[int](($Width-402)/2)}
        for($bar=0;$bar -lt $barCount;$bar++) {
            $p=Panel ('Hotbar '+($bar+1)+' (if enabled)') $barX ($Height-30-$barCount*39+$bar*39) 402 37
            $panels.Add($p)
            Write-Text (Join-Path $source ('Containers/ShortcutBar_'+$bar+'.xml')) ('<Archive code="0">'+(Rect $p.X $p.Y $p.W $p.H)+'<Int32 name="bar_orientation" value="0"/></Archive>')
        }
        for($i=0;$i -lt $panels.Count;$i++){for($j=$i+1;$j -lt $panels.Count;$j++){
            $a=$panels[$i];$b=$panels[$j]
            if($a.X -lt $b.X+$b.W -and $a.X+$a.W -gt $b.X -and $a.Y -lt $b.Y+$b.H -and $a.Y+$a.H -gt $b.Y){throw ('Generated panels overlap: '+$a.Label+' / '+$b.Label)}
        }}
        if(!(Test-Path -LiteralPath $final)) {
            $arguments=[object[]]@($StoreRoot,$name,$source,'AO Nexus original starter layout',('Classic AO XML · '+$Width+'x'+$Height+' logical canvas · in-game validation pending'))
            for($a=0;$a -lt $arguments.Length;$a++) { $arguments[$a]=([string]$arguments[$a]).psobject.BaseObject }
            $template=$capture.Invoke($null,$arguments)
            $generated=Join-Path $StoreRoot ('Templates/'+$template.Id)
            $template.Id=$id
            Write-Text (Join-Path $generated 'manifest.json') ($json.Serialize($template))
            Require-Child (Join-Path $StoreRoot 'Templates') $generated
            Require-Child (Join-Path $StoreRoot 'Templates') $final
            Move-Item -LiteralPath $generated -Destination $final
        }
        $preview=Join-Path $previewRoot ($design.Key+'.svg'); Preview $design $panels $side $preview
        $results.Add([pscustomobject]@{Id=$id;Name=$name;Description=$design.Description;Source=$design.Source;Preview=$preview;TemplateDirectory=$final;Panels=$panels.ToArray()})
    }
    $notes=@('# AO Nexus starter layouts', '', 'Five original layout-only starting points for a '+$Width+'×'+$Height+' logical game client canvas. They are community-inspired arrangements, not copies of screenshots or bundled third-party preference files.', '', 'Templates are captured through the same whitelist engine as user layouts. They do not change hotbar actions/count/selected pages, key bindings, pet names, equipment or item maps, chat routing/groups/names/history, skins, credentials, or server settings. They change selected window visibility, docking order, saved floating-window geometry, the existing Window1 chat rectangle, and the horizontal frames of the first two hotbars (three for Compact Solo and Pet Commander). Hotbars beyond those are unchanged. If fewer bars are enabled, their count remains unchanged: enable additional bars manually in the game if wanted.', '', 'Use Game GUI to apply to a test character first. Nexus backs up changed files and requires affected clients to exit; restore from Backups if needed. These files have passed XML, preservation, bounds and restore tests, but have not been visually confirmed inside a running AO client. Client versions, skin dimensions, resolution and DPI can alter placement. Pet windows may require a summoned pet. The right sidepanel is positioned by the selected skin; its dotted preview is illustrative. Chat Window1 usually exists after a normal first logout; extra chat windows remain unchanged. Unmodified extra hotbars may overlap the new chat position and should be moved manually if necessary.', '', 'Generated screenshots are not supplied. SVG previews are explicitly schematic; solid rectangles reflect generated XML coordinates.', '', 'Repeat generation preserves existing template folders with matching stable IDs. It does not replace templates the user may have customized.', '')
    foreach($item in $results){$notes+=@('## '+$item.Name,$item.Description,'Pattern reference: '+$item.Source,'','![Schematic]('+[IO.Path]::GetFileName($item.Preview)+')','')}
    $notes+=@('## XML and resolution evidence','Observed local AO Prefs use Root/Archive, Rect WindowFrame, named DockArea files and RollupArea member arrays. Only fresh synthetic XML is packaged; no user names or local character data are included.','Rect coordinate order and fixed-window caveat: https://forums-archive.anarchy-online.com/showthread.php?505330-GUI-settings-Across-all-characters=','DPI changes the effective GUI scale: https://forums.funcom.com/t/ui-size-problem/195910','')
    Write-Text (Join-Path $previewRoot 'README.md') ($notes -join "`r`n")
    Write-Text (Join-Path $previewRoot 'catalog.json') (ConvertTo-Json -InputObject $results.ToArray() -Depth 4)
    $cards=foreach($item in $results){'<section><h2>'+(Esc $item.Name)+'</h2><p>'+(Esc $item.Description)+'</p><img src="'+[IO.Path]::GetFileName($item.Preview)+'" alt="Schematic layout, not an in-game screenshot"><p><a href="'+(Esc $item.Source)+'">Community reference</a></p></section>'}
    Write-Text (Join-Path $previewRoot 'index.html') ('<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>AO Nexus starter layouts</title><style>body{background:#09131c;color:#d3edf2;font:16px system-ui;margin:32px auto;max-width:1200px;padding:0 20px}h1,h2,a{color:#4fc3d4}p{line-height:1.5;color:#a8c6d0}section{border:1px solid #284553;padding:20px;margin:24px 0;border-radius:6px}img{width:100%;height:auto}header{max-width:950px}</style><header><h1>Five layout-only starting points</h1><p>Schematic previews, not game screenshots. Solid rectangles match the generated XML. The right sidepanel is skin-managed. Existing hotbar actions/count, key bindings, pet names, chat subscriptions and health bars are preserved. Test one character first; restore its automatic backup if needed. No live preferences were changed to make these previews.</p></header>'+($cards -join '')+'</html>')
    $results
} finally {
    $full=[IO.Path]::GetFullPath($scratch);$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$full.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($full).StartsWith('AONexus-StarterLayouts-')){throw 'Unsafe scratch cleanup path.'}
    Remove-Item -LiteralPath $full -Recurse -Force
}
