param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Web.Extensions
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$type=$assembly.GetType('AOLauncher.GuiLayouts',$true)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static'
$json=New-Object Web.Script.Serialization.JavaScriptSerializer
$checks=0
function Assert($condition,$message){if(!$condition){throw $message};$script:checks++}
function Call($name,[object[]]$values=@()){for($i=0;$i -lt $values.Length;$i++){if($null -ne $values[$i]){$values[$i]=$values[$i].psobject.BaseObject}};$type.GetMethod($name,$flags).Invoke($null,$values)}
function Reject($action,$message){$failed=$false;try{&$action|Out-Null}catch{$failed=$true};Assert $failed $message}
function Write-Fixture($path,$text){[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))|Out-Null;[IO.File]::WriteAllText($path,$text,[Text.UTF8Encoding]::new($false))}
function Read-Fixture($path){[IO.File]::ReadAllText($path)}
function BE([uint32]$value){$bytes=[BitConverter]::GetBytes($value);[Array]::Reverse($bytes);,$bytes}
function Macro-Bytes($entries){$list=New-Object Collections.Generic.List[byte];$list.AddRange([byte[]](BE $entries.Count));foreach($entry in $entries){$list.AddRange([byte[]](BE $entry[0]));foreach($text in @($entry[1],$entry[2])){$bytes=[Text.Encoding]::ASCII.GetBytes($text);$list.AddRange([byte[]](BE $bytes.Length));$list.AddRange($bytes)}};,$list.ToArray()}
function Same-Bytes($path,$bytes){[Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -eq [Convert]::ToBase64String($bytes)}
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('AONexus-GuiLayouts-tests-'+[guid]::NewGuid().ToString('N'))
$library=Join-Path $testRoot 'Library';$source=Join-Path $testRoot 'Source/Char101';$target=Join-Path $testRoot 'Target/Char202'
$sourcePrefs='<Root><Value name="NumHotbars" value="2"/><Value name="PetName1" value="SOURCE-PET"/><Archive name="KeyBindings"><Int32 name="secret-key" value="123"/></Archive><Archive name="NanoViewConfig"><Rect name="WindowFrame" value="Rect(1,2,3,4)"/><String name="DockableViewDockName" value="&quot;DockArea0&quot;"/><Archive name="item_position_map"><String name="secret-item" value="SOURCE-ITEM"/></Archive></Archive></Root>'
$targetPrefs='<Root><Value name="NumHotbars" value="1"/><Value name="PetName1" value="TARGET-PET"/><Archive name="KeyBindings"><Int32 name="secret-key" value="321"/></Archive><Archive name="NanoViewConfig"><Rect name="WindowFrame" value="Rect(5,6,7,8)"/><Archive name="item_position_map"><String name="secret-item" value="TARGET-ITEM"/></Archive></Archive><Value name="UnknownFutureOption" value="keep"/></Root>'
try {
    Write-Fixture (Join-Path $source 'Prefs.xml') $sourcePrefs
    Write-Fixture (Join-Path $source 'Containers/ShortcutBar_0.xml') '<Archive code="0"><Rect name="WindowFrame" value="Rect(20,20,400,60)"/><Int32 name="bar_orientation" value="1"/><Archive name="position_map"><String name="source-action" value="SOURCE-ACTION"/></Archive></Archive>'
    Write-Fixture (Join-Path $source 'Containers/Container_123x999.xml') '<Archive><String name="bag" value="SOURCE-BAG"/></Archive>'
    Write-Fixture (Join-Path $source 'Chat/Windows/Window1/Config.xml') '<Archive code="0"><Archive name="chat_window_config"><Rect name="WindowFrame" value="Rect(3,4,5,6)"/></Archive><String name="name" value="Combat"/><Array name="selected_group_ids"><Int64 value="999"/></Array><String name="output_group" value="PRIVATE-CHANNEL"/><Bool name="is_logged" value="true"/></Archive>'
    Write-Fixture (Join-Path $source 'Chat/Windows/Window1/InputHistory.xml') 'PRIVATE-HISTORY'
    Write-Fixture (Join-Path $source 'Chat/Windows/Window1/Log.txt') 'PRIVATE-CHAT'
    Write-Fixture (Join-Path $source 'DockAreas/DockArea0.xml') '<Archive code="0"><String name="dock_name" value="&quot;DockArea0&quot;"/><String name="dock_type" value="&quot;DockTabbedWindow&quot;"/><Archive name="dock_config" code="0"><Rect name="WindowFrame" value="Rect(10,20,30,40)"/><Array name="docked_view_identities"><String value="&quot;nano_window&quot;"/><String value="&quot;Container_123x999.xml&quot;"/></Array><Array name="dock_node_configs"><Archive><Float name="page_height" value="100"/></Archive><Archive><Float name="page_height" value="200"/></Archive></Array></Archive></Archive>'
    Write-Fixture (Join-Path $target 'Prefs.xml') $targetPrefs
    Write-Fixture (Join-Path $target 'Containers/ShortcutBar_0.xml') '<Archive code="0"><Rect name="WindowFrame" value="Rect(1,1,2,2)"/><Archive name="position_map"><String name="target-action" value="TARGET-ACTION"/></Archive></Archive>'
    Write-Fixture (Join-Path $target 'DockAreas/DockArea0.xml') '<Archive><Archive name="dock_config"><Array name="docked_view_identities"><String value="&quot;Container_123x555.xml&quot;"/></Array><Array name="dock_node_configs"><Archive><Float name="page_height" value="777"/></Archive></Array></Archive></Archive>'
    Write-Fixture (Join-Path $target 'DockAreas/DockArea9.xml') '<Archive><Archive name="dock_config"><Array name="docked_view_identities"><String value="&quot;nano_window&quot;"/><String value="&quot;Container_123x888.xml&quot;"/></Array></Archive></Archive>'
    Write-Fixture (Join-Path $target 'Containers/Container_123x555.xml') 'TARGET-BAG'
    Write-Fixture (Join-Path $target 'Chat/Windows/Window1/InputHistory.xml') 'TARGET-HISTORY'
    $before=@{};foreach($file in Get-ChildItem $target -File -Recurse){$before[$file.FullName]=[IO.File]::ReadAllBytes($file.FullName)}
    $template=Call Capture @($library,'Example',$source,'Source char','18.8 synthetic')
    Assert ($template.FileCount -eq 4) 'Capture contains only four supported layout XML files'
    $templateFolder=Join-Path $library ('Templates/'+$template.Id)
    $captured=(Get-ChildItem (Join-Path $templateFolder 'Files') -File -Recurse | ForEach-Object {Read-Fixture $_.FullName}) -join ''
    foreach($private in @('SOURCE-PET','SOURCE-ITEM','SOURCE-ACTION','SOURCE-BAG','PRIVATE-HISTORY','PRIVATE-CHAT','PRIVATE-CHANNEL','selected_group_ids','is_logged','Container_123x999.xml','KeyBindings')){Assert (!$captured.Contains($private)) "Capture excludes $private"}
    Assert (@(Call ListTemplates @($library)).Count -eq 1) 'Template listing finds saved metadata'
    Call RenameTemplate @($library,$template.Id,'Renamed')
    Assert ((@(Call ListTemplates @($library)))[0].Name -eq 'Renamed') 'Template can be renamed'
    $backup=Call Apply @($library,$template.Id,$target)
    Assert ($backup.State -eq 'Applied') 'Application completes with a backup'
    $after=Read-Fixture (Join-Path $target 'Prefs.xml')
    Assert ($after.Contains('Rect(1,2,3,4)') -and $after.Contains('TARGET-PET') -and $after.Contains('TARGET-ITEM') -and $after.Contains('UnknownFutureOption') -and $after.Contains('321')) 'Layout merge preserves destination pets, items, keybindings and unknown settings'
    Assert ((Read-Fixture (Join-Path $target 'Containers/ShortcutBar_0.xml')).Contains('TARGET-ACTION')) 'Hotbar actions survive layout copy'
    $dock=Read-Fixture (Join-Path $target 'DockAreas/DockArea0.xml')
    Assert ($dock.Contains('nano_window') -and $dock.Contains('Container_123x555.xml') -and $dock.Contains('777') -and !$dock.Contains('Container_123x999.xml')) 'Portable docking retains destination backpack membership and page geometry'
    $otherDock=Read-Fixture (Join-Path $target 'DockAreas/DockArea9.xml')
    Assert (!$otherDock.Contains('nano_window') -and $otherDock.Contains('Container_123x888.xml')) 'Moved portable view is removed from old destination dock without losing its backpack'
    Assert ((Read-Fixture (Join-Path $target 'Chat/Windows/Window1/InputHistory.xml')) -eq 'TARGET-HISTORY') 'Destination input history is untouched'
    Assert ((Read-Fixture (Join-Path $target 'Containers/Container_123x555.xml')) -eq 'TARGET-BAG') 'Backpack files are untouched'
    Assert (@(Call ListBackups @($library,$target)).Count -eq 1) 'Backups are scoped to the target character'
    Call Restore @($library,$backup.Id,$target)
    foreach($path in $before.Keys){Assert ([Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -eq [Convert]::ToBase64String($before[$path])) 'Restore returns exact original file bytes'}
    Assert (!(Test-Path (Join-Path $target 'Chat/Windows/Window1/Config.xml'))) 'Restore removes only a newly created layout file'
    Reject {Call Restore @($library,$backup.Id,$source)} 'Backup cannot be restored to another character'
    # Inject a failure after one write. Original bytes must be restored and new files removed.
    $type.GetField('BeforeWrite',$flags).SetValue($null,[Action[int]]{param($index) if($index -eq 1){throw 'Synthetic write failure'}})
    Reject {Call Apply @($library,$template.Id,$target)} 'Partial application reports failure'
    $type.GetField('BeforeWrite',$flags).SetValue($null,$null)
    foreach($path in $before.Keys){Assert ([Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -eq [Convert]::ToBase64String($before[$path])) 'Failed application rolls back exact original bytes'}
    Assert (@(Call ListBackups @($library,$target) | Where-Object State -eq 'Rolled back').Count -eq 1) 'Rollback outcome is recorded'
    $backup=Call Apply @($library,$template.Id,$target)
    Write-Fixture (Join-Path $target 'Prefs.xml') '<Root><Value name="NewPersonalSetting" value="do not overwrite"/></Root>'
    Call Restore @($library,$backup.Id,$target)
    Assert ((Read-Fixture (Join-Path $target 'Prefs.xml')).Contains('NewPersonalSetting')) 'Layout-only restore preserves newer personal data'
    Assert ((Read-Fixture (Join-Path $target 'Prefs.xml')).Contains('Rect(5,6,7,8)')) 'Layout-only restore restores earlier geometry after game rewrites'
    Assert (@(Call ListBackups @($library,$target)).Count -ge 4) 'Restore creates a backup of the current state'
    $components=$assembly.GetType('AOLauncher.GuiLayoutComponents',$true)
    $all=[Enum]::ToObject($components,15);$pets=[Enum]::ToObject($components,8);$hotbars=[Enum]::ToObject($components,2);$keys=[Enum]::ToObject($components,4)
    $optional=Call CaptureWithOptions @($library,'Optional components',$source,'Fixture','test',$all)
    Assert ([int]$optional.Components -eq 15) 'Optional capture records selected component flags'
    Write-Fixture (Join-Path $target 'Prefs.xml') $targetPrefs
    Call PreflightApplyWithOptions @($library,$optional.Id,$target,$pets)
    Call ApplyWithOptions @($library,$optional.Id,$target,$pets)|Out-Null
    $petResult=Read-Fixture (Join-Path $target 'Prefs.xml')
    Assert ($petResult.Contains('SOURCE-PET') -and $petResult.Contains('321') -and $petResult.Contains('Rect(5,6,7,8)')) 'Pet-only rollout preserves destination keys and geometry'
    Call ApplyWithOptions @($library,$optional.Id,$target,$keys)|Out-Null
    Assert ((Read-Fixture (Join-Path $target 'Prefs.xml')).Contains('123')) 'Key bindings copy only when explicitly selected'
    Call ApplyWithOptions @($library,$optional.Id,$target,$hotbars)|Out-Null
    $barResult=Read-Fixture (Join-Path $target 'Containers/ShortcutBar_0.xml')
    Assert ($barResult.Contains('SOURCE-ACTION') -and !$barResult.Contains('TARGET-ACTION')) 'Explicit hotbar selection replaces action contents'
    Reject {Call ApplyWithOptions @($library,$template.Id,$target,$pets)} 'Cannot select components absent from a template'
    Call DeleteTemplate @($library,$optional.Id)
    # Text macros: copied whole, only when selected, and restorable.
    $macros=$assembly.GetType('AOLauncher.TextMacros',$true)
    $macroFlag=[Enum]::ToObject($components,16);$hotMacro=[Enum]::ToObject($components,18)
    $sourceMacros=Macro-Bytes @(@(1,'Follow','/follow'),@(2,'ass','/assist SOURCE-MACRO'))
    $targetMacros=Macro-Bytes (,@(1,'mine','/say TARGET-MACRO'))
    [IO.File]::WriteAllBytes((Join-Path $source 'TextMacro.bin'),$sourceMacros)
    [IO.File]::WriteAllBytes((Join-Path $target 'TextMacro.bin'),$targetMacros)
    $parsed=$macros.GetMethod('Parse',$flags).Invoke($null,@(,$sourceMacros))
    Assert ($parsed.Count -eq 2 -and $parsed[1].Id -eq 2 -and $parsed[1].Name -eq 'ass' -and $parsed[1].Text -eq '/assist SOURCE-MACRO') 'Macro file format is parsed'
    foreach($bad in @(([byte[]](0,0,0,2,0,0,0,1)),([byte[]](0,0,0,0,9)),([byte[]](0,0,0,1,0,0,0,1,255,255,255,255)))){$failed=$false;try{$macros.GetMethod('Parse',$flags).Invoke($null,@(,$bad))|Out-Null}catch{$failed=$true};Assert $failed 'Malformed macro files are rejected'}
    $layoutOnly=Call Capture @($library,'No macros',$source,'Fixture','test')
    Assert (@($layoutOnly.Files | Where-Object Path -eq 'TextMacro.bin').Count -eq 0) 'Layout-only capture excludes macros'
    Call DeleteTemplate @($library,$layoutOnly.Id)
    $withMacros=Call CaptureWithOptions @($library,'With macros',$source,'Fixture','test',$hotMacro)
    Assert ([int]$withMacros.Components -eq 18 -and @($withMacros.Files | Where-Object Path -eq 'TextMacro.bin').Count -eq 1) 'Macro capture stores the macro file'
    $barBefore=Read-Fixture (Join-Path $target 'Containers/ShortcutBar_0.xml')
    Call PreflightApplyWithOptions @($library,$withMacros.Id,$target,$macroFlag)
    $macroBackup=Call ApplyWithOptions @($library,$withMacros.Id,$target,$macroFlag)
    Assert (Same-Bytes (Join-Path $target 'TextMacro.bin') $sourceMacros) 'Macros replace the target macro file byte for byte'
    Assert ((Read-Fixture (Join-Path $target 'Containers/ShortcutBar_0.xml')) -eq $barBefore) 'Macros-only copy leaves hotbars untouched'
    Assert (@($macroBackup.Changes).Count -eq 1) 'Macros-only copy changes only the macro file'
    Call Restore @($library,$macroBackup.Id,$target)
    Assert (Same-Bytes (Join-Path $target 'TextMacro.bin') $targetMacros) 'Macro restore returns the original macros'
    $macroBackup=Call ApplyWithOptions @($library,$withMacros.Id,$target,$macroFlag)
    $newer=Macro-Bytes @(@(1,'Follow','/follow'),@(2,'ass','/assist SOURCE-MACRO'),@(3,'new','/say NEWER'))
    [IO.File]::WriteAllBytes((Join-Path $target 'TextMacro.bin'),$newer)
    $countBefore=@(Call ListBackups @($library,$target)).Count
    Call Restore @($library,$macroBackup.Id,$target)
    Assert (Same-Bytes (Join-Path $target 'TextMacro.bin') $targetMacros) 'Macro restore after a game rewrite returns the original macros'
    $undo=@(Call ListBackups @($library,$target))[0]
    Assert (@(Call ListBackups @($library,$target)).Count -eq $countBefore+1 -and $undo.State -eq 'Applied') 'Newer macros are kept in an undo backup'
    $fresh=Join-Path $testRoot 'Fresh/Char707';Write-Fixture (Join-Path $fresh 'Prefs.xml') '<Root/>'
    $freshBackup=Call ApplyWithOptions @($library,$withMacros.Id,$fresh,$hotMacro)
    Assert ((Same-Bytes (Join-Path $fresh 'TextMacro.bin') $sourceMacros) -and (Read-Fixture (Join-Path $fresh 'Containers/ShortcutBar_0.xml')).Contains('SOURCE-ACTION')) 'Hotbars and macros copy together to a char without macros'
    Call Restore @($library,$freshBackup.Id,$fresh)
    Assert (!(Test-Path (Join-Path $fresh 'TextMacro.bin'))) 'Restore removes a newly created macro file'
    $templateMacro=Join-Path $library ('Templates/'+$withMacros.Id+'/Files/TextMacro.bin');[IO.File]::WriteAllBytes($templateMacro,$targetMacros)
    Reject {Call ApplyWithOptions @($library,$withMacros.Id,$target,$macroFlag)} 'Macro template tampering is rejected'
    [IO.File]::WriteAllBytes((Join-Path $source 'TextMacro.bin'),[byte[]](0,0,0,5,1))
    Reject {Call CaptureWithOptions @($library,'Broken macros',$source,'Fixture','test',$macroFlag)} 'Unsupported macro files are not captured'
    Remove-Item -LiteralPath (Join-Path $source 'TextMacro.bin'),(Join-Path $target 'TextMacro.bin')
    Call DeleteTemplate @($library,$withMacros.Id)
    for($round=0;$round -lt 6;$round++){
        Write-Fixture (Join-Path $target 'Prefs.xml') $targetPrefs
        $retentionBackup=Call Apply @($library,$template.Id,$target)
        if($retentionBackup.Id){Call Restore @($library,$retentionBackup.Id,$target)}
    }
    Assert (@(Call ListBackups @($library,$target)).Count -le 10) 'Complete recovery snapshots are bounded to ten per character'
    Reject {Call Apply @($library,'../outside',$target)} 'Template IDs cannot escape library'
    Reject {Call Capture @($library,'Bad',$testRoot,'','')} 'Source must be a character directory'
    $manifestPath=Join-Path $templateFolder 'manifest.json';$manifest=Read-Fixture $manifestPath
    $modified=$json.DeserializeObject($manifest);$modified.Files[0].Path='../outside.xml';Write-Fixture $manifestPath ($json.Serialize($modified))
    Reject {Call Apply @($library,$template.Id,$target)} 'Manifest traversal is rejected'
    Write-Fixture $manifestPath $manifest
    $xmlPath=Join-Path $templateFolder ('Files/'+$template.Files[0].Path);$original=Read-Fixture $xmlPath;Write-Fixture $xmlPath ($original+' ')
    Reject {Call Apply @($library,$template.Id,$target)} 'Template hash tampering is rejected'
    Write-Fixture $xmlPath $original
    $badSource=Join-Path $testRoot 'Bad/Char303';Write-Fixture (Join-Path $badSource 'Prefs.xml') '<!DOCTYPE Root [<!ENTITY test SYSTEM "file:///C:/Windows/win.ini">]><Root><Value name="NumHotbars" value="&test;"/></Root>'
    Reject {Call Capture @($library,'DTD',$badSource,'','')} 'DTD and external entities are rejected'
    Write-Fixture (Join-Path $badSource 'Prefs.xml') ('x'*(1024*1024+1))
    Reject {Call Capture @($library,'Oversized',$badSource,'','')} 'Oversized preference files are rejected'
    $junction=Join-Path $testRoot 'Char404'
    New-Item -ItemType Junction -Path $junction -Target $source | Out-Null
    try {Reject {Call Capture @($library,'Junction',$junction,'','')} 'Character junction is rejected'}finally{[IO.Directory]::Delete($junction,$false)}
    $nested=Join-Path $templateFolder 'Linked'
    New-Item -ItemType Junction -Path $nested -Target $source | Out-Null
    try {Reject {Call DeleteTemplate @($library,$template.Id)} 'Template deletion never follows a junction';Assert (Test-Path (Join-Path $source 'Prefs.xml')) 'Junction target remains intact'}finally{[IO.Directory]::Delete($nested,$false)}
    Call DeleteTemplate @($library,$template.Id)
    Assert (@(Call ListTemplates @($library)).Count -eq 0) 'Deleting a template removes only its library directory'
    Assert (@(Call ListBackups @($library,$target)).Count -ge 2) 'Deleting a template preserves recovery backups'
    $reset=Join-Path $testRoot 'Reset/Char606';$game=Join-Path $testRoot 'Game'
    Write-Fixture (Join-Path $game 'cd_image/gui/Default/CharPrefs.xml') '<Root><Value name="NumHotbars" value="1"/><Value name="shortcutbar_window" value="true"/><Value name="team_view" value="false"/><Archive name="KeyBindings"><Int32 name="bad" value="1"/></Archive></Root>'
    Write-Fixture (Join-Path $reset 'Prefs.xml') ($targetPrefs.Replace('value="1"','value="4"')+' ')
    Copy-Item -LiteralPath (Join-Path $source 'Containers') -Destination $reset -Recurse
    Copy-Item -LiteralPath (Join-Path $source 'Chat') -Destination $reset -Recurse
    Copy-Item -LiteralPath (Join-Path $source 'DockAreas') -Destination $reset -Recurse
    $original=@{};Get-ChildItem $reset -File -Recurse | ForEach-Object {$original[$_.FullName]=(Get-FileHash $_.FullName).Hash}
    Call ResetToGameDefault @($library,$game,$reset,$true)|Out-Null
    foreach($entry in $original.GetEnumerator()){Assert ((Get-FileHash $entry.Key).Hash -eq $entry.Value) 'Reset preflight is read-only'}
    $resetBackup=Call ResetToGameDefault @($library,$game,$reset,$false)
    $result=Read-Fixture (Join-Path $reset 'Prefs.xml')
    Assert ($result.Contains('name="shortcutbar_window" value="true"') -and $result.Contains('name="team_view" value="false"')) 'Reset uses target installation default visibility'
    Assert ($result.Contains('name="NumHotbars" value="4"') -and $result.Contains('TARGET-PET') -and $result.Contains('secret-key') -and !$result.Contains('name="bad"')) 'Reset preserves count, pet names and bindings'
    Assert (!$result.Contains('WindowFrame') -and $result.Contains('TARGET-ITEM') -and $result.Contains('UnknownFutureOption')) 'Reset removes supported geometry only'
    $bar=Read-Fixture (Join-Path $reset 'Containers/ShortcutBar_0.xml')
    Assert ($bar.Contains('SOURCE-ACTION') -and !$bar.Contains('WindowFrame')) 'Reset preserves actions while resetting bar geometry'
    $chat=Read-Fixture (Join-Path $reset 'Chat/Windows/Window1/Config.xml')
    Assert ($chat.Contains('Combat') -and $chat.Contains('PRIVATE-CHANNEL') -and $chat.Contains('999') -and !$chat.Contains('WindowFrame')) 'Reset preserves chat identity and subscriptions'
    $dock=Read-Fixture (Join-Path $reset 'DockAreas/DockArea0.xml')
    Assert ($dock.Contains('Container_123x999') -and !$dock.Contains('nano_window')) 'Reset preserves backpack docking and clears portable view overrides'
    Call Restore @($library,$resetBackup.Id,$reset)|Out-Null
    foreach($entry in $original.GetEnumerator()){Assert ((Get-FileHash $entry.Key).Hash -eq $entry.Value) 'Reset backup restores original bytes'}
    Reject {Call ResetToGameDefault @($library,(Join-Path $testRoot 'NoGame'),$reset,$false)} 'Missing game defaults block reset'
    Write-Fixture (Join-Path $game 'cd_image/gui/Default/CharPrefs.xml') '<!DOCTYPE Root [<!ENTITY x SYSTEM "file:///secret">]><Root>&x;</Root>'
    Reject {Call ResetToGameDefault @($library,$game,$reset,$false)} 'Reset rejects external entities in installation defaults'
    Write-Output "PASS: $checks GUI layout capture, reset, exclusion, merge, backup, restore, rollback, integrity, traversal, DTD, bounds and junction checks. Synthetic files only."
} finally {
    $type.GetField('BeforeWrite',$flags).SetValue($null,$null)
    $resolved=[IO.Path]::GetFullPath($testRoot)
    if(!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'AONexus-GuiLayouts-tests-*'){throw 'Unsafe cleanup path'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
