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
        public bool? ShowClose { get; set; }
        public bool? ShowMinimize { get; set; }
        public bool? ShowMaximize { get; set; }
        public int TitleBarHeight { get; set; } = 36;
    }

    /// <summary>
    /// Shared borderless FACM window shell for normal interactive top-level WinForms.
    /// The top interaction band is intentionally integrated into the page canvas: it keeps drag,
    /// minimize/maximize/close and resize behavior without looking like a separate title bar.
    /// Embedded League pages are deliberately skipped at Load time, so a Form may still be
    /// composed into LeagueHub without gaining a second shell.
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
        private static bool _globalInstalled;

        public static void InstallGlobal()
        {
            if (_globalInstalled) return;
            _globalInstalled = true;
            Application.Idle += HandleApplicationIdle;
        }

        public static void Prepare(Form form, FacmWindowChromeOptions options = null)
        {
            if (form == null || form.IsDisposed) return;

            ChromeState state;
            if (!States.TryGetValue(form, out state))
            {
                state = new ChromeState(form, options ?? new FacmWindowChromeOptions());
                States[form] = state;
                form.Load += state.HandleLoad;
                form.FormClosed += state.HandleClosed;
            }
            state.AttachIfPossible();
        }

        /// <summary>
        /// Compatibility hook retained for callers compiled around the earlier chrome subtitle API.
        /// The integrated borderless shell deliberately omits secondary title-bar copy.
        /// </summary>
        public static void SetSubtitle(Form form, string subtitle)
        {
            if (form == null || form.IsDisposed) return;
            Prepare(form);
        }

        public static void RefreshTheme(Form form)
        {
            if (form == null || form.IsDisposed) return;
            ChromeState state;
            if (States.TryGetValue(form, out state)) state.RefreshTheme();
        }

        internal static bool IsManaged(Form form)
        {
            return form != null && States.ContainsKey(form);
        }

        internal static bool TryGetLayoutForSmokeTest(Form form, out Rectangle titleBounds, out Rectangle contentBounds)
        {
            titleBounds = Rectangle.Empty;
            contentBounds = Rectangle.Empty;
            ChromeState state;
            return form != null && States.TryGetValue(form, out state) &&
                   state.TryGetLayoutForSmokeTest(out titleBounds, out contentBounds);
        }

        internal static bool TryGetVisualForSmokeTest(Form form, out Color topBackground, out Color contentBackground, out int topHeight)
        {
            topBackground = Color.Empty;
            contentBackground = Color.Empty;
            topHeight = 0;
            ChromeState state;
            return form != null && States.TryGetValue(form, out state) &&
                   state.TryGetVisualForSmokeTest(out topBackground, out contentBackground, out topHeight);
        }

        public static void EnableOutsideClose(Form form, bool closeOnEscape = true)
        {
            if (form == null || form.IsDisposed) return;
            form.KeyPreview = true;
            form.Deactivate -= HandleExistingChromeDeactivate;
            form.Deactivate += HandleExistingChromeDeactivate;
            if (closeOnEscape)
            {
                form.KeyDown -= HandleExistingChromeKeyDown;
                form.KeyDown += HandleExistingChromeKeyDown;
            }
        }

        private static void HandleApplicationIdle(object sender, EventArgs e)
        {
            var count = Application.OpenForms.Count;
            var forms = new Form[count];
            for (var index = 0; index < count; index++)
                forms[index] = Application.OpenForms[index];

            foreach (var form in forms)
            {
                if (form == null || form.IsDisposed || !form.TopLevel || !form.Visible) continue;
                // MainForm, CompactMenuForm, CleanupReviewForm and desktop-pet surfaces already
                // own a purposeful borderless shell; never wrap one custom chrome inside another.
                if (form.FormBorderStyle == FormBorderStyle.None) continue;
                Prepare(form);
            }
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
            if (form == null || form.IsDisposed || !form.IsHandleCreated || form.WindowState == FormWindowState.Minimized) return;
            try
            {
                form.BeginInvoke(new Action(delegate
                {
                    if (form.IsDisposed || !form.Visible || form.ContainsFocus || form.WindowState == FormWindowState.Minimized) return;
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
            private readonly bool _showClose;
            private readonly bool _showMinimize;
            private readonly bool _showMaximize;
            private Label _titleLabel;
            private Label _badge;
            private Panel _titleBar;
            private Panel _content;
            private FacmChromeButton _maximizeButton;
            private BorderlessResizeWindow _resizeWindow;
            private int _titleHeight;
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
                _showClose = options.ShowClose ?? _originalControlBox;
                _showMinimize = options.ShowMinimize ?? (_originalControlBox && _originalMinimizeBox && form.ShowInTaskbar);
                _showMaximize = options.ShowMaximize ?? (_originalControlBox && _originalMaximizeBox && _allowResize);
                _closeOnDeactivate = options.CloseOnDeactivate ?? _originalControlBox;
                _closeOnEscape = options.CloseOnEscape ?? _originalControlBox;
            }

            public void HandleLoad(object sender, EventArgs e)
            {
                AttachIfPossible();
            }

            public void RefreshTheme()
            {
                if (!_attached || _form.IsDisposed) return;
                _form.BackColor = FacmDesignSystem.BorderSoft;
                if (_content != null && !_content.IsDisposed) _content.BackColor = FacmDesignSystem.Canvas;
                if (_titleBar != null && !_titleBar.IsDisposed) _titleBar.BackColor = FacmDesignSystem.Canvas;
                if (_titleLabel != null && !_titleLabel.IsDisposed) _titleLabel.ForeColor = FacmDesignSystem.Text;
                if (_badge != null && !_badge.IsDisposed)
                {
                    _badge.BackColor = FacmDesignSystem.Blend(FacmDesignSystem.AccentSecondary, FacmDesignSystem.Accent, 0.18F);
                    _badge.ForeColor = Color.White;
                }
                if (_titleBar != null) _titleBar.Invalidate(true);
                UpdateRegion();
            }

            public void AttachIfPossible()
            {
                if (_attached || _form.IsDisposed || !_form.TopLevel || _originalBorderStyle == FormBorderStyle.None) return;
                if (!_form.IsHandleCreated && !_form.Visible) return;
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

            public bool TryGetLayoutForSmokeTest(out Rectangle titleBounds, out Rectangle contentBounds)
            {
                titleBounds = _titleBar == null || _titleBar.IsDisposed ? Rectangle.Empty : _titleBar.Bounds;
                contentBounds = _content == null || _content.IsDisposed ? Rectangle.Empty : _content.Bounds;
                return _attached && !titleBounds.IsEmpty && !contentBounds.IsEmpty;
            }

            public bool TryGetVisualForSmokeTest(out Color topBackground, out Color contentBackground, out int topHeight)
            {
                topBackground = _titleBar == null || _titleBar.IsDisposed ? Color.Empty : _titleBar.BackColor;
                contentBackground = _content == null || _content.IsDisposed ? Color.Empty : _content.BackColor;
                topHeight = _titleHeight;
                return _attached && topBackground != Color.Empty && contentBackground != Color.Empty && topHeight > 0;
            }

            private void Attach()
            {
                _attached = true;
                _titleHeight = Math.Max(34, _options.TitleBarHeight);
                var controls = new Control[_form.Controls.Count];
                for (var index = 0; index < controls.Length; index++)
                    controls[index] = _form.Controls[index];

                _content = new Panel
                {
                    Dock = DockStyle.None,
                    Padding = _originalPadding,
                    BackColor = FacmDesignSystem.Canvas
                };

                _form.SuspendLayout();
                try
                {
                    foreach (var control in controls)
                    {
                        _form.Controls.Remove(control);
                        _content.Controls.Add(control);
                    }
                    for (var index = 0; index < controls.Length; index++)
                        _content.Controls.SetChildIndex(controls[index], index);

                    _form.FormBorderStyle = FormBorderStyle.None;
                    _form.ControlBox = false;
                    _form.MinimizeBox = false;
                    _form.MaximizeBox = false;
                    _form.Padding = new Padding(1);
                    _form.BackColor = FacmDesignSystem.BorderSoft;
                    _form.ClientSize = new Size(_originalClientSize.Width, _originalClientSize.Height + _titleHeight);
                    if (!_originalMinimumSize.IsEmpty)
                        _form.MinimumSize = new Size(_originalMinimumSize.Width, _originalMinimumSize.Height + _titleHeight);
                    if (!_originalMaximumSize.IsEmpty)
                        _form.MaximumSize = new Size(_originalMaximumSize.Width, _originalMaximumSize.Height + _titleHeight);

                    _titleBar = BuildTitleBar(_titleHeight);
                    _form.Controls.Add(_content);
                    _form.Controls.Add(_titleBar);
                    LayoutChrome();
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
                RefreshTheme();
                UpdateTitleLayout();
                UpdateRegion();
            }

            private void LayoutChrome()
            {
                if (!_attached || _form.IsDisposed || _titleBar == null || _titleBar.IsDisposed || _content == null || _content.IsDisposed)
                    return;

                var left = _form.Padding.Left;
                var top = _form.Padding.Top;
                var width = Math.Max(0, _form.ClientSize.Width - _form.Padding.Horizontal);
                var height = Math.Max(0, _form.ClientSize.Height - _form.Padding.Vertical);
                var titleHeight = Math.Min(_titleHeight, height);

                // Keep the interaction band and page content in explicit, non-overlapping bounds.
                // The two regions intentionally share the same canvas color so the shell reads as
                // one borderless surface instead of a title bar stacked above a page.
                _titleBar.SetBounds(left, top, width, titleHeight);
                _content.SetBounds(left, top + titleHeight, width, Math.Max(0, height - titleHeight));
                _titleBar.BringToFront();
            }

            private Panel BuildTitleBar(int height)
            {
                var bar = new Panel
                {
                    Dock = DockStyle.None,
                    Size = new Size(Math.Max(1, _form.ClientSize.Width - _form.Padding.Horizontal), height),
                    BackColor = FacmDesignSystem.Canvas,
                    Padding = Padding.Empty
                };
                bar.MouseDown += BeginDrag;
                bar.DoubleClick += ToggleMaximize;

                const int badgeSize = 24;
                _badge = new Label
                {
                    Text = "F", // ui-text-contract: allow brand glyph
                    Location = new Point(8, Math.Max(0, (height - badgeSize) / 2)),
                    Size = new Size(badgeSize, badgeSize),
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.White,
                    BackColor = FacmDesignSystem.Blend(FacmDesignSystem.AccentSecondary, FacmDesignSystem.Accent, 0.18F),
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold)
                };
                FacmDesignSystem.Round(_badge, 7);
                _badge.MouseDown += BeginDrag;
                _badge.DoubleClick += ToggleMaximize;

                _titleLabel = new Label
                {
                    Text = _form.Text,
                    Location = new Point(40, 0),
                    Height = height,
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = FacmDesignSystem.Text,
                    BackColor = Color.Transparent,
                    Font = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Bold)
                };
                _titleLabel.MouseDown += BeginDrag;
                _titleLabel.DoubleClick += ToggleMaximize;

                var right = 4;
                var buttonTop = Math.Max(0, (height - 28) / 2);
                if (_showClose)
                {
                    var close = CreateChromeButton("×", ChromeButtonKind.Close);
                    close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    close.Location = new Point(Math.Max(0, bar.ClientSize.Width - right - close.Width), buttonTop);
                    close.Click += delegate { _form.Close(); };
                    bar.Controls.Add(close);
                    right += close.Width + 2;
                }

                if (_showMaximize)
                {
                    _maximizeButton = CreateChromeButton("□", ChromeButtonKind.Normal);
                    _maximizeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    _maximizeButton.Location = new Point(Math.Max(0, bar.ClientSize.Width - right - _maximizeButton.Width), buttonTop);
                    _maximizeButton.Click += delegate { ToggleMaximize(null, EventArgs.Empty); };
                    bar.Controls.Add(_maximizeButton);
                    right += _maximizeButton.Width + 2;
                }

                if (_showMinimize)
                {
                    var minimize = CreateChromeButton("─", ChromeButtonKind.Normal);
                    minimize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    minimize.Location = new Point(Math.Max(0, bar.ClientSize.Width - right - minimize.Width), buttonTop);
                    minimize.Click += delegate { _form.WindowState = FormWindowState.Minimized; };
                    bar.Controls.Add(minimize);
                    right += minimize.Width + 2;
                }

                _titleLabel.Tag = right;
                bar.Controls.Add(_badge);
                bar.Controls.Add(_titleLabel);
                return bar;
            }

            private void UpdateTitleLayout()
            {
                if (!_attached || _titleLabel == null || _titleLabel.IsDisposed) return;
                var right = _titleLabel.Tag is int ? (int)_titleLabel.Tag : 4;
                var chromeWidth = _titleBar == null || _titleBar.IsDisposed ? _form.ClientSize.Width : _titleBar.ClientSize.Width;
                _titleLabel.Location = new Point(40, 0);
                _titleLabel.Width = Math.Max(60, chromeWidth - 44 - right);
            }

            private static FacmChromeButton CreateChromeButton(string text, ChromeButtonKind kind)
            {
                return new FacmChromeButton
                {
                    Text = text,
                    Kind = kind,
                    Size = new Size(32, 28),
                    Font = new Font("Segoe UI", text == "×" ? 14F : 9.5F, FontStyle.Regular)
                };
            }

            private void BeginDrag(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                if (_form.WindowState == FormWindowState.Maximized)
                {
                    if (_showMaximize) ToggleMaximize(null, EventArgs.Empty);
                    return;
                }
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
                LayoutChrome();
                UpdateTitleLayout();
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
                    ? (Kind == ChromeButtonKind.Close ? FacmDesignSystem.Error : FacmDesignSystem.SurfaceHover)
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

                var packed = unchecked((int)m.LParam.ToInt64());
                var screen = new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff));
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
