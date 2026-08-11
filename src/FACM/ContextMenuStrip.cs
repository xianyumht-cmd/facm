using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FACM
{
    /// <summary>
    /// FACM's tray/context menu can be opened from FACM.PetHost, which is a separate WPF process.
    /// WinForms' normal AutoClose is not reliable when the next click is delivered to that other
    /// process or to the desktop, so watch mouse-button transitions only while this menu is visible.
    /// </summary>
    internal sealed class ContextMenuStrip : System.Windows.Forms.ContextMenuStrip
    {
        private const int VkLButton = 0x01;
        private const int VkRButton = 0x02;
        private const int VkMButton = 0x04;

        private readonly Timer _outsideClickTimer;
        private bool _leftWasDown;
        private bool _rightWasDown;
        private bool _middleWasDown;
        private bool _disposeStarted;

        public ContextMenuStrip()
        {
            AutoClose = true;
            _outsideClickTimer = new Timer { Interval = 35 };
            _outsideClickTimer.Tick += HandleOutsideClickTick;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (_disposeStarted || IsDisposed || Disposing) return;
            _leftWasDown = IsButtonDown(VkLButton);
            _rightWasDown = IsButtonDown(VkRButton);
            _middleWasDown = IsButtonDown(VkMButton);
            _outsideClickTimer.Start();
        }

        protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
        {
            if (!_disposeStarted)
                _outsideClickTimer.Stop();
            base.OnClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposeStarted)
            {
                _disposeStarted = true;
                _outsideClickTimer.Stop();
                _outsideClickTimer.Tick -= HandleOutsideClickTick;
                _outsideClickTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private void HandleOutsideClickTick(object sender, EventArgs e)
        {
            // A WM_TIMER message can already be queued when a dropdown is closed/disposed. Never let
            // that late tick touch ToolStripDropDown state, because WinForms may otherwise recreate a
            // handle for an object whose disposal has already started.
            if (_disposeStarted || IsDisposed || Disposing) return;

            bool visible;
            try { visible = Visible; }
            catch (ObjectDisposedException) { return; }
            if (!visible) return;

            var leftDown = IsButtonDown(VkLButton);
            var rightDown = IsButtonDown(VkRButton);
            var middleDown = IsButtonDown(VkMButton);
            var newPress = (leftDown && !_leftWasDown) ||
                           (rightDown && !_rightWasDown) ||
                           (middleDown && !_middleWasDown);

            _leftWasDown = leftDown;
            _rightWasDown = rightDown;
            _middleWasDown = middleDown;

            if (!newPress) return;

            Point cursor;
            try { cursor = Control.MousePosition; }
            catch { return; }

            if (_disposeStarted || IsDisposed || Disposing) return;

            // A submenu is a separate top-level ToolStripDropDown window, so its screen rectangle is
            // outside the root ContextMenuStrip.Bounds. Treat every visible descendant dropdown as
            // part of this menu tree; otherwise clicking "桌面形态" children is misclassified as an
            // outside click and the root closes before the child receives MouseUp/Click.
            if (ContainsScreenPoint(this, cursor)) return;

            try
            {
                Close(ToolStripDropDownCloseReason.AppClicked);
            }
            catch (ObjectDisposedException)
            {
                // Closing the owner application can dispose the dropdown between the physical mouse
                // sample above and this call. At that point the desired state is already closed.
            }
        }

        private static bool ContainsScreenPoint(ToolStrip strip, Point cursor)
        {
            if (strip == null || strip.IsDisposed || strip.Disposing || !strip.Visible) return false;

            try
            {
                var bounds = strip.RectangleToScreen(strip.ClientRectangle);
                if (bounds.Contains(cursor)) return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            foreach (ToolStripItem item in strip.Items)
            {
                var dropDownItem = item as ToolStripDropDownItem;
                if (dropDownItem == null || !dropDownItem.HasDropDownItems) continue;

                ToolStripDropDown child;
                try { child = dropDownItem.DropDown; }
                catch (ObjectDisposedException) { continue; }
                catch (InvalidOperationException) { continue; }

                if (child != null && ContainsScreenPoint(child, cursor)) return true;
            }

            return false;
        }

        private static bool IsButtonDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
