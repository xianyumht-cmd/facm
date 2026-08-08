using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FACM.Services;

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
                if (!Apply(menu)) continue;

                AppliedHandles.Add(handle);
                menu.FormClosed += delegate { AppliedHandles.Remove(handle); };
            }
        }

        private static bool Apply(CompactMenuForm menu)
        {
            var owner = Application.OpenForms.OfType<MainForm>().FirstOrDefault(form => !form.IsDisposed);
            if (owner == null) return false;

            var bottomButtons = menu.Controls
                .Cast<Control>()
                .Where(IsCompactMenuButton)
                .Where(control => control.Top >= menu.Height * 0.79 && control.Top <= menu.Height * 0.92)
                .OrderBy(control => control.Left)
                .ToList();

            // Future/native layouts may already contain all five actions. Do not inject duplicates.
            if (bottomButtons.Count >= 5) return true;
            if (bottomButtons.Count != 3) return false;

            var createButton = typeof(CompactMenuForm).GetMethod(
                "CreateButton",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (createButton == null) return false;

            var logButton = bottomButtons[0];
            var themeButton = bottomButtons[1];
            var exitButton = bottomButtons[2];
            themeButton.Text = "面板主题";

            var petButton = createButton.Invoke(
                menu,
                new object[] { "桌面宠物", new Rectangle(0, 0, 72, 40), false }) as Control;
            var mayhemButton = createButton.Invoke(
                menu,
                new object[] { "海斗排行榜", new Rectangle(0, 0, 72, 40), false }) as Control;
            if (petButton == null || mayhemButton == null)
            {
                if (petButton != null) petButton.Dispose();
                if (mayhemButton != null) mayhemButton.Dispose();
                return false;
            }

            petButton.Click += delegate { owner.OpenPetSelector(); };
            mayhemButton.Click += delegate { owner.OpenMayhemLookup(); };
            menu.Controls.Add(petButton);
            menu.Controls.Add(mayhemButton);

            var ordered = new[] { logButton, themeButton, petButton, mayhemButton, exitButton };
            var margin = Math.Max(10, (int)Math.Round(menu.Width * 16D / 420D));
            var gap = Math.Max(4, (int)Math.Round(menu.Width * 7D / 420D));
            var available = menu.ClientSize.Width - margin * 2 - gap * 4;
            var width = Math.Max(58, available / 5);
            var y = bottomButtons.Min(control => control.Top);
            var height = bottomButtons.Max(control => control.Height);

            for (var index = 0; index < ordered.Length; index++)
            {
                var control = ordered[index];
                control.Location = new Point(margin + index * (width + gap), y);
                control.Size = new Size(width, height);
            }

            return true;
        }

        private static bool IsCompactMenuButton(Control control)
        {
            if (control == null) return false;
            var type = control.GetType();
            return type.DeclaringType == typeof(CompactMenuForm) &&
                   string.Equals(type.Name, "ThemedButton", StringComparison.Ordinal);
        }
    }
}
