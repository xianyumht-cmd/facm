using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FACM.League
{
    /// <summary>
    /// Lightweight visual pass for League surfaces. This deliberately avoids desktop capture,
    /// undocumented acrylic APIs and per-frame blur so the WinForms UI stays responsive on
    /// Windows 10 while still reading as a softer, glass-like surface.
    /// </summary>
    internal static class LeagueSoftGlassSkin
    {
        public static T Apply<T>(T form) where T : Form
        {
            if (form == null) return null;
            InstallRecursive(form);
            return form;
        }

        private static void InstallRecursive(Control control)
        {
            if (control == null || control.IsDisposed) return;

            Soften(control);
            control.ControlAdded -= HandleControlAdded;
            control.ControlAdded += HandleControlAdded;

            foreach (Control child in control.Controls)
                InstallRecursive(child);
        }

        private static void HandleControlAdded(object sender, ControlEventArgs e)
        {
            InstallRecursive(e.Control);
        }

        private static void Soften(Control control)
        {
            var button = control as Button;
            if (button != null)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Blend(button.BackColor, Color.FromArgb(122, 151, 194), 0.34F);
                button.FlatAppearance.MouseOverBackColor = Blend(button.BackColor, Color.White, 0.08F);
                button.FlatAppearance.MouseDownBackColor = Blend(button.BackColor, Color.White, 0.13F);
                Round(button, button.Height >= 44 ? 12 : 10);
                return;
            }

            var list = control as ListView;
            if (list != null)
            {
                list.BorderStyle = BorderStyle.None;
                list.BackColor = Mist(list.BackColor, 0.05F);
                return;
            }

            var panel = control as Panel;
            if (panel != null && panel.BackColor != Color.Transparent)
            {
                panel.BackColor = Mist(panel.BackColor, 0.035F);
                return;
            }

            var flow = control as FlowLayoutPanel;
            if (flow != null && flow.BackColor != Color.Transparent)
                flow.BackColor = Mist(flow.BackColor, 0.035F);
        }

        private static Color Mist(Color source, float amount)
        {
            if (source == Color.Transparent || source.A == 0) return source;
            var brightness = (source.R + source.G + source.B) / 3;
            return brightness < 150
                ? Blend(source, Color.FromArgb(217, 229, 247), amount)
                : Blend(source, Color.White, amount * 0.45F);
        }

        private static Color Blend(Color source, Color target, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            return Color.FromArgb(
                source.A,
                source.R + (int)Math.Round((target.R - source.R) * amount),
                source.G + (int)Math.Round((target.G - source.G) * amount),
                source.B + (int)Math.Round((target.B - source.B) * amount));
        }

        private static void Round(Control control, int radius)
        {
            if (control.Width <= 1 || control.Height <= 1 || radius <= 0) return;
            var diameter = Math.Min(radius * 2, Math.Min(control.Width, control.Height));
            using (var path = new GraphicsPath())
            {
                var right = control.Width - 1;
                var bottom = control.Height - 1;
                path.AddArc(0, 0, diameter, diameter, 180, 90);
                path.AddArc(right - diameter, 0, diameter, diameter, 270, 90);
                path.AddArc(right - diameter, bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(0, bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();

                var old = control.Region;
                control.Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }
    }
}
