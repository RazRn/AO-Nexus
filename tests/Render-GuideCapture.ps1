# Capture the real off-screen WinForms controls. Guide callouts are rendered by
# the capture fixture, never inserted into the application or a user's profile.
. (Join-Path $PSScriptRoot 'Guide-ImagePlan.ps1')
Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class GuideCaptureDC {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd,IntPtr dc,uint flags);
    [DllImport("gdi32.dll")] public static extern bool SetViewportOrgEx(IntPtr dc,int x,int y,IntPtr previous);
}
'@
function Export-GuideCapture($Control,[string]$Name,[string]$Directory) {
    if (!$GuideImagePlan.Contains($Name)) { return }
    $plan=$GuideImagePlan[$Name]
    if ($Control.ClientSize.Width -ne $plan.Size[0] -or $Control.ClientSize.Height -ne $plan.Size[1]) {
        throw "Update image plan for $Name : expected $($plan.Size -join 'x'), got $($Control.ClientSize)"
    }
    $crop=if($plan.Crop){$plan.Crop}else{@(0,0,$plan.Size[0],$plan.Size[1])}
    $bitmap=[Drawing.Bitmap]::new($crop[2],$crop[3])
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try {
        $dc=$graphics.GetHdc()
        try {
            [GuideCaptureDC]::SetViewportOrgEx($dc,-$crop[0],-$crop[1],[IntPtr]::Zero)|Out-Null
            if(![GuideCaptureDC]::PrintWindow($Control.Handle,$dc,1)){throw "Capture failed: $Name"}
        } finally {$graphics.ReleaseHdc($dc)}
        $pen=[Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#ff4b4b'),2)
        $brush=[Drawing.SolidBrush]::new($pen.Color)
        $font=[Drawing.Font]::new('Segoe UI',10,[Drawing.FontStyle]::Bold)
        try {
            $number=0
            foreach($box in $plan.Boxes){
                $number++;$x=$box[0]-$crop[0];$y=$box[1]-$crop[1]
                if($x -lt 0 -or $y -lt 0 -or ($x+$box[2]) -ge $bitmap.Width -or ($y+$box[3]) -ge $bitmap.Height){throw "Callout outside $Name"}
                $graphics.DrawRectangle($pen,$x,$y,$box[2],$box[3])
                if(!$plan.NoNumbers){
                    # A compact numbered tab in the outer margin leaves labels readable.
                    $graphics.FillRectangle($brush,$x-18,$y,18,21)
                    $graphics.DrawString([string]$number,$font,[Drawing.Brushes]::Black,$x-16,$y+1)
                }
            }
            [IO.Directory]::CreateDirectory($Directory)|Out-Null
            $bitmap.Save((Join-Path $Directory $Name),[Drawing.Imaging.ImageFormat]::Png)
        } finally {$font.Dispose();$brush.Dispose();$pen.Dispose()}
    } finally {$graphics.Dispose();$bitmap.Dispose()}
}
