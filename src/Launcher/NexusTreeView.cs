using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AOLauncher
{
    // Keep the native tree's navigation, checkbox actions and accessibility, while
    // painting selected/inactive rows and glyphs with the shared application palette.
    internal sealed class NexusTreeView : TreeView
    {
        internal NexusTreeView() { DrawMode = TreeViewDrawMode.OwnerDrawAll; }
        // Optional chip at the right edge of a row (a character's plugin set). Clicking it raises
        // RightTextClicked so the owner can show a picker.
        internal Func<TreeNode, string> RightText;
        internal event Action<TreeNode, Rectangle> RightTextClicked;
        // Same width on every row so the set buttons line up.
        Rectangle ChipBounds(TreeNode node, Graphics graphics, string text)
        {
            var bounds = node.Bounds;
            int width = Math.Min(NexusChrome.Scale(this, 150), Math.Max(0, (ClientSize.Width - bounds.Left) / 2));
            int height = Math.Max(1, ItemHeight - 6);
            return new Rectangle(ClientSize.Width - width - 8, bounds.Top + (bounds.Height - height) / 2, width, height);
        }

        bool ChipAt(Point point, out TreeNode node, out Rectangle chip)
        {
            chip = Rectangle.Empty;
            node = RightText == null ? null : GetNodeAt(point);
            string text = node == null ? null : RightText(node);
            if (string.IsNullOrEmpty(text)) return false;
            using (var graphics = CreateGraphics()) chip = ChipBounds(node, graphics, text);
            return chip.Contains(point);
        }

        // The native tree runs its own selection/drag loop on a left press and swallows the
        // release, so the set button is handled here before the tree sees the click. Double
        // clicks on it are swallowed too, so they never open Edit.
        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONDBLCLK = 0x0203;
            if ((m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK) && RightText != null)
            {
                long value = m.LParam.ToInt64();
                var point = new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
                if (ChipAt(point, out var node, out var chip))
                {
                    if (!Focused) Focus();
                    if (m.Msg == WM_LBUTTONDOWN) BeginInvoke((Action)(() => RightTextClicked?.Invoke(node, chip)));
                    m.Result = IntPtr.Zero;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                // TVS_NOTOOLTIPS: ShowNodeToolTips=false disables node infotips,
                // but the native truncated-label tooltip otherwise remains active.
                parameters.Style |= 0x0080;
                return parameters;
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = HitTest(e.Location);
            Cursor = hit.Node != null && (hit.Location & (TreeViewHitTestLocations.Label | TreeViewHitTestLocations.StateImage | TreeViewHitTestLocations.PlusMinus | TreeViewHitTestLocations.Image)) != 0
                ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnDrawNode(DrawTreeNodeEventArgs e)
        {
            if (e.Node == null || e.Bounds.Height == 0) return;
            e.DrawDefault = false;
            Rectangle textBounds = e.Node.Bounds;
            var row = new Rectangle(0, e.Bounds.Top, ClientSize.Width, e.Bounds.Height);
            bool selected = e.Node == SelectedNode;
            Color background = selected ? Theme.Selection : e.Node.BackColor.IsEmpty ? BackColor : e.Node.BackColor;
            Color foreground = !Enabled ? Theme.Muted : e.Node.ForeColor.IsEmpty ? ForeColor : e.Node.ForeColor;
            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, row);

            // Native hit regions supply the glyph positions, including horizontal
            // scrolling, indentation, DPI and rows whose creation checkbox is hidden.
            int checkLeft = -1, checkRight = -1, expandLeft = -1, expandRight = -1;
            int middle = row.Top + row.Height / 2;
            for (int x = 0; x < Math.Min(ClientSize.Width, Math.Max(0, textBounds.Left)); x++)
            {
                var hit = HitTest(x, middle);
                if (hit.Node != e.Node) continue;
                if ((hit.Location & TreeViewHitTestLocations.StateImage) != 0)
                { if (checkLeft < 0) checkLeft = x; checkRight = x + 1; }
                if ((hit.Location & TreeViewHitTestLocations.PlusMinus) != 0)
                { if (expandLeft < 0) expandLeft = x; expandRight = x + 1; }
            }
            if (checkLeft >= 0 && CheckBoxes)
            {
                // Inset the artwork without shrinking the native click target.
                // A dark box and accent tick match the checkboxes in our dialogs.
                int size = Math.Max(1, Math.Min(NexusChrome.Scale(this, 13), Math.Min(row.Height - 6, checkRight - checkLeft - 2)));
                var box = new Rectangle(checkLeft + (checkRight - checkLeft - size) / 2, middle - size / 2, size, size);
                using (var brush = new SolidBrush(Theme.SurfaceRaised)) e.Graphics.FillRectangle(brush, box);
                using (var pen = new Pen(Enabled && e.Node.Checked ? Theme.Accent : Theme.Muted)) e.Graphics.DrawRectangle(pen, box.X, box.Y, box.Width - 1, box.Height - 1);
                if (e.Node.Checked)
                {
                    var state = e.Graphics.Save();
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var pen = new Pen(Enabled ? Theme.Accent : Theme.Muted, Math.Max(1.5F, size / 8F)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        e.Graphics.DrawLines(pen, new[] { new PointF(box.Left + size * .22F, box.Top + size * .48F), new PointF(box.Left + size * .42F, box.Top + size * .68F), new PointF(box.Left + size * .76F, box.Top + size * .28F) });
                    e.Graphics.Restore(state);
                }
            }
            if (expandLeft >= 0 && e.Node.Nodes.Count != 0)
            {
                // An unboxed chevron distinguishes navigation from selection.
                float radius = Math.Max(1F, Math.Min(NexusChrome.Scale(this, 4), Math.Min((expandRight - expandLeft - 4) / 2F, (row.Height - 6) / 2F)));
                float center = (expandLeft + expandRight - 1) / 2F;
                var points = e.Node.IsExpanded
                    ? new[] { new PointF(center - radius, middle - radius / 2), new PointF(center, middle + radius / 2), new PointF(center + radius, middle - radius / 2) }
                    : new[] { new PointF(center - radius / 2, middle - radius), new PointF(center + radius / 2, middle), new PointF(center - radius / 2, middle + radius) };
                var state = e.Graphics.Save();
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Enabled ? Theme.Foreground : Theme.Muted, Math.Max(1.5F, radius / 2F)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    e.Graphics.DrawLines(pen, points);
                e.Graphics.Restore(state);
            }
            var text = new Rectangle(textBounds.Left + 2, row.Top, Math.Max(1, ClientSize.Width - textBounds.Left - 5), row.Height);
            string right = RightText?.Invoke(e.Node);
            if (!string.IsNullOrEmpty(right))
            {
                var font = e.Node.NodeFont ?? Font;
                var chip = ChipBounds(e.Node, e.Graphics, right);
                using (var brush = new SolidBrush(Theme.SurfaceRaised)) e.Graphics.FillRectangle(brush, chip);
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, chip.X, chip.Y, chip.Width - 1, chip.Height - 1);
                TextRenderer.DrawText(e.Graphics, right + "  ▾", font, Rectangle.Inflate(chip, -6, 0), Theme.Foreground,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                text.Width = Math.Max(1, chip.Left - text.Left - 8);
            }
            TextRenderer.DrawText(e.Graphics, e.Node.Text, e.Node.NodeFont ?? Font, text, foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (selected && Focused && ShowFocusCues)
                using (var pen = new Pen(Theme.Accent)) e.Graphics.DrawRectangle(pen, 0, row.Top, Math.Max(0, row.Width - 1), Math.Max(0, row.Height - 1));
        }
    }
}
