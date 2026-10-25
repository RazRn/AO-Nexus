# Coordinates are client-area pixels at the fixed fixture sizes. Capture fails
# when a source size changes, so callouts cannot silently drift after a redesign.
$GuideImagePlan = [ordered]@{
    'launcher-overview.png' = @{ Size=@(1280,1000); Boxes=@() }
    'account-setup.png' = @{ Size=@(640,740); Boxes=@(@(35,65,570,130),@(35,204,570,150),@(35,380,570,117)) }
    'game-and-server.png' = @{ Size=@(640,740); Boxes=@(@(35,65,570,58),@(35,129,570,110)) }
    'preset-shortcuts.png' = @{ Size=@(600,540); Boxes=@(@(25,99,550,34),@(25,173,550,132),@(473,479,98,39)) }
    'launch-progress.png' = @{ Size=@(1280,1000); Crop=@(0,0,1280,134); Boxes=,@(2,117,1275,13); NoNumbers=$true }
    'server-health.png' = @{ Size=@(1280,1000); Crop=@(900,145,355,550); Boxes=@(@(918,270,314,299),@(918,576,314,109)) }
    'activity-log.png' = @{ Size=@(900,340); Boxes=@(@(18,12,744,43),@(778,284,107,41)) }
    'update-available.png' = @{ Size=@(620,600); Boxes=@(@(29,146,562,274),@(438,539,152,39)) }
    'gui-layouts.png' = @{ Size=@(1040,700); Boxes=@(@(38,157,407,51),@(463,120,538,336),@(461,529,116,40)) }
    'gui-skins.png' = @{ Size=@(1040,700); Boxes=@(@(38,127,964,39),@(38,164,612,38),@(35,529,268,41)) }
    'gui-backups.png' = @{ Size=@(1040,700); Boxes=@(@(38,126,963,28),@(35,529,150,41)) }
}
