using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM
{
    internal static class CompactMenuEnhancer
    {
        private static readonly HashSet<IntPtr> AppliedHandles = new HashSet<IntPtr>();
        private static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            UiTextRuntime.Install();
            Application.Idle += ApplyToOpenForms;
        }

        private static void ApplyToOpenForms(object sender, EventArgs e)
        {
            foreach (Form form in Application.OpenForms)
            {
                var menu = form as CompactMenuForm;
                if (menu == null || menu.IsDisposed || !menu.IsHandleCreated) continue;

                var handle = menu.Handle;
                if (AppliedHandles.Contains(handle)) continue;

                Apply(menu);
                AppliedHandles.Add(handle);
                menu.FormClosed += delegate { AppliedHandles.Remove(handle); };
            }
        }

        private static void Apply(CompactMenuForm menu)
        {
            var owner = menu.Owner as MainForm;
            if (owner == null) return;

            var bottomButtons = menu.Controls
                .OfType<Button>()
                .Where(button => button.Top >= menu.Height * 0.79 && button.Top <= menu.Height * 0.92)
                .OrderBy(button => button.Left)
                .ToList();
            if (bottomButtons.Count < 3) return;

            var logButton = bottomButtons[0];
            var themeButton = bottomButtons[1];
            var exitButton = bottomButtons[bottomButtons.Count - 1];
            themeButton.Text = "面板主题";

            var petButton = CloneButton(logButton, "桌面宠物");
            petButton.Click += delegate { owner.OpenPetSelector(); };
            var mayhemButton = CloneButton(logButton, "海斗排行");
            mayhemButton.Click += delegate { owner.OpenMayhemLookup(); };

            menu.Controls.Add(petButton);
            menu.Controls.Add(mayhemButton);

            var ordered = new[] { logButton, themeButton, petButton, mayhemButton, exitButton };
            var margin = Math.Max(10, (int)Math.Round(menu.Width * 16D / 420D));
            var gap = Math.Max(4, (int)Math.Round(menu.Width * 7D / 420D));
            var available = menu.ClientSize.Width - margin * 2 - gap * 4;
            var width = Math.Max(58, available / 5);
            var y = bottomButtons.Min(button => button.Top);
            var height = bottomButtons.Max(button => button.Height);
            var settings = AppSettings.Load();
            var theme = ThemeCatalog.Get(settings.ThemeId);

            for (var index = 0; index < ordered.Length; index++)
            {
                var button = ordered[index];
                button.Location = new Point(margin + index * (width + gap), y);
                button.Size = new Size(width, height);
                button.Font = new Font(theme.FontName, Math.Max(7F, menu.Height / 680F * 7.6F), FontStyle.Bold);
                ApplyShape(button, theme);
            }
        }

        private static Button CloneButton(Button source, string text)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = source.FlatStyle,
                BackColor = source.BackColor,
                ForeColor = source.ForeColor,
                Font = source.Font,
                Cursor = Cursors.Hand,
                TabStop = false,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = source.FlatAppearance.BorderColor;
            button.FlatAppearance.BorderSize = source.FlatAppearance.BorderSize;
            button.FlatAppearance.MouseOverBackColor = source.FlatAppearance.MouseOverBackColor;
            button.FlatAppearance.MouseDownBackColor = source.FlatAppearance.MouseDownBackColor;
            return button;
        }

        private static void ApplyShape(Control control, ThemeDefinition theme)
        {
            if (control.Width <= 0 || control.Height <= 0) return;
            using (var path = CreatePath(
                new Rectangle(0, 0, control.Width, control.Height),
                Math.Max(2, theme.ButtonRadius),
                theme.UsesAngularCorners))
            {
                var old = control.Region;
                control.Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        private static GraphicsPath CreatePath(Rectangle bounds, int radius, bool angular)
        {
            var path = new GraphicsPath();
            if (angular)
            {
                var cut = Math.Min(Math.Max(4, radius + 3), Math.Min(bounds.Width, bounds.Height) / 3);
                path.AddPolygon(new[]
                {
                    new Point(bounds.Left + cut, bounds.Top),
                    new Point(bounds.Right - cut, bounds.Top),
                    new Point(bounds.Right, bounds.Top + cut),
                    new Point(bounds.Right, bounds.Bottom - cut),
                    new Point(bounds.Right - cut, bounds.Bottom),
                    new Point(bounds.Left + cut, bounds.Bottom),
                    new Point(bounds.Left, bounds.Bottom - cut),
                    new Point(bounds.Left, bounds.Top + cut)
                });
                path.CloseFigure();
                return path;
            }

            var diameter = Math.Min(Math.Max(2, radius * 2), Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
