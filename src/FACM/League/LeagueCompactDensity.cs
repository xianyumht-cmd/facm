using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FACM.League
{
    /// <summary>
    /// Applies a conservative compact-density pass to League WinForms surfaces.
    /// The goal is to remove wasted chrome and vertical air without shrinking normal body text
    /// or rewriting the business forms. Large visual/data regions are preserved while toolbars,
    /// section rows, paddings and fixed navigation rails become denser.
    /// </summary>
    internal static class LeagueCompactDensity
    {
        public static T Apply<T>(T form) where T : Form
        {
            if (form == null || form.IsDisposed) return form;

            CompactFormBounds(form);
            CompactRecursive(form);

            var hub = form as LeagueHubForm;
            if (hub != null)
                CompactHub(hub);

            return form;
        }

        /// <summary>
        /// Handles controls created after the initial form construction, such as Hub context
        /// actions and rebuilt sub-navigation buttons.
        /// </summary>
        public static void ApplyAdded(Control control)
        {
            if (control == null || control.IsDisposed) return;
            CompactRecursive(control);

            var flow = control.Parent as FlowLayoutPanel;
            if (flow != null && flow.Dock == DockStyle.Top && flow.Height >= 50 && flow.Height <= 78)
            {
                flow.Height = Scale(flow.Height, 0.82F, 38);
                flow.Padding = ScalePadding(flow.Padding, 0.76F);
            }
        }

        private static void CompactFormBounds(Form form)
        {
            if (form == null) return;

            if (form.ClientSize.Width >= 680 && form.ClientSize.Height >= 600)
            {
                form.ClientSize = new Size(
                    Scale(form.ClientSize.Width, 0.94F, 640),
                    Scale(form.ClientSize.Height, 0.90F, 560));
            }

            if (form.MinimumSize.Width >= 640 && form.MinimumSize.Height >= 560)
            {
                form.MinimumSize = new Size(
                    Scale(form.MinimumSize.Width, 0.94F, 620),
                    Scale(form.MinimumSize.Height, 0.90F, 540));
            }
        }

        private static void CompactRecursive(Control control)
        {
            if (control == null || control.IsDisposed) return;

            CompactControl(control);
            foreach (Control child in control.Controls)
                CompactRecursive(child);
        }

        private static void CompactControl(Control control)
        {
            if (control == null || control.IsDisposed) return;

            var table = control as TableLayoutPanel;
            if (table != null)
            {
                CompactTable(table);
            }
            else
            {
                var flow = control as FlowLayoutPanel;
                if (flow != null)
                    CompactFlow(flow);
                else
                {
                    var button = control as Button;
                    if (button != null)
                        CompactButton(button);
                    else
                    {
                        var label = control as Label;
                        if (label != null)
                            CompactLabel(label);
                        else
                        {
                            var panel = control as Panel;
                            if (panel != null)
                                panel.Padding = ScalePadding(panel.Padding, 0.78F);
                        }
                    }
                }
            }

            if (!(control is Form))
                control.Margin = ScaleMargin(control.Margin);
        }

        private static void CompactTable(TableLayoutPanel table)
        {
            table.Padding = ScalePadding(table.Padding, 0.72F);

            foreach (RowStyle row in table.RowStyles)
            {
                if (row.SizeType != SizeType.Absolute) continue;
                var height = row.Height;
                if (height >= 30F && height <= 68F)
                    row.Height = Scale(height, 0.82F, 26F);
                else if (height > 68F && height <= 120F)
                    row.Height = Scale(height, 0.90F, 58F);
            }

            foreach (ColumnStyle column in table.ColumnStyles)
            {
                if (column.SizeType != SizeType.Absolute) continue;
                var width = column.Width;
                if (width >= 72F && width <= 220F)
                    column.Width = Scale(width, 0.92F, 64F);
            }
        }

        private static void CompactFlow(FlowLayoutPanel flow)
        {
            flow.Padding = ScalePadding(flow.Padding, 0.76F);
            if (flow.Dock == DockStyle.Top && flow.Height >= 50 && flow.Height <= 78)
                flow.Height = Scale(flow.Height, 0.82F, 38);
        }

        private static void CompactButton(Button button)
        {
            button.Padding = ScalePadding(button.Padding, 0.78F);

            if (button.Dock != DockStyle.Fill && !button.AutoSize)
            {
                if (button.Height > 34 && button.Height <= 64)
                    button.Height = Scale(button.Height, 0.84F, 30);
                else if (button.Height > 64)
                    button.Height = Scale(button.Height, 0.92F, 48);

                if (button.Width > 150 && button.Width <= 230)
                    button.Width = Scale(button.Width, 0.90F, 112);
            }
        }

        private static void CompactLabel(Label label)
        {
            if (label.Font != null && label.Font.Size >= 16F)
            {
                var size = Math.Max(14.5F, label.Font.Size - 1.5F);
                var old = label.Font;
                label.Font = new Font(old.FontFamily, size, old.Style, old.Unit);
                old.Dispose();
            }
        }

        private static void CompactHub(LeagueHubForm hub)
        {
            hub.ClientSize = new Size(1280, 760);
            hub.MinimumSize = new Size(980, 640);

            var direct = hub.Controls.Cast<Control>().ToArray();
            var header = direct.OfType<Panel>().FirstOrDefault(item => item.Dock == DockStyle.Top);
            var sidebar = direct.OfType<Panel>().FirstOrDefault(item => item.Dock == DockStyle.Left);
            var body = direct.OfType<Panel>().FirstOrDefault(item => item.Dock == DockStyle.Fill);

            if (header != null)
            {
                header.Height = 68;
                header.Padding = Padding.Empty;

                var labels = header.Controls.OfType<Label>().OrderBy(item => item.Top).ToArray();
                if (labels.Length > 0)
                {
                    var title = labels[0];
                    title.Location = new Point(24, 8);
                    title.Height = 27;
                    if (title.Font != null && title.Font.Size > 15.5F)
                    {
                        var old = title.Font;
                        title.Font = new Font(old.FontFamily, 15.5F, old.Style, old.Unit);
                        old.Dispose();
                    }
                }
                if (labels.Length > 1)
                {
                    var hint = labels[1];
                    hint.Location = new Point(24, 36);
                    hint.Height = 20;
                }
            }

            if (sidebar != null)
            {
                sidebar.Width = 148;
                sidebar.Padding = new Padding(10, 12, 10, 10);
                var buttons = sidebar.Controls.OfType<Button>().OrderBy(item => item.Top).ToArray();
                var top = 12;
                foreach (var button in buttons)
                {
                    button.Location = new Point(10, top);
                    button.Size = new Size(128, 40);
                    button.Padding = new Padding(10, 0, 6, 0);
                    top += 48;
                }
            }

            if (body == null) return;

            var contextRail = body.Controls.OfType<Panel>().FirstOrDefault(item => item.Dock == DockStyle.Right);
            var mainArea = body.Controls.OfType<Panel>().FirstOrDefault(item => item.Dock == DockStyle.Fill);

            if (contextRail != null)
            {
                contextRail.Width = 204;
                contextRail.Padding = new Padding(12, 12, 12, 12);

                foreach (var label in contextRail.Controls.OfType<Label>().Where(item => item.Dock == DockStyle.Top))
                {
                    if (label.Font != null && label.Font.Size >= 11F)
                        label.Height = 26;
                    else if (label.Font != null && label.Font.Bold)
                        label.Height = 22;
                    else
                        label.Height = 46;
                }

                var actions = contextRail.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
                if (actions != null)
                    actions.Padding = new Padding(0, 6, 0, 0);
            }

            if (mainArea != null)
            {
                var subnav = mainArea.Controls.OfType<FlowLayoutPanel>().FirstOrDefault(item => item.Dock == DockStyle.Top);
                if (subnav != null)
                {
                    subnav.Height = 44;
                    subnav.Padding = new Padding(18, 6, 14, 4);
                }
            }
        }

        private static Padding ScalePadding(Padding value, float factor)
        {
            return new Padding(
                Scale(value.Left, factor, 0),
                Scale(value.Top, factor, 0),
                Scale(value.Right, factor, 0),
                Scale(value.Bottom, factor, 0));
        }

        private static Padding ScaleMargin(Padding value)
        {
            return new Padding(
                Scale(value.Left, 0.86F, 0),
                Scale(value.Top, 0.68F, 0),
                Scale(value.Right, 0.86F, 0),
                Scale(value.Bottom, 0.68F, 0));
        }

        private static int Scale(int value, float factor, int minimum)
        {
            if (value <= 0) return value;
            return Math.Max(minimum, (int)Math.Round(value * factor));
        }

        private static float Scale(float value, float factor, float minimum)
        {
            if (value <= 0F) return value;
            return Math.Max(minimum, (float)Math.Round(value * factor, 1));
        }
    }
}
