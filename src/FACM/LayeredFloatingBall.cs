using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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

        private readonly Form _form;
        private readonly Timer _timer;
        private bool _hovered;
        private bool _disposed;
        private float _hover;
        private float _phase;

        private LayeredFloatingBall(Form form)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _form.Region = null;
            _form.HandleCreated += HandleCreated;
            _form.Shown += RefreshNow;
            _form.VisibleChanged += RefreshNow;
            _form.LocationChanged += RefreshNow;
            _form.MouseEnter += delegate { _hovered = true; };
            _form.MouseLeave += delegate { _hovered = false; };
            _form.FormClosed += delegate { Dispose(); };

            _timer = new Timer { Interval = 33 };
            _timer.Tick += Tick;
            _timer.Start();

            if (_form.IsHandleCreated) ApplyLayeredStyle();
        }

        public static LayeredFloatingBall Attach(Form form)
        {
            return new LayeredFloatingBall(form);
        }

        internal static Bitmap RenderForSmokeTest(int size, float hover = 0f, float phase = 0f)
        {
            return RenderBall(size, Math.Max(0f, Math.Min(1f, hover)), phase);
        }

        private void HandleCreated(object sender, EventArgs e)
        {
            ApplyLayeredStyle();
            RenderLayered();
        }

        private void RefreshNow(object sender, EventArgs e)
        {
            if (_disposed || !_form.Visible) return;
            RenderLayered();
        }

        private void Tick(object sender, EventArgs e)
        {
            if (_disposed || !_form.Visible) return;
            var target = _hovered ? 1f : 0f;
            _hover += (target - _hover) * 0.18f;
            _phase += 0.045f;
            if (_phase > Math.PI * 2f) _phase -= (float)(Math.PI * 2f);
            RenderLayered();
        }

        private void ApplyLayeredStyle()
        {
            if (_disposed || !_form.IsHandleCreated) return;
            _form.Region = null;
            var style = GetWindowLong(_form.Handle, GwlExStyle);
            style |= WsExLayered | WsExToolWindow | WsExNoActivate;
            SetWindowLong(_form.Handle, GwlExStyle, style);
        }

        private void RenderLayered()
        {
            if (_disposed || !_form.IsHandleCreated || !_form.Visible) return;
            using (var bitmap = RenderBall(Math.Min(_form.Width, _form.Height), _hover, _phase))
            {
                IntPtr screenDc = IntPtr.Zero;
                IntPtr memoryDc = IntPtr.Zero;
                IntPtr hBitmap = IntPtr.Zero;
                IntPtr oldBitmap = IntPtr.Zero;
                try
                {
                    screenDc = GetDC(IntPtr.Zero);
                    memoryDc = CreateCompatibleDC(screenDc);
                    hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                    oldBitmap = SelectObject(memoryDc, hBitmap);

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

                    UpdateLayeredWindow(
                        _form.Handle,
                        screenDc,
                        ref destination,
                        ref size,
                        memoryDc,
                        ref source,
                        0,
                        ref blend,
                        UlwAlpha);
                }
                finally
                {
                    if (oldBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, oldBitmap);
                    if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                    if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                    if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        private static Bitmap RenderBall(int size, float hover, float phase)
        {
            size = Math.Max(64, size);
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

                var pulse = 0.5f + 0.5f * (float)Math.Sin(phase);
                var glowAlpha = 30 + (int)(34 * hover) + (int)(8 * pulse);
                var haloRect = new RectangleF(5.5f, 5.5f, size - 11f, size - 11f);
                using (var haloPath = new GraphicsPath())
                {
                    haloPath.AddEllipse(haloRect);
                    using (var halo = new PathGradientBrush(haloPath))
                    {
                        halo.CenterPoint = new PointF(size * 0.5f, size * 0.5f);
                        halo.CenterColor = Color.FromArgb(glowAlpha, 77, 205, 255);
                        halo.SurroundColors = new[] { Color.FromArgb(0, 77, 205, 255) };
                        graphics.FillPath(halo, haloPath);
                    }
                }

                var sphere = new RectangleF(9f, 9f, size - 18f, size - 18f);
                using (var spherePath = new GraphicsPath())
                {
                    spherePath.AddEllipse(sphere);
                    using (var body = new PathGradientBrush(spherePath))
                    {
                        body.CenterPoint = new PointF(sphere.Left + sphere.Width * 0.33f, sphere.Top + sphere.Height * 0.26f);
                        body.CenterColor = Color.FromArgb(250, 183, 232, 255);
                        body.SurroundColors = new[] { Color.FromArgb(248, 32, 88, 166) };
                        graphics.FillPath(body, spherePath);
                    }
                }

                using (var lowerPath = new GraphicsPath())
                {
                    lowerPath.AddEllipse(
                        sphere.Left + sphere.Width * 0.08f,
                        sphere.Top + sphere.Height * 0.50f,
                        sphere.Width * 0.84f,
                        sphere.Height * 0.40f);
                    using (var shade = new PathGradientBrush(lowerPath))
                    {
                        shade.CenterColor = Color.FromArgb(96, 31, 91, 190);
                        shade.SurroundColors = new[] { Color.FromArgb(0, 45, 119, 212) };
                        graphics.FillPath(shade, lowerPath);
                    }
                }

                var ringAlpha = 180 + (int)(55 * hover);
                using (var outerRing = new Pen(Color.FromArgb(Math.Min(255, ringAlpha), 111, 220, 255), 1.7f))
                    graphics.DrawEllipse(outerRing, sphere);
                using (var innerRing = new Pen(Color.FromArgb(120 + (int)(45 * hover), 203, 242, 255), 1.0f))
                    graphics.DrawEllipse(innerRing, sphere.X + 4f, sphere.Y + 4f, sphere.Width - 8f, sphere.Height - 8f);

                using (var glassPath = new GraphicsPath())
                {
                    glassPath.AddEllipse(
                        sphere.X + sphere.Width * 0.13f,
                        sphere.Y + sphere.Height * 0.10f,
                        sphere.Width * 0.48f,
                        sphere.Height * 0.31f);
                    using (var glass = new PathGradientBrush(glassPath))
                    {
                        glass.CenterColor = Color.FromArgb(138 + (int)(28 * hover), 255, 255, 255);
                        glass.SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) };
                        graphics.FillPath(glass, glassPath);
                    }
                }

                var coreSize = 31f;
                var core = new RectangleF((size - coreSize) / 2f, (size - coreSize) / 2f, coreSize, coreSize);
                using (var corePath = new GraphicsPath())
                {
                    corePath.AddEllipse(core);
                    using (var coreBrush = new PathGradientBrush(corePath))
                    {
                        coreBrush.CenterPoint = new PointF(core.Left + core.Width * 0.34f, core.Top + core.Height * 0.26f);
                        coreBrush.CenterColor = Color.FromArgb(255, 251, 254, 255);
                        coreBrush.SurroundColors = new[] { Color.FromArgb(250, 111, 190, 244) };
                        graphics.FillPath(coreBrush, corePath);
                    }
                }
                using (var coreRing = new Pen(Color.FromArgb(210, 222, 247, 255), 1f))
                    graphics.DrawEllipse(coreRing, core);

                using (var font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var textBrush = new SolidBrush(Color.FromArgb(32, 73, 132)))
                {
                    const string logo = "F";
                    var measured = graphics.MeasureString(logo, font);
                    graphics.DrawString(logo, font, textBrush, (size - measured.Width) / 2f - 0.3f, (size - measured.Height) / 2f - 1.8f);
                }

                var dotAngle = phase * 0.85f;
                var dotX = size * 0.5f + 25.5f * (float)Math.Cos(dotAngle);
                var dotY = size * 0.5f + 8.5f * (float)Math.Sin(dotAngle);
                using (var dot = new SolidBrush(Color.FromArgb(175 + (int)(55 * hover), 218, 249, 255)))
                    graphics.FillEllipse(dot, dotX - 1.6f, dotY - 1.6f, 3.2f, 3.2f);
            }
            return bitmap;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
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
