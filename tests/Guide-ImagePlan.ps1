# Coordinates are client-area pixels at the fixed fixture sizes. Capture fails
# when a source size changes, so callouts cannot silently drift after a redesign.
$GuideImagePlan = [ordered]@{
    'launcher-overview.png' = @{ Size=@(1280,1000); Boxes=@() }
    'character-search.png' = @{ Size=@(1280,1000); Crop=@(24,230,600,480); Boxes=@() }
    'login-confirmation.png' = @{ Size=@(620,340); Boxes=@() }
    'character-status.png' = @{ Size=@(720,640); Boxes=@() }
    'setup-preferences.png' = @{ Size=@(680,994); Boxes=@(@(25,61,627,64),@(25,132,627,64),@(25,203,627,112)) }
    'account-setup.png' = @{ Size=@(640,740); Boxes=@(@(35,65,570,130),@(35,204,570,150),@(35,380,570,117)) }
    'game-and-server.png' = @{ Size=@(640,740); Boxes=@(@(35,65,570,58),@(35,129,570,110)) }
    'preset-shortcuts.png' = @{ Size=@(600,540); Boxes=@(@(25,99,550,34),@(25,173,550,132),@(473,479,98,39)) }
    'launch-progress.png' = @{ Size=@(1280,1000); Crop=@(0,0,1280,134); Boxes=,@(2,117,1275,13); NoNumbers=$true }
    'server-health.png' = @{ Size=@(1280,1000); Crop=@(900,145,355,550); Boxes=@(@(918,270,314,299),@(918,576,314,109)) }
    'activity-log.png' = @{ Size=@(900,340); Boxes=@(@(18,12,744,43),@(611,284,109,41)) }
    'update-available.png' = @{ Size=@(620,600); Boxes=@(@(29,146,562,274),@(438,539,152,39)) }
    # Full client areas from Verify-GameGuiUsability: synthetic data, no crops or annotations.
    'gui-layouts-1.2.png' = @{ Size=@(1120,740); Boxes=@(); Versioned=$true; Source='en-1-default-tab0.png' }
    'gui-skins-1.2.png' = @{ Size=@(1120,740); Boxes=@(); Versioned=$true; Source='en-1-default-tab1.png' }
    'gui-scripts-1.2.png' = @{ Size=@(1120,740); Boxes=@(); Versioned=$true; Source='en-1-default-tab2.png' }
    'gui-macros-1.2.png' = @{ Size=@(1120,740); Boxes=@(); Versioned=$true; Source='en-1-default-macros.png' }
    'gui-backups-1.2.png' = @{ Size=@(1120,740); Boxes=@(); Versioned=$true; Source='en-1-default-tab3.png' }
}
