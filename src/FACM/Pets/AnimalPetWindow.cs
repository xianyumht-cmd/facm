using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FACM.Pets
{
    internal sealed class AnimalPetWindow : Form
    {
        private const int PetSize = 132;
        private const int GwlExStyle = -20;
        private const int WsExLayered = 0x00080000;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int UlwAlpha = 0x00000002;
        private const byte AcSrcOver = 0x00;
        private const byte AcSrcAlpha = 0x01;

        private readonly Timer _timer;
        private readonly Random _random = new Random(unchecked(Environment.TickCount * 397));
        private AnimalPetDefinition _pet;
        private float _phase;
        private float _x;
        private float _y;
        private float _vx;
        private float _vy;
        private DateTime _stateUntilUtc;
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
            Shown += delegate
            {
                EnsurePosition();
                ApplyLayeredStyle();
                RenderLayered();
            };
            HandleCreated += delegate
            {
                ApplyLayeredStyle();
                RenderLayered();
            };

            _timer = new Timer { Interval = 40 };
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

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // UpdateLayeredWindow owns all visible pixels.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // UpdateLayeredWindow owns all visible pixels.
        }

        public string PetId
        {
            get { return _pet == null ? string.Empty : _pet.Id; }
        }

        public void SetPet(AnimalPetDefinition pet)
        {
            _pet = pet ?? AnimalPetCatalog.Get(AnimalPetCatalog.DefaultPetId);
            _phase = 0f;
            ChooseNewMotion(true);
            RenderLayered();
        }

        public void ResetToPrimaryScreen()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            var x = area.Left + (area.Width - Width) / 2;
            var y = area.Top + (area.Height - Height) / 2;
            SetPetLocation(x, y);
            ChooseNewMotion(true);
            TopMost = true;
            if (!Visible) Show();
            BringToFront();
            RenderLayered();
        }

        internal static Bitmap RenderForSmokeTest(AnimalPetDefinition pet, float phase, bool facingRight)
        {
            return RenderPet(pet ?? AnimalPetCatalog.Get(AnimalPetCatalog.DefaultPetId), PetSize, phase, facingRight);
        }

        private void EnsurePosition()
        {
            if (_positionInitialized) return;
            ResetToPrimaryScreen();
        }

        private void Tick(object sender, EventArgs e)
        {
            if (_disposed || !Visible || _pet == null) return;
            _phase += 0.13f;
            if (_phase > Math.PI * 200f) _phase = 0f;

            if (!_dragging)
            {
                if (DateTime.UtcNow >= _stateUntilUtc) ChooseNewMotion(false);
                MoveOneFrame();
            }
            RenderLayered();
        }

        private void ChooseNewMotion(bool forceMove)
        {
            var idle = !forceMove && _random.NextDouble() < 0.16;
            var duration = idle ? 0.8 + _random.NextDouble() * 1.5 : 1.7 + _random.NextDouble() * 3.5;
            _stateUntilUtc = DateTime.UtcNow.AddSeconds(duration);
            if (idle)
            {
                _vx = 0f;
                _vy = 0f;
                return;
            }

            var angle = _random.NextDouble() * Math.PI * 2.0;
            var baseSpeed = 1.05f + (float)_random.NextDouble() * 0.95f;
            var speed = baseSpeed * Math.Max(0.35f, _pet.Speed);
            _vx = (float)Math.Cos(angle) * speed;
            _vy = (float)Math.Sin(angle) * speed;

            switch (_pet.Motion)
            {
                case AnimalMotionStyle.Walk:
                    _vy *= 0.62f;
                    break;
                case AnimalMotionStyle.Hop:
                    _vy *= 0.70f;
                    break;
                case AnimalMotionStyle.Crawl:
                    _vx *= 0.72f;
                    _vy *= 0.52f;
                    break;
                case AnimalMotionStyle.Waddle:
                    _vx *= 0.78f;
                    _vy *= 0.55f;
                    break;
                case AnimalMotionStyle.Fly:
                    _vx *= 1.05f;
                    _vy *= 1.05f;
                    break;
            }

            if (Math.Abs(_vx) < 0.38f) _vx = _random.Next(0, 2) == 0 ? -0.55f : 0.55f;
            _facingRight = _vx >= 0f;
        }

        private void MoveOneFrame()
        {
            EnsurePosition();
            _x += _vx;
            _y += _vy;

            var center = new Point((int)Math.Round(_x + Width / 2f), (int)Math.Round(_y + Height / 2f));
            var area = Screen.FromPoint(center).WorkingArea;
            var outsideX = Width * 0.24f;
            var outsideY = Height * 0.18f;
            var minX = area.Left - outsideX;
            var maxX = area.Right - Width + outsideX;
            var minY = area.Top - outsideY;
            var maxY = area.Bottom - Height + outsideY;

            if (_x < minX)
            {
                _x = minX;
                _vx = Math.Abs(_vx) + 0.25f;
                _facingRight = true;
            }
            else if (_x > maxX)
            {
                _x = maxX;
                _vx = -Math.Abs(_vx) - 0.25f;
                _facingRight = false;
            }

            if (_y < minY)
            {
                _y = minY;
                _vy = Math.Abs(_vy) + 0.18f;
            }
            else if (_y > maxY)
            {
                _y = maxY;
                _vy = -Math.Abs(_vy) - 0.18f;
            }

            Location = new Point((int)Math.Round(_x), (int)Math.Round(_y));
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
                var handler = PetRightClicked;
                if (handler != null) handler(this, EventArgs.Empty);
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
            RenderLayered();
        }

        private void HandleMouseUp(object sender, MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _dragging = false;
            Capture = false;
            _stateUntilUtc = DateTime.UtcNow.AddMilliseconds(650);
            if (_moved)
            {
                KeepMostlyVisible();
                return;
            }
            var handler = PetClicked;
            if (handler != null) handler(this, EventArgs.Empty);
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
            using (var bitmap = RenderPet(_pet, Math.Min(Width, Height), _phase, _facingRight))
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
                    var blend = new BlendFunction
                    {
                        BlendOp = AcSrcOver,
                        BlendFlags = 0,
                        SourceConstantAlpha = 255,
                        AlphaFormat = AcSrcAlpha
                    };
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

        private static Bitmap RenderPet(AnimalPetDefinition pet, int size, float phase, bool facingRight)
        {
            size = Math.Max(96, size);
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

                var bounce = GetBounce(pet.Motion, phase);
                var legSwing = (float)Math.Sin(phase * 1.8f);
                var wing = 0.5f + 0.5f * (float)Math.Sin(phase * 2.8f);

                g.TranslateTransform(size / 2f, size / 2f);
                if (!facingRight) g.ScaleTransform(-1f, 1f);
                g.TranslateTransform(-size / 2f, -size / 2f + bounce);

                DrawShadow(g, size, pet.Motion, phase);
                switch (pet.Id)
                {
                    case "shiba": DrawDog(g, size, pet, legSwing); break;
                    case "rabbit": DrawRabbit(g, size, pet, legSwing); break;
                    case "hamster": DrawHamster(g, size, pet, legSwing); break;
                    case "fox": DrawFox(g, size, pet, legSwing); break;
                    case "panda": DrawPanda(g, size, pet, legSwing); break;
                    case "chick": DrawChick(g, size, pet, legSwing, wing); break;
                    case "penguin": DrawPenguin(g, size, pet, legSwing); break;
                    case "turtle": DrawTurtle(g, size, pet, legSwing); break;
                    case "butterfly": DrawButterfly(g, size, pet, wing); break;
                    default: DrawCat(g, size, pet, legSwing); break;
                }
            }
            return bitmap;
        }

        private static float GetBounce(AnimalMotionStyle style, float phase)
        {
            switch (style)
            {
                case AnimalMotionStyle.Hop:
                    return -Math.Abs((float)Math.Sin(phase * 1.25f)) * 13f;
                case AnimalMotionStyle.Fly:
                    return (float)Math.Sin(phase * 0.9f) * 5f - 5f;
                case AnimalMotionStyle.Waddle:
                    return Math.Abs((float)Math.Sin(phase * 1.2f)) * -2.2f;
                default:
                    return Math.Abs((float)Math.Sin(phase * 1.6f)) * -1.6f;
            }
        }

        private static void DrawShadow(Graphics g, int size, AnimalMotionStyle style, float phase)
        {
            var alpha = style == AnimalMotionStyle.Fly ? 22 : 48;
            var width = style == AnimalMotionStyle.Fly ? 38f : 61f;
            var y = style == AnimalMotionStyle.Fly ? size * 0.88f : size * 0.84f;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(size * 0.5f - width / 2f, y, width, 10f);
                using (var shadow = new PathGradientBrush(path))
                {
                    shadow.CenterColor = Color.FromArgb(alpha, 31, 49, 68);
                    shadow.SurroundColors = new[] { Color.FromArgb(0, 31, 49, 68) };
                    g.FillPath(shadow, path);
                }
            }
        }

        private static void DrawCat(Graphics g, int s, AnimalPetDefinition p, float swing)
        {
            var body = new RectangleF(31, 60, 65, 47);
            FillEllipse(g, p.Primary, body);
            DrawTail(g, p.Primary, new PointF(88, 78), new PointF(116, 57 + swing * 5f), 11f);
            DrawLegPair(g, p.Secondary, swing, 44, 94, 84, 94);
            FillEllipse(g, p.Primary, new RectangleF(38, 30, 52, 48));
            FillTriangle(g, p.Primary, new PointF(43, 39), new PointF(48, 15), new PointF(62, 34));
            FillTriangle(g, p.Primary, new PointF(70, 33), new PointF(83, 15), new PointF(87, 42));
            FillTriangle(g, Color.FromArgb(245, 221, 170), new PointF(48, 34), new PointF(50, 22), new PointF(58, 34));
            FillTriangle(g, Color.FromArgb(245, 221, 170), new PointF(75, 33), new PointF(81, 22), new PointF(82, 36));
            DrawFace(g, 52, 52, p.Accent, true);
            using (var pen = new Pen(Color.FromArgb(150, p.Accent), 1.4f))
            {
                g.DrawLine(pen, 45, 59, 27, 55);
                g.DrawLine(pen, 45, 63, 26, 65);
                g.DrawLine(pen, 82, 59, 99, 55);
                g.DrawLine(pen, 82, 63, 100, 66);
            }
        }

        private static void DrawDog(Graphics g, int s, AnimalPetDefinition p, float swing)
        {
            FillEllipse(g, p.Primary, new RectangleF(29, 61, 69, 45));
            DrawTail(g, p.Primary, new PointF(91, 73), new PointF(114, 54 - swing * 4f), 12f);
            DrawLegPair(g, p.Secondary, swing, 43, 94, 82, 94);
            FillEllipse(g, p.Primary, new RectangleF(38, 29, 54, 52));
            FillEllipse(g, Darken(p.Primary, 0.77f), new RectangleF(29, 34, 20, 35));
            FillEllipse(g, Darken(p.Primary, 0.77f), new RectangleF(82, 34, 20, 35));
            FillEllipse(g, p.Secondary, new RectangleF(49, 55, 34, 22));
            DrawFace(g, 53, 50, p.Accent, false);
            FillEllipse(g, p.Accent, new RectangleF(63, 61, 8, 6));
        }

        private static void DrawRabbit(Graphics g, int s, AnimalPetDefinition p, float swing)
        {
            FillEllipse(g, p.Primary, new RectangleF(32, 61, 67, 47));
            FillEllipse(g, p.Secondary, new RectangleF(91, 68, 22, 22));
            DrawLegPair(g, Color.FromArgb(232, 234, 239), swing, 45, 96, 82, 96);
            FillEllipse(g, p.Primary, new RectangleF(41, 35, 52, 47));
            FillRoundedEar(g, p.Primary, p.Secondary, new RectangleF(47, 7, 15, 39), -6f);
            FillRoundedEar(g, p.Primary, p.Secondary, new RectangleF(70, 5, 15, 42), 5f);
            DrawFace(g, 55, 54, p.Accent, true);
            FillEllipse(g, Color.FromArgb(242, 142, 166), new RectangleF(66, 62, 6, 5));
        }

        private static void DrawHamster(Graphics g, int s, AnimalPetDefinition p, float swing)
        {
            FillEllipse(g, p.Primary, new RectangleF(31, 48, 72, 63));
            FillEllipse(g, p.Secondary, new RectangleF(42, 59, 49, 44));
            FillEllipse(g, p.Primary, new RectangleF(37, 29, 58, 53));
            FillEllipse(g, p.Secondary, new RectangleF(40, 28, 16, 16));
            FillEllipse(g, p.Secondary, new RectangleF(79, 28, 16, 16));
            FillEllipse(g, Color.FromArgb(245, 167, 160), new RectangleF(44, 58, 11, 9));
            FillEllipse(g, Color.FromArgb(245, 167, 160), new RectangleF(79, 58, 11, 9));
            DrawFace(g, 54, 49, p.Accent, true);
            FillEllipse(g, p.Secondary, new RectangleF(43 + swing * 2f, 101, 18, 8));
            FillEllipse(g, p.Secondary, new RectangleF(77 - swing * 2f, 101, 18, 8));
        }

        private static void DrawFox(Graphics g, int s, AnimalPetDefinition p, float swing)
        {
            FillEllipse(g, p.Primary, new RectangleF(31, 60, 64, 46));
            using (var tail = new GraphicsPath())
            {
                tail.AddBezier(89, 83, 108, 96, 123, 77, 111, 55);
                tail.AddBezier(111, 55, 100, 64, 98, 70, 89, 83);
                g.FillPath(new SolidBrush(p.Primary), tail);
                using (var tip = new SolidBrush(p.Secondary)) g.FillEllipse(tip, 104, 55, 16, 17);
            }
            DrawLegPair(g, Darken(p.Primary, 0.78f), swing, 44, 94, 81, 94);
            FillEllipse(g, p.Primary, new RectangleF(39, 29, 54, 49));
            FillTriangle(g, p.Primary, new PointF(43, 39), new PointF(50, 12), new PointF(63, 34));
            FillTriangle(g, p.Primary, new PointF(72, 34), new PointF(84, 12), new PointF(89, 42));
            FillEllipse(g, p.Secondary, new RectangleF(51, 54, 34, 23));
            DrawFace(g, 54, 49, p.Accent, false);
            FillEllipse(g, p.Accent, new RectangleF(78, 62, 7, 5));
        }

        private static void DrawPanda(Graphics g, int s, AnimalPetDefinition p, float swing)
        {
            FillEllipse(g, p.Secondary, new RectangleF(31, 59, 69, 50));
            FillEllipse(g, p.Primary, new RectangleF(39, 31, 55, 53));
            FillEllipse(g, p.Secondary, new RectangleF(36, 25, 19, 19));
            FillEllipse(g, p.Secondary, new RectangleF(79, 25, 19, 19));
            FillEllipse(g, p.Secondary, new RectangleF(50, 47, 14, 19));
            FillEllipse(g, p.Secondary, new RectangleF(72, 47, 14, 19));
            FillEllipse(g, Color.White, new RectangleF(55, 52, 4, 5));
            FillEllipse(g, Color.White, new RectangleF(77, 52, 4, 5));
            FillEllipse(g, p.Secondary, new RectangleF(64, 65, 8, 6));
            FillEllipse(g, p.Secondary, new RectangleF(36 + swing * 2f, 96, 24, 13));
            FillEllipse(g, p.Secondary, new RectangleF(77 - swing * 2f, 96, 24, 13));
        }

        private static void DrawChick(Graphics g, int s, AnimalPetDefinition p, float swing, float wing)
        {
            FillEllipse(g, p.Primary, new RectangleF(37, 42, 60, 66));
            FillEllipse(g, p.Secondary, new RectangleF(46, 27, 46, 45));
            FillEllipse(g, Darken(p.Primary, 0.92f), new RectangleF(29, 62 - wing * 5f, 25, 30));
            FillEllipse(g, Darken(p.Primary, 0.92f), new RectangleF(84, 62 - wing * 5f, 25, 30));
            FillTriangle(g, p.Accent, new PointF(83, 52), new PointF(101, 58), new PointF(83, 63));
            FillEllipse(g, Color.FromArgb(72, 66, 51), new RectangleF(58, 45, 5, 7));
            FillEllipse(g, Color.FromArgb(72, 66, 51), new RectangleF(77, 45, 5, 7));
            using (var pen = new Pen(p.Accent, 3f))
            {
                g.DrawLine(pen, 57, 103, 54 + swing * 3f, 113);
                g.DrawLine(pen, 78, 103, 81 - swing * 3f, 113);
            }
        }

        private static void DrawPenguin(Graphics g, int s, AnimalPetDefinition p, float swing)
        {
            FillEllipse(g, p.Primary, new RectangleF(35, 34, 65, 76));
            FillEllipse(g, p.Secondary, new RectangleF(48, 47, 41, 56));
            FillEllipse(g, p.Primary, new RectangleF(42, 25, 50, 48));
            FillEllipse(g, Color.White, new RectangleF(54, 45, 7, 9));
            FillEllipse(g, Color.White, new RectangleF(75, 45, 7, 9));
            FillEllipse(g, Color.FromArgb(52, 59, 65), new RectangleF(56, 48, 4, 5));
            FillEllipse(g, Color.FromArgb(52, 59, 65), new RectangleF(77, 48, 4, 5));
            FillTriangle(g, p.Accent, new PointF(68, 57), new PointF(85, 62), new PointF(68, 67));
            FillEllipse(g, p.Accent, new RectangleF(42 + swing * 3f, 102, 25, 10));
            FillEllipse(g, p.Accent, new RectangleF(74 - swing * 3f, 102, 25, 10));
        }

        private static void DrawTurtle(Graphics g, int s, AnimalPetDefinition p, float swing)
        {
            FillEllipse(g, p.Secondary, new RectangleF(29, 56, 72, 48));
            FillEllipse(g, p.Primary, new RectangleF(36, 60, 57, 39));
            using (var pen = new Pen(Color.FromArgb(100, p.Accent), 2f))
            {
                g.DrawArc(pen, 45, 67, 38, 25, 205, 130);
                g.DrawLine(pen, 64, 61, 64, 99);
            }
            FillEllipse(g, p.Primary, new RectangleF(91, 68, 25, 22));
            FillEllipse(g, p.Accent, new RectangleF(104, 74, 4, 5));
            FillEllipse(g, p.Primary, new RectangleF(33 + swing * 2f, 94, 20, 10));
            FillEllipse(g, p.Primary, new RectangleF(78 - swing * 2f, 94, 20, 10));
            FillEllipse(g, p.Primary, new RectangleF(34 - swing * 2f, 55, 18, 9));
            FillEllipse(g, p.Primary, new RectangleF(78 + swing * 2f, 55, 18, 9));
        }

        private static void DrawButterfly(Graphics g, int s, AnimalPetDefinition p, float wing)
        {
            var wingScale = 0.68f + wing * 0.32f;
            var topH = 42f * wingScale;
            var lowerH = 30f * wingScale;
            using (var left = new GraphicsPath())
            {
                left.AddBezier(64, 63, 45, 24, 18, 28, 31, 63);
                left.AddBezier(31, 63, 21, 87, 45, 101, 64, 72);
                using (var brush = new PathGradientBrush(left))
                {
                    brush.CenterColor = Color.FromArgb(235, p.Secondary);
                    brush.SurroundColors = new[] { Color.FromArgb(220, p.Primary) };
                    g.FillPath(brush, left);
                }
            }
            using (var right = new GraphicsPath())
            {
                right.AddBezier(68, 63, 87, 24, 114, 28, 101, 63);
                right.AddBezier(101, 63, 111, 87, 87, 101, 68, 72);
                using (var brush = new PathGradientBrush(right))
                {
                    brush.CenterColor = Color.FromArgb(235, p.Secondary);
                    brush.SurroundColors = new[] { Color.FromArgb(220, p.Primary) };
                    g.FillPath(brush, right);
                }
            }
            using (var spot = new SolidBrush(Color.FromArgb(135, 255, 255, 255)))
            {
                g.FillEllipse(spot, 35, 47, 13, 13);
                g.FillEllipse(spot, 84, 47, 13, 13);
                g.FillEllipse(spot, 42, 73, 10, 10);
                g.FillEllipse(spot, 80, 73, 10, 10);
            }
            using (var body = new SolidBrush(p.Accent)) g.FillEllipse(body, 61, 43, 11, 48);
            using (var pen = new Pen(Color.FromArgb(190, p.Accent), 1.6f))
            {
                g.DrawBezier(pen, 65, 47, 59, 32 - topH * 0.05f, 51, 29, 48, 28);
                g.DrawBezier(pen, 68, 47, 74, 32 - lowerH * 0.05f, 82, 29, 85, 28);
            }
        }

        private static void DrawFace(Graphics g, float x, float y, Color dark, bool smile)
        {
            FillEllipse(g, dark, new RectangleF(x, y, 5.5f, 7f));
            FillEllipse(g, dark, new RectangleF(x + 22f, y, 5.5f, 7f));
            FillEllipse(g, Color.FromArgb(245, 255, 255, 255), new RectangleF(x + 1.2f, y + 0.8f, 1.7f, 2f));
            FillEllipse(g, Color.FromArgb(245, 255, 255, 255), new RectangleF(x + 23.2f, y + 0.8f, 1.7f, 2f));
            FillEllipse(g, dark, new RectangleF(x + 11f, y + 10f, 6f, 4.5f));
            if (smile)
            {
                using (var pen = new Pen(Color.FromArgb(150, dark), 1.2f))
                {
                    g.DrawArc(pen, x + 8f, y + 11f, 8f, 8f, 5, 75);
                    g.DrawArc(pen, x + 15f, y + 11f, 8f, 8f, 100, 75);
                }
            }
        }

        private static void DrawLegPair(Graphics g, Color color, float swing, float leftX, float y, float rightX, float rightY)
        {
            var offset = swing * 3.4f;
            FillEllipse(g, color, new RectangleF(leftX + offset, y, 19, 13));
            FillEllipse(g, color, new RectangleF(rightX - offset, rightY, 19, 13));
        }

        private static void DrawTail(Graphics g, Color color, PointF start, PointF end, float width)
        {
            using (var pen = new Pen(color, width))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawBezier(pen, start, new PointF(start.X + 10, start.Y - 5), new PointF(end.X - 7, end.Y + 7), end);
            }
        }

        private static void FillRoundedEar(Graphics g, Color outer, Color inner, RectangleF rect, float tilt)
        {
            var state = g.Save();
            g.TranslateTransform(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            g.RotateTransform(tilt);
            var local = new RectangleF(-rect.Width / 2f, -rect.Height / 2f, rect.Width, rect.Height);
            FillEllipse(g, outer, local);
            FillEllipse(g, inner, new RectangleF(local.X + 4, local.Y + 6, local.Width - 8, local.Height - 12));
            g.Restore(state);
        }

        private static void FillEllipse(Graphics g, Color color, RectangleF rect)
        {
            using (var brush = new SolidBrush(color)) g.FillEllipse(brush, rect);
        }

        private static void FillTriangle(Graphics g, Color color, PointF a, PointF b, PointF c)
        {
            using (var brush = new SolidBrush(color)) g.FillPolygon(brush, new[] { a, b, c });
        }

        private static Color Darken(Color value, float factor)
        {
            factor = Math.Max(0f, Math.Min(1f, factor));
            return Color.FromArgb(value.A, (int)(value.R * factor), (int)(value.G * factor), (int)(value.B * factor));
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing && _timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                }
            }
            base.Dispose(disposing);
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
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PointNative pptDst, ref SizeNative psize, IntPtr hdcSrc, ref PointNative pprSrc, int crKey, ref BlendFunction pblend, int dwFlags);

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
