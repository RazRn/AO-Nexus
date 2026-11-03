using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AOLauncher
{
    // The plugins of a set as in the mockup: sections (PROFESSION HANDLERS, MANAGERS, ...) with the
    // plugins in columns of checkboxes. Headers fold a section; plugins with a safety note carry an
    // amber mark. Arrow keys move, Space ticks, Enter folds a section. Owner-drawn for speed.
    internal sealed class PluginSetComboCell : DataGridViewComboBoxCell
    {
        public override Type EditType => typeof(PluginSetComboEditor);

        protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex,
            DataGridViewElementStates state, object value, object formattedValue, string errorText,
            DataGridViewCellStyle style, DataGridViewAdvancedBorderStyle border, DataGridViewPaintParts parts)
        {
            base.Paint(graphics, clipBounds, cellBounds, rowIndex, state, value, formattedValue, errorText, style, border,
                parts & (DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground | DataGridViewPaintParts.Border));
            bool selected = (state & DataGridViewElementStates.Selected) != 0;
            int width = NexusChrome.Scale(DataGridView, 20);
            var button = new Rectangle(cellBounds.Right - width - 1, cellBounds.Top + 1, width, Math.Max(0, cellBounds.Height - 2));
            if ((parts & DataGridViewPaintParts.ContentForeground) != 0)
            {
                using (var brush = new SolidBrush(Theme.SurfaceRaised)) graphics.FillRectangle(brush, button);
                using (var pen = new Pen(Theme.Border)) graphics.DrawLine(pen, button.Left, button.Top, button.Left, button.Bottom);
                NexusChrome.DrawArrow(graphics, button, false, DataGridView.Enabled ? Theme.Foreground : Theme.Muted, NexusChrome.Scale(DataGridView, 4));
                var text = new Rectangle(cellBounds.X + 4, cellBounds.Y, Math.Max(0, cellBounds.Width - width - 8), cellBounds.Height);
                TextRenderer.DrawText(graphics, Convert.ToString(formattedValue), style.Font, text,
                    DataGridView.Enabled ? (selected ? style.SelectionForeColor : style.ForeColor) : Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            if ((parts & DataGridViewPaintParts.Focus) != 0 && DataGridView.ContainsFocus && DataGridView.CurrentCell == this)
                using (var pen = new Pen(Theme.Accent)) graphics.DrawRectangle(pen, Rectangle.Inflate(cellBounds, -1, -1));
        }
    }

    internal sealed class PluginSetComboEditor : DataGridViewComboBoxEditingControl
    {
        public PluginSetComboEditor() { Theme.StyleCombo(this); }
        public override void ApplyCellStyleToEditingControl(DataGridViewCellStyle style)
        {
            base.ApplyCellStyleToEditingControl(style);
            Theme.StyleCombo(this);
        }
        protected override void WndProc(ref Message message)
        {
            int kind = message.Msg;
            base.WndProc(ref message);
            if (kind == NexusChrome.WmPaint || kind == NexusChrome.WmPrint || kind == NexusChrome.WmPrintClient)
                NexusChrome.Paint(this, kind == NexusChrome.WmPaint ? IntPtr.Zero : message.WParam, false, graphics =>
                {
                    if (Width < 2 || Height < 2) return;
                    var info = new NexusChrome.ComboInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NexusChrome.ComboInfo)) };
                    var arrow = NexusChrome.GetComboBoxInfo(Handle, ref info)
                        ? Rectangle.FromLTRB(info.Button.Left, info.Button.Top, info.Button.Right, info.Button.Bottom)
                        : new Rectangle(Width - NexusChrome.Scale(this, 20), 1, NexusChrome.Scale(this, 20) - 1, Height - 2);
                    var button = Rectangle.FromLTRB(Math.Max(1, arrow.Left - NexusChrome.Scale(this, 2)), 1, Width - 1, Height - 1);
                    using (var brush = new SolidBrush(Theme.SurfaceRaised)) graphics.FillRectangle(brush, button);
                    using (var pen = new Pen(Theme.Border)) graphics.DrawLine(pen, button.Left, button.Top, button.Left, button.Bottom);
                    NexusChrome.DrawArrow(graphics, arrow, false, Theme.Foreground, NexusChrome.Scale(this, 4));
                    using (var pen = new Pen(ContainsFocus || DroppedDown ? Theme.Accent : Theme.Border)) graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                });
        }
    }

    internal sealed class PluginGrid : Control
    {
        internal sealed class Section
        {
            internal string Key, Title, Hint;
            internal bool Collapsed;
            internal List<PluginInfo> Plugins = new List<PluginInfo>();
        }

        sealed class Hit { internal Section Section; internal PluginInfo Plugin; internal Rectangle Bounds; }

        Font markFont;
        readonly SlimScrollBar scroll;
        int scrollPosition;
        // Intended scroll bar state: Control.Visible reads false while the page is hidden.
        bool scrolling;
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400 };
        List<Section> sections = new List<Section>();
        readonly List<Hit> hits = new List<Hit>();
        Hit hover, focus;
        string tipFor;
        int contentHeight;

        internal Func<PluginInfo, bool> IsChecked = plugin => false;
        internal Func<PluginInfo, bool> IsInherited = plugin => false;
        // Latest in-game test of a plugin (Test plugins), shown as a coloured dot; null when untested.
        internal Func<PluginInfo, PluginTestRecord> TestOf = plugin => null;
        internal event Action<PluginInfo, bool> CheckChanged;
        internal event Action<PluginInfo> FocusChanged;
        internal PluginInfo FocusedPlugin => focus?.Plugin;

        internal PluginGrid()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Surface; ForeColor = Theme.Foreground; TabStop = true;
            AccessibleRole = AccessibleRole.List;
            scroll = new SlimScrollBar(this, true, SetScrollPosition, delta => SetScrollPosition(scrollPosition - delta / 120 * RowHeight * 3))
                { Dock = DockStyle.Right, Width = 10, Name = "pluginGridScroll" };
            Controls.Add(scroll);
            Disposed += (sender, e) => { tips.Dispose(); markFont?.Dispose(); };
        }

        float scale;
        // Graphics.DpiX works on every runtime (DeviceDpi needs .NET 4.7+ WinForms).
        float Scale
        {
            get
            {
                if (scale > 0) return scale;
                try { using (var g = CreateGraphics()) scale = g.DpiX / 96f; } catch { scale = 1f; }
                return scale;
            }
        }
        protected override void OnHandleCreated(EventArgs e) { scale = 0; base.OnHandleCreated(e); }
        int HeaderHeight => (int)(30 * Scale);
        int RowHeight => (int)(26 * Scale);

        internal void SetSections(List<Section> value)
        {
            string focusId = focus?.Plugin?.Id, focusKey = focus?.Section?.Key;
            sections = value ?? new List<Section>();
            Relayout();
            focus = hits.FirstOrDefault(hit => focusId != null && hit.Plugin?.Id == focusId) ?? hits.FirstOrDefault(hit => hit.Plugin == null && hit.Section.Key == focusKey);
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        void Relayout()
        {
            if (scroll == null) return;
            scroll.Width = Math.Max(10, (int)(10 * Scale));
            hits.Clear();
            int width = ClientSize.Width - (scrolling ? scroll.Width : 0) - 4;
            int columns = Math.Max(1, Math.Min(3, width / (int)(190 * Scale)));
            int columnWidth = Math.Max(80, width / columns);
            int y = 2;
            foreach (var section in sections)
            {
                hits.Add(new Hit { Section = section, Bounds = new Rectangle(0, y, width, HeaderHeight) });
                y += HeaderHeight;
                if (section.Collapsed) continue;
                for (int i = 0; i < section.Plugins.Count; i++)
                {
                    int column = i % columns, row = i / columns;
                    hits.Add(new Hit { Section = section, Plugin = section.Plugins[i], Bounds = new Rectangle(column * columnWidth, y + row * RowHeight, columnWidth, RowHeight) });
                }
                y += (section.Plugins.Count + columns - 1) / columns * RowHeight + (int)(6 * Scale);
            }
            contentHeight = y;
            bool needs = contentHeight > ClientSize.Height;
            if (scrolling != needs) { scrolling = needs; scroll.Visible = needs; Relayout(); return; }
            SetScrollPosition(scrollPosition);
        }

        void SetScrollPosition(int value)
        {
            scrollPosition = Math.Max(0, Math.Min(Math.Max(0, contentHeight - ClientSize.Height), value));
            scroll.UpdateState(new ScrollState(0, Math.Max(0, contentHeight - ClientSize.Height), Math.Max(1, ClientSize.Height), scrollPosition), scrolling);
            Invalidate();
        }

        int Offset => scrolling ? scrollPosition : 0;
        Rectangle OnScreen(Hit hit) { var r = hit.Bounds; r.Offset(0, -Offset); return r; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bold = new Font(Font.FontFamily, 8.5F, FontStyle.Bold))
            using (var hint = new Font(Font.FontFamily, 8.5F))
                foreach (var hit in hits)
                {
                    var r = OnScreen(hit);
                    if (r.Bottom < 0 || r.Top > ClientSize.Height) continue;
                    if (hit.Plugin == null) DrawHeader(g, hit, r, bold, hint);
                    else DrawPlugin(g, hit, r);
                }
            if (hits.Count == 0)
                TextRenderer.DrawText(g, AOLauncher.Localization.Texts.Get("Text_No_plugins_match_FF29ECC2498E"), Font, new Point(4, 8), Theme.Muted);
        }

        void DrawHeader(Graphics g, Hit hit, Rectangle r, Font bold, Font hint)
        {
            int checkedCount = hit.Section.Plugins.Count(IsChecked);
            var chevron = new Rectangle(r.X + 2, r.Y + r.Height / 2 - 5, 10, 10);
            using (var brush = new SolidBrush(Theme.Muted))
                g.FillPolygon(brush, hit.Section.Collapsed
                    ? new[] { new Point(chevron.Left + 2, chevron.Top), new Point(chevron.Right - 1, chevron.Top + 5), new Point(chevron.Left + 2, chevron.Bottom) }
                    : new[] { new Point(chevron.Left, chevron.Top + 2), new Point(chevron.Right, chevron.Top + 2), new Point(chevron.Left + 5, chevron.Bottom - 1) });
            string title = hit.Section.Title + "  " + (checkedCount > 0 ? checkedCount + "/" : "") + hit.Section.Plugins.Count;
            var titleSize = TextRenderer.MeasureText(g, title, bold);
            TextRenderer.DrawText(g, title, bold, new Point(r.X + (int)(16 * Scale), r.Y + (r.Height - titleSize.Height) / 2), checkedCount > 0 ? Theme.Foreground : Theme.Muted, TextFormatFlags.NoPrefix);
            if (!string.IsNullOrEmpty(hit.Section.Hint))
                TextRenderer.DrawText(g, hit.Section.Hint, hint, new Rectangle(r.X + (int)(24 * Scale) + titleSize.Width, r.Y, r.Width - titleSize.Width - 30, r.Height), Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (focus == hit && Focused) using (var pen = new Pen(Theme.Accent) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, r.X, r.Y + 2, r.Width - 1, r.Height - 5);
        }

        void DrawPlugin(Graphics g, Hit hit, Rectangle r)
        {
            bool on = IsChecked(hit.Plugin);
            if (hit == hover || hit == focus)
                using (var brush = new SolidBrush(hit == focus ? Theme.Selection : Theme.SurfaceRaised)) g.FillRectangle(brush, Rectangle.Inflate(r, -2, -1));
            int size = (int)(15 * Scale);
            var box = new Rectangle(r.X + (int)(8 * Scale), r.Y + (r.Height - size) / 2, size, size);
            using (var fill = new SolidBrush(on ? Theme.Accent : Theme.Surface)) g.FillRectangle(fill, box);
            using (var pen = new Pen(on ? Theme.Accent : Theme.Border, 1.5f)) g.DrawRectangle(pen, box);
            if (on)
                using (var pen = new Pen(Theme.Background, 2f * Scale))
                    g.DrawLines(pen, new[] { new PointF(box.Left + box.Width * 0.22f, box.Top + box.Height * 0.52f), new PointF(box.Left + box.Width * 0.42f, box.Top + box.Height * 0.72f), new PointF(box.Left + box.Width * 0.78f, box.Top + box.Height * 0.3f) });
            int textX = box.Right + (int)(8 * Scale);
            int noteWidth = string.IsNullOrEmpty(hit.Plugin.Note) ? 0 : (int)(14 * Scale);
            var test = TestOf(hit.Plugin);
            int testWidth = test == null ? 0 : (int)(12 * Scale);
            TextRenderer.DrawText(g, hit.Plugin.Name + (IsInherited(hit.Plugin) ? " · ALL" : ""), Font, new Rectangle(textX, r.Y, r.Right - textX - noteWidth - testWidth - 4, r.Height), on ? Theme.Foreground : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (test != null)
            {
                int dotSize = (int)(7 * Scale);
                var dot = new Rectangle(r.Right - noteWidth - testWidth + (testWidth - dotSize) / 2 - 2, r.Y + (r.Height - dotSize) / 2, dotSize, dotSize);
                using (var brush = new SolidBrush(TestColor(test.Status))) g.FillEllipse(brush, dot);
            }
            if (noteWidth > 0)
            {
                var mark = new Rectangle(r.Right - noteWidth - 2, r.Y + (r.Height - noteWidth) / 2, noteWidth - 2, noteWidth - 2);
                using (var brush = new SolidBrush(Theme.Amber)) g.FillEllipse(brush, mark);
                if (markFont == null) markFont = new Font(Font.FontFamily, 7F, FontStyle.Bold);
                TextRenderer.DrawText(g, "!", markFont, mark, Theme.Background, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        Hit HitAt(Point point) => hits.FirstOrDefault(hit => OnScreen(hit).Contains(point));

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = HitAt(e.Location);
            // Repaint only the rows that change, not the whole list.
            if (hit != hover) { InvalidateHit(hover); hover = hit; InvalidateHit(hover); }
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
            string key = hit?.Plugin?.Id ?? hit?.Section?.Key;
            if (key != tipFor) { tipFor = key; tips.SetToolTip(this, TipFor(hit)); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); InvalidateHit(hover); hover = null; }

        void InvalidateHit(Hit hit) { if (hit != null) Invalidate(Rectangle.Inflate(OnScreen(hit), 0, 1)); }

        string TipFor(Hit hit)
        {
            if (hit == null) return null;
            if (hit.Plugin == null) return hit.Section.Collapsed ? AOLauncher.Localization.Texts.Get("Text_Click_to_show_7EA872F504E1") : AOLauncher.Localization.Texts.Get("Text_Click_to_fold_8D3F56517FF6");
            return (IsInherited(hit.Plugin) ? Localization.Texts.T("Selected in ALL; edit it there.") + "\r\n\r\n" : "") + Describe(hit.Plugin, TestOf(hit.Plugin));
        }

        internal static Color TestColor(string status) =>
            status == nameof(PluginStatus.Running) ? Theme.Accent : status == nameof(PluginStatus.Started) ? Theme.Muted
            : status == nameof(PluginStatus.Errors) ? LogTone.Warning : LogTone.Error;

        internal static string TestText(PluginTestRecord test)
        {
            if (test == null) return null;
            var check = new PluginCheck { Status = Enum.TryParse(test.Status, out PluginStatus status) ? status : PluginStatus.Failed, Message = test.Message };
            return string.Format(AOLauncher.Localization.Texts.Get("Text_Startup_test_0_d_MMM_HH_005CDE336EBE"), new DateTime(test.TestedUtcTicks, DateTimeKind.Utc).ToLocalTime(), test.Character, check.StatusText())
                + AOLauncher.Localization.Texts.Get("Text_gameplay_not_verified_2A49E09AD01B")
                + (string.IsNullOrEmpty(test.Profession) ? "" : " · " + test.Profession)
                + (string.IsNullOrEmpty(test.ServerEndpoint) ? "" : " · " + test.ServerEndpoint)
                + (string.IsNullOrEmpty(test.Message) || check.Status == PluginStatus.Running ? "" : " · " + test.Message);
        }

        internal static string Describe(PluginInfo plugin, PluginTestRecord test = null)
        {
            var lines = new List<string> { plugin.Name };
            if (test != null) lines.Add(TestText(test));
            if (!string.IsNullOrEmpty(plugin.About)) lines.Add(plugin.About);
            if (!string.IsNullOrEmpty(plugin.Note)) lines.Add("⚠ " + plugin.Note);
            if (!string.IsNullOrEmpty(plugin.Settings)) lines.Add(string.Format(AOLauncher.Localization.Texts.Get("Text_In_game_0_opens_its_window_09E5061732F6"), plugin.Settings));
            var others = plugin.Commands.Where(command => !string.Equals(command, plugin.Settings, StringComparison.OrdinalIgnoreCase)).ToList();
            if (others.Count > 0) lines.Add(AOLauncher.Localization.Texts.Get("Text_Commands_6540C54F379A") + string.Join(" ", others.Select(command => "/" + command)));
            return string.Join("\r\n", lines);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var hit = HitAt(e.Location);
            if (hit == null || e.Button != MouseButtons.Left) return;
            Activate(hit);
        }

        void Activate(Hit hit)
        {
            SetFocus(hit);
            if (hit.Plugin == null) { hit.Section.Collapsed = !hit.Section.Collapsed; Relayout(); focus = hits.FirstOrDefault(item => item.Plugin == null && item.Section == hit.Section); }
            else if (!IsInherited(hit.Plugin)) CheckChanged?.Invoke(hit.Plugin, !IsChecked(hit.Plugin));
            Invalidate();
        }

        void SetFocus(Hit hit)
        {
            focus = hit;
            if (hit == null) return;
            var r = OnScreen(hit);
            if (scrolling && r.Top < 0) SetScrollPosition(hit.Bounds.Top);
            else if (scrolling && r.Bottom > ClientSize.Height) SetScrollPosition(hit.Bounds.Bottom - ClientSize.Height);
            if (hit.Plugin != null) FocusChanged?.Invoke(hit.Plugin);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!scrolling) return;
            SetScrollPosition(scrollPosition - e.Delta / 120 * RowHeight * 3);
        }

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys k && (k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right || k == Keys.Space || k == Keys.Enter) || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (hits.Count == 0) return;
            int index = focus == null ? -1 : hits.IndexOf(focus);
            switch (e.KeyCode)
            {
                case Keys.Down: case Keys.Right: SetFocus(hits[Math.Min(hits.Count - 1, index + 1)]); e.Handled = true; break;
                case Keys.Up: case Keys.Left: SetFocus(hits[Math.Max(0, index - 1)]); e.Handled = true; break;
                case Keys.Space: case Keys.Enter: if (focus != null) Activate(focus); e.Handled = true; break;
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (focus == null && hits.Count > 0) focus = hits[0]; Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
}
