using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using FACM.Services;

namespace FACM
{
    internal sealed class MainForm : Form
    {
        private const int BallSize = 68;
        private readonly AppSettings _settings = AppSettings.Load();
        private readonly Timer _animationTimer;
        private readonly NotifyIcon _tray;
        private CompactMenuForm _menu;
        private bool _hovered;
        private bool _dragging;
        private bool _moved;
        private bool _startCleanup;
        private Point _dragCursor;
        private Point _dragWindow;
        private float _hoverProgress;
        private float _pulse;

        public MainForm(bool startCleanup = false)
        {
            _startCleanup = startCleanup;
            Text = "FACM";
            ShowInTaskbar = false;
            TopMost = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(BallSize, BallSize);
            MinimumSize = MaximumSize = Size;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9F);

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "FACM 3.0",
                Visible = true,
                ContextMenuStrip = BuildTrayMenu()
            };
            _tray.DoubleClick += delegate { Show(); Activate(); ToggleMenu(); };

            _animationTimer = new Timer { Interval = 25 };
            _animationTimer.Tick += Animate;
            _animationTimer.Start();

            MouseEnter += delegate { _hovered = true; };
            MouseLeave += delegate { _hovered = false; };
            MouseDown += BeginDrag;
            MouseMove += ContinueDrag;
            MouseUp += EndDrag;
            Shown += HandleShown;
            FormClosed += delegate
            {
                _animationTimer.Stop();
                _tray.Visible = false;
                _tray.Dispose();
                if (_menu != null) _menu.Dispose();
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var inset = 5f - 2.3f * _hoverProgress;
            var bounds = new RectangleF(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);
            using (var shadow = new SolidBrush(Color.FromArgb(75, 0, 0, 0)))
            {
                e.Graphics.FillEllipse(shadow, bounds.X + 2, bounds.Y + 5, bounds.Width, bounds.Height);
            }

            using (var path = new GraphicsPath())
            {
                path.AddEllipse(bounds);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterPoint = new PointF(bounds.X + bounds.Width * 0.35f, bounds.Y + bounds.Height * 0.28f);
                    brush.CenterColor = Color.FromArgb(91, 205, 255);
                    brush.SurroundColors = new[] { Color.FromArgb(45, 79, 219) };
                    e.Graphics.FillPath(brush, path);
                }
            }

            var glowAlpha = 42 + (int)(45 * _hoverProgress) + (int)(9 * Math.Sin(_pulse));
            using (var glow = new Pen(Color.FromArgb(Math.Max(0, Math.Min(110, glowAlpha)), 137, 219, 255), 2.3f))
            {
                e.Graphics.DrawEllipse(glow, bounds);
            }

            using (var inner = new Pen(Color.FromArgb(55, 255, 255, 255), 1f))
            {
                e.Graphics.DrawEllipse(inner, bounds.X + 5, bounds.Y + 5, bounds.Width - 10, bounds.Height - 10);
            }

            using (var font = new Font("Segoe UI", 21F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var textBrush = new SolidBrush(Color.White))
            {
                const string text = "F";
                var size = e.Graphics.MeasureString(text, font);
                e.Graphics.DrawString(text, font, textBrush, (Width - size.Width) / 2f - 1f, (Height - size.Height) / 2f - 3f);
            }

            using (var versionFont = new Font("Segoe UI", 6.6F, FontStyle.Bold, GraphicsUnit.Point))
            using (var versionBrush = new SolidBrush(Color.FromArgb(210, 235, 255)))
            {
                e.Graphics.DrawString("3.0", versionFont, versionBrush, 26f, 45f);
            }

            using (var dot = new SolidBrush(ElevationService.IsAdministrator
                ? Color.FromArgb(92, 224, 166)
                : Color.FromArgb(255, 191, 89)))
            {
                e.Graphics.FillEllipse(dot, Width - 18, 9, 8, 8);
            }
        }

        public void CloseMenu()
        {
            if (_menu == null) return;
            var menu = _menu;
            _menu = null;
            if (!menu.IsDisposed) menu.Close();
        }

        public void ExitApplication()
        {
            CloseMenu();
            Close();
        }

        private void HandleShown(object sender, EventArgs e)
        {
            RestorePosition();
            if (!_startCleanup) return;

            BeginInvoke(new Action(delegate
            {
                if (IsDisposed) return;
                if (_menu == null) ToggleMenu();
                if (_menu != null && !_menu.IsDisposed)
                {
                    _menu.BeginInvoke(new Action(_menu.StartEnvironmentCleanup));
                }
                _startCleanup = false;
            }));
        }

        private ContextMenuStrip BuildTrayMenu()
        {
            var menu = new ContextMenuStrip { Font = new Font("Microsoft YaHei UI", 9F) };
            menu.Items.Add("打开控制中心", null, delegate { Show(); ToggleMenu(); });
            menu.Items.Add("清理环境", null, delegate
            {
                Show();
                if (_menu == null) ToggleMenu();
                if (_menu != null) _menu.BeginInvoke(new Action(_menu.StartEnvironmentCleanup));
            });
            menu.Items.Add("打开日志", null, delegate { OpenLog(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitApplication(); });
            return menu;
        }

        private void Animate(object sender, EventArgs e)
        {
            var target = _hovered || _menu != null ? 1f : 0f;
            _hoverProgress += (target - _hoverProgress) * 0.22f;
            _pulse += 0.08f;
            Invalidate();
        }

        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _tray.ContextMenuStrip.Show(Cursor.Position);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _moved = false;
            _dragCursor = Cursor.Position;
            _dragWindow = Location;
            Capture = true;
        }

        private void ContinueDrag(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var delta = new Size(Cursor.Position.X - _dragCursor.X, Cursor.Position.Y - _dragCursor.Y);
            if (Math.Abs(delta.Width) + Math.Abs(delta.Height) > 4) _moved = true;
            Location = _dragWindow + delta;
            CloseMenu();
        }

        private void EndDrag(object sender, MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _dragging = false;
            Capture = false;
            if (_moved) SnapToEdge();
            else ToggleMenu();
        }

        private void ToggleMenu()
        {
            if (_menu != null)
            {
                CloseMenu();
                return;
            }

            _menu = new CompactMenuForm(this, _settings);
            _menu.FormClosed += delegate { _menu = null; Invalidate(); };
            PositionMenu(_menu);
            _menu.Show(this);
            _menu.Activate();
        }

        private void PositionMenu(Form menu)
        {
            var area = Screen.FromControl(this).WorkingArea;
            var openLeft = Left > area.Left + area.Width / 2;
            var x = openLeft ? Left - menu.Width - 14 : Right + 14;
            var y = Math.Max(area.Top + 8, Math.Min(Top + Height / 2 - menu.Height / 2, area.Bottom - menu.Height - 8));
            x = Math.Max(area.Left + 8, Math.Min(x, area.Right - menu.Width - 8));
            menu.Location = new Point(x, y);
        }

        private void SnapToEdge()
        {
            var area = Screen.FromControl(this).WorkingArea;
            var x = Left + Width / 2 < area.Left + area.Width / 2 ? area.Left + 8 : area.Right - Width - 8;
            var y = Math.Max(area.Top + 8, Math.Min(Top, area.Bottom - Height - 8));
            Location = new Point(x, y);
            SavePosition();
        }

        private void RestorePosition()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            if (_settings.BallX == int.MinValue || _settings.BallY == int.MinValue)
            {
                Location = new Point(area.Right - Width - 12, area.Top + (area.Height - Height) / 2);
            }
            else
            {
                Location = new Point(
                    Math.Max(area.Left + 4, Math.Min(_settings.BallX, area.Right - Width - 4)),
                    Math.Max(area.Top + 4, Math.Min(_settings.BallY, area.Bottom - Height - 4)));
            }
        }

        private void SavePosition()
        {
            _settings.BallX = Left;
            _settings.BallY = Top;
            _settings.Save();
        }

        private static void OpenLog()
        {
            try
            {
                var path = AppLog.CurrentLogPath;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                if (!File.Exists(path)) File.WriteAllText(path, string.Empty);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception exception)
            {
                MessageBox.Show("无法打开日志：" + exception.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
