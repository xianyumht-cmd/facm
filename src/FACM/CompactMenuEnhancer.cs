using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

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
            var bottomButtons = menu.Controls
                .OfType<Button>()
                .Where(button => button.Top >= 570 && button.Top <= 590)
                .OrderBy(button => button.Left)
                .ToList();

            if (bottomButtons.Count < 4) return;

            var informationButton = bottomButtons[1];
            menu.Controls.Remove(informationButton);
            informationButton.Dispose();

            ResizeButton(bottomButtons[0], 16, 122);
            ResizeButton(bottomButtons[2], 149, 122);
            ResizeButton(bottomButtons[3], 282, 122);
        }

        private static void ResizeButton(Button button, int left, int width)
        {
            button.Location = new Point(left, 578);
            button.Size = new Size(width, 40);
        }
    }
}
