using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AOLauncher
{
    internal static class BackgroundUi
    {
        internal static bool DropdownOpen()
        {
            foreach (Form form in Application.OpenForms)
                if (HasDropdown(form)) return true;
            return false;
        }
        private static bool HasDropdown(Control parent)
        {
            if (parent is ComboBox combo && combo.DroppedDown) return true;
            foreach (Control child in parent.Controls) if (HasDropdown(child)) return true;
            return false;
        }
    }

    // Keep the native control implementations for editing, keyboard navigation and
    // accessibility. Only their chrome is drawn with the launcher's palette.
    internal sealed class NexusTabControl : TabControl
    {
        private bool sizingTabs;
        private bool themeInitialized;
        internal NexusTabControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            ForeColor = Theme.Foreground;
            Padding = new Point(14, 6);
            SizeMode = TabSizeMode.Fixed;
            themeInitialized = true;
            UpdateTabSize();
        }

        private void UpdateTabSize()
        {
            if (!themeInitialized || sizingTabs || IsDisposed || Disposing) return;
            sizingTabs = true;
            try
            {
                int width = NexusChrome.Scale(this, 72), height = Font.Height;
                var proposed = new Size(int.MaxValue, int.MaxValue);
                using (var graphics = IsHandleCreated ? CreateGraphics() : null)
                    foreach (TabPage page in TabPages)
                    {
                        Size measured = graphics == null
                            ? TextRenderer.MeasureText(page.Text, Font, proposed, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix)
                            : TextRenderer.MeasureText(graphics, page.Text, Font, proposed, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                        width = Math.Max(width, measured.Width);
                        height = Math.Max(height, measured.Height);
                    }
                var size = new Size(width + NexusChrome.Scale(this, 28), height + NexusChrome.Scale(this, 12));
                if (ItemSize != size) ItemSize = size;
            }
            finally { sizingTabs = false; }
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateTabSize(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateTabSize(); Invalidate(); }
        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control is TabPage page) page.TextChanged += TabTextChanged;
            UpdateTabSize();
        }
        protected override void OnControlRemoved(ControlEventArgs e)
        {
            if (e.Control is TabPage page) page.TextChanged -= TabTextChanged;
            base.OnControlRemoved(e);
            UpdateTabSize();
        }
        private void TabTextChanged(object sender, EventArgs e) { UpdateTabSize(); Invalidate(); }

        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Theme.Background); }
        protected override void OnPaint(PaintEventArgs e)
        {
            // TabControl exposes a legacy BackColor getter that can still return
            // SystemColors.Control. Paint the full strip and frame explicitly.
            e.Graphics.Clear(Theme.Background);
            Rectangle page = DisplayRectangle;
            page.Inflate(2, 2);
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, page.X, page.Y, Math.Max(0, page.Width - 1), Math.Max(0, page.Height - 1));
            for (int index = 0; index < TabCount; index++)
            {
                Rectangle tab = GetTabRect(index);
                bool selected = index == SelectedIndex;
                using (var brush = new SolidBrush(selected ? Theme.SurfaceRaised : Theme.Background)) e.Graphics.FillRectangle(brush, tab);
                var text = Rectangle.Inflate(tab, -NexusChrome.Scale(this, 6), 0);
                TextRenderer.DrawText(e.Graphics, TabPages[index].Text, Font, text,
                    !Enabled ? Theme.Muted : selected ? Theme.Accent : Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (selected)
                {
                    int inset = NexusChrome.Scale(this, 4);
                    using (var pen = new Pen(Enabled ? Theme.Accent : Theme.Border, NexusChrome.Scale(this, 2)))
                        e.Graphics.DrawLine(pen, tab.Left + inset, tab.Bottom - 2, tab.Right - inset, tab.Bottom - 2);
                    if (Focused && ShowFocusCues)
                        ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(tab, -4, -4), Theme.Accent, Theme.SurfaceRaised);
                }
            }
            base.OnPaint(e);
        }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    }

    internal sealed class NexusComboBox : ComboBox
    {
        private Control heightReference;
        private bool matchingHeight;
        internal NexusComboBox()
        {
            BackColor = Theme.SurfaceRaised;
            ForeColor = Theme.Foreground;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            UpdateItemHeight();
        }
        private void UpdateItemHeight()
        {
            ItemHeight = Math.Max(Font.Height + NexusChrome.Scale(this, 4), NexusChrome.Scale(this, 20));
            MatchReferenceHeight();
        }
        internal void MatchHeight(Control reference)
        {
            if (heightReference != null) heightReference.SizeChanged -= ReferenceSizeChanged;
            heightReference = reference;
            if (heightReference != null) heightReference.SizeChanged += ReferenceSizeChanged;
            MatchReferenceHeight();
        }
        private void ReferenceSizeChanged(object sender, EventArgs e) => MatchReferenceHeight();
        private void MatchReferenceHeight()
        {
            if (matchingHeight || heightReference == null || !IsHandleCreated) return;
            matchingHeight = true;
            try
            {
                // Native ComboBox ignores Height. Adjust its owner-drawn selection area,
                // preserving the adjacent button's measured height and the native border.
                int desired = Math.Max(Font.Height + NexusChrome.Scale(this, 4), ItemHeight + heightReference.Height - Height);
                if (ItemHeight != desired) ItemHeight = desired;
            }
            finally { matchingHeight = false; }
        }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateItemHeight(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing && heightReference != null) heightReference.SizeChanged -= ReferenceSizeChanged;
            base.Dispose(disposing);
        }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateItemHeight(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateItemHeight(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnDropDown(EventArgs e) { base.OnDropDown(e); Invalidate(); }
        protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            base.OnDrawItem(e);
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            Color background = selected && Enabled ? Theme.Selection : BackColor;
            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            string value = e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text;
            Rectangle bounds = e.Bounds;
            bounds.Inflate(-NexusChrome.Scale(this, 5), 0);
            TextRenderer.DrawText(e.Graphics, value, Font, bounds, Enabled ? ForeColor : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0 && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2), Theme.Accent, background);
        }
        protected override void WndProc(ref Message m)
        {
            int message = m.Msg;
            base.WndProc(ref m);
            // Invalidate after the native combo has completed its resize and pixel-copy work.
            if (message == 0x0005) Invalidate(); // WM_SIZE
            if (message == NexusChrome.WmPaint || message == NexusChrome.WmPrint || message == NexusChrome.WmPrintClient)
                NexusChrome.Paint(this, message == NexusChrome.WmPaint ? IntPtr.Zero : m.WParam, false, PaintChrome);
        }
        private void PaintChrome(Graphics graphics)
        {
            if (Width < 2 || Height < 2) return;
            var info = new NexusChrome.ComboInfo { Size = Marshal.SizeOf(typeof(NexusChrome.ComboInfo)) };
            Rectangle button = NexusChrome.GetComboBoxInfo(Handle, ref info)
                ? Rectangle.FromLTRB(info.Button.Left, info.Button.Top, info.Button.Right, info.Button.Bottom)
                : new Rectangle(Width - NexusChrome.Scale(this, 20), 1, NexusChrome.Scale(this, 20) - 1, Height - 2);
            Rectangle arrow = button;
            // rcButton excludes the native raised seam immediately to its left.
            // Cover that seam too before drawing the single dark divider.
            button = Rectangle.FromLTRB(button.Left - NexusChrome.Scale(this, 2), 1, button.Right, Height - 1);
            button.Intersect(new Rectangle(1, 1, Width - 2, Height - 2));
            if (!button.IsEmpty)
            {
                using (var brush = new SolidBrush(Enabled ? Theme.SurfaceRaised : Theme.Surface)) graphics.FillRectangle(brush, button);
                using (var pen = new Pen(Theme.Border)) graphics.DrawLine(pen, button.Left, button.Top, button.Left, button.Bottom);
                NexusChrome.DrawArrow(graphics, arrow, false, Enabled ? Theme.Foreground : Theme.Muted, NexusChrome.Scale(this, 4));
            }
            using (var pen = new Pen(Enabled && (ContainsFocus || DroppedDown) ? Theme.Accent : Theme.Border))
                graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    internal sealed class NexusTextBox : TextBox
    {
        internal NexusTextBox() { BackColor = Theme.Surface; ForeColor = Theme.Foreground; BorderStyle = BorderStyle.FixedSingle; }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void WndProc(ref Message m)
        {
            int message = m.Msg;
            base.WndProc(ref m);
            if (BorderStyle == BorderStyle.None) return;
            if (message == NexusChrome.WmPaint || message == NexusChrome.WmNcPaint || message == NexusChrome.WmPrint)
                NexusChrome.Paint(this, message == NexusChrome.WmPrint ? m.WParam : IntPtr.Zero, true, graphics =>
                {
                    if (Width > 1 && Height > 1)
                        using (var pen = new Pen(Enabled && Focused ? Theme.Accent : Theme.Border)) graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                });
        }
    }

    internal sealed class NexusNumericUpDown : NumericUpDown
    {
        private readonly List<SpinnerWindow> spinnerWindows = new List<SpinnerWindow>();
        internal NexusNumericUpDown()
        {
            BackColor = Theme.Surface;
            ForeColor = Theme.Foreground;
            BorderStyle = BorderStyle.FixedSingle;
            foreach (Control child in Controls)
                if (!(child is TextBoxBase)) spinnerWindows.Add(new SpinnerWindow(this, child));
        }
        protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width > 1 && Height > 1)
                using (var pen = new Pen(Enabled && ContainsFocus ? Theme.Accent : Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var window in spinnerWindows) window.Dispose();
            base.Dispose(disposing);
        }

        private sealed class SpinnerWindow : NativeWindow, IDisposable
        {
            private readonly NexusNumericUpDown owner;
            private readonly Control buttons;
            internal SpinnerWindow(NexusNumericUpDown owner, Control buttons)
            {
                this.owner = owner; this.buttons = buttons;
                buttons.HandleCreated += HandleCreated;
                buttons.HandleDestroyed += HandleDestroyed;
                if (buttons.IsHandleCreated) AssignHandle(buttons.Handle);
            }
            private void HandleCreated(object sender, EventArgs e) { AssignHandle(buttons.Handle); }
            private void HandleDestroyed(object sender, EventArgs e) { ReleaseHandle(); }
            protected override void WndProc(ref Message m)
            {
                int message = m.Msg;
                base.WndProc(ref m);
                if (message == NexusChrome.WmPaint || message == NexusChrome.WmPrint || message == NexusChrome.WmPrintClient ||
                    message == 0x200 || message == 0x201 || message == 0x202 || message == 0x2A3)
                    NexusChrome.Paint(buttons, message == NexusChrome.WmPrint || message == NexusChrome.WmPrintClient ? m.WParam : IntPtr.Zero, false, PaintButtons);
            }
            private void PaintButtons(Graphics graphics)
            {
                Rectangle area = buttons.ClientRectangle;
                if (area.Width < 1 || area.Height < 2) return;
                Rectangle upper = new Rectangle(0, 0, area.Width, area.Height / 2);
                Rectangle lower = new Rectangle(0, upper.Bottom, area.Width, area.Height - upper.Height);
                using (var brush = new SolidBrush(Theme.SurfaceRaised)) graphics.FillRectangle(brush, area);
                if (owner.Enabled && buttons.Capture)
                {
                    Point pointer = buttons.PointToClient(Control.MousePosition);
                    using (var brush = new SolidBrush(Color.FromArgb(31, 58, 74))) graphics.FillRectangle(brush, pointer.Y < upper.Bottom ? upper : lower);
                }
                using (var pen = new Pen(Theme.Border))
                {
                    graphics.DrawLine(pen, 0, 0, 0, area.Height);
                    graphics.DrawLine(pen, 0, upper.Bottom, area.Width, upper.Bottom);
                }
                Color arrow = owner.Enabled ? Theme.Foreground : Theme.Muted;
                NexusChrome.DrawArrow(graphics, upper, true, arrow, NexusChrome.Scale(owner, 3));
                NexusChrome.DrawArrow(graphics, lower, false, arrow, NexusChrome.Scale(owner, 3));
            }
            public void Dispose()
            {
                buttons.HandleCreated -= HandleCreated;
                buttons.HandleDestroyed -= HandleDestroyed;
                ReleaseHandle();
            }
        }
    }

    internal sealed class NexusCheckBox : CheckBox
    {
        private bool hover;
        internal NexusCheckBox() { ForeColor = Theme.Foreground; DoubleBuffered = true; }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnCheckStateChanged(EventArgs e) { base.OnCheckStateChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color background = BackColor.A == 255 ? BackColor : Parent?.BackColor ?? Theme.Background;
            e.Graphics.Clear(background);
            Rectangle bounds = new Rectangle(Padding.Left, Padding.Top, Math.Max(1, Width - Padding.Horizontal), Math.Max(1, Height - Padding.Vertical));
            int size = Math.Min(NexusChrome.Scale(this, 13), bounds.Height);
            bool right = CheckAlign == ContentAlignment.TopRight || CheckAlign == ContentAlignment.MiddleRight || CheckAlign == ContentAlignment.BottomRight;
            int top = CheckAlign == ContentAlignment.TopLeft || CheckAlign == ContentAlignment.TopRight ? bounds.Top :
                CheckAlign == ContentAlignment.BottomLeft || CheckAlign == ContentAlignment.BottomRight ? bounds.Bottom - size : bounds.Top + (bounds.Height - size) / 2;
            Rectangle box = new Rectangle(right ? bounds.Right - size : bounds.Left, top, size, size);
            using (var brush = new SolidBrush(Theme.SurfaceRaised)) e.Graphics.FillRectangle(brush, box);
            using (var pen = new Pen(Enabled && (hover || Focused) ? Theme.Accent : Theme.Border)) e.Graphics.DrawRectangle(pen, box.X, box.Y, Math.Max(0, box.Width - 1), Math.Max(0, box.Height - 1));
            Color mark = Enabled ? Theme.Accent : Theme.Muted;
            if (CheckState == CheckState.Indeterminate)
            {
                var fill = Rectangle.Inflate(box, -Math.Max(2, size / 4), -Math.Max(2, size / 4));
                using (var brush = new SolidBrush(mark)) e.Graphics.FillRectangle(brush, fill);
            }
            else if (Checked)
                using (var pen = new Pen(mark, NexusChrome.Scale(this, 2))) e.Graphics.DrawLines(pen, new[]
                {
                    new Point(box.Left + size / 5, box.Top + size / 2),
                    new Point(box.Left + size * 2 / 5, box.Top + size * 3 / 4),
                    new Point(box.Left + size * 4 / 5, box.Top + size / 4)
                });
            int gap = NexusChrome.Scale(this, 4);
            Rectangle text = right ? Rectangle.FromLTRB(bounds.Left, bounds.Top, box.Left - gap, bounds.Bottom) : Rectangle.FromLTRB(box.Right + gap, bounds.Top, bounds.Right, bounds.Bottom);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
            if (AutoEllipsis) flags |= TextFormatFlags.EndEllipsis;
            if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
            TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? ForeColor : Theme.Muted, flags);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(text, -1, -1), Theme.Accent, background);
        }
    }

    internal static class NexusChrome
    {
        internal const int WmPaint = 0xF, WmNcPaint = 0x85, WmPrint = 0x317, WmPrintClient = 0x318;
        internal static int Scale(Control control, int value) => Math.Max(1, (int)Math.Round(value * control.DeviceDpi / 96.0));
        internal static void Paint(Control control, IntPtr targetDc, bool nonClient, Action<Graphics> paint)
        {
            if (control.IsDisposed || !control.IsHandleCreated) return;
            IntPtr windowDc = IntPtr.Zero;
            try
            {
                if (targetDc == IntPtr.Zero && nonClient) targetDc = windowDc = GetWindowDC(control.Handle);
                if (targetDc != IntPtr.Zero) { using (var graphics = Graphics.FromHdc(targetDc)) paint(graphics); }
                else { using (var graphics = control.CreateGraphics()) paint(graphics); }
            }
            finally { if (windowDc != IntPtr.Zero) ReleaseDC(control.Handle, windowDc); }
        }
        internal static void DrawArrow(Graphics graphics, Rectangle area, bool up, Color color, int radius)
        {
            int x = area.Left + area.Width / 2, y = area.Top + area.Height / 2;
            int height = Math.Max(2, radius);
            using (var brush = new SolidBrush(color)) graphics.FillPolygon(brush, up
                ? new[] { new Point(x - radius, y + height / 2), new Point(x + radius, y + height / 2), new Point(x, y - height / 2) }
                : new[] { new Point(x - radius, y - height / 2), new Point(x + radius, y - height / 2), new Point(x, y + height / 2) });
        }
        [StructLayout(LayoutKind.Sequential)] internal struct NativeRect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct ComboInfo
        {
            internal int Size;
            internal NativeRect Item, Button;
            internal int ButtonState;
            internal IntPtr Combo, Edit, List;
        }
        [DllImport("user32.dll")] internal static extern bool GetComboBoxInfo(IntPtr combo, ref ComboInfo info);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr handle);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr handle, IntPtr dc);
    }

    internal sealed class NexusButton : Button
    {
        private bool hover;
        private bool pressed;
        internal NexusButton() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color background = Enabled && pressed ? FlatAppearance.MouseDownBackColor : Enabled && hover ? FlatAppearance.MouseOverBackColor : BackColor;
            if (background.IsEmpty) background = BackColor;
            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, ClientRectangle);
            using (var pen = new Pen(FlatAppearance.BorderColor.IsEmpty ? Theme.Border : FlatAppearance.BorderColor))
                e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            var textBounds = new Rectangle(Padding.Left + 3, Padding.Top, Math.Max(1, Width - Padding.Horizontal - 6), Math.Max(1, Height - Padding.Vertical));
            Color textColor = Enabled ? ForeColor : BackColor.GetBrightness() > .45F ? Theme.Background : Theme.Muted;
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(4, 4, Math.Max(0, Width - 8), Math.Max(0, Height - 8)), ForeColor, background);
        }
    }

    // These are ordinary WinForms controls, with no native-window or browser dependencies.
    internal sealed class NexusHero : Panel
    {
        private readonly Image backdrop;
        internal NexusHero()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            backdrop = AppArtwork.CreateBackdrop();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            e.Graphics.Clear(Theme.Background);
            if (backdrop != null && Width > 0 && Height > 0)
            {
                float scale = Math.Max((float)Width / backdrop.Width, (float)Height / backdrop.Height);
                float height = backdrop.Height * scale;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(backdrop, new RectangleF(0, -(height - Height) * .48f, backdrop.Width * scale, height));
            }
            using (var shade = new LinearGradientBrush(ClientRectangle, Color.FromArgb(245, 8, 18, 28), Color.FromArgb(30, 8, 18, 28), 0F))
                e.Graphics.FillRectangle(shade, ClientRectangle);
            using (var shade = new LinearGradientBrush(ClientRectangle, Color.Transparent, Color.FromArgb(110, 8, 18, 28), 90F))
                e.Graphics.FillRectangle(shade, ClientRectangle);
            using (var line = new Pen(Theme.Border)) e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
            using (var line = new Pen(Theme.Accent, 2)) e.Graphics.DrawLine(line, 24, Height - 1, 150, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) backdrop?.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class NexusPanel : Panel
    {
        internal NexusPanel()
        {
            DoubleBuffered = true;
            // Windows otherwise preserves the old right/bottom border inside a grown panel.
            SetStyle(ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width > 0 && Height > 0)
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
