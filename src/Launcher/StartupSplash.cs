using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace AOLauncher
{
    // Small loading screen shown while the main window is built. It runs on its own UI
    // thread, so it keeps animating while the main thread loads settings and lays out the
    // window. The main window stays invisible (Opacity 0) until its first full layout and
    // paint are done, then appears in one step and this screen closes.
    internal sealed class StartupSplash : Form
    {
        static StartupSplash instance;
        static readonly object gate = new object();

        readonly System.Windows.Forms.Timer animation = new System.Windows.Forms.Timer { Interval = 30 };
        readonly Image mark;
        string status;
        float phase;

        internal static void Begin()
        {
            using (var ready = new ManualResetEventSlim(false))
            {
                var thread = new Thread(() =>
                {
                    StartupSplash splash = null;
                    try
                    {
                        splash = new StartupSplash();
                        splash.Shown += (sender, e) => ready.Set();
                        lock (gate) instance = splash;
                        Application.Run(splash);
                    }
                    catch { }
                    finally { lock (gate) if (instance == splash) instance = null; try { ready.Set(); } catch (ObjectDisposedException) { } }
                }) { IsBackground = true, Name = "AO Nexus startup screen" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                ready.Wait(1500); // never hold up startup for the loading screen itself
            }
        }

        internal static void Status(string text)
        {
            StartupSplash splash; lock (gate) splash = instance;
            if (splash == null) return;
            try { splash.BeginInvoke((Action)(() => { splash.status = text; splash.Invalidate(); })); } catch { }
        }

        internal static void End()
        {
            StartupSplash splash; lock (gate) { splash = instance; instance = null; }
            if (splash == null) return;
            try { splash.BeginInvoke((Action)splash.Close); } catch { }
        }

        StartupSplash()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            Text = "AO Nexus";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(440, 210);
            BackColor = Theme.Background;
            DoubleBuffered = true;
            try { AppArtwork.ApplyIcon(this); } catch { }
            try { var header = AppArtwork.CreateHeaderMark(); mark = (Image)header.Image.Clone(); header.Dispose(); } catch { }
            status = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "da" ? "Starter AO Nexus…" : "Starting AO Nexus…";
            animation.Tick += (sender, e) => { phase = (phase + 0.012f) % 1.4f; Invalidate(); };
            Shown += (sender, e) => animation.Start();
            FormClosed += (sender, e) => { animation.Stop(); animation.Dispose(); mark?.Dispose(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float scale = DeviceDpi / 96f;
            int pad = (int)(28 * scale);
            using (var border = new Pen(Theme.Border)) g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            int logo = (int)(64 * scale);
            if (mark != null) g.DrawImage(mark, new Rectangle(pad, pad, logo, logo));
            int textLeft = pad + (mark != null ? logo + (int)(18 * scale) : 0);
            using (var title = new Font("Segoe UI Semibold", 20F))
            using (var small = new Font("Segoe UI", 10F))
            using (var titleBrush = new SolidBrush(Theme.Navy))
            using (var mutedBrush = new SolidBrush(Theme.Muted))
            {
                g.DrawString("AO Nexus", title, titleBrush, textLeft, pad + 2 * scale);
                g.DrawString("Version " + AppUpdates.DisplayVersion, small, mutedBrush, textLeft + 2 * scale, pad + 40 * scale);
                g.DrawString(status ?? "", small, mutedBrush, pad, ClientSize.Height - pad - 40 * scale);
            }
            // Indeterminate progress: a soft bar sweeping across a track.
            var track = new RectangleF(pad, ClientSize.Height - pad - 10 * scale, ClientSize.Width - 2 * pad, 4 * scale);
            using (var trackBrush = new SolidBrush(Theme.Border)) g.FillRectangle(trackBrush, track);
            float width = track.Width * 0.3f, x = track.Left - width + (track.Width + width) * (phase / 1.4f);
            var bar = RectangleF.Intersect(new RectangleF(x, track.Top, width, track.Height), track);
            if (bar.Width > 0) using (var barBrush = new SolidBrush(Theme.Accent)) g.FillRectangle(barBrush, bar);
        }
    }
}
