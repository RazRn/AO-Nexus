# Full client-area captures at fixed fixture sizes. Capture fails if the
# layout size changes, so the guide cannot silently publish an outdated crop.
$GuideImagePlan = [ordered]@{
    'launcher-overview.png' = @{ Size=@(1280,1000); Boxes=@() }
    'character-search.png' = @{ Size=@(1280,1000); Boxes=@() }
    'login-confirmation.png' = @{ Size=@(620,340); Boxes=@() }
    'character-status.png' = @{ Size=@(720,740); Boxes=@() }
    'setup-preferences.png' = @{ Size=@(680,994); Boxes=@() }
    'account-setup.png' = @{ Size=@(640,740); Boxes=@() }
    'game-and-server.png' = @{ Size=@(640,740); Boxes=@() }
    'preset-shortcuts.png' = @{ Size=@(600,540); Boxes=@() }
    'launch-progress.png' = @{ Size=@(1280,1000); Boxes=@() }
    'server-health.png' = @{ Size=@(1280,1000); Boxes=@() }
    'activity-log.png' = @{ Size=@(900,340); Boxes=@() }
    'update-available.png' = @{ Size=@(620,600); Boxes=@() }
    'settings.png' = @{ Size=@(820,760); Boxes=@() }
    'settings-accounts.png' = @{ Size=@(820,760); Boxes=@() }
    'settings-sidebar.png' = @{ Size=@(820,760); Boxes=@() }
    'settings-updates.png' = @{ Size=@(820,760); Boxes=@() }
    'settings-graphics.png' = @{ Size=@(820,760); Boxes=@() }
    'settings-advanced.png' = @{ Size=@(820,760); Boxes=@() }
    'settings-diagnostics.png' = @{ Size=@(820,760); Boxes=@() }
    'plugins.png' = @{ Size=@(1280,740); Boxes=@() }
    'control-panel.png' = @{ Size=@(1280,850); Boxes=@() }
    'damage-dumper.png' = @{ Size=@(1280,850); Boxes=@() }
    'timers.png' = @{ Size=@(880,650); Boxes=@() }
    # Full client areas from Verify-GameGuiUsability: synthetic data, no crops or annotations.
    'gui-layouts-1.3.png' = @{ Size=@(1120,740); Boxes=@(); Versioned=$true; Source='en-1-default-tab0.png' }
    'gui-skins-1.3.png' = @{ Size=@(1120,740); Boxes=@(); Versioned=$true; Source='en-1-default-tab1.png' }
    'gui-scripts-1.3.png' = @{ Size=@(1120,740); Boxes=@(); Versioned=$true; Source='en-1-default-tab2.png' }
    'gui-macros-1.3.png' = @{ Size=@(1120,740); Boxes=@(); Versioned=$true; Source='en-1-default-macros.png' }
    'gui-backups-1.3.png' = @{ Size=@(1120,740); Boxes=@(); Versioned=$true; Source='en-1-default-tab3.png' }
}
