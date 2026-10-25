param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\src\Launcher\bin\Release\AONexus.exe'),
    [string]$PreviewDirectory = (Join-Path $PSScriptRoot '..\logs\news-multiselect-previews')
)
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Use 32-bit Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Web.Extensions
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NewsTestNative {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    public static void ClickAt(IntPtr window, int x, int y) {
        var point = new IntPtr((y << 16) | (x & 0xffff));
        SendMessage(window, 0x0200, IntPtr.Zero, point);
        SendMessage(window, 0x0201, new IntPtr(1), point);
        SendMessage(window, 0x0202, IntPtr.Zero, point);
    }
}
'@
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$panelType = $assembly.GetType('AOLauncher.NewsPanel', $true)
$mainType = $assembly.GetType('AOLauncher.MainForm', $true)
$textsType = $assembly.GetType('AOLauncher.Localization.Texts', $true)
$storeType = $assembly.GetType('AOLauncher.SettingsStore', $true)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$staticFlags = [Reflection.BindingFlags]'Public,NonPublic,Static'
$serializer = [Web.Script.Serialization.JavaScriptSerializer]::new()
$script:checks = 0
$script:selectionEvents = 0
function Assert-Check([bool]$Condition, [string]$Message) { if (!$Condition) { throw "FAIL: $Message" }; $script:checks++ }
function Field($Object, [string]$Name) { $Object.GetType().GetField($Name, $flags).GetValue($Object) }
function Invoke-Private($Object, [string]$Name) { $Object.GetType().GetMethod($Name, $flags).Invoke($Object, @()) }
function Set-Language([string]$Language) { $textsType.GetMethod('SetLanguage', $staticFlags).Invoke($null, @($Language)) | Out-Null }
function Layout($Control) { $Control.PerformLayout(); foreach ($child in $Control.Controls) { $child.PerformLayout() }; [Windows.Forms.Application]::DoEvents() }
function Selected($Panel) { @($Panel.SelectedSourceIds | Sort-Object) -join '|' }
function Articles($Panel) { @((Field $Panel 'cards').Controls | Where-Object { $_.Tag -like 'article:*' } | ForEach-Object { $_.Tag } | Sort-Object) -join '|' }
function Deserialize([string]$Json) { $storeType.GetMethod('Deserialize', $staticFlags).Invoke($null, [object[]]@($Json)) }
function Show-Offscreen($Form) {
    $Form.ShowInTaskbar = $false
    $Form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $Form.Location = [Drawing.Point]::new(-32000,-32000)
    $Form.Show(); Layout $Form
}
function Preview($Control, [string]$Name) {
    if (!$PreviewDirectory) { return }
    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PreviewDirectory)) | Out-Null
    $bitmap = [Drawing.Bitmap]::new($Control.Width,$Control.Height)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap); $dc = $graphics.GetHdc()
        try { Assert-Check ([NewsTestNative]::PrintWindow($Control.Handle,$dc,0)) 'The owned synthetic news window renders natively.' }
        finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        $bitmap.Save((Join-Path $PreviewDirectory $Name),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}
function Check-CardWidths($Panel) {
    $cards = Field $Panel 'cards'
    foreach ($card in $cards.Controls) {
        Assert-Check ($card.Width -le $cards.ClientSize.Width) 'News cards fit the scroll viewport.'
        foreach ($child in $card.Controls) { Assert-Check ($child.Right -le $card.Width) 'Article text stays inside its card.' }
    }
}
function Check-WebsiteMenu($Panel, [string]$Expected) {
    $menu = Invoke-Private $Panel 'CreateSourceMenu'
    try {
        $names = @($menu.Items | ForEach-Object { $_.Text } | Sort-Object) -join '|'
        Assert-Check ($names -ceq $Expected) 'Website choices contain exactly the selected public publishers.'
        Assert-Check (!$menu.ShowImageMargin -and !$menu.ShowCheckMargin) 'Website links have no unused native image or check gutter.'
    } finally { $menu.Dispose() }
}
function Native-ItemClick($Menu, $Item) {
    Assert-Check ($Menu.Visible -and !$Menu.IsDisposed -and $Item.Available) 'Native click targets only a visible item in the owned test popup.'
    $bounds = $Item.Bounds
    [NewsTestNative]::ClickAt($Menu.Handle,$bounds.X + [int]($bounds.Width / 2),$bounds.Y + [int]($bounds.Height / 2))
}
function Check-WebsiteItemDismissal($Panel, $OwnerForm) {
    # Exercise the production popup directly. Deliberately throwing from a legacy
    # native callback can terminate Windows PowerShell before its catch block runs.
    $newsType = $assembly.GetType('AOLauncher.ServerNews',$true)
    $sources = $newsType.GetField('Sources',$staticFlags).GetValue($null)
    $safeUrl = $newsType.GetMethod('SafeUrl',$staticFlags)
    $savedUrls = @{}
    $priorSelection = $Panel.SelectedSourceIds
    $script:websiteItemClicks = 0
    try {
        foreach ($source in $sources) {
            $savedUrls[$source.Id] = $source.SiteUrl
            # Only this synthetic process's objects change. Reject this inert URI
            # before dispatching any click, so OpenUrl cannot start a browser.
            $source.GetType().GetProperty('SiteUrl',$flags).GetSetMethod($true).Invoke($source,[object[]]@('about:blank')) | Out-Null
            Assert-Check ($null -eq $safeUrl.Invoke($null,[object[]]@($source,$source.SiteUrl))) 'The production URL validator rejects the fixture destination before clicking.'
        }
        $Panel.SetServerFilters([string[]]@('live','prk','rebirth'))
        foreach ($source in $sources) {
            Invoke-Private $Panel 'OpenSource' | Out-Null
            $menu = @((Field $Panel 'ownedMenus') | Where-Object { $_.Visible })[0]
            try {
                $choice = @($menu.Items | Where-Object { $_.Tag -eq $source.Id })[0]
                Assert-Check ($null -ne $choice) 'The actual website popup exposes the expected selected publisher.'
                $choice.add_Click([EventHandler]{ $script:websiteItemClicks++ })
                $before = $script:websiteItemClicks
                Native-ItemClick $menu $choice
                Assert-Check ($script:websiteItemClicks -eq $before + 1) 'Native item click invokes the actual website action exactly once.'
                Assert-Check (!$menu.Visible) 'Clicking a website dismisses its popup without a disposed-control exception.'
                [Windows.Forms.Application]::DoEvents()
                Assert-Check ($menu.IsDisposed) 'The clicked popup is disposed only after its native dismissal finishes.'
            } finally { if ($menu -and !$menu.IsDisposed) { $menu.Close(); $menu.Dispose() } }
        }
        Assert-Check ($script:websiteItemClicks -eq 3) 'Each public server website choice can be clicked and dismissed safely.'
    } finally {
        foreach ($source in $sources) {
            if ($savedUrls.ContainsKey($source.Id)) { $source.GetType().GetProperty('SiteUrl',$flags).GetSetMethod($true).Invoke($source,[object[]]@($savedUrls[$source.Id])) | Out-Null }
        }
        $Panel.SetServerFilters([string[]]$priorSelection)
    }
}

Set-Language 'en'
$panel = [Activator]::CreateInstance($panelType,[object[]]@($true))
$form = [Windows.Forms.Form]::new()
$form.ClientSize = [Drawing.Size]::new(360,640)
$panel.Dock = [Windows.Forms.DockStyle]::Fill
$form.Controls.Add($panel)
try {
    $panel.add_SelectionPreferencesChanged([EventHandler]{ $script:selectionEvents++ })
    Show-Offscreen $form
    $cards = Field $panel 'cards'
    Assert-Check (Field $panel 'offlinePreview') 'News fixture avoids personal cache and network.'
    Assert-Check (!(Field $panel 'automaticRefresh').Enabled) 'Offline news never starts the network refresh timer.'
    Assert-Check ((Field $panel 'filter') -is [Windows.Forms.Button]) 'The source picker offers multiple choices.'
    Assert-Check ((Selected $panel) -eq 'live|prk|rebirth') 'Fresh news defaults to all public publishers.'
    Assert-Check ($null -eq $panelType.GetField('rememberSelection',$flags)) 'Automatic persistence does not require a Remember checkbox.'
    Assert-Check ((Articles $panel) -eq 'article:live|article:prk|article:rebirth') 'Default preview shows one article from each publisher.'
    Assert-Check ((Field $panel 'status').Text -like 'Preview*') 'Synthetic headlines are explicitly labelled as previews.'
    $panel.ApplySelectionPreferences([string[]]@('rebirth','prk'))
    Assert-Check ($script:selectionEvents -eq 0) 'Initializing saved preferences is silent.'
    Assert-Check ((Articles $panel) -eq 'article:prk|article:rebirth') 'Two selected publishers produce their union without duplication.'
    Check-WebsiteMenu $panel 'AO Rebirth|Project Rubi-Ka'
    $panel.SetServerFilters([string[]]@('rebirth','prk','prk'))
    Assert-Check ($script:selectionEvents -eq 0 -and $cards.Controls.Count -eq 2) 'Reordering or repeating the same source IDs does not change the selection.'
    $returned = $panel.SelectedSourceIds; $returned[0] = 'custom'
    Assert-Check ((Selected $panel) -eq 'prk|rebirth') 'Mutating a returned selection array cannot change the panel.'
    $panel.SetServerFilters([string[]]@('live'))
    Assert-Check ($script:selectionEvents -eq 1 -and (Articles $panel) -eq 'article:live') 'A changed selection notifies once and updates the feed.'
    Check-WebsiteMenu $panel 'Funcom AO'
    $panel.SetServerFilter('live-2019')
    Assert-Check ((Articles $panel) -eq 'article:live' -and $script:selectionEvents -eq 1) 'Both official dimensions retain their shared feed alias.'
    $panel.SetServerFilters([string[]]@())
    Assert-Check ((Selected $panel) -eq '' -and (Articles $panel) -eq '') 'Clearing every source displays no unrelated articles.'
    Assert-Check ($cards.Controls.Count -eq 1 -and @($cards.Controls[0].Controls | Where-Object { $_.Text -match '(?i)select|choose' }).Count -gt 0) 'An empty selection explains how to choose sources.'
    Assert-Check (!(Field $panel 'sourceLink').Visible) 'No selection exposes no unrelated website link.'
    Check-WebsiteMenu $panel ''
    $panel.SetServerFilter('rebirth-local')
    Assert-Check ((Selected $panel) -eq 'custom' -and (Articles $panel) -eq '') 'The local development alias uses the custom-server explanation.'
    Assert-Check (!(Field $panel 'sourceLink').Visible) 'Custom servers do not expose a public server website.'
    $panel.SetServerFilters([string[]]@('rebirth','custom'))
    Assert-Check ((Articles $panel) -eq 'article:rebirth' -and $cards.Controls.Count -eq 2) 'A custom-server explanation can coexist with a selected public feed.'
    Check-WebsiteMenu $panel 'AO Rebirth'
    $panel.ApplySelectionPreferences([string[]]@(' LIVE-2019 ',$null,'live','not-a-source'))
    Assert-Check ((Selected $panel) -eq 'live') 'Saved source IDs are trimmed, case-normalized, aliased and deduplicated; unknown IDs are ignored.'
    $panel.ApplySelectionPreferences([string[]]@())
    Assert-Check ((Selected $panel) -eq '') 'An intentionally empty saved selection stays empty.'
    $panel.ApplySelectionPreferences($null)
    Assert-Check ((Selected $panel) -eq 'live|prk|rebirth') 'Missing saved source IDs retain the all-public default.'
    $eventCount = $script:selectionEvents
    $panel.ApplySelectionPreferences([string[]]@('all'))
    Assert-Check ((Selected $panel) -eq 'live|prk|rebirth' -and $script:selectionEvents -eq $eventCount) 'Initializing the all-publishers alias is silent.'
    (Field $panel 'filter').PerformClick()
    $selectionMenu = Field $panel 'selectionMenu'
    try {
        $prkChoice = @($selectionMenu.Items | Where-Object Text -eq 'Project Rubi-Ka')[0]
        Assert-Check ($null -ne $prkChoice -and $prkChoice.Checked) 'The picker visibly marks the selected publisher.'
        $prkChoice.PerformClick()
        Assert-Check ((Selected $panel) -eq 'live|rebirth' -and !$prkChoice.Checked) 'Clicking a checked publisher removes only that feed.'
        $prkChoice.PerformClick()
        Assert-Check ((Selected $panel) -eq 'live|prk|rebirth' -and $prkChoice.Checked) 'Clicking it again restores that feed alongside the others.'
        @($selectionMenu.Items | Where-Object Name -eq 'clearNewsSources')[0].PerformClick()
        Assert-Check ((Selected $panel) -eq '' -and !$prkChoice.Checked) 'The Clear menu action deselects every feed and updates its checkmarks.'
        @($selectionMenu.Items | Where-Object Name -eq 'selectAllNewsSources')[0].PerformClick()
        Assert-Check ((Selected $panel) -eq 'live|prk|rebirth' -and $prkChoice.Checked) 'The All menu action selects all public feeds.'
        Assert-Check ($selectionMenu.Visible) 'Changing several choices leaves the source picker open.'
        Assert-Check ($selectionMenu.Renderer.GetType().Name -eq 'SourceMenuRenderer' -and !$selectionMenu.Renderer.ColorTable.UseSystemColors) 'The checkable picker uses dark application colors.'
    } finally { if (!$selectionMenu.IsDisposed) { $selectionMenu.Close(); $selectionMenu.Dispose() } }
    Assert-Check ($null -eq (Field $panel 'selectionMenu')) 'Closing the picker clears its active reference without an exception.'
    [Windows.Forms.Application]::DoEvents()
    $picker = Field $panel 'filter'
    $down = [Windows.Forms.KeyEventArgs]::new([Windows.Forms.Keys]::Down)
    $picker.GetType().GetMethod('OnKeyDown',$flags).Invoke($picker,[object[]]@($down)) | Out-Null
    $keyboardMenu = Field $panel 'selectionMenu'
    try {
        Assert-Check ($null -ne $keyboardMenu -and $keyboardMenu.Visible -and $down.Handled) 'Down opens the news picker from the keyboard.'
        $choice = @($keyboardMenu.Items | Where-Object Name -eq 'newsSource-prk')[0]
        $choice.Select()
        $space = [Windows.Forms.KeyEventArgs]::new([Windows.Forms.Keys]::Space)
        $keyboardMenu.GetType().GetMethod('OnKeyDown',$flags).Invoke($keyboardMenu,[object[]]@($space)) | Out-Null
        Assert-Check ((Selected $panel) -eq 'live|rebirth' -and $space.Handled -and $keyboardMenu.Visible) 'Space toggles the highlighted publisher without closing the picker.'
        $keyboardMenu.GetType().GetMethod('ProcessDialogKey',$flags).Invoke($keyboardMenu,[object[]]@([Windows.Forms.Keys]::Escape)) | Out-Null
        Assert-Check ($null -eq (Field $panel 'selectionMenu') -and !$keyboardMenu.Visible) 'Escape safely dismisses the picker.'
        [Windows.Forms.Application]::DoEvents()
        Assert-Check ($keyboardMenu.IsDisposed) 'Dismissed picker resources are released after native close completes.'
    } finally { if ($keyboardMenu -and !$keyboardMenu.IsDisposed) { $keyboardMenu.Close(); $keyboardMenu.Dispose() } }
    $panel.SetServerFilters([string[]]@('prk','rebirth'))
    # Two selected sources open a menu, never a browser. Exercise its real close path too.
    Invoke-Private $panel 'OpenSource' | Out-Null
    $websiteMenu = @((Field $panel 'ownedMenus') | Where-Object { $_.Visible })[0]
    Assert-Check ($null -ne $websiteMenu -and $websiteMenu.Items.Count -eq 2) 'Multiple selected websites open only their two-link popup.'
    $websiteMenu.Close(); [Windows.Forms.Application]::DoEvents()
    Assert-Check ($websiteMenu.IsDisposed) 'The website popup safely releases resources after closing.'
    Check-WebsiteItemDismissal $panel $form
    $beforeLanguageEvents = $script:selectionEvents
    Set-Language 'da'; $panel.RefreshLanguage()
    Assert-Check ((Field $panel 'heading').Text -eq 'TRANSMISSIONER' -and (Field $panel 'refresh').Text -eq 'Opdater') 'News headings and actions translate to Danish.'
    Assert-Check ((Selected $panel) -eq 'prk|rebirth' -and $script:selectionEvents -eq $beforeLanguageEvents) 'Language changes preserve selection without preference events.'
    $panel.RefreshNewsAsync().GetAwaiter().GetResult() | Out-Null
    Assert-Check ((Selected $panel) -eq 'prk|rebirth' -and $script:selectionEvents -eq $beforeLanguageEvents -and (Field $panel 'status').Text -like 'Forhåndsvisning*') 'Manual refresh preserves selection and remains offline.'
    $panel.SetServerFilter('all'); Set-Language 'en'; $panel.RefreshLanguage()
    foreach ($snapshot in (Field $panel 'snapshots')) { $snapshot.IsCached = $true; $snapshot.Unavailable = $true }
    Invoke-Private $panel 'Render' | Out-Null
    $liveCard = @($cards.Controls | Where-Object { $_.Tag -eq 'article:live' })[0]
    Assert-Check (@($liveCard.Controls | Where-Object { $_.Text -like '*Latest saved article*' }).Count -eq 1) 'Cached news does not claim that no newer article exists.'
    Check-CardWidths $panel
    $form.ClientSize = [Drawing.Size]::new(300,500); Layout $form; Check-CardWidths $panel
} finally { $form.Dispose() }

# Only synthetic settings are serialized in memory. MainForm(true) prevents profile writes.
$main = [Activator]::CreateInstance($mainType,[object[]]@($true))
try {
    $config = Field $main 'settings'; $news = Field $main 'newsPanel'
    Assert-Check ((Field $main 'smoke') -and (Field $news 'offlinePreview')) 'Preference integration uses fictional launcher data and offline news.'
    $accountsBefore = $serializer.Serialize($config.Accounts); $presetsBefore = $serializer.Serialize($config.Presets)
    $news.SetServerFilters([string[]]@('live','prk'))
    Assert-Check ((@($config.NewsSourceIds | Sort-Object) -join '|') -eq 'live|prk') 'Changing the selected feeds automatically updates launcher settings.'
    $news.SetServerFilters([string[]]@('prk','rebirth'))
    Assert-Check ((@($config.NewsSourceIds | Sort-Object) -join '|') -eq 'prk|rebirth') 'Remembered selections update when feeds change.'
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $fixtureDirectory = [IO.Path]::GetFullPath((Join-Path $tempParent ('AO-Nexus-NewsRegression-' + [Guid]::NewGuid().ToString('N'))))
    try {
        $storeType.GetMethod('SaveToDirectory',$staticFlags).Invoke($null,[object[]]@($config,$fixtureDirectory)) | Out-Null
        Assert-Check ([IO.File]::Exists((Join-Path $fixtureDirectory 'AONexus.json'))) 'Synthetic news preferences are saved to their dedicated temporary directory.'
        $diskSettings = $storeType.GetMethod('ReadFromDirectory',$staticFlags).Invoke($null,[object[]]@($fixtureDirectory))
        $diskPanel = [Activator]::CreateInstance($panelType,[object[]]@($true))
        try {
            $diskPanel.ApplySelectionPreferences([string[]]$diskSettings.NewsSourceIds)
            Assert-Check ((Selected $diskPanel) -eq 'prk|rebirth') 'A disk-backed restart restores the saved subset.'
            Assert-Check ($serializer.Serialize($diskSettings.Accounts) -ceq $accountsBefore -and $serializer.Serialize($diskSettings.Presets) -ceq $presetsBefore) 'Saving and reloading news preferences preserves synthetic accounts and presets.'
        } finally { $diskPanel.Dispose() }
    } finally {
        $resolvedFixture = [IO.Path]::GetFullPath($fixtureDirectory)
        if ([IO.Path]::GetDirectoryName($resolvedFixture) -ne $tempParent -or [IO.Path]::GetFileName($resolvedFixture) -notmatch '^AO-Nexus-NewsRegression-[0-9a-f]{32}$') { throw 'Refusing to remove an unexpected test directory.' }
        if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
    }
    $saved = Deserialize ($serializer.Serialize($config))
    $restarted = [Activator]::CreateInstance($panelType,[object[]]@($true))
    try {
        $restarted.ApplySelectionPreferences([string[]]$saved.NewsSourceIds)
        Assert-Check ((Selected $restarted) -eq 'prk|rebirth') 'A simulated restart restores the saved subset.'
    } finally { $restarted.Dispose() }
    $news.SetServerFilters([string[]]@())
    Assert-Check ($config.NewsSourceIds.Count -eq 0 -and (Selected $news) -eq '') 'An empty selection is automatically saved as empty.'
    $saved = Deserialize ($serializer.Serialize($config))
    $restarted = [Activator]::CreateInstance($panelType,[object[]]@($true))
    try {
        $restarted.ApplySelectionPreferences([string[]]$saved.NewsSourceIds)
        Assert-Check ((Selected $restarted) -eq '') 'An intentionally empty selection survives a restart.'
        $oldSettings = Deserialize '{}'
        Assert-Check (($oldSettings.NewsSourceIds -join '|') -eq 'live|prk|rebirth') 'Existing settings without news preferences default to all public feeds.'
        $oddSettings = Deserialize '{"NewsSourceIds":[" REBIRTH ","live-2019","LIVE",null,"unknown","custom"]}'
        Assert-Check (($oddSettings.NewsSourceIds -join '|') -eq 'live|rebirth|custom') 'Settings normalization retains only valid canonical unique IDs.'
        $noneSettings = Deserialize '{"NewsSourceIds":[]}'
        Assert-Check ($noneSettings.NewsSourceIds.Count -eq 0) 'A saved empty selection stays empty through deserialization.'
        $allSettings = Deserialize '{"NewsSourceIds":["all","prk"]}'
        Assert-Check (($allSettings.NewsSourceIds -join '|') -eq 'live|prk|rebirth') 'The all alias expands to the three public publishers.'
    } finally { $restarted.Dispose() }
    Assert-Check ($serializer.Serialize($config.Accounts) -ceq $accountsBefore -and $serializer.Serialize($config.Presets) -ceq $presetsBefore) 'News preferences leave accounts, characters and presets unchanged.'
    Assert-Check (!(Field $main 'running') -and $null -eq (Field $main 'backendRunner')) 'News integration never starts a backend.'
} finally { $main.Dispose() }

foreach ($language in @('en','da')) {
    foreach ($scale in @(1,1.5)) {
        $main = [Activator]::CreateInstance($mainType,[object[]]@($true))
        $selectionMenu = $null
        try {
            (Field $main 'settings').Language = $language; Invoke-Private $main 'ApplyLanguage' | Out-Null
            $main.Size = $main.MinimumSize
            if ($scale -gt 1) { $main.Font = [Drawing.Font]::new($main.Font.FontFamily,$main.Font.Size * $scale,$main.Font.Style); $main.Scale([Drawing.SizeF]::new($scale,$scale)) }
            $news = Field $main 'newsPanel'; $news.SetServerFilters([string[]]@('prk','rebirth'))
            Show-Offscreen $main
            $picker = Field $news 'filter'; $refresh = Field $news 'refresh'
            Assert-Check ($picker.Visible -and $refresh.Visible) 'Picker and refresh remain visible at minimum size and 150% scale.'
            Assert-Check ($picker.Parent.ClientRectangle.Contains($picker.Bounds) -and $refresh.Parent.ClientRectangle.Contains($refresh.Bounds)) 'Picker and refresh fit their toolbar.'
            Check-CardWidths $news
            foreach ($card in (Field $news 'cards').Controls) {
                if ($card.Tag -notlike 'article:*') { continue }
                $source = $card.Controls['articleSource']; $date = $card.Controls['articleDate']
                $title = $card.Controls['articleTitle']; $excerpt = $card.Controls['articleExcerpt']
                Assert-Check ($source.Top -eq $date.Top -and $source.Right -lt $date.Left) 'News source and date share a row without overlap.'
                Assert-Check ($date.Right -eq $title.Right -and $title.Right -eq $excerpt.Right) 'Date is right aligned with the text inset.'
                Assert-Check ($excerpt.Top -gt $title.Bottom -and $excerpt.Top - $title.Bottom -le 12) 'Summary follows the wrapped title with a small gap.'
            }
            Preview $main ('news-multiselect-' + $language + '-' + $scale + '.png')
            $selectionMenu = Invoke-Private $news 'CreateSelectionMenu'
            $selectionMenu.Show([Drawing.Point]::new(-32000,-32000)); [Windows.Forms.Application]::DoEvents()
            Preview $selectionMenu ('news-picker-' + $language + '-' + $scale + '.png')
            Assert-Check ((Selected $news) -eq 'prk|rebirth') 'Opening the picker does not change the feeds.'
        } finally {
            if ($selectionMenu) { $selectionMenu.Close(); $selectionMenu.Dispose() }
            $main.Dispose()
        }
    }
}
"SUCCESS $script:checks news selection, persistence, layout and offline checks passed. No real profiles, passwords, websites, network, backend or games were accessed."
