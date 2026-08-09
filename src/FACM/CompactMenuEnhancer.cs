using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FACM.Services;

namespace FACM
{
    internal static class CompactMenuEnhancer
    {
        private const int WmPaint = 0x000F;
        private const int WmShowWindow = 0x0018;
        private static readonly HashSet<IntPtr> AppliedHandles = new HashSet<IntPtr>();
        private static readonly Dictionary<IntPtr, OutsideClickWatcher> OutsideWatchers = new Dictionary<IntPtr, OutsideClickWatcher>();
        private static readonly ShowMessageFilter MessageFilter = new ShowMessageFilter();
        private static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            UiTextRuntime.Install();

            // The old implementation waited for Application.Idle and moved custom-painted buttons
            // after the form had already been shown. That left stale pixels until each button happened
            // to repaint on hover. WM_SHOWWINDOW is useful when it reaches the message filter, while the
            // first WM_PAINT is the hard boundary: finish the compatibility layout before that paint is
            // dispatched. Idle remains only as a safety fallback for unusual handles.
            Application.AddMessageFilter(MessageFilter);
            Application.Idle += ApplyToOpenForms;
            Application.ApplicationExit += delegate
            {
                try { Application.RemoveMessageFilter(MessageFilter); } catch { }
                foreach (var watcher in OutsideWatchers.Values.ToArray()) watcher.Dispose();
                OutsideWatchers.Clear();
                AppliedHandles.Clear();
            };
        }

        internal static bool ApplyForSmokeTest(CompactMenuForm menu)
        {
            return Apply(menu);
        }

        private static void ApplyToOpenForms(object sender, EventArgs e)
        {
            foreach (Form form in Application.OpenForms)
            {
                var menu = form as CompactMenuForm;
                if (menu == null || menu.IsDisposed || !menu.IsHandleCreated) continue;
                Apply(menu);
            }
        }

        private static bool TryApplyHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return false;
            var menu = Control.FromHandle(handle) as CompactMenuForm;
            return menu != null && !menu.IsDisposed && Apply(menu);
        }

        private static bool Apply(CompactMenuForm menu)
        {
            if (menu == null || menu.IsDisposed) return false;
            var handle = menu.IsHandleCreated ? menu.Handle : IntPtr.Zero;
            if (handle != IntPtr.Zero && AppliedHandles.Contains(handle)) return true;

            var owner = Application.OpenForms.OfType<MainForm>().FirstOrDefault(form => !form.IsDisposed);
            if (owner == null) return false;

            menu.SuspendLayout();
            try
            {
                var bottomButtons = menu.Controls
                    .Cast<Control>()
                    .Where(IsCompactMenuButton)
                    .Where(control => control.Top >= menu.Height * 0.79 && control.Top <= menu.Height * 0.92)
                    .OrderBy(control => control.Left)
                    .ToList();

                if (bottomButtons.Count == 3)
                {
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
                }
                else if (bottomButtons.Count < 5)
                {
                    return false;
                }
            }
            finally
            {
                menu.ResumeLayout(true);
            }

            menu.PerformLayout();
            menu.Invalidate(true);
            if (menu.IsHandleCreated)
            {
                handle = menu.Handle;
                AppliedHandles.Add(handle);
                AttachOutsideClickWatcher(menu, handle);
                menu.FormClosed += delegate
                {
                    AppliedHandles.Remove(handle);
                    OutsideClickWatcher watcher;
                    if (OutsideWatchers.TryGetValue(handle, out watcher))
                    {
                        OutsideWatchers.Remove(handle);
                        watcher.Dispose();
                    }
                };

                // Make the first frame deterministic even when a theme uses custom painting.
                try
                {
                    menu.BeginInvoke(new Action(delegate
                    {
                        if (!menu.IsDisposed) menu.Invalidate(true);
                    }));
                }
                catch { }
            }
            return true;
        }

        private static void AttachOutsideClickWatcher(CompactMenuForm menu, IntPtr handle)
        {
            if (OutsideWatchers.ContainsKey(handle)) return;
            OutsideWatchers[handle] = new OutsideClickWatcher(menu);
        }

        private static bool IsCompactMenuButton(Control control)
        {
            if (control == null) return false;
            var type = control.GetType();
            return type.DeclaringType == typeof(CompactMenuForm) &&
                   string.Equals(type.Name, "ThemedButton", StringComparison.Ordinal);
        }

        private sealed class ShowMessageFilter : IMessageFilter
        {
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg == WmShowWindow || m.Msg == WmPaint) TryApplyHandle(m.HWnd);
                return false;
            }
        }

        private sealed class OutsideClickWatcher : IDisposable
        {
            private const int VkLButton = 0x01;
            private static readonly FieldInfo DialogOpenField = typeof(CompactMenuForm).GetField(
                "_dialogOpen",
                BindingFlags.Instance | BindingFlags.NonPublic);

            private readonly CompactMenuForm _menu;
            private readonly Timer _timer;
            private bool _armed;
            private bool _wasDown;

            public OutsideClickWatcher(CompactMenuForm menu)
            {
                _menu = menu;
                _timer = new Timer { Interval = 40 };
                _timer.Tick += Tick;
                _timer.Start();
            }

            private void Tick(object sender, EventArgs e)
            {
                if (_menu.IsDisposed)
                {
                    Dispose();
                    return;
                }
                if (!_menu.Visible) return;

                var down = (GetAsyncKeyState(VkLButton) & 0x8000) != 0;

                // A menu may be opened by the very click we are currently observing (especially when
                // PetHost reports the click over IPC). Do not treat that same press as an outside click.
                if (!_armed)
                {
                    if (!down)
                    {
                        _armed = true;
                        _wasDown = false;
                    }
                    return;
                }

                if (down && !_wasDown && !IsDialogOpen())
                {
                    var cursor = Cursor.Position;
                    if (!_menu.Bounds.Contains(cursor))
                    {
                        _timer.Stop();
                        _menu.Close();
                        _wasDown = down;
                        return;
                    }
                }
                _wasDown = down;
            }

            private bool IsDialogOpen()
            {
                try
                {
                    return DialogOpenField != null && (bool)DialogOpenField.GetValue(_menu);
                }
                catch
                {
                    return false;
                }
            }

            public void Dispose()
            {
                try { _timer.Stop(); } catch { }
                try { _timer.Dispose(); } catch { }
            }

            [DllImport("user32.dll")]
            private static extern short GetAsyncKeyState(int virtualKey);
        }
    }
}
