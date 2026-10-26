param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
$form=[Activator]::CreateInstance($a.GetType('AOLauncher.MainForm'),[object[]]@($true))
$script:parentInvalidations=0;$script:stripInvalidations=0;$checks=0
function Assert($ok,$text){if(!$ok){throw $text};$script:checks++}
try {
 $form.ShowInTaskbar=$false;$form.StartPosition='Manual';$form.Location=[Drawing.Point]::new(-18000,-18000)
 $form.Show();[Windows.Forms.Application]::DoEvents()
 $hero=$form.GetType().GetField('header',$flags).GetValue($form)
 $strip=$hero.Controls.Find('headerActivityStrip',$false)[0]
 $timer=$hero.GetType().GetField('activityTimer',$flags).GetValue($hero)
 $paint=$hero.GetType().GetMethod('PaintActivity',$flags)
 $accent=$a.GetType('AOLauncher.Theme').GetField('Accent',$flags).GetValue($null).ToArgb()
 $amber=$a.GetType('AOLauncher.Theme').GetField('Amber',$flags).GetValue($null).ToArgb()
 $hero.add_Invalidated({$script:parentInvalidations++})
 $strip.add_Invalidated({$script:stripInvalidations++})
 foreach($width in @(740,1280,1920)) {
  $form.ClientSize=[Drawing.Size]::new($width,900);$form.PerformLayout();[Windows.Forms.Application]::DoEvents()
  Assert ($strip.Left -eq 0 -and $strip.Width -eq $hero.ClientSize.Width -and $strip.Bottom -eq $hero.ClientSize.Height) 'Strip must span the complete header at every window size.'
  Assert ($hero.Controls.GetChildIndex($strip) -eq 0) 'Other docked header controls must not cover the strip.'
  $bitmap=[Drawing.Bitmap]::new($strip.Width,$strip.Height);$graphics=[Drawing.Graphics]::FromImage($bitmap)
  try {
   $hero.GetType().GetMethod('EndActivity',$flags).Invoke($hero,@())|Out-Null
   $paint.Invoke($hero,[object[]]@($graphics,[long]0))|Out-Null
   Assert ($bitmap.GetPixel(0,$bitmap.Height-1).ToArgb() -eq $accent) 'Idle blue line must start at the first client-area pixel.'
   $hero.GetType().GetMethod('BeginActivity',$flags).Invoke($hero,@([int]6))|Out-Null
   $timer.Stop()
   foreach($completed in @(0,1,5,6)) {
    $hero.GetType().GetMethod('ReportActivity',$flags).Invoke($hero,@([int]$completed))|Out-Null
    $counts=@()
    foreach($milliseconds in @(0,1,16,1000,2500,4500,4900,4984,4999,5000,5001,5016,10000)) {
     $paint.Invoke($hero,[object[]]@($graphics,[long]$milliseconds))|Out-Null
     $amberPixels=0;$cyanPixels=0
     for($x=0;$x -lt $bitmap.Width;$x++) {
      $color=$bitmap.GetPixel($x,0).ToArgb()
      if($color -eq $amber){$amberPixels++};if($color -eq $accent){$cyanPixels++}
     }
     if($completed -lt 6){Assert ($amberPixels -gt 0) 'Running indicator must never disappear at a wrap boundary.'}
     else {Assert ($amberPixels -eq 0 -and $cyanPixels -eq $bitmap.Width) 'Completed batch must show a solid cyan strip.'}
     $counts+= $amberPixels
    }
    Assert (($counts | Select-Object -Unique).Count -eq 1) 'Amber area must remain constant across the animation loop.'
   }
  } finally {$graphics.Dispose();$bitmap.Dispose()}
 }
 $script:parentInvalidations=0;$script:stripInvalidations=0
 $timer.GetType().GetMethod('OnTick',$flags).Invoke($timer,@([EventArgs]::Empty))|Out-Null
 Assert ($script:parentInvalidations -eq 0 -and $script:stripInvalidations -gt 0) 'Animation ticks must invalidate only the strip, not the artwork or labels.'
 $hero.GetType().GetMethod('EndActivity',$flags).Invoke($hero,@())|Out-Null
 Assert (!$timer.Enabled) 'Ending a login batch must stop the timer.'
} finally {$form.Dispose()}
"PASS: $checks header edge, wrap, progress, resize and repaint-isolation checks; synthetic logins only."
