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

                var margin = Math.Max(5f, size * 0.09f);
                var bodyRect = new RectangleF(margin, margin - 1f, size - margin * 2f, size - margin * 2f);
                var radius = Math.Max(12f, bodyRect.Width * 0.29f);

                // Soft separation shadow only. No neon halo, orbiting dot or breathing animation.
                for (var index = 3; index >= 1; index--)
                {
                    var expand = index * 1.2f;
                    var shadowRect = new RectangleF(
                        bodyRect.X - expand,
                        bodyRect.Y - expand + 2.2f,
                        bodyRect.Width + expand * 2f,
                        bodyRect.Height + expand * 2f);
                    using (var shadowPath = CreateRoundedPath(shadowRect, radius + expand))
                    using (var shadow = new SolidBrush(Color.FromArgb(8 + index * 6, 0, 0, 0)))
                        graphics.FillPath(shadow, shadowPath);
                }

                var baseSurface = theme.IsLight
                    ? Blend(theme.Surface, Color.White, 0.12f)
                    : Blend(theme.Surface, Color.Black, 0.16f);
                var hoverSurface = theme.IsLight
                    ? Blend(baseSurface, Color.Black, 0.035f)
                    : Blend(baseSurface, Color.White, 0.075f);
                var surface = Blend(baseSurface, hoverSurface, hover);

                using (var bodyPath = CreateRoundedPath(bodyRect, radius))
                using (var body = new SolidBrush(Color.FromArgb(244, surface)))
                using (var border = new Pen(
                    Color.FromArgb(
                        76 + (int)(74 * hover),
                        hover > 0.02f ? theme.Accent : theme.Border),
                    1f + hover * 0.18f))
                {
                    graphics.FillPath(body, bodyPath);
                    graphics.DrawPath(border, bodyPath);
                }

                // A single restrained top highlight gives depth without making the entry look glossy.
                var highlightRect = RectangleF.Inflate(bodyRect, -3.2f, -3.2f);
                using (var highlightPath = CreateRoundedPath(highlightRect, Math.Max(8f, radius - 3f)))
                using (var highlight = new Pen(Color.FromArgb(theme.IsLight ? 38 : 24, Color.White), 1f))
                    graphics.DrawPath(highlight, highlightPath);

                // Tiny theme accent doubles as the shell's status/identity mark.
                var accentWidth = bodyRect.Width * 0.23f;
                var accentRect = new RectangleF(
                    bodyRect.X + (bodyRect.Width - accentWidth) / 2f,
                    bodyRect.Bottom - 6.2f,
                    accentWidth,
                    2.2f);
                using (var accentPath = CreateRoundedPath(accentRect, 1.2f))
                using (var accent = new SolidBrush(Color.FromArgb(185 + (int)(55 * hover), theme.Accent)))
                    graphics.FillPath(accent, accentPath);

                var logoColor = theme.IsLight
                    ? Blend(theme.TextPrimary, Color.Black, 0.06f)
                    : Blend(theme.TextPrimary, Color.White, 0.08f);
                using (var font = new Font("Segoe UI", Math.Max(18f, size * 0.32f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var textBrush = new SolidBrush(Color.FromArgb(245, logoColor)))
                {
                    const string logo = "F";
                    var measured = graphics.MeasureString(logo, font);
                    graphics.DrawString(
                        logo,
                        font,
                        textBrush,
                        (size - measured.Width) / 2f - 0.2f,
                        (size - measured.Height) / 2f - 2.4f);
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
