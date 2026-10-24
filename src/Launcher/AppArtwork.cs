using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AOLauncher
{
    internal static class AppArtwork
    {
        internal static void ApplyIcon(Form form)
        {
            Icon icon;
            using (var stream = Open("Launcher.ico"))
            using (var source = new Icon(stream))
                icon = (Icon)source.Clone();
            form.Icon = icon;
            // Each window owns a detached copy, so closing a dialog cannot invalidate another window's icon.
            form.Disposed += (sender, e) => icon.Dispose();
        }

        internal static PictureBox CreateHeaderMark()
        {
            Bitmap bitmap;
            using (var stream = Open("Launcher.png"))
            using (var source = Image.FromStream(stream))
                bitmap = new Bitmap(source);
            var mark = new PictureBox
            {
                Image = bitmap,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                TabStop = false,
                AccessibleRole = AccessibleRole.Graphic,
                AccessibleName = "AO Nexus"
            };
            mark.Disposed += (sender, e) => bitmap.Dispose();
            return mark;
        }

        internal static Image CreateBackdrop()
        {
            using (var stream = typeof(AppArtwork).Assembly.GetManifestResourceStream("AOLauncher.Assets.NexusBackdrop.png"))
            {
                if (stream == null) return null;
                using (var source = Image.FromStream(stream)) return new Bitmap(source);
            }
        }

        private static Stream Open(string filename)
        {
            return typeof(AppArtwork).Assembly.GetManifestResourceStream("AOLauncher.Assets." + filename)
                ?? throw new InvalidOperationException("The embedded launcher artwork is missing.");
        }
    }
}
