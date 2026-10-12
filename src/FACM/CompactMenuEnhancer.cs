using System;
using System.Collections.Generic;
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

            // Shell actions stay bounded in CompactMenuForm/MainForm. This enhancer only applies
            // presentation/runtime behavior such as the desktop launcher, hover descriptions and outside-click close.
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
                SafeApply(menu);
            }
        }

        private static bool TryApplyHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return false;
            var menu = Control.FromHandle(handle) as CompactMenuForm;
            return menu != null && !menu.IsDisposed && SafeApply(menu);
        }

        private static bool SafeApply(CompactMenuForm menu)
        {
            try
            {
                return Apply(menu);
            }
            catch (Exception exception)
            {
                // Presentation enhancers must never be able to terminate the FACM message loop.
                // DesktopLauncherEnhancer keeps legacy controls visible until its replacement is complete,
                // so a future visual regression falls back instead of becoming an application crash.
                AppLog.Error("Control center presentation enhancer failed; legacy controls remain available", exception);
                return false;
            }
        }

        private static bool Apply(CompactMenuForm menu)
        {
            if (menu == null || menu.IsDisposed) return false;
            var handle = menu.IsHandleCreated ? menu.Handle : IntPtr.Zero;
            if (handle != IntPtr.Zero && AppliedHandles.Contains(handle)) return true;
            if (Application.OpenForms.OfType<MainForm>().FirstOrDefault(form => !form.IsDisposed) == null) return false;

            HoverDescriptionEnhancer.ApplyCompactMenu(menu);
            DesktopLauncherEnhancer.Apply(menu);
            menu.PerformLayout();
            menu.Invalidate(true);
            if (!menu.IsHandleCreated) return true;

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

            try
            {
                menu.BeginInvoke(new Action(delegate
                {
                    if (!menu.IsDisposed) menu.Invalidate(true);
                }));
            }
            catch { }
            return true;
        }

        private static void AttachOutsideClickWatcher(CompactMenuForm menu, IntPtr handle)
        {
            if (OutsideWatchers.ContainsKey(handle)) return;
            OutsideWatchers[handle] = new OutsideClickWatcher(menu);
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

                // A menu may be opened by the very click we are currently observing. Arm only after
                // that click is released so the control center is not immediately closed again.
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
