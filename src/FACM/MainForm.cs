using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FACM.Services;

namespace FACM
{
    internal sealed class MainForm : Form
    {
        private const int BallSize = 64;
        private readonly AppSettings _settings = AppSettings.Load();
        private readonly Timer _animationTimer;
        private readonly NotifyIcon _tray;
        private CompactMenuForm _menu;
        private bool _hovered;
        private bool _dragging;
        private bool _moved;
        private Point _dragCursor;
        private Point _dragWindow;
        private float _hoverProgress;
        private float _pulse;

        public MainForm()
        {
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
                Text = "FACM 悬浮球",
                Visible = true,
                ContextMenuStrip = BuildTrayMenu()
            };
            _tray.DoubleClick += delegate { Show(); Activate(); };

            _animationTimer = new Timer { Interval = 25 };
            _animationTimer.Tick += Animate;
            _animationTimer.Start();

            MouseEnter += delegate { _hovered = true; };
            MouseLeave += delegate { _hovered = false; };
            MouseDown += BeginDrag;
            MouseMove += ContinueDrag;
            MouseUp += EndDrag;
            Shown += delegate { RestorePosition(); };
            FormClosed += delegate
            {
                _animationTimer.Stop();
                _tray.Visible = false;
                _tray.Dispose();
                if (_menu != null) _menu.Dispose();
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var hoverInset = 4f - 2f * _hoverProgress;
            var bounds = new RectangleF(hoverInset, hoverInset, Width - hoverInset * 2 - 1, Height - hoverInset * 2 - 1);

            using (var shadow = new SolidBrush(Color.FromArgb(75, 0, 0, 0)))
            {
                e.Graphics.FillEllipse(shadow, bounds.X + 2, bounds.Y + 4, bounds.Width, bounds.Height);
            }

            using (var path = new GraphicsPath())
            {
                path.AddEllipse(bounds);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(70 + (int)(25 * _hoverProgress), 190, 255);
                    brush.SurroundColors = new[] { Color.FromArgb(31, 93, 239) };
                    e.Graphics.FillPath(brush, path);
                }
            }

            var glowAlpha = 32 + (int)(35 * _hoverProgress) + (int)(10 * Math.Sin(_pulse));
            using (var glow = new Pen(Color.FromArgb(Math.Max(0, Math.Min(100, glowAlpha)), 167, 224, 255), 2f))
            {
                e.Graphics.DrawEllipse(glow, bounds);
            }

            using (var font = new Font("Segoe UI", 20F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var textBrush = new SolidBrush(Color.White))
            {
                var text = "F";
                var size = e.Graphics.MeasureString(text, font);
                e.Graphics.DrawString(text, font, textBrush, (Width - size.Width) / 2f, (Height - size.Height) / 2f - 1f);
            }

            using (var dot = new SolidBrush(Color.FromArgb(72, 224, 158)))
            {
                e.Graphics.FillEllipse(dot, Width - 17, 8, 8, 8);
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

        private ContextMenuStrip BuildTrayMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("展开 FACM", null, delegate { Show(); ToggleMenu(); });
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
            var x = openLeft ? Left - menu.Width - 12 : Right + 12;
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
