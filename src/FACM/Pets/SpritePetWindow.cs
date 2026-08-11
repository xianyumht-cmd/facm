using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace FACM.Pets
{
    internal sealed class SpritePetWindow : Form
    {
        private const int PetSize = 164;
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
        private Bitmap _sheet;
        private CancellationTokenSource _loadCancellation;
        private double _lastTickSeconds;
        private double _stateUntilSeconds;
        private double _animationSeconds;
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
        private int _lastFrame = -1;
        private int _lastDirection = -1;
        private bool _lastFacing;

        public SpritePetWindow(AnimalPetDefinition pet)
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
                RenderLayered(true);
                await LoadSpriteAsync();
                RenderLayered(true);
            };
            HandleCreated += delegate
            {
                ApplyLayeredStyle();
                RenderLayered(true);
            };
            FormClosed += delegate { DisposeResources(); };

            _lastTickSeconds = _clock.Elapsed.TotalSeconds;
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
            _animationSeconds = 0;
            _vx = _vy = 0f;
            _lastFrame = -1;
            _lastDirection = -1;
            ChooseNewMotion(true);
            await LoadSpriteAsync();
            RenderLayered(true);
        }

        public void ResetToPrimaryScreen()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            var x = area.Left + (area.Width - Width) / 2;
            var y = area.Top + (area.Height - Height) / 2;
            SetPetLocation(x, y);
            _vx = _vy = 0f;
            ChooseNewMotion(true);
            TopMost = true;
            if (!Visible) Show();
            BringToFront();
            RenderLayered(true);
        }

        internal static Bitmap RenderForSmokeTest(AnimalPetDefinition pet, Bitmap sheet, int frameIndex, int directionRow, bool facingRight)
        {
            return RenderPet(pet ?? AnimalPetCatalog.Get(AnimalPetCatalog.DefaultPetId), sheet, PetSize, frameIndex, directionRow, facingRight);
        }

        internal static int DirectionRowForVector(float vx, float vy)
        {
            if (Math.Abs(vx) < 0.001f && Math.Abs(vy) < 0.001f) return 0;
            var angle = Math.Atan2(vy, vx);
            if (angle < 0) angle += Math.PI * 2.0;
            var octant = (int)Math.Round(angle / (Math.PI / 4.0)) % 8;
            return octant;
        }

        private async Task LoadSpriteAsync()
        {
            if (_disposed || _pet == null) return;
            if (_loadCancellation != null)
            {
                _loadCancellation.Cancel();
                _loadCancellation.Dispose();
            }
            _loadCancellation = new CancellationTokenSource();
            var token = _loadCancellation.Token;
            var expectedId = _pet.Id;
            Bitmap loaded = null;
            try
            {
                loaded = await SpritePetAssetService.LoadAsync(_pet, token).ConfigureAwait(true);
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

            var old = _sheet;
            _sheet = loaded;
            if (old != null) old.Dispose();
            _lastFrame = -1;
            _lastDirection = -1;
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
            _animationSeconds += delta;

            if (!_dragging)
            {
                if (now >= _stateUntilSeconds) ChooseNewMotion(false);
                SmoothVelocity(dt);
                MoveOneFrame(dt);
            }

            var frame = CurrentFrameIndex();
            var direction = CurrentDirectionRow();
            var facing = _facingRight;
            if (frame != _lastFrame || direction != _lastDirection || facing != _lastFacing)
                RenderLayered(false);
        }

        private int CurrentFrameIndex()
        {
            if (_pet == null) return 0;
            var count = Math.Max(1, _pet.FrameCount);
            var fps = Math.Max(1f, _pet.FramesPerSecond);
            if (Math.Abs(_vx) + Math.Abs(_vy) < 3f && _pet.Motion != AnimalMotionStyle.Fly)
                return 0;
            return (int)(_animationSeconds * fps) % count;
        }

        private int CurrentDirectionRow()
        {
            if (_pet == null || !_pet.DirectionalRows) return _pet == null ? 0 : _pet.AnimationRow;
            var vx = Math.Abs(_vx) < 1f ? _targetVx : _vx;
            var vy = Math.Abs(_vy) < 1f ? _targetVy : _vy;
            return DirectionRowForVector(vx, vy);
        }

        private void ChooseNewMotion(bool forceMove)
        {
            if (_pet == null) return;
            var now = _clock.Elapsed.TotalSeconds;
            var fly = _pet.Motion == AnimalMotionStyle.Fly;
            var idleChance = fly ? 0.02 : 0.12;
            var idle = !forceMove && _random.NextDouble() < idleChance;
            var duration = idle
                ? 0.45 + _random.NextDouble() * 1.15
                : fly ? 0.55 + _random.NextDouble() * 1.25 : 1.45 + _random.NextDouble() * 3.0;
            _stateUntilSeconds = now + duration;

            if (idle)
            {
                _targetVx = 0f;
                _targetVy = 0f;
                return;
            }

            var angle = _random.NextDouble() * Math.PI * 2.0;
            float pixelsPerSecond;
            if (fly)
                pixelsPerSecond = (82f + (float)_random.NextDouble() * 58f) * Math.Max(0.55f, _pet.Speed);
            else if (_pet.Motion == AnimalMotionStyle.Crawl)
                pixelsPerSecond = (34f + (float)_random.NextDouble() * 34f) * Math.Max(0.45f, _pet.Speed);
            else
                pixelsPerSecond = (46f + (float)_random.NextDouble() * 34f) * Math.Max(0.45f, _pet.Speed);

            _targetVx = (float)Math.Cos(angle) * pixelsPerSecond;
            _targetVy = (float)Math.Sin(angle) * pixelsPerSecond;

            if (_pet.Motion == AnimalMotionStyle.Walk)
                _targetVy *= 0.30f;
            else if (_pet.Motion == AnimalMotionStyle.Hop)
                _targetVy *= 0.46f;
            else if (_pet.Motion == AnimalMotionStyle.Waddle)
                _targetVy *= 0.34f;

            if (Math.Abs(_targetVx) > 2f) _facingRight = _targetVx >= 0f;
        }

        private void SmoothVelocity(float dt)
        {
            var fly = _pet != null && _pet.Motion == AnimalMotionStyle.Fly;
            var response = fly ? 7.5f : 5.4f;
            var blend = 1f - (float)Math.Exp(-response * dt);
            _vx += (_targetVx - _vx) * blend;
            _vy += (_targetVy - _vy) * blend;
            if (Math.Abs(_vx) > 2f) _facingRight = _vx >= 0f;
        }

        private void MoveOneFrame(float dt)
        {
            EnsurePosition();
            var fly = _pet != null && _pet.Motion == AnimalMotionStyle.Fly;
            var jitterX = fly ? (float)Math.Sin(_animationSeconds * 17.0) * 10f : 0f;
            var jitterY = fly ? (float)Math.Cos(_animationSeconds * 13.0) * 8f : 0f;

            // Free wandering is intentional. A desktop pet may leave every monitor and later wander
            // back in; the explicit "复位桌面位置" command is the recovery path, not an invisible wall.
            _x += (_vx + jitterX) * dt;
            _y += (_vy + jitterY) * dt;

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
            _vx = _vy = 0f;
            _stateUntilSeconds = _clock.Elapsed.TotalSeconds + 0.45;
            if (_moved) return;

            var clicked = PetClicked;
            if (clicked != null) clicked(this, EventArgs.Empty);
        }

        private void ApplyLayeredStyle()
        {
            if (_disposed || !IsHandleCreated) return;
            Region = null;
            var style = GetWindowLong(Handle, GwlExStyle);
            style |= WsExLayered | WsExToolWindow | WsExNoActivate;
            SetWindowLong(Handle, GwlExStyle, style);
        }

        private void RenderLayered(bool force)
        {
            if (_disposed || !IsHandleCreated || !Visible || _pet == null) return;
            var frame = CurrentFrameIndex();
            var direction = CurrentDirectionRow();
            if (!force && frame == _lastFrame && direction == _lastDirection && _facingRight == _lastFacing) return;

            using (var bitmap = RenderPet(_pet, _sheet, Math.Min(Width, Height), frame, direction, _facingRight))
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

            _lastFrame = frame;
            _lastDirection = direction;
            _lastFacing = _facingRight;
        }

        private static Bitmap RenderPet(AnimalPetDefinition pet, Bitmap sheet, int size, int frameIndex, int directionRow, bool facingRight)
        {
            size = Math.Max(120, size);
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = pet.PixelArt ? SmoothingMode.None : SmoothingMode.AntiAlias;
                g.PixelOffsetMode = pet.PixelArt ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
                g.InterpolationMode = pet.PixelArt ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;

                if (sheet == null)
                {
                    DrawLoadingMark(g, size);
                    return bitmap;
                }

                var source = SpritePetAssetService.GetFrameRectangle(pet, sheet, frameIndex, directionRow);
                if (source.Width <= 0 || source.Height <= 0)
                {
                    DrawLoadingMark(g, size);
                    return bitmap;
                }

                if (pet.Motion != AnimalMotionStyle.Fly)
                    DrawShadow(g, size, pet.Motion);

                var maxSide = size * Math.Max(0.45f, Math.Min(1.02f, pet.VisualScale));
                var ratio = Math.Min(maxSide / source.Width, maxSide / source.Height);
                var width = source.Width * ratio;
                var height = source.Height * ratio;
                var y = size / 2f - height / 2f - (pet.Motion == AnimalMotionStyle.Fly ? 4f : 1f);

                g.TranslateTransform(size / 2f, 0f);
                if (!pet.DirectionalRows && !facingRight) g.ScaleTransform(-1f, 1f);
                var destination = new RectangleF(-width / 2f, y, width, height);
                g.DrawImage(sheet, destination, source, GraphicsUnit.Pixel);
            }
            return bitmap;
        }

        private static void DrawShadow(Graphics g, int size, AnimalMotionStyle motion)
        {
            var width = motion == AnimalMotionStyle.Crawl ? 74f : 66f;
            var y = size * 0.82f;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(size / 2f - width / 2f, y, width, 9f);
                using (var shadow = new PathGradientBrush(path))
                {
                    shadow.CenterColor = Color.FromArgb(34, 18, 27, 38);
                    shadow.SurroundColors = new[] { Color.FromArgb(0, 18, 27, 38) };
                    g.FillPath(shadow, path);
                }
            }
        }

        private static void DrawLoadingMark(Graphics g, int size)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(150, 120, 155, 215), 3f))
            {
                g.DrawArc(pen, size / 2f - 16f, size / 2f - 16f, 32f, 32f, -70f, 250f);
            }
        }

        private void DisposeResources()
        {
            if (_disposed) return;
            _disposed = true;
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); }
            if (_loadCancellation != null) { _loadCancellation.Cancel(); _loadCancellation.Dispose(); _loadCancellation = null; }
            if (_sheet != null) { _sheet.Dispose(); _sheet = null; }
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
