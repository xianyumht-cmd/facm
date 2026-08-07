using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FACM.Pets
{
    internal sealed class AnimalPetWindow : Form
    {
        private const int PetSize = 150;
        private const int GwlExStyle = -20;
        private const int WsExLayered = 0x00080000;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int UlwAlpha = 0x00000002;
        private const byte AcSrcOver = 0x00;
        private const byte AcSrcAlpha = 0x01;

        private readonly Timer _timer;
        private readonly Random _random = new Random(unchecked(Environment.TickCount * 397));
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private AnimalPetDefinition _pet;
        private Bitmap _artwork;
        private CancellationTokenSource _artCancellation;
        private double _lastTickSeconds;
        private double _stateUntilSeconds;
        private double _lastRenderSeconds;
        private float _phase;
        private float _x;
        private float _y;
        private float _vx;
        private float _vy;
        private float _targetVx;
        private float _targetVy;
        private bool _dragging;
        private bool _moved;
        private Point _dragCursor;
        private Point _dragWindow;
        private bool _facingRight = true;
        private bool _disposed;
        private bool _positionInitialized;

        public AnimalPetWindow(AnimalPetDefinition pet)
        {
            _pet = pet ?? AnimalPetCatalog.Get(AnimalPetCatalog.DefaultPetId);
            Text = "FACM 桌面宠物";
            ShowInTaskbar = false;
            TopMost = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(PetSize, PetSize);
            MinimumSize = MaximumSize = Size;
            BackColor = Color.Black;
            TransparencyKey = Color.Empty;
            DoubleBuffered = false;

            MouseDown += HandleMouseDown;
            MouseMove += HandleMouseMove;
            MouseUp += HandleMouseUp;
            Shown += async delegate
            {
                EnsurePosition();
                ApplyLayeredStyle();
                await LoadArtworkAsync();
                RenderLayered();
            };
            HandleCreated += delegate
            {
                ApplyLayeredStyle();
                RenderLayered();
            };
            FormClosed += delegate { DisposeResources(); };

            _lastTickSeconds = _clock.Elapsed.TotalSeconds;
            _lastRenderSeconds = _lastTickSeconds;
            _timer = new Timer { Interval = 16 };
            _timer.Tick += Tick;
            _timer.Start();
            ChooseNewMotion(true);
        }

        public event EventHandler PetClicked;
        public event EventHandler PetRightClicked;

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public string PetId
        {
            get { return _pet == null ? string.Empty : _pet.Id; }
        }

        public async void SetPet(AnimalPetDefinition pet)
        {
            _pet = pet ?? AnimalPetCatalog.Get(AnimalPetCatalog.DefaultPetId);
            _phase = 0f;
            ChooseNewMotion(true);
            await LoadArtworkAsync();
            RenderLayered();
        }

        public void ResetToPrimaryScreen()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            var x = area.Left + (area.Width - Width) / 2;
            var y = area.Top + (area.Height - Height) / 2;
            SetPetLocation(x, y);
            _vx = 0f;
            _vy = 0f;
            ChooseNewMotion(true);
            TopMost = true;
            if (!Visible) Show();
            BringToFront();
            RenderLayered();
        }

        internal static Bitmap RenderForSmokeTest(AnimalPetDefinition pet, Bitmap artwork, float phase, bool facingRight)
        {
            return RenderPet(pet ?? AnimalPetCatalog.Get(AnimalPetCatalog.DefaultPetId), artwork, PetSize, phase, facingRight);
        }

        private async Task LoadArtworkAsync()
        {
            if (_disposed || _pet == null) return;
            if (_artCancellation != null)
            {
                _artCancellation.Cancel();
                _artCancellation.Dispose();
            }
            _artCancellation = new CancellationTokenSource();
            var token = _artCancellation.Token;
            var expectedId = _pet.Id;
            Bitmap loaded = null;
            try
            {
                loaded = await AnimalPetArtService.LoadAsync(_pet, token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                loaded = null;
            }

            if (_disposed || token.IsCancellationRequested || _pet == null || !string.Equals(_pet.Id, expectedId, StringComparison.OrdinalIgnoreCase))
            {
                if (loaded != null) loaded.Dispose();
                return;
            }

            var old = _artwork;
            _artwork = loaded;
            if (old != null) old.Dispose();
        }

        private void EnsurePosition()
        {
            if (_positionInitialized) return;
            ResetToPrimaryScreen();
        }

        private void Tick(object sender, EventArgs e)
        {
            if (_disposed || !Visible || _pet == null) return;
            var now = _clock.Elapsed.TotalSeconds;
            var delta = now - _lastTickSeconds;
            _lastTickSeconds = now;
            if (delta <= 0) return;
            if (delta > 0.050) delta = 0.050;

            var dt = (float)delta;
            var phaseSpeed = _pet.Motion == AnimalMotionStyle.Fly ? 8.0f : 6.2f;
            _phase += dt * phaseSpeed;
            if (_phase > Math.PI * 200f) _phase = 0f;

            if (!_dragging)
            {
                if (now >= _stateUntilSeconds) ChooseNewMotion(false);
                SmoothVelocity(dt);
                MoveOneFrame(dt);
            }

            if (now - _lastRenderSeconds >= 1D / 30D)
            {
                _lastRenderSeconds = now;
                RenderLayered();
            }
        }

        private void ChooseNewMotion(bool forceMove)
        {
            var idle = !forceMove && _random.NextDouble() < 0.14;
            var now = _clock.Elapsed.TotalSeconds;
            var duration = idle ? 0.65 + _random.NextDouble() * 1.4 : 2.0 + _random.NextDouble() * 3.8;
            _stateUntilSeconds = now + duration;
            if (idle)
            {
                _targetVx = 0f;
                _targetVy = 0f;
                return;
            }

            var angle = _random.NextDouble() * Math.PI * 2.0;
            var pixelsPerSecond = (34f + (float)_random.NextDouble() * 22f) * Math.Max(0.38f, _pet.Speed);
            _targetVx = (float)Math.Cos(angle) * pixelsPerSecond;
            _targetVy = (float)Math.Sin(angle) * pixelsPerSecond;

            switch (_pet.Motion)
            {
                case AnimalMotionStyle.Walk: _targetVy *= 0.48f; break;
                case AnimalMotionStyle.Hop: _targetVy *= 0.54f; break;
                case AnimalMotionStyle.Crawl: _targetVx *= 0.72f; _targetVy *= 0.38f; break;
                case AnimalMotionStyle.Waddle: _targetVx *= 0.76f; _targetVy *= 0.42f; break;
                case AnimalMotionStyle.Fly: _targetVx *= 1.12f; _targetVy *= 0.92f; break;
            }

            if (Math.Abs(_targetVx) < 18f)
                _targetVx = _random.Next(0, 2) == 0 ? -22f : 22f;
            _facingRight = _targetVx >= 0f;
        }

        private void SmoothVelocity(float dt)
        {
            var response = _pet.Motion == AnimalMotionStyle.Fly ? 3.2f : 4.8f;
            var blend = 1f - (float)Math.Exp(-response * dt);
            _vx += (_targetVx - _vx) * blend;
            _vy += (_targetVy - _vy) * blend;
            if (Math.Abs(_vx) > 2f) _facingRight = _vx >= 0f;
        }

        private void MoveOneFrame(float dt)
        {
            EnsurePosition();
            var flyDrift = _pet.Motion == AnimalMotionStyle.Fly ? (float)Math.Sin(_phase * 0.72f) * 5.5f : 0f;
            _x += _vx * dt;
            _y += (_vy + flyDrift) * dt;

            var center = new Point((int)Math.Round(_x + Width / 2f), (int)Math.Round(_y + Height / 2f));
            var area = Screen.FromPoint(center).WorkingArea;
            var outsideX = Width * 0.22f;
            var outsideY = Height * 0.16f;
            var minX = area.Left - outsideX;
            var maxX = area.Right - Width + outsideX;
            var minY = area.Top - outsideY;
            var maxY = area.Bottom - Height + outsideY;
            var bounced = false;

            if (_x < minX)
            {
                _x = minX;
                _vx = Math.Abs(_vx);
                _targetVx = Math.Max(24f, Math.Abs(_targetVx));
                _facingRight = true;
                bounced = true;
            }
            else if (_x > maxX)
            {
                _x = maxX;
                _vx = -Math.Abs(_vx);
                _targetVx = -Math.Max(24f, Math.Abs(_targetVx));
                _facingRight = false;
                bounced = true;
            }

            if (_y < minY)
            {
                _y = minY;
                _vy = Math.Abs(_vy);
                _targetVy = Math.Abs(_targetVy);
                bounced = true;
            }
            else if (_y > maxY)
            {
                _y = maxY;
                _vy = -Math.Abs(_vy);
                _targetVy = -Math.Abs(_targetVy);
                bounced = true;
            }

            if (bounced) _stateUntilSeconds = Math.Min(_stateUntilSeconds, _clock.Elapsed.TotalSeconds + 1.2);

            var next = new Point((int)Math.Round(_x), (int)Math.Round(_y));
            if (next != Location) Location = next;
        }

        private void SetPetLocation(int x, int y)
        {
            _x = x;
            _y = y;
            _positionInitialized = true;
            Location = new Point(x, y);
        }

        private void HandleMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var right = PetRightClicked;
                if (right != null) right(this, EventArgs.Empty);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _moved = false;
            _dragCursor = Cursor.Position;
            _dragWindow = Location;
            Capture = true;
        }

        private void HandleMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var cursor = Cursor.Position;
            var dx = cursor.X - _dragCursor.X;
            var dy = cursor.Y - _dragCursor.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 4) _moved = true;
            SetPetLocation(_dragWindow.X + dx, _dragWindow.Y + dy);
        }

        private void HandleMouseUp(object sender, MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _dragging = false;
            Capture = false;
            _vx = 0f;
            _vy = 0f;
            _stateUntilSeconds = _clock.Elapsed.TotalSeconds + 0.55;
            if (_moved)
            {
                KeepMostlyVisible();
                return;
            }
            var clicked = PetClicked;
            if (clicked != null) clicked(this, EventArgs.Empty);
        }

        private void KeepMostlyVisible()
        {
            var center = new Point(Left + Width / 2, Top + Height / 2);
            var area = Screen.FromPoint(center).WorkingArea;
            var marginX = Width / 4;
            var marginY = Height / 5;
            var x = Math.Max(area.Left - marginX, Math.Min(Left, area.Right - Width + marginX));
            var y = Math.Max(area.Top - marginY, Math.Min(Top, area.Bottom - Height + marginY));
            SetPetLocation(x, y);
        }

        private void ApplyLayeredStyle()
        {
            if (_disposed || !IsHandleCreated) return;
            Region = null;
            var style = GetWindowLong(Handle, GwlExStyle);
            style |= WsExLayered | WsExToolWindow | WsExNoActivate;
            SetWindowLong(Handle, GwlExStyle, style);
        }

        private void RenderLayered()
        {
            if (_disposed || !IsHandleCreated || !Visible || _pet == null) return;
            using (var bitmap = RenderPet(_pet, _artwork, Math.Min(Width, Height), _phase, _facingRight))
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
                    var destination = new PointNative(Left, Top);
                    var source = new PointNative(0, 0);
                    var size = new SizeNative(bitmap.Width, bitmap.Height);
                    var blend = new BlendFunction { BlendOp = AcSrcOver, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AcSrcAlpha };
                    UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, UlwAlpha);
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

        private static Bitmap RenderPet(AnimalPetDefinition pet, Bitmap artwork, int size, float phase, bool facingRight)
        {
            size = Math.Max(110, size);
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                var bob = MotionBob(pet.Motion, phase);
                var tilt = MotionTilt(pet.Motion, phase);
                var squash = MotionSquash(pet.Motion, phase);
                DrawShadow(g, size, pet.Motion, phase);

                g.TranslateTransform(size / 2f, size / 2f + bob);
                if (!facingRight) g.ScaleTransform(-1f, 1f);
                g.RotateTransform(tilt);
                g.ScaleTransform(1f + squash, 1f - squash * 0.72f);

                if (artwork != null)
                {
                    var scale = Math.Max(0.55f, Math.Min(1.05f, pet.VisualScale));
                    var maxSide = size * scale;
                    var ratio = Math.Min(maxSide / artwork.Width, maxSide / artwork.Height);
                    var width = artwork.Width * ratio;
                    var height = artwork.Height * ratio;
                    g.DrawImage(artwork, new RectangleF(-width / 2f, -height / 2f - 4f, width, height));
                }
                else
                {
                    DrawFallback(g);
                }
            }
            return bitmap;
        }

        private static float MotionBob(AnimalMotionStyle style, float phase)
        {
            switch (style)
            {
                case AnimalMotionStyle.Hop: return -Math.Abs((float)Math.Sin(phase * 0.82f)) * 10f + 2f;
                case AnimalMotionStyle.Fly: return (float)Math.Sin(phase * 0.70f) * 5f - 5f;
                case AnimalMotionStyle.Waddle: return -(float)Math.Abs(Math.Sin(phase * 0.95f)) * 2.2f;
                case AnimalMotionStyle.Crawl: return -(float)Math.Abs(Math.Sin(phase * 0.65f)) * 0.8f;
                default: return -(float)Math.Abs(Math.Sin(phase * 0.95f)) * 1.8f;
            }
        }

        private static float MotionTilt(AnimalMotionStyle style, float phase)
        {
            switch (style)
            {
                case AnimalMotionStyle.Waddle: return (float)Math.Sin(phase * 0.92f) * 3.6f;
                case AnimalMotionStyle.Fly: return (float)Math.Sin(phase * 0.60f) * 2.4f;
                case AnimalMotionStyle.Walk: return (float)Math.Sin(phase * 0.95f) * 1.2f;
                default: return 0f;
            }
        }

        private static float MotionSquash(AnimalMotionStyle style, float phase)
        {
            if (style == AnimalMotionStyle.Hop) return (float)Math.Sin(phase * 1.64f) * 0.025f;
            if (style == AnimalMotionStyle.Waddle) return (float)Math.Sin(phase * 0.92f) * 0.012f;
            return 0f;
        }

        private static void DrawShadow(Graphics g, int size, AnimalMotionStyle style, float phase)
        {
            var fly = style == AnimalMotionStyle.Fly;
            var width = fly ? 42f : 68f;
            var y = size * 0.83f;
            var pulse = fly ? 0.85f + 0.10f * (float)Math.Sin(phase * 0.7f) : 1f;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(size / 2f - width * pulse / 2f, y, width * pulse, fly ? 8f : 10f);
                using (var shadow = new PathGradientBrush(path))
                {
                    shadow.CenterColor = Color.FromArgb(fly ? 24 : 42, 22, 35, 52);
                    shadow.SurroundColors = new[] { Color.FromArgb(0, 22, 35, 52) };
                    g.FillPath(shadow, path);
                }
            }
        }

        private static void DrawFallback(Graphics g)
        {
            using (var bubble = new SolidBrush(Color.FromArgb(238, 245, 248, 252)))
            using (var accent = new SolidBrush(Color.FromArgb(220, 75, 110, 156)))
            {
                g.FillEllipse(bubble, -38f, -38f, 76f, 76f);
                g.FillEllipse(accent, -18f, -4f, 14f, 18f);
                g.FillEllipse(accent, 4f, -4f, 14f, 18f);
                g.FillEllipse(accent, -8f, 10f, 16f, 18f);
                g.FillEllipse(accent, -25f, -23f, 13f, 15f);
                g.FillEllipse(accent, 12f, -23f, 13f, 15f);
            }
        }

        private void DisposeResources()
        {
            if (_disposed) return;
            _disposed = true;
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); }
            if (_artCancellation != null) { _artCancellation.Cancel(); _artCancellation.Dispose(); _artCancellation = null; }
            if (_artwork != null) { _artwork.Dispose(); _artwork = null; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PointNative { public int X; public int Y; public PointNative(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)]
        private struct SizeNative { public int CX; public int CY; public SizeNative(int cx, int cy) { CX = cx; CY = cy; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

        [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PointNative pptDst, ref SizeNative psize, IntPtr hdcSrc, ref PointNative pprSrc, int crKey, ref BlendFunction pblend, int dwFlags);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteObject(IntPtr hObject);
    }
}
