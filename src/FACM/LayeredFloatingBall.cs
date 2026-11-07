using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM
{
    internal sealed class LayeredFloatingBall : IDisposable
    {
        private const int GwlExStyle = -20;
        private const int WsExLayered = 0x00080000;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int UlwAlpha = 0x00000002;
        private const byte AcSrcOver = 0x00;
        private const byte AcSrcAlpha = 0x01;
        private const int InitialRenderMaxAttempts = 12;

        private readonly Form _form;
        private readonly Timer _hoverTimer;
        private readonly Timer _initialRenderTimer;
        private ThemeDefinition _theme;
        private bool _hovered;
        private bool _disposed;
        private bool _layeredReady;
        private int _initialRenderAttempts;
        private int _lastLayeredError;
        private float _hover;

        private LayeredFloatingBall(Form form, ThemeDefinition theme)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _theme = theme ?? ThemeCatalog.Get(ThemeCatalog.DefaultThemeId);
            _form.Region = null;
            _form.HandleCreated += HandleCreated;
            _form.Shown += RefreshNow;
            _form.VisibleChanged += RefreshNow;
            _form.LocationChanged += RefreshNow;
            _form.MouseEnter += delegate
            {
                _hovered = true;
                StartHoverTransition();
            };
            _form.MouseLeave += delegate
            {
                _hovered = false;
                StartHoverTransition();
            };
            _form.FormClosed += delegate { Dispose(); };

            // The previous orb rendered a perpetual glow/orbit animation at ~30 FPS even when idle.
            // The FACM shell is intentionally quiet: redraw only while hover is transitioning.
            _hoverTimer = new Timer { Interval = 16 };
            _hoverTimer.Tick += TickHover;

            // Removing the old perpetual timer also removed its accidental startup self-healing. A
            // layered window can transiently reject the very first UpdateLayeredWindow call while the
            // native window is still being shown. Retry only the initial frame for a short bounded
            // window, then stay idle once a real layered frame has succeeded.
            _initialRenderTimer = new Timer { Interval = 80 };
            _initialRenderTimer.Tick += TickInitialRender;

            if (_form.IsHandleCreated) ApplyLayeredStyle();
        }

        public static LayeredFloatingBall Attach(Form form)
        {
            return new LayeredFloatingBall(form, ThemeCatalog.Get(ThemeCatalog.DefaultThemeId));
        }

        public static LayeredFloatingBall Attach(Form form, ThemeDefinition theme)
        {
            return new LayeredFloatingBall(form, theme);
        }

        internal string CurrentSkinIdForSmokeTest { get { return _theme.Id; } }

        public void SetTheme(ThemeDefinition theme)
        {
            if (_disposed) return;
            _theme = theme ?? ThemeCatalog.Get(ThemeCatalog.DefaultThemeId);
            TryRenderOrSchedule();
        }

        internal static Bitmap RenderForSmokeTest(int size, float hover = 0f, float phase = 0f)
        {
            return RenderShell(
                size,
                Math.Max(0f, Math.Min(1f, hover)),
                ThemeCatalog.Get(ThemeCatalog.DefaultThemeId));
        }

        internal static Bitmap RenderSkinForSmokeTest(int size, string skinId)
        {
            return RenderShell(size, 0f, ThemeCatalog.Get(skinId));
        }

        private void HandleCreated(object sender, EventArgs e)
        {
            ApplyLayeredStyle();
            TryRenderOrSchedule();
        }

        private void RefreshNow(object sender, EventArgs e)
        {
            if (_disposed || !_form.Visible) return;
            TryRenderOrSchedule();
        }

        private void StartHoverTransition()
        {
            if (_disposed) return;
            if (!_hoverTimer.Enabled) _hoverTimer.Start();
        }

        private void TickHover(object sender, EventArgs e)
        {
            if (_disposed)
            {
                _hoverTimer.Stop();
                return;
            }

            var target = _hovered ? 1f : 0f;
            _hover += (target - _hover) * 0.24f;
            if (Math.Abs(target - _hover) < 0.015f)
            {
                _hover = target;
                _hoverTimer.Stop();
            }
            TryRenderOrSchedule();
        }

        private void TickInitialRender(object sender, EventArgs e)
        {
            if (_disposed || !_form.Visible || !_form.IsHandleCreated)
            {
                _initialRenderTimer.Stop();
                return;
            }

            _initialRenderAttempts++;
            ApplyLayeredStyle();
            if (RenderLayered())
            {
                _initialRenderTimer.Stop();
                return;
            }

            if (_initialRenderAttempts < InitialRenderMaxAttempts) return;

            _initialRenderTimer.Stop();
            AppLog.Error(
                "FACM shell layered frame did not become visible after " +
                _initialRenderAttempts + " attempts; lastWin32Error=" + _lastLayeredError,
                null);
        }

        private void TryRenderOrSchedule()
        {
            if (_disposed || !_form.Visible || !_form.IsHandleCreated) return;

            if (RenderLayered())
            {
                _initialRenderTimer.Stop();
                return;
            }

            if (_initialRenderAttempts >= InitialRenderMaxAttempts) return;
            if (!_initialRenderTimer.Enabled) _initialRenderTimer.Start();
        }

        private void ApplyLayeredStyle()
        {
            if (_disposed || !_form.IsHandleCreated) return;
            _form.Region = null;
            var style = GetWindowLong(_form.Handle, GwlExStyle);
            style |= WsExLayered | WsExToolWindow | WsExNoActivate;
            SetWindowLong(_form.Handle, GwlExStyle, style);
        }

        private bool RenderLayered()
        {
            if (_disposed || !_form.IsHandleCreated || !_form.Visible) return false;
            using (var bitmap = RenderShell(Math.Min(_form.Width, _form.Height), _hover, _theme))
            {
                IntPtr screenDc = IntPtr.Zero;
                IntPtr memoryDc = IntPtr.Zero;
                IntPtr hBitmap = IntPtr.Zero;
                IntPtr oldBitmap = IntPtr.Zero;
                try
                {
                    screenDc = GetDC(IntPtr.Zero);
                    if (screenDc == IntPtr.Zero)
                    {
                        _lastLayeredError = Marshal.GetLastWin32Error();
                        return false;
                    }

                    memoryDc = CreateCompatibleDC(screenDc);
                    if (memoryDc == IntPtr.Zero)
                    {
                        _lastLayeredError = Marshal.GetLastWin32Error();
                        return false;
                    }

                    hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                    if (hBitmap == IntPtr.Zero)
                    {
                        _lastLayeredError = Marshal.GetLastWin32Error();
                        return false;
                    }

                    oldBitmap = SelectObject(memoryDc, hBitmap);
                    if (oldBitmap == IntPtr.Zero || oldBitmap == new IntPtr(-1))
                    {
                        _lastLayeredError = Marshal.GetLastWin32Error();
                        return false;
                    }

                    var destination = new PointNative(_form.Left, _form.Top);
                    var source = new PointNative(0, 0);
                    var size = new SizeNative(bitmap.Width, bitmap.Height);
                    var blend = new BlendFunction
                    {
                        BlendOp = AcSrcOver,
                        BlendFlags = 0,
                        SourceConstantAlpha = 255,
                        AlphaFormat = AcSrcAlpha
                    };

                    var updated = UpdateLayeredWindow(
                        _form.Handle,
                        screenDc,
                        ref destination,
                        ref size,
                        memoryDc,
                        ref source,
                        0,
                        ref blend,
                        UlwAlpha);
                    if (!updated)
                    {
                        _lastLayeredError = Marshal.GetLastWin32Error();
                        return false;
                    }

                    if (!_layeredReady)
                    {
                        _layeredReady = true;
                        AppLog.Info(
                            "FACM shell layered frame ready; attempts=" + _initialRenderAttempts +
                            "; size=" + bitmap.Width + "x" + bitmap.Height +
                            "; location=" + _form.Left + "," + _form.Top);
                    }
                    _lastLayeredError = 0;
                    return true;
                }
                finally
                {
                    if (oldBitmap != IntPtr.Zero && oldBitmap != new IntPtr(-1) && memoryDc != IntPtr.Zero)
                        SelectObject(memoryDc, oldBitmap);
                    if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                    if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                    if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        private static Bitmap RenderShell(int size, float hover, ThemeDefinition theme)
        {
            size = Math.Max(56, size);
            theme = theme ?? ThemeCatalog.Get(ThemeCatalog.DefaultThemeId);
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                // Circular, quiet floating identity inspired by system utility launchers.
                // The accent ring is decorative: it never claims CPU, memory or game status.
                var inset = Math.Max(5f, size * 0.09f);
                var diameter = size - inset * 2f;
                var circle = new RectangleF(inset, inset - 0.6f, diameter, diameter);
                var surface = theme.IsLight
                    ? Blend(theme.Surface, Color.White, 0.1f)
                    : Blend(theme.Surface, Color.FromArgb(6, 17, 37), 0.4f);
                var hoverSurface = Blend(surface, theme.Accent, theme.IsLight ? 0.06f : 0.11f);
                surface = Blend(surface, hoverSurface, hover);

                for (var index = 3; index >= 1; index--)
                {
                    var shadowRect = RectangleF.Inflate(circle, index * 1.25f, index * 1.25f);
                    shadowRect.Y += 1.8f;
                    using (var shadow = new SolidBrush(Color.FromArgb(7 + index * 7, 0, 0, 0)))
                        graphics.FillEllipse(shadow, shadowRect);
                }

                using (var body = new SolidBrush(Color.FromArgb(250, surface)))
                using (var edge = new Pen(Color.FromArgb(110, theme.Border), 1.2f))
                {
                    graphics.FillEllipse(body, circle);
                    graphics.DrawEllipse(edge, circle);
                }

                var ring = RectangleF.Inflate(circle, -2.4f, -2.4f);
                using (var quietRing = new Pen(Color.FromArgb(68, theme.Accent), 1.75f))
                    graphics.DrawEllipse(quietRing, ring);
                using (var activeRing = new Pen(Color.FromArgb(195 + (int)(60 * hover), theme.Accent),
                    2.5f + hover * 0.35f))
                {
                    activeRing.StartCap = LineCap.Round;
                    activeRing.EndCap = LineCap.Round;
                    graphics.DrawArc(activeRing, ring, -118f, 286f);
                }

                var accentDotSize = Math.Max(4f, size * 0.085f);
                var dot = new RectangleF(
                    circle.Right - accentDotSize * 1.7f,
                    circle.Bottom - accentDotSize * 1.7f,
                    accentDotSize, accentDotSize);
                using (var dotOutline = new SolidBrush(surface))
                    graphics.FillEllipse(dotOutline, RectangleF.Inflate(dot, 1.4f, 1.4f));
                using (var dotBrush = new SolidBrush(theme.Accent))
                    graphics.FillEllipse(dotBrush, dot);

                var logoColor = theme.IsLight ? theme.TextPrimary : Blend(theme.TextPrimary, Color.White, 0.12f);
                using (var logo = new Font("Microsoft YaHei UI", Math.Max(17f, size * 0.32f),
                    FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Color.FromArgb(252, logoColor)))
                {
                    var measured = graphics.MeasureString("G", logo);
                    graphics.DrawString("G", logo, brush, // ui-text-contract: allow
                        (size - measured.Width) / 2f - 0.4f,
                        (size - measured.Height) / 2f - 1.2f);
                }
            }
            return bitmap;
        }

        private static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Max(2f, Math.Min(Math.Min(bounds.Width, bounds.Height), radius * 2f));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color Blend(Color first, Color second, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(first.A + (second.A - first.A) * amount),
                (int)(first.R + (second.R - first.R) * amount),
                (int)(first.G + (second.G - first.G) * amount),
                (int)(first.B + (second.B - first.B) * amount));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_hoverTimer != null)
            {
                _hoverTimer.Stop();
                _hoverTimer.Dispose();
            }
            if (_initialRenderTimer != null)
            {
                _initialRenderTimer.Stop();
                _initialRenderTimer.Dispose();
            }
            if (_form != null)
            {
                _form.HandleCreated -= HandleCreated;
                _form.Shown -= RefreshNow;
                _form.VisibleChanged -= RefreshNow;
                _form.LocationChanged -= RefreshNow;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PointNative
        {
            public int X;
            public int Y;
            public PointNative(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SizeNative
        {
            public int CX;
            public int CY;
            public SizeNative(int cx, int cy) { CX = cx; CY = cy; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hwnd,
            IntPtr hdcDst,
            ref PointNative pptDst,
            ref SizeNative psize,
            IntPtr hdcSrc,
            ref PointNative pprSrc,
            int crKey,
            ref BlendFunction pblend,
            int dwFlags);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
