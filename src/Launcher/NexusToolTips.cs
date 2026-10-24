using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace AOLauncher
{
    internal static class NexusToolTips
    {
        private static readonly ConditionalWeakTable<Control, Binding> bindings =
            new ConditionalWeakTable<Control, Binding>();

        internal static void Attach(TreeView tree)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (tree.IsDisposed) return;
            bindings.GetValue(tree, control => new Binding((TreeView)control));
            tree.ShowNodeToolTips = false;
        }

        internal static void Attach(DataGridView grid)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (grid.IsDisposed) return;
            bindings.GetValue(grid, control => new Binding((DataGridView)control));
            grid.ShowCellToolTips = false;
        }

        private sealed class Binding : IDisposable
        {
            private const TextFormatFlags TextFlags = TextFormatFlags.WordBreak |
                TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
            private readonly Control owner;
            private readonly TreeView tree;
            private readonly DataGridView grid;
            private readonly ToolTip tip;
            private object hoverItem;
            private string hoverText = string.Empty;
            private bool disposed;

            internal Binding(TreeView control) : this((Control)control)
            {
                tree = control;
                tree.AfterExpand += TreeLayoutChanged;
                tree.AfterCollapse += TreeLayoutChanged;
            }

            internal Binding(DataGridView control) : this((Control)control)
            {
                grid = control;
                grid.Scroll += GridScrolled;
                grid.ColumnWidthChanged += GridColumnChanged;
            }

            private Binding(Control control)
            {
                owner = control;
                // Let WinForms retain its normal initial, reshow and autopop delays.
                // Updating only on a different item avoids restarting them on every pixel.
                tip = new ToolTip {
                    OwnerDraw = true, BackColor = Theme.SurfaceRaised,
                    ForeColor = Theme.Foreground, UseAnimation = false, UseFading = false
                };
                tip.Popup += Popup;
                tip.Draw += Draw;
                owner.MouseMove += MouseMoved;
                owner.MouseLeave += Clear;
                owner.MouseDown += PointerAction;
                owner.MouseWheel += PointerAction;
                owner.KeyDown += KeyAction;
                owner.SizeChanged += Clear;
                owner.FontChanged += Clear;
                owner.VisibleChanged += Clear;
                owner.EnabledChanged += Clear;
                owner.HandleDestroyed += Clear;
                owner.Disposed += OwnerDisposed;
            }

            private string TextAt(Point location, out object item)
            {
                item = null;
                if (!owner.ClientRectangle.Contains(location)) return string.Empty;
                if (tree != null)
                {
                    TreeNode node = tree.GetNodeAt(location);
                    item = node;
                    return node?.ToolTipText ?? string.Empty;
                }

                DataGridView.HitTestInfo hit = grid.HitTest(location.X, location.Y);
                if (hit.ColumnIndex < 0 || hit.ColumnIndex >= grid.Columns.Count) return string.Empty;
                if (hit.Type == DataGridViewHitTestType.ColumnHeader)
                {
                    DataGridViewColumn column = grid.Columns[hit.ColumnIndex];
                    item = column;
                    return column.ToolTipText ?? string.Empty;
                }
                if (hit.Type == DataGridViewHitTestType.Cell && hit.RowIndex >= 0 && hit.RowIndex < grid.Rows.Count)
                {
                    DataGridViewCell cell = grid.Rows[hit.RowIndex].Cells[hit.ColumnIndex];
                    item = cell;
                    string explicitTip = cell.ToolTipText;
                    return string.IsNullOrEmpty(explicitTip) ? ClippedCellText(cell) : explicitTip;
                }
                return string.Empty;
            }

            private string ClippedCellText(DataGridViewCell cell)
            {
                if (cell.IsInEditMode) return string.Empty;
                string value = cell.FormattedValue as string;
                if (string.IsNullOrWhiteSpace(value)) return string.Empty;
                Rectangle visible = grid.GetCellDisplayRectangle(cell.ColumnIndex, cell.RowIndex, true);
                if (visible.Width <= 0 || visible.Height <= 0) return string.Empty;
                Size preferred = cell.PreferredSize;
                bool clipped = preferred.Width > visible.Width || preferred.Height > visible.Height;

                DataGridViewCellStyle style = cell.InheritedStyle;
                if (clipped && style.WrapMode == DataGridViewTriState.True && cell is DataGridViewTextBoxCell)
                {
                    // Unconstrained PreferredSize chooses a compact aspect ratio, not
                    // the existing row's wrapped layout. Measure again at its real width
                    // so a fully visible, multiline value does not get a redundant tip.
                    DataGridViewAdvancedBorderStyle borders = grid.AdvancedCellBorderStyle;
                    int horizontal = BorderWidth(borders.Left) + BorderWidth(borders.Right) + style.Padding.Horizontal;
                    int vertical = BorderWidth(borders.Top) + BorderWidth(borders.Bottom) + style.Padding.Vertical;
                    Size text = TextRenderer.MeasureText(value, style.Font ?? grid.Font,
                        new Size(Math.Max(1, visible.Width - horizontal), int.MaxValue),
                        TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                    // TextBox cells reserve one text-margin pixel above and below wrapped text.
                    clipped = text.Width + horizontal > visible.Width || text.Height + vertical + 2 > visible.Height;
                }
                return clipped ? value : string.Empty;
            }

            private static int BorderWidth(DataGridViewAdvancedCellBorderStyle border)
            {
                if (border == DataGridViewAdvancedCellBorderStyle.None) return 0;
                return border == DataGridViewAdvancedCellBorderStyle.InsetDouble ||
                    border == DataGridViewAdvancedCellBorderStyle.OutsetDouble ? 2 : 1;
            }

            private void MouseMoved(object sender, MouseEventArgs e)
            {
                if (disposed) return;
                object item;
                string text = TextAt(e.Location, out item);
                if (ReferenceEquals(item, hoverItem) && text == hoverText) return;
                hoverItem = item;
                hoverText = text;
                tip.SetToolTip(owner, text);
            }

            private void Popup(object sender, PopupEventArgs e)
            {
                if (disposed || !owner.Visible || !owner.Enabled)
                {
                    e.Cancel = true;
                    return;
                }
                // A refresh, collapse or scroll may have replaced the hovered row while
                // the native hover timer was running. Never display the previous row's tip.
                object item;
                string current = TextAt(owner.PointToClient(Control.MousePosition), out item);
                if (current.Length == 0 ||
                    !ReferenceEquals(item, hoverItem) || current != hoverText)
                {
                    e.Cancel = true;
                    return;
                }
                float scale = Math.Max(owner.DeviceDpi / 96F, owner.Font.SizeInPoints / 10F);
                int padding = Math.Max(8, (int)Math.Ceiling(10 * scale));
                Rectangle screen = Screen.FromControl(owner).WorkingArea;
                int width = Math.Max(80, Math.Min((int)(440 * scale), screen.Width - 2 * padding - 24));
                Size measured = TextRenderer.MeasureText(current, owner.Font, new Size(width, int.MaxValue), TextFlags);
                e.ToolTipSize = new Size(Math.Min(width, measured.Width) + 2 * padding,
                    Math.Min(measured.Height + 2 * padding, Math.Max(40, screen.Height - 24)));
            }

            private void Draw(object sender, DrawToolTipEventArgs e)
            {
                using (var background = new SolidBrush(Theme.SurfaceRaised))
                    e.Graphics.FillRectangle(background, e.Bounds);
                using (var border = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(border, e.Bounds.X, e.Bounds.Y,
                        Math.Max(0, e.Bounds.Width - 1), Math.Max(0, e.Bounds.Height - 1));
                float scale = Math.Max(owner.DeviceDpi / 96F, owner.Font.SizeInPoints / 10F);
                int padding = Math.Max(8, (int)Math.Ceiling(10 * scale));
                Rectangle content = Rectangle.Inflate(e.Bounds, -padding, -padding);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, owner.Font, content,
                    Theme.Foreground, Theme.SurfaceRaised, TextFlags | TextFormatFlags.EndEllipsis);
            }

            private void Clear(object sender, EventArgs e)
            {
                if (disposed) return;
                hoverItem = null;
                hoverText = string.Empty;
                tip.SetToolTip(owner, string.Empty);
            }

            private void PointerAction(object sender, MouseEventArgs e) => Clear(sender, e);
            private void KeyAction(object sender, KeyEventArgs e) => Clear(sender, e);
            private void TreeLayoutChanged(object sender, TreeViewEventArgs e) => Clear(sender, e);
            private void GridScrolled(object sender, ScrollEventArgs e) => Clear(sender, e);
            private void GridColumnChanged(object sender, DataGridViewColumnEventArgs e) => Clear(sender, e);
            private void OwnerDisposed(object sender, EventArgs e) => Dispose();

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                owner.MouseMove -= MouseMoved;
                owner.MouseLeave -= Clear;
                owner.MouseDown -= PointerAction;
                owner.MouseWheel -= PointerAction;
                owner.KeyDown -= KeyAction;
                owner.SizeChanged -= Clear;
                owner.FontChanged -= Clear;
                owner.VisibleChanged -= Clear;
                owner.EnabledChanged -= Clear;
                owner.HandleDestroyed -= Clear;
                owner.Disposed -= OwnerDisposed;
                if (tree != null)
                {
                    tree.AfterExpand -= TreeLayoutChanged;
                    tree.AfterCollapse -= TreeLayoutChanged;
                }
                if (grid != null)
                {
                    grid.Scroll -= GridScrolled;
                    grid.ColumnWidthChanged -= GridColumnChanged;
                }
                tip.Popup -= Popup;
                tip.Draw -= Draw;
                tip.Dispose();
                bindings.Remove(owner);
            }
        }
    }
}
