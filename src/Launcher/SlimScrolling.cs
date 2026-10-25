using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AOLauncher
{
    // Keep the native control responsible for scrolling and input. Its native scroll
    // chrome is clipped outside the viewport; only the slim, themed thumbs are shown.
    internal static class SlimScrolling
    {
        internal static Control Wrap(Control content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            var existing = content.Parent?.Parent as SlimScrollHost;
            return existing != null && existing.Content == content ? existing : new SlimScrollHost(content);
        }

        internal static void Refresh(Control content)
        {
            var host = content as SlimScrollHost ?? content?.Parent?.Parent as SlimScrollHost;
            host?.QueueRefresh();
        }

        internal static void LayoutNow(Control content)
        {
            var host = content as SlimScrollHost ?? content?.Parent?.Parent as SlimScrollHost;
            host?.UpdateScrollbars();
        }

        internal static void SetVisible(Control content, bool visible)
        {
            var host = content?.Parent?.Parent as SlimScrollHost;
            if (host != null && host.Content == content) host.Visible = visible;
            if (content != null) content.Visible = visible;
        }
    }

    internal sealed class SlimScrollHost : Panel
    {
        private readonly Panel viewport;
        private readonly ScrollObserver observer;
        private bool queued;
        private bool updating;
        internal Control Content { get; }
        internal SlimScrollBar VerticalBar { get; }
        internal SlimScrollBar HorizontalBar { get; }

        internal SlimScrollHost(Control content)
        {
            Content = content;
            Dock = content.Dock;
            Anchor = content.Anchor;
            Margin = content.Margin;
            MinimumSize = content.MinimumSize;
            MaximumSize = content.MaximumSize;
            Bounds = content.Bounds;
            TabIndex = content.TabIndex;
            Name = string.IsNullOrEmpty(content.Name) ? "slimScrollHost" : content.Name + "ScrollHost";
            // An unparented panel reports the Windows default rather than the
            // dark colour it would inherit once inserted into the dialog.
            BackColor = content.BackColor == SystemColors.Control || content.BackColor == SystemColors.Window
                ? Theme.Surface : content.BackColor;
            content.BackColor = BackColor;
            TabStop = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            viewport = new Panel { Margin = Padding.Empty, BackColor = BackColor, TabStop = false };
            VerticalBar = new SlimScrollBar(this, true);
            HorizontalBar = new SlimScrollBar(this, false);
            Controls.Add(viewport);
            Controls.Add(VerticalBar);
            Controls.Add(HorizontalBar);
            content.Dock = DockStyle.None;
            content.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            content.Margin = Padding.Empty;
            // The viewport, not the original Dock/Anchor settings, owns this size.
            content.MinimumSize = Size.Empty;
            content.MaximumSize = Size.Empty;
            viewport.Controls.Add(content);
            observer = new ScrollObserver(this);
            content.HandleCreated += ContentHandleCreated;
            content.HandleDestroyed += ContentHandleDestroyed;
            content.Layout += ContentLayout;
            content.TextChanged += ContentChanged;
            content.BackColorChanged += ContentChanged;
            content.VisibleChanged += ContentChanged;
            if (content is ScrollableControl scrollable) scrollable.Scroll += ContentScrolled;
            if (content is DataGridView grid) grid.Scroll += ContentScrolled;
            if (content.IsHandleCreated) observer.AssignHandle(content.Handle);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); QueueRefresh(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); QueueRefresh(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); QueueRefresh(); }
        private void ContentHandleCreated(object sender, EventArgs e) { observer.AssignHandle(Content.Handle); QueueRefresh(); }
        private void ContentHandleDestroyed(object sender, EventArgs e) { observer.ReleaseHandle(); }
        private void ContentLayout(object sender, LayoutEventArgs e) { QueueRefresh(); }
        private void ContentChanged(object sender, EventArgs e) { QueueRefresh(); }
        private void ContentScrolled(object sender, ScrollEventArgs e) { QueueRefresh(); }

        internal void QueueRefresh()
        {
            // Resizing the content below raises layout/native size messages. The
            // bounded settling loop handles those changes within this update;
            // queuing them again can keep the UI message pump busy indefinitely.
            if (updating || queued || IsDisposed || Disposing || !IsHandleCreated) return;
            queued = true;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    queued = false;
                    if (!IsDisposed && !Disposing) UpdateScrollbars();
                }));
            }
            catch (InvalidOperationException) { queued = false; }
        }

        internal void UpdateScrollbars()
        {
            if (updating || Content.IsDisposed || !Content.IsHandleCreated) return;
            updating = true;
            try
            {
                BackColor = viewport.BackColor = Content.BackColor;
                int rail = Math.Max(10, (int)Math.Round(10 * DeviceDpi / 96.0));
                // Bounds settle in at most a few passes when adding one scrollbar
                // causes overflow on the other axis. No idle timer or repaint polling.
                for (int pass = 0; pass < 4; pass++)
                {
                    ScrollState vertical = ReadState(true), horizontal = ReadState(false);
                    bool showVertical = vertical.Maximum > vertical.Minimum;
                    bool showHorizontal = horizontal.Maximum > horizontal.Minimum;
                    int width = Math.Max(0, ClientSize.Width - (showVertical ? rail : 0));
                    int height = Math.Max(0, ClientSize.Height - (showHorizontal ? rail : 0));
                    viewport.Bounds = new Rectangle(0, 0, width, height);
                    var bounds = new Rectangle(0, 0,
                        width + (HasNativeBar(true) ? NativeMetric(2) : 0),
                        height + (HasNativeBar(false) ? NativeMetric(3) : 0));
                    bool changed = Content.Bounds != bounds;
                    if (changed) Content.Bounds = bounds;
                    VerticalBar.Bounds = new Rectangle(width, 0, showVertical ? rail : 0, height);
                    HorizontalBar.Bounds = new Rectangle(0, height, width, showHorizontal ? rail : 0);
                    VerticalBar.UpdateState(vertical, showVertical);
                    HorizontalBar.UpdateState(horizontal, showHorizontal);
                    if (!changed) break;
                }
            }
            finally { updating = false; }
        }

        private int NativeMetric(int index)
        {
            try { return Native.GetSystemMetricsForDpi(index, (uint)DeviceDpi); }
            catch (EntryPointNotFoundException) { return Native.GetSystemMetrics(index); }
        }

        private bool HasNativeBar(bool vertical)
        {
            if (Content is DataGridView grid)
                return grid.Controls.Cast<Control>().Any(c => (vertical ? c is VScrollBar : c is HScrollBar) && c.Visible);
            int style = Native.GetWindowLong(Content.Handle, -16);
            return (style & (vertical ? 0x00200000 : 0x00100000)) != 0;
        }

        private ScrollState ReadState(bool vertical)
        {
            if (Content is DataGridView grid)
            {
                if (vertical)
                {
                    int[] rows = grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible && !r.Frozen).Select(r => r.Index).ToArray();
                    int page = Math.Max(1, grid.DisplayedRowCount(false) - grid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible && r.Frozen));
                    return new ScrollState(0, Math.Max(0, rows.Length - page), page,
                        Math.Max(0, Array.IndexOf(rows, grid.FirstDisplayedScrollingRowIndex)));
                }
                var bar = grid.Controls.OfType<HScrollBar>().FirstOrDefault();
                if (bar != null && bar.Visible)
                    return new ScrollState(bar.Minimum, Math.Max(bar.Minimum, bar.Maximum - bar.LargeChange + 1), bar.LargeChange, bar.Value);
                return ScrollState.Empty;
            }
            var info = Native.Info();
            if (!HasNativeBar(vertical) || !Native.GetScrollInfo(Content.Handle, vertical ? 1 : 0, ref info)) return ScrollState.Empty;
            int max = (int)Math.Max(info.nMin, (long)info.nMax - Math.Max(1L, info.nPage) + 1);
            return new ScrollState(info.nMin, max, (int)Math.Min(int.MaxValue, Math.Max(1L, info.nPage)), info.nPos);
        }

        internal void SetScrollPosition(bool vertical, int position)
        {
            if (Content.IsDisposed || !Content.IsHandleCreated) return;
            ScrollState state = ReadState(vertical);
            position = Math.Max(state.Minimum, Math.Min(state.Maximum, position));
            if (Content is TreeView tree && vertical)
            {
                TreeNode node = tree.Nodes.Count > 0 ? tree.Nodes[0] : null;
                for (int i = 0; i < position && node != null; i++) node = node.NextVisibleNode;
                if (node != null) tree.TopNode = node;
            }
            else if (Content is TextBoxBase && vertical)
            {
                int first = Native.SendMessage(Content.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32(); // EM_GETFIRSTVISIBLELINE
                Native.SendMessage(Content.Handle, 0x00B6, IntPtr.Zero, new IntPtr(position - first)); // EM_LINESCROLL
            }
            else if (Content is DataGridView grid)
            {
                if (vertical)
                {
                    int[] rows = grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible && !r.Frozen).Select(r => r.Index).ToArray();
                    if (rows.Length > 0) grid.FirstDisplayedScrollingRowIndex = rows[Math.Min(position, rows.Length - 1)];
                }
                else grid.HorizontalScrollingOffset = position;
            }
            else if (Content is ScrollableControl scrollable)
            {
                Point current = scrollable.AutoScrollPosition;
                scrollable.AutoScrollPosition = vertical ? new Point(-current.X, position) : new Point(position, -current.Y);
            }
            else ScrollNative(vertical, position, state);
            UpdateScrollbars();
        }

        private void ScrollNative(bool vertical, int position, ScrollState state)
        {
            int message = vertical ? 0x0115 : 0x0114;
            if (position == state.Minimum || position == state.Maximum)
            {
                Native.SendMessage(Content.Handle, message, new IntPtr(position == state.Minimum ? 6 : 7), IntPtr.Zero);
            }
            else if (position <= ushort.MaxValue)
            {
                Native.SendMessage(Content.Handle, message, new IntPtr((position << 16) | 4), IntPtr.Zero);
            }
            else
            {
                // WM_*SCROLL carries only a 16-bit thumb position. Page/line input
                // reaches wider native ranges without wrapping at 65535.
                for (int i = 0; i < 512; i++)
                {
                    ScrollState current = ReadState(vertical);
                    long delta = (long)position - current.Position;
                    if (delta == 0) break;
                    int command = Math.Abs(delta) >= current.PageSize ? (delta > 0 ? 3 : 2) : (delta > 0 ? 1 : 0);
                    Native.SendMessage(Content.Handle, message, new IntPtr(command), IntPtr.Zero);
                    int next = ReadState(vertical).Position;
                    if (next == current.Position || (delta > 0 && next >= position) || (delta < 0 && next <= position)) break;
                }
            }
            Native.SendMessage(Content.Handle, message, new IntPtr(8), IntPtr.Zero);
        }

        internal void ForwardWheel(bool vertical, int delta)
        {
            if (!Content.IsHandleCreated) return;
            Native.SendMessage(Content.Handle, vertical ? 0x020A : 0x020E, new IntPtr(delta << 16), IntPtr.Zero);
            QueueRefresh();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Content.HandleCreated -= ContentHandleCreated;
                Content.HandleDestroyed -= ContentHandleDestroyed;
                Content.Layout -= ContentLayout;
                Content.TextChanged -= ContentChanged;
                Content.BackColorChanged -= ContentChanged;
                Content.VisibleChanged -= ContentChanged;
                if (Content is ScrollableControl scrollable) scrollable.Scroll -= ContentScrolled;
                if (Content is DataGridView grid) grid.Scroll -= ContentScrolled;
                observer.ReleaseHandle();
            }
            base.Dispose(disposing);
        }

        private sealed class ScrollObserver : NativeWindow
        {
            private readonly SlimScrollHost owner;
            internal ScrollObserver(SlimScrollHost owner) { this.owner = owner; }
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                switch (m.Msg)
                {
                    case 0x0005: // WM_SIZE
                    case 0x000C: // WM_SETTEXT
                    case 0x0047: // WM_WINDOWPOSCHANGED
                    case 0x007D: // WM_STYLECHANGED
                    case 0x00B6: // EM_LINESCROLL
                    case 0x00C2: // EM_REPLACESEL
                    case 0x0100: // WM_KEYDOWN
                    case 0x0101: // WM_KEYUP
                    case 0x0114: // WM_HSCROLL
                    case 0x0115: // WM_VSCROLL
                    case 0x020A: // WM_MOUSEWHEEL
                    case 0x020E: // WM_MOUSEHWHEEL
                    case 0x1100: // TVM_INSERTITEMA
                    case 0x1101: // TVM_DELETEITEM
                    case 0x1102: // TVM_EXPAND
                    case 0x110B: // TVM_SELECTITEM
                    case 0x1132: // TVM_INSERTITEMW
                    case 0x113F: // TVM_SETITEMW (text width)
                        owner.QueueRefresh(); break;
                }
            }
        }

        private static class Native
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct ScrollInfo { public uint cbSize, fMask; public int nMin, nMax; public uint nPage; public int nPos, nTrackPos; }
            internal static ScrollInfo Info() => new ScrollInfo { cbSize = (uint)Marshal.SizeOf(typeof(ScrollInfo)), fMask = 0x17 };
            [DllImport("user32.dll")] internal static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo info);
            [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
            [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
            [DllImport("user32.dll")] internal static extern int GetSystemMetricsForDpi(int index, uint dpi);
            [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        }
    }

    internal struct ScrollState
    {
        internal static readonly ScrollState Empty = new ScrollState(0, 0, 1, 0);
        internal int Minimum, Maximum, PageSize, Position;
        internal ScrollState(int minimum, int maximum, int page, int position)
        { Minimum = minimum; Maximum = maximum; PageSize = Math.Max(1, page); Position = Math.Max(minimum, Math.Min(maximum, position)); }
    }

    internal sealed class SlimScrollBar : Control
    {
        private readonly SlimScrollHost owner;
        private readonly bool vertical;
        private ScrollState state = ScrollState.Empty;
        private bool dragging;
        private bool hovered;
        private int dragOffset;
        internal int Minimum => state.Minimum;
        internal int Maximum => state.Maximum;
        internal int PageSize => state.PageSize;
        internal int Position => state.Position;

        internal SlimScrollBar(SlimScrollHost owner, bool vertical)
        {
            this.owner = owner;
            this.vertical = vertical;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = false;
            Cursor = Cursors.Arrow;
            AccessibleName = vertical ? "Vertical scrollbar" : "Horizontal scrollbar";
            AccessibleRole = AccessibleRole.ScrollBar;
            Visible = false;
        }

        internal void UpdateState(ScrollState value, bool visible)
        {
            bool changed = state.Minimum != value.Minimum || state.Maximum != value.Maximum || state.PageSize != value.PageSize || state.Position != value.Position;
            state = value;
            if (Visible != visible) Visible = visible;
            if (BackColor != owner.BackColor) { BackColor = owner.BackColor; changed = true; }
            if (changed) Invalidate();
        }

        internal void ScrollTo(int position) { owner.SetScrollPosition(vertical, position); }

        private Rectangle Thumb
        {
            get
            {
                int extent = vertical ? Height : Width;
                int thickness = vertical ? Width : Height;
                int padding = Math.Max(2, (int)Math.Round(2 * DeviceDpi / 96.0));
                int usable = Math.Max(0, extent - padding * 2);
                int length = Math.Min(usable, Math.Max((int)Math.Round(24 * DeviceDpi / 96.0),
                    (int)((long)usable * state.PageSize / Math.Max(1L, (long)state.Maximum - state.Minimum + state.PageSize))));
                int position = padding + (state.Maximum <= state.Minimum ? 0 :
                    (int)((long)(usable - length) * (state.Position - state.Minimum) / (state.Maximum - state.Minimum)));
                return vertical ? new Rectangle(padding, position, Math.Max(0, thickness - padding * 2), length)
                    : new Rectangle(position, padding, length, Math.Max(0, thickness - padding * 2));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (state.Maximum <= state.Minimum) return;
            Rectangle thumb = Thumb;
            if (thumb.Width <= 0 || thumb.Height <= 0) return;
            using (var brush = new SolidBrush(dragging ? Theme.Accent : hovered ? Color.FromArgb(107, 158, 180) : Color.FromArgb(67, 107, 127)))
                e.Graphics.FillRectangle(brush, thumb);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || state.Maximum <= state.Minimum) return;
            owner.Content.Focus();
            Rectangle thumb = Thumb;
            int cursor = vertical ? e.Y : e.X, begin = vertical ? thumb.Top : thumb.Left;
            if (thumb.Contains(e.Location))
            {
                dragging = true; dragOffset = cursor - begin; Capture = true;
            }
            else ScrollTo(state.Position + (cursor < begin ? -state.PageSize : state.PageSize));
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            Rectangle thumb = Thumb;
            int padding = Math.Max(2, (int)Math.Round(2 * DeviceDpi / 96.0));
            int track = Math.Max(1, (vertical ? Height - thumb.Height : Width - thumb.Width) - padding * 2);
            int coordinate = Math.Max(0, Math.Min(track, (vertical ? e.Y : e.X) - dragOffset - padding));
            ScrollTo(state.Minimum + (int)((long)coordinate * (state.Maximum - state.Minimum) / track));
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) { dragging = false; Capture = false; Invalidate(); }
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) { dragging = false; Invalidate(); }
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); owner.ForwardWheel(vertical, e.Delta); }
    }
}
