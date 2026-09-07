using System;
using System.Drawing;
using System.Windows.Forms;

namespace FACM.Theming
{
    internal static class FacmControlPrimitivesSmokeTest
    {
        public static void Validate()
        {
            using (var button = new FacmActionButton())
            using (var toggle = new FacmToggleSwitch())
            using (var badge = new FacmStatusBadge())
            {
                if (!(button is Button) || !button.TabStop)
                    throw new InvalidOperationException("FACM shared action button must retain native Button keyboard/focus semantics.");
                button.Tone = FacmButtonTone.Primary;
                if (button.Tone != FacmButtonTone.Primary)
                    throw new InvalidOperationException("FACM shared action button lost its semantic tone state.");

                if (!(toggle is CheckBox))
                    throw new InvalidOperationException("FACM shared toggle must retain native CheckBox semantics.");
                var changed = 0;
                toggle.CheckedChanged += delegate { changed++; };
                toggle.Checked = true;
                if (!toggle.Checked || changed != 1)
                    throw new InvalidOperationException("FACM shared toggle no longer raises CheckedChanged exactly once for a normal state change.");

                badge.Tone = FacmStatusTone.Success;
                if (badge.Tone != FacmStatusTone.Success)
                    throw new InvalidOperationException("FACM shared status badge lost its semantic tone state.");
            }

            if (FacmDesignSystem.ControlRadius < 0 || FacmDesignSystem.CardRadius < 0 || FacmDesignSystem.WindowRadius < 0)
                throw new InvalidOperationException("FACM shared design radii must remain non-negative.");

            ValidateToggleResizeRepaint();
            ValidateWindowChromeLayout();
        }

        private static void ValidateToggleResizeRepaint()
        {
            var background = Color.FromArgb(17, 25, 47);
            using (var host = new Panel { BackColor = background, Size = new Size(600, 40) })
            using (var toggle = new FacmToggleSwitch
            {
                Checked = true,
                Text = string.Empty,
                Location = Point.Empty,
                Size = new Size(240, 32)
            })
            using (var bitmap = new Bitmap(600, 40))
            {
                host.Controls.Add(toggle);
                var unused = toggle.Handle;

                using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(background);
                toggle.DrawToBitmap(bitmap, new Rectangle(0, 0, toggle.Width, toggle.Height));

                // Probe the center of the original checked track. The first render must actually
                // paint it, then a wider layout pass must erase that old coordinate completely.
                var oldTrackCenter = new Point(217, 16);
                if (bitmap.GetPixel(oldTrackCenter.X, oldTrackCenter.Y).ToArgb() == background.ToArgb())
                    throw new InvalidOperationException("FACM toggle repaint smoke did not capture the original switch track.");

                toggle.Width = 520;
                toggle.DrawToBitmap(bitmap, new Rectangle(0, 0, toggle.Width, toggle.Height));
                if (bitmap.GetPixel(oldTrackCenter.X, oldTrackCenter.Y).ToArgb() != background.ToArgb())
                    throw new InvalidOperationException("FACM toggle left stale pixels at its previous track position after resize.");
            }
        }

        private static void ValidateWindowChromeLayout()
        {
            using (var form = new Form
            {
                ClientSize = new Size(800, 500),
                Padding = new Padding(8),
                FormBorderStyle = FormBorderStyle.Sizable,
                ControlBox = true,
                MinimizeBox = true,
                MaximizeBox = true,
                ShowInTaskbar = true
            })
            using (var page = new Panel { Dock = DockStyle.Fill })
            {
                form.Controls.Add(page);

                // Force a handle so Prepare attaches without showing a real window in CI.
                var unused = form.Handle;
                FacmWindowChrome.Prepare(form, new FacmWindowChromeOptions
                {
                    CloseOnDeactivate = false,
                    CloseOnEscape = false,
                    TitleBarHeight = 42
                });
                form.PerformLayout();

                RequireChromeSeparated(form, "initial");
                if (page.Parent == form)
                    throw new InvalidOperationException("FACM window chrome did not move page content into its reserved content host.");

                form.ClientSize = new Size(980, 660);
                form.PerformLayout();
                RequireChromeSeparated(form, "resized");
            }
        }

        private static void RequireChromeSeparated(Form form, string stage)
        {
            Rectangle title;
            Rectangle content;
            if (!FacmWindowChrome.TryGetLayoutForSmokeTest(form, out title, out content))
                throw new InvalidOperationException("FACM window chrome layout snapshot was unavailable during " + stage + " smoke validation.");
            if (title.Height < 34)
                throw new InvalidOperationException("FACM window chrome title band collapsed during " + stage + " smoke validation.");
            if (content.Top < title.Bottom)
                throw new InvalidOperationException("FACM window chrome content overlaps the title band during " + stage + " smoke validation.");
            if (content.Top != title.Bottom)
                throw new InvalidOperationException("FACM window chrome left an unexpected layout gap below the title band during " + stage + " smoke validation.");
            if (title.Left != content.Left || title.Width != content.Width)
                throw new InvalidOperationException("FACM window chrome title/content horizontal bounds diverged during " + stage + " smoke validation.");
        }
    }
}
