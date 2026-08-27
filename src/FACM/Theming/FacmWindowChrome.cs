using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FACM.Theming
{
    internal sealed class FacmWindowChromeOptions
    {
        public bool? CloseOnDeactivate { get; set; }
        public bool? CloseOnEscape { get; set; }
        public bool? AllowResize { get; set; }
        public bool? ShowMinimize { get; set; }
        public bool? ShowMaximize { get; set; }
        public int TitleBarHeight { get; set; } = 42;
    }

    /// <summary>
    /// Shared borderless FACM window shell for normal interactive top-level WinForms.
    /// Embedded League pages are deliberately skipped at Load time, so a Form may still be
    /// composed into LeagueHub without gaining a second title bar. Outside-click semantics match
    /// the control center: losing activation to the desktop or another process closes the surface,
    /// while focus moving to another FACM/native child dialog in this process does not.
    /// </summary>
    internal static class FacmWindowChrome
    {
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private static readonly Dictionary<Form, ChromeState> States = new Dictionary<Form, ChromeState>();

        public static void Prepare(Form form, FacmWindowChromeOptions options = null)
        {
            if (form == null || form.IsDisposed || States.ContainsKey(form)) return;

            var state = new ChromeState(form, options ?? new FacmWindowChromeOptions());
            States[form] = state;
            form.Load += state.HandleLoad;
            form.FormClosed += state.HandleClosed;
        }

        public static void EnableOutsideClose(Form form, bool closeOnEscape = true)
        {
            if (form == null || form.IsDisposed) return;
            form.KeyPreview = true;
            form.Deactivate += HandleExistingChromeDeactivate;
            if (closeOnEscape) form.KeyDown += HandleExistingChromeKeyDown;
        }

        private static void HandleExistingChromeDeactivate(object sender, EventArgs e)
        {
            var form = sender as Form;
            if (form == null || form.IsDisposed) return;
            QueueOutsideClose(form);
        }

        private static void HandleExistingChromeKeyDown(object sender, KeyEventArgs e)
        {
            var form = sender as Form;
            if (form == null || form.IsDisposed || e.KeyCode != Keys.Escape) return;
            e.Handled = true;
            form.Close();
        }

        private static void QueueOutsideClose(Form form)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
            try
            {
                form.BeginInvoke(new Action(delegate
                {
                    if (form.IsDisposed || !form.Visible || form.ContainsFocus) return;
                    var foreground = GetForegroundWindow();
                    if (foreground == IntPtr.Zero) return;
                    uint processId;
                    GetWindowThreadProcessId(foreground, out processId);
                    if (processId == (uint)Process.GetCurrentProcess().Id) return;
                    form.Close();
                }));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private sealed class ChromeState
        {
            private readonly Form _form;
            private readonly FacmWindowChromeOptions _options;
            private readonly FormBorderStyle _originalBorderStyle;
            private readonly bool _originalControlBox;
            private readonly bool _originalMinimizeBox;
            private readonly bool _originalMaximizeBox;
            private readonly Padding _originalPadding;
            private readonly Size _originalClientSize;
            private readonly Size _originalMinimumSize;
            private readonly Size _originalMaximumSize;
            private readonly bool _allowResize;
            private readonly bool _closeOnDeactivate;
            private readonly bool _closeOnEscape;
            private readonly bool _showMinimize;
            private readonly bool _showMaximize;
            private Panel _titleBar;
            private Label _titleLabel;
            private FacmChromeButton _maximizeButton;
            private BorderlessResizeWindow _resizeWindow;
            private bool _attached;

            public ChromeState(Form form, FacmWindowChromeOptions options)
            {
                _form = form;
                _options = options;
                _originalBorderStyle = form.FormBorderStyle;
                _originalControlBox = form.ControlBox;
                _originalMinimizeBox = form.MinimizeBox;
                _originalMaximizeBox = form.MaximizeBox;
                _originalPadding = form.Padding;
                _originalClientSize = form.ClientSize;
                _originalMinimumSize = form.MinimumSize;
                _originalMaximumSize = form.MaximumSize;

                var originallyResizable = _originalBorderStyle == FormBorderStyle.Sizable ||
                                          _originalBorderStyle == FormBorderStyle.SizableToolWindow;
                _allowResize = options.AllowResize ?? originallyResizable;
                _showMinimize = options.ShowMinimize ?? (_originalControlBox && _originalMinimizeBox && form.ShowInTaskbar);
                _showMaximize = options.ShowMaximize ?? (_originalControlBox && _originalMaximizeBox && _allowResize);
                _closeOnDeactivate = options.CloseOnDeactivate ?? _originalControlBox;
                _closeOnEscape = options.CloseOnEscape ?? _originalControlBox;
            }

            public void HandleLoad(object sender, EventArgs e)
            {
                if (_attached || _form.IsDisposed || !_form.TopLevel) return;
                Attach();
            }

            public void HandleClosed(object sender, FormClosedEventArgs e)
            {
                _form.Load -= HandleLoad;
                _form.FormClosed -= HandleClosed;
                _form.TextChanged -= HandleTextChanged;
                _form.Resize -= HandleResize;
                _form.Deactivate -= HandleDeactivate;
                _form.KeyDown -= HandleKeyDown;
                if (_resizeWindow != null)
                {
                    _resizeWindow.ReleaseHandle();
                    _resizeWindow = null;
                }
                States.Remove(_form);
            }

            private void Attach()
            {
                _attached = true;
                var titleHeight = Math.Max(34, _options.TitleBarHeight);
                var controls = new Control[_form.Controls.Count];
                _form.Controls.CopyTo(controls, 0);

                var content = new Panel
                {
                    Dock = DockStyle.Fill,
                    Padding = _originalPadding,
                    BackColor = _form.BackColor
                };

                _form.SuspendLayout();
                try
                {
                    foreach (var control in controls)
                    {
                        _form.Controls.Remove(control);
                        content.Controls.Add(control);
                    }
                    for (var index = 0; index < controls.Length; index++)
                        content.Controls.SetChildIndex(controls[index], index);

                    _form.FormBorderStyle = FormBorderStyle.None;
                    _form.ControlBox = false;
                    _form.MinimizeBox = false;
                    _form.MaximizeBox = false;
                    _form.Padding = new Padding(1);
                    _form.BackColor = FacmDesignSystem.BorderSoft;
                    _form.ClientSize = new Size(_originalClientSize.Width, _originalClientSize.Height + titleHeight);
                    if (!_originalMinimumSize.IsEmpty)
                        _form.MinimumSize = new Size(_originalMinimumSize.Width, _originalMinimumSize.Height + titleHeight);
                    if (!_originalMaximumSize.IsEmpty)
                        _form.MaximumSize = new Size(_originalMaximumSize.Width, _originalMaximumSize.Height + titleHeight);

                    _titleBar = BuildTitleBar(titleHeight);
                    _form.Controls.Add(content);
                    _form.Controls.Add(_titleBar);
                    _form.Controls.SetChildIndex(_titleBar, 0);
                }
                finally
                {
                    _form.ResumeLayout(true);
                }

                _form.KeyPreview = true;
                _form.TextChanged += HandleTextChanged;
                _form.Resize += HandleResize;
                if (_closeOnDeactivate) _form.Deactivate += HandleDeactivate;
                if (_closeOnEscape) _form.KeyDown += HandleKeyDown;

                if (_allowResize && _form.IsHandleCreated)
                {
                    _resizeWindow = new BorderlessResizeWindow(_form);
                    _resizeWindow.AssignHandle(_form.Handle);
                }
                UpdateRegion();
            }

            private Panel BuildTitleBar(int height)
            {
                var bar = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = height,
                    BackColor = FacmDesignSystem.CanvasRaised,
                    Padding = Padding.Empty
                };
                bar.MouseDown += BeginDrag;
                bar.DoubleClick += ToggleMaximize;

                var badge = new Label
                {
                    Text = "F",
                    Location = new Point(10, 7),
                    Size = new Size(28, 28),
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.White,
                    BackColor = FacmDesignSystem.Blend(FacmDesignSystem.AccentSecondary, FacmDesignSystem.Accent, 0.18F),
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold)
                };
                FacmDesignSystem.Round(badge, 8);
                badge.MouseDown += BeginDrag;

                _titleLabel = new Label
                {
                    Text = _form.Text,
                    Location = new Point(46, 0),
                    Height = height,
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = FacmDesignSystem.Text,
                    BackColor = Color.Transparent,
                    Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                };
                _titleLabel.MouseDown += BeginDrag;
                _titleLabel.DoubleClick += ToggleMaximize;

                var right = 8;
                var close = CreateChromeButton("×", ChromeButtonKind.Close);
                close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                close.Location = new Point(Math.Max(0, _form.ClientSize.Width - right - close.Width), 5);
                close.Click += delegate { _form.Close(); };
                bar.Controls.Add(close);
                right += close.Width + 4;

                if (_showMaximize)
                {
                    _maximizeButton = CreateChromeButton("□", ChromeButtonKind.Normal);
                    _maximizeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    _maximizeButton.Location = new Point(Math.Max(0, _form.ClientSize.Width - right - _maximizeButton.Width), 5);
                    _maximizeButton.Click += delegate { ToggleMaximize(null, EventArgs.Empty); };
                    bar.Controls.Add(_maximizeButton);
                    right += _maximizeButton.Width + 4;
                }

                if (_showMinimize)
                {
                    var minimize = CreateChromeButton("─", ChromeButtonKind.Normal);
                    minimize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    minimize.Location = new Point(Math.Max(0, _form.ClientSize.Width - right - minimize.Width), 5);
                    minimize.Click += delegate { _form.WindowState = FormWindowState.Minimized; };
                    bar.Controls.Add(minimize);
                    right += minimize.Width + 4;
                }

                _titleLabel.Width = Math.Max(80, _form.ClientSize.Width - 54 - right);
                bar.Controls.Add(badge);
                bar.Controls.Add(_titleLabel);
                return bar;
            }

            private static FacmChromeButton CreateChromeButton(string text, ChromeButtonKind kind)
            {
                return new FacmChromeButton
                {
                    Text = text,
                    Kind = kind,
                    Size = new Size(34, 30),
                    Font = new Font("Segoe UI", text == "×" ? 15F : 10F, FontStyle.Regular)
                };
            }

            private void BeginDrag(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || _form.WindowState == FormWindowState.Maximized) return;
                ReleaseCapture();
                SendMessage(_form.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }

            private void ToggleMaximize(object sender, EventArgs e)
            {
                if (!_allowResize || !_showMaximize) return;
                _form.WindowState = _form.WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal
                    : FormWindowState.Maximized;
            }

            private void HandleTextChanged(object sender, EventArgs e)
            {
                if (_titleLabel != null && !_titleLabel.IsDisposed) _titleLabel.Text = _form.Text;
            }

            private void HandleResize(object sender, EventArgs e)
            {
                if (_maximizeButton != null && !_maximizeButton.IsDisposed)
                    _maximizeButton.Text = _form.WindowState == FormWindowState.Maximized ? "❐" : "□";
                UpdateRegion();
            }

            private void HandleDeactivate(object sender, EventArgs e)
            {
                QueueOutsideClose(_form);
            }

            private void HandleKeyDown(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Escape) return;
                e.Handled = true;
                _form.Close();
            }

            private void UpdateRegion()
            {
                if (_form.IsDisposed) return;
                if (_form.WindowState == FormWindowState.Maximized)
                {
                    var previous = _form.Region;
                    _form.Region = null;
                    if (previous != null) previous.Dispose();
                    return;
                }
                FacmDesignSystem.Round(_form, FacmDesignSystem.WindowRadius);
            }
        }

        private enum ChromeButtonKind
        {
            Normal,
            Close
        }

        private sealed class FacmChromeButton : Button
        {
            private bool _hover;
            public ChromeButtonKind Kind { get; set; }

            public FacmChromeButton()
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                BackColor = Color.Transparent;
                ForeColor = FacmDesignSystem.TextMuted;
                Cursor = Cursors.Hand;
                TabStop = false;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                _hover = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                _hover = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var fill = _hover
                    ? (Kind == ChromeButtonKind.Close ? Color.FromArgb(184, 58, 72) : FacmDesignSystem.SurfaceHover)
                    : Color.Transparent;
                using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, ClientRectangle);
                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    ClientRectangle,
                    _hover || Kind == ChromeButtonKind.Close ? FacmDesignSystem.Text : FacmDesignSystem.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private sealed class BorderlessResizeWindow : NativeWindow
        {
            private readonly Form _form;
            public BorderlessResizeWindow(Form form) { _form = form; }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != WM_NCHITTEST || _form.IsDisposed || _form.WindowState != FormWindowState.Normal) return;
                if ((int)m.Result != HTCLIENT) return;

                var screen = new Point((short)(m.LParam.ToInt32() & 0xffff), (short)((m.LParam.ToInt32() >> 16) & 0xffff));
                var point = _form.PointToClient(screen);
                const int grip = 7;
                var left = point.X <= grip;
                var right = point.X >= _form.ClientSize.Width - grip;
                var top = point.Y <= grip;
                var bottom = point.Y >= _form.ClientSize.Height - grip;

                if (left && top) m.Result = (IntPtr)HTTOPLEFT;
                else if (right && top) m.Result = (IntPtr)HTTOPRIGHT;
                else if (left && bottom) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (right && bottom) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)HTLEFT;
                else if (right) m.Result = (IntPtr)HTRIGHT;
                else if (top) m.Result = (IntPtr)HTTOP;
                else if (bottom) m.Result = (IntPtr)HTBOTTOM;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
