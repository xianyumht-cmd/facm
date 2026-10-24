using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace FACM.Presentation.Glass
{
    // Separate, offline UI acceptance harness. Production services and configuration are never constructed.
    // The orb uses the exact layered renderer from the shipping desktop entry.
    internal sealed class NativeCompactPreviewBallForm : Form
    {
        private readonly LayeredFloatingBall _orb;
        private readonly NativeCompactPreviewPanelForm _panel;
        private readonly ContextMenuStrip _menu;
        private readonly NotifyIcon _tray;
        private readonly Icon _icon;
        private Point _mouseDown;
        private Point _startPosition;
        private bool _dragging;
        private bool _moved;
        private bool _openedWhenPressed;

        internal NativeCompactPreviewBallForm()
        {
            Text = "GGman · 界面预览"; // ui-text-contract: allow - preview-only shell
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(56, 56);
            MinimumSize = MaximumSize = Size;
            BackColor = Color.Black;
            AutoScaleMode = AutoScaleMode.None;
            _icon = BrandIcon.Create();
            Icon = _icon;
            _orb = LayeredFloatingBall.Attach(this);
            _panel = new NativeCompactPreviewPanelForm();
            _panel.HideRequested += delegate { _panel.Hide(); };
            _panel.PlacementRequested += delegate { PositionPanel(); };
            _menu = new ContextMenuStrip();
            _menu.Items.Add("打开界面预览", null, delegate { ShowPanel(); });
            _menu.Items.Add("退出预览", null, delegate { Close(); });
            _tray = new NotifyIcon
            {
                Icon = _icon, Text = "GGman · 纯界面预览", // ui-text-contract: allow - preview-only tray
                ContextMenuStrip = _menu, Visible = true
            };
            _tray.DoubleClick += delegate { ShowPanel(); };
            MouseDown += OnOrbDown;
            MouseMove += OnOrbMove;
            MouseUp += OnOrbUp;
            Shown += delegate
            {
                var area = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(area.Right - Width - 18, area.Top + (area.Height - Height) / 2);
            };
            FormClosed += delegate
            {
                _panel.Close();
                _panel.Dispose();
                _tray.Visible = false;
                _tray.Dispose();
                _menu.Dispose();
                _orb.Dispose();
                _icon.Dispose();
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        private void OnOrbDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _menu.Show(Cursor.Position);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _moved = false;
            _openedWhenPressed = _panel.Visible;
            _mouseDown = Cursor.Position;
            _startPosition = Location;
            Capture = true;
        }

        private void OnOrbMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var delta = new Size(Cursor.Position.X - _mouseDown.X, Cursor.Position.Y - _mouseDown.Y);
            if (Math.Abs(delta.Width) + Math.Abs(delta.Height) > 4) _moved = true;
            var target = _startPosition + delta;
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            target.X = Math.Max(area.Left - Width / 3, Math.Min(target.X, area.Right - Width * 2 / 3));
            target.Y = Math.Max(area.Top, Math.Min(target.Y, area.Bottom - Height));
            Location = target;
            _panel.Hide();
        }

        private void OnOrbUp(object sender, MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _dragging = false;
            Capture = false;
            if (_moved) return;
            if (_openedWhenPressed) _panel.Hide();
            else ShowPanel();
        }

        private void ShowPanel()
        {
            if (_panel.IsDisposed) return;
            _panel.ShowHome();
            PositionPanel();
            _panel.Show();
            _panel.BringToFront();
            _panel.Activate();
        }

        private void PositionPanel()
        {
            var area = Screen.FromControl(this).WorkingArea;
            bool onRight = Left > area.Left + area.Width / 2;
            int x = onRight ? Left - _panel.Width - 14 : Right + 14;
            int y = Top + Height / 2 - _panel.Height / 2;
            _panel.Location = new Point(
                Math.Max(area.Left + 8, Math.Min(x, area.Right - _panel.Width - 8)),
                Math.Max(area.Top + 8, Math.Min(y, area.Bottom - _panel.Height - 8)));
        }
    }

    internal sealed class NativeCompactPreviewPanelForm : Form
    {
        private const int HomeWidth = 380;
        private const int HomeHeight = 285;
        private const int DetailWidth = 548;
        private const int DetailHeight = 478;
        private readonly List<Hit> _hits = new List<Hit>();
        private readonly string[] _quickNames = { "亚索", "薇恩", "格温", "金克丝" };
        private readonly string[] _tiers = { "T0", "A", "T1", "—" };
        private readonly string[] _rankNames = { "亚索", "薇恩", "格温", "金克丝", "永恩", "阿狸", "剑姬", "霞", "凯南", "卡莎" };
        private readonly string[] _modeNames = { "海克斯大乱斗", "普通大乱斗", "召唤师峡谷" };
        private readonly Color _back = Color.FromArgb(20, 27, 36);
        private readonly Color _panel = Color.FromArgb(31, 42, 54);
        private readonly Color _line = Color.FromArgb(58, 74, 93);
        private readonly Color _text = Color.FromArgb(239, 246, 253);
        private readonly Color _muted = Color.FromArgb(164, 182, 200);
        private readonly Color _accent = Color.FromArgb(144, 193, 239);
        private string _page = "home";
        private int _mode;
        private int _hero;
        private float _scale = 1f;
        private Rectangle _workingArea;
        private bool _changingSize;

        internal event EventHandler HideRequested;
        internal event EventHandler PlacementRequested;
        internal string CurrentPage { get { return _page; } }

        internal NativeCompactPreviewPanelForm()
        {
            Text = "GGman · 纯界面预览"; // ui-text-contract: allow - preview-only shell
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = _back;
            ForeColor = _text;
            DoubleBuffered = true;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            ClientSize = new Size(HomeWidth, HomeHeight);
            Deactivate += delegate
            {
                if (Visible && HideRequested != null) HideRequested(this, EventArgs.Empty);
            };
            Shown += delegate { ApplyPageSize(); };
        }

        internal void ShowHome()
        {
            _page = "home";
            ApplyPageSize();
            Invalidate();
        }

        private void Open(string page)
        {
            if (page == "close")
            {
                if (HideRequested != null) HideRequested(this, EventArgs.Empty);
                return;
            }
            if (page == "back") { ShowHome(); return; }
            if (page == "mode")
            {
                _mode = (_mode + 1) % _modeNames.Length;
                Invalidate();
                return;
            }
            if (page.StartsWith("hero:", StringComparison.Ordinal))
            {
                int index;
                if (int.TryParse(page.Substring(5), out index) && index >= 0 && index < _quickNames.Length)
                    _hero = index;
                Invalidate();
                return;
            }
            if (page != "mayhem" && page != "workbench" && page != "records" &&
                page != "tools" && page != "repair" && page != "settings") return;
            _page = page;
            ApplyPageSize();
            Invalidate();
        }

        private void ApplyPageSize()
        {
            if (_changingSize || IsDisposed) return;
            _changingSize = true;
            try
            {
                _scale = Math.Max(1f, DeviceDpi / 96f);
                var desiredWidth = (int)Math.Round((_page == "home" ? HomeWidth : DetailWidth) * _scale);
                var desiredHeight = (int)Math.Round((_page == "home" ? HomeHeight : DetailHeight) * _scale);
                _workingArea = Screen.FromControl(this).WorkingArea;
                MinimumSize = Size.Empty;
                MaximumSize = Size.Empty;
                ClientSize = new Size(Math.Min(desiredWidth, Math.Max(220, _workingArea.Width - 16)),
                                      Math.Min(desiredHeight, Math.Max(180, _workingArea.Height - 16)));
                MinimumSize = MaximumSize = Size;
                Location = new Point(Math.Max(_workingArea.Left + 8, Math.Min(Left, _workingArea.Right - Width - 8)),
                                     Math.Max(_workingArea.Top + 8, Math.Min(Top, _workingArea.Bottom - Height - 8)));
            }
            finally { _changingSize = false; }
            if (Visible && PlacementRequested != null) PlacementRequested(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            var point = new PointF(e.X / _scale, e.Y / _scale);
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                if (_hits[i].Bounds.Contains(point)) { Open(_hits[i].Action); return; }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (HideRequested != null) HideRequested(this, EventArgs.Empty);
                e.Handled = true;
            }
            if (e.KeyCode == Keys.Back && _page != "home")
            {
                ShowHome();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            e.Graphics.ScaleTransform(_scale, _scale);
            DrawScene(e.Graphics, ClientSize.Width / _scale, ClientSize.Height / _scale);
        }

        private void DrawScene(Graphics g, float w, float h)
        {
            _hits.Clear();
            g.Clear(_back);
            var frame = new RectangleF(0.6f, 0.6f, w - 1.2f, h - 1.2f);
            Fill(g, frame, _back, 11f, Color.FromArgb(81, 100, 120));
            using (var brush = new LinearGradientBrush(
                new RectangleF(0, 0, w, Math.Max(1, h)), Color.FromArgb(28, 42, 57),
                _back, LinearGradientMode.Vertical))
            using (var path = Rounded(frame, 11f))
                g.FillPath(brush, path);

            Fill(g, new RectangleF(10, 8, 24, 24), Color.FromArgb(40, 77, 112), 7f, null);
            Write(g, "G", new RectangleF(10, 8, 24, 23), 15f, _accent, true, StringAlignment.Center);
            Write(g, "GGman", new RectangleF(42, 10, 145, 20), 13.5f, _text, true);
            if (_page == "home")
                Write(g, "仅供界面预览", new RectangleF(w - 128, 13, 96, 15), 10f, _muted, false, StringAlignment.Far);
            else
                Write(g, "交互演示 · 无真实功能", new RectangleF(w - 205, 13, 172, 15), 10f, _muted, false, StringAlignment.Far);
            Write(g, "×", new RectangleF(w - 30, 7, 22, 25), 20f, _muted, false, StringAlignment.Center);
            HitTest("close", w - 33, 5, 32, 32);
            using (var pen = new Pen(_line))
                g.DrawLine(pen, 0, 40, w, 40);

            if (_page == "home") DrawHome(g, w, h);
            else DrawDetail(g, w, h);
        }

        private void DrawHome(Graphics g, float w, float h)
        {
            var mode = new RectangleF(13, 52, w - 26, 37);
            Fill(g, mode, _panel, 8f, _line);
            Write(g, "◈", new RectangleF(24, 59, 25, 20), 17f, _accent, true);
            Write(g, _modeNames[_mode], new RectangleF(48, 61, 200, 20), 12f, _text, true);
            Write(g, "切换模式 ›", new RectangleF(w - 123, 62, 102, 17), 10f, _muted, false, StringAlignment.Far);
            HitTest("mode", mode);
            var gap = 8f;
            var left = 13f;
            var width = Math.Max(116f, (w - 34f) / 2f);
            DrawTile(g, new RectangleF(left, 99, width, 64), "▣", "LOL 工作台", "workbench");
            DrawTile(g, new RectangleF(left + width + gap, 99, width, 64), "▥", "战绩", "records");
            DrawTile(g, new RectangleF(left, 171, width, 64), "✧", "海克斯攻略", "mayhem");
            DrawTile(g, new RectangleF(left + width + gap, 171, width, 64), "⚙", "快捷工具", "tools");
            using (var pen = new Pen(_line)) g.DrawLine(pen, 13, h - 42, w - 13, h - 42);
            Write(g, "清理修复", new RectangleF(16, h - 33, 104, 22), 10.6f, _muted, false);
            Write(g, "个性化", new RectangleF(w / 2 - 48, h - 33, 96, 22), 10.6f, _muted, false, StringAlignment.Center);
            Write(g, "更多 ›", new RectangleF(w - 111, h - 33, 92, 22), 10.6f, _muted, false, StringAlignment.Far);
            HitTest("repair", 12, h - 39, 112, 35);
            HitTest("settings", w / 2 - 50, h - 39, 100, 35);
            HitTest("settings", w - 115, h - 39, 110, 35);
        }

        private void DrawTile(Graphics g, RectangleF area, string glyph, string title, string action)
        {
            Fill(g, area, _panel, 9f, _line);
            Fill(g, new RectangleF(area.Left + 12, area.Top + 19, 27, 27),
                Color.FromArgb(38, 61, 84), 7f, null);
            Write(g, glyph, new RectangleF(area.Left + 14, area.Top + 21, 23, 23), 16, _accent, true, StringAlignment.Center);
            Write(g, title, new RectangleF(area.Left + 46, area.Top + 20, area.Width - 52, 27), 12.1f, _text, true);
            HitTest(action, area);
        }

        private void DrawDetail(Graphics g, float w, float h)
        {
            Write(g, "‹  返回", new RectangleF(15, 48, 80, 24), 11f, _accent, true);
            HitTest("back", 8, 43, 96, 33);
            var title = _page == "mayhem" ? _modeNames[_mode] : TitleFor(_page);
            Write(g, title, new RectangleF(125, 48, w - 145, 23), 12.2f, _text, true, StringAlignment.Far);
            if (_page == "mayhem") DrawMayhem(g, w, h);
            else DrawOther(g, w, h);
        }

        private void DrawMayhem(Graphics g, float w, float h)
        {
            Fill(g, new RectangleF(13, 79, w - 26, 59), _panel, 9f, _line);
            DrawPortrait(g, new RectangleF(22, 86, 45, 45), _quickNames[_hero], _hero);
            Write(g, _quickNames[_hero], new RectangleF(78, 89, 75, 22), 16, _text, true);
            Tier(g, new RectangleF(147, 89, 38, 21), _tiers[_hero]);
            Write(g, "英雄强度 · 示例", new RectangleF(78, 114, 155, 15), 10.2f, _muted, false);
            Write(g, "当前模式攻略 ›", new RectangleF(w - 169, 97, 153, 22), 11f, _accent, true, StringAlignment.Far);
            HitTest("hero:" + ((_hero + 1) % 4), w - 169, 81, 150, 53);

            Fill(g, new RectangleF(13, 147, w - 26, 163), _panel, 9f, _line);
            Write(g, "当前模式 TOP 10", new RectangleF(24, 156, 184, 20), 12.2f, _text, true);
            Write(g, "虚构排行 · 仅布局演示", new RectangleF(w - 235, 157, 211, 17), 10f, _muted, false, StringAlignment.Far);
            var colWidth = (w - 52) / 2f;
            for (int i = 0; i < 10; i++)
            {
                int col = i / 5;
                int row = i % 5;
                float x = 23 + col * (colWidth + 6f);
                float y = 180 + row * 25f;
                if (row != 4)
                    using (var pen = new Pen(Color.FromArgb(50, 65, 80)))
                        g.DrawLine(pen, x, y + 23, x + colWidth - 10, y + 23);
                Write(g, (i + 1).ToString("00"), new RectangleF(x, y + 2, 28, 18), 10.5f,
                    i < 3 ? Color.FromArgb(224, 191, 116) : _muted, true);
                Write(g, _rankNames[i], new RectangleF(x + 32, y + 2, colWidth - 87, 19), 11f, _text, false);
                Tier(g, new RectangleF(x + colWidth - 48, y + 2, 35, 19), _tiers[i % 4]);
            }

            Fill(g, new RectangleF(13, 320, w - 26, 111), _panel, 9f, _line);
            Write(g, "快速选英雄", new RectangleF(24, 329, 154, 19), 12.2f, _text, true);
            Write(g, "头像上方显示评级", new RectangleF(w - 194, 329, 170, 19), 10f, _muted, false, StringAlignment.Far);
            float cellW = (w - 48) / 4f;
            for (int i = 0; i < 4; i++)
            {
                float x = 22 + cellW * i;
                Tier(g, new RectangleF(x + (cellW - 42) / 2, 351, 42, 18), _tiers[i]);
                DrawPortrait(g, new RectangleF(x + (cellW - 39) / 2, 372, 39, 39), _quickNames[i], i);
                Write(g, _quickNames[i], new RectangleF(x, 412, cellW, 16), 9.8f, _muted, false, StringAlignment.Center);
                HitTest("hero:" + i, x, 350, cellW, 80);
            }
            Write(g, "演示数据不是实际排名 · 用于检查尺寸和内容裁剪",
                new RectangleF(15, h - 32, w - 30, 18), 10.2f, _muted, false, StringAlignment.Center);
        }

        private void DrawOther(Graphics g, float w, float h)
        {
            var title = TitleFor(_page);
            Fill(g, new RectangleF(13, 80, w - 26, 65), _panel, 9, _line);
            Write(g, title, new RectangleF(25, 94, w - 50, 24), 16f, _text, true);
            Write(g, "以下为界面交互占位数据，无真实操作", new RectangleF(25, 121, w - 50, 18), 10.5f, _muted, false);
            string[] items = _page == "workbench" ? new[] { "游戏状态与当前模式", "实时对局", "海克斯攻略", "个人战绩", "我的 GGman" }
                : _page == "records" ? new[] { "最近对局", "对局详情", "英雄表现", "历史统计", "数据来源状态" }
                : _page == "tools" ? new[] { "常用快捷工具", "游戏客户端辅助", "快捷键与热键", "操作记录", "高级设置" }
                : _page == "repair" ? new[] { "环境状态", "检查项目", "修复建议", "操作确认", "处理结果" }
                : new[] { "视觉主题", "字体及缩放", "界面密度", "悬浮球设置", "关于 GGman" };
            Fill(g, new RectangleF(13, 155, w - 26, 266), _panel, 9f, _line);
            for (int i = 0; i < items.Length; i++)
            {
                float y = 164 + i * 50;
                Write(g, items[i], new RectangleF(26, y + 7, w - 102, 26), 12f, _text, i == 0);
                Write(g, "›", new RectangleF(w - 57, y + 5, 27, 26), 16f, _accent, false, StringAlignment.Far);
                if (i < items.Length - 1)
                    using (var pen = new Pen(_line)) g.DrawLine(pen, 27, y + 48, w - 27, y + 48);
                if (_page == "workbench" && i == 2) HitTest("mayhem", 14, y, w - 28, 48);
            }
            Write(g, "仅检查视觉与返回交互，设置不会保存",
                new RectangleF(13, h - 33, w - 26, 20), 10.1f, _muted, false, StringAlignment.Center);
        }

        private static string TitleFor(string page)
        {
            switch (page)
            {
                case "workbench": return "LOL 工作台";
                case "records": return "战绩";
                case "tools": return "快捷工具";
                case "repair": return "清理修复";
                case "settings": return "个性化";
                default: return "海克斯攻略";
            }
        }

        private void DrawPortrait(Graphics g, RectangleF bounds, string label, int n)
        {
            var palette = new[] {
                Color.FromArgb(58, 77, 119), Color.FromArgb(105, 62, 112),
                Color.FromArgb(48, 99, 107), Color.FromArgb(81, 68, 109)
            };
            Fill(g, bounds, palette[n % palette.Length], 7f, Color.FromArgb(105, 130, 152));
            Write(g, label.Substring(0, 1), bounds, bounds.Width > 41 ? 20f : 17f, _text, true, StringAlignment.Center);
        }

        private void Tier(Graphics g, RectangleF rect, string tier)
        {
            var fill = tier == "T0" ? Color.FromArgb(44, 88, 66)
                : tier == "T1" ? Color.FromArgb(111, 83, 42)
                : tier == "A" ? Color.FromArgb(43, 75, 111) : Color.FromArgb(54, 66, 78);
            var ink = tier == "T0" ? Color.FromArgb(165, 243, 195)
                : tier == "T1" ? Color.FromArgb(248, 220, 149) : _accent;
            Fill(g, rect, fill, 5f, null);
            Write(g, tier, rect, 10.5f, ink, true, StringAlignment.Center);
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(Math.Min(r.Width, r.Height), radius * 2f);
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static void Fill(Graphics g, RectangleF rect, Color color, float radius, Color? border)
        {
            using (var p = Rounded(rect, radius))
            using (var brush = new SolidBrush(color))
            {
                g.FillPath(brush, p);
                if (border.HasValue)
                    using (var pen = new Pen(border.Value, 0.9f)) g.DrawPath(pen, p);
            }
        }

        private static void Write(Graphics g, string value, RectangleF bounds, float size,
            Color color, bool bold, StringAlignment align = StringAlignment.Near)
        {
            using (var font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var fmt = new StringFormat(StringFormat.GenericDefault))
            {
                fmt.Alignment = align;
                fmt.LineAlignment = StringAlignment.Center;
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                fmt.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(value, font, brush, bounds, fmt);
            }
        }

        private void HitTest(string action, RectangleF bounds)
        {
            _hits.Add(new Hit { Action = action, Bounds = bounds });
        }
        private void HitTest(string action, float x, float y, float w, float h)
        {
            HitTest(action, new RectangleF(x, y, w, h));
        }
        private struct Hit { internal string Action; internal RectangleF Bounds; }

        // Offscreen GDI screenshots prove the preview can paint, unlike a successful C# build alone.
        // They are not a substitute for operator DPI/focus acceptance.
        internal static int RunSmokeTest(string outputFolder)
        {
            try
            {
                System.IO.Directory.CreateDirectory(outputFolder);
                using (var form = new NativeCompactPreviewPanelForm())
                {
                    foreach (var scene in new[] { "home", "mayhem", "workbench", "records", "tools", "repair", "settings" })
                    {
                        form._page = scene;
                        int width = scene == "home" ? HomeWidth : DetailWidth;
                        int height = scene == "home" ? HomeHeight : DetailHeight;
                        using (var image = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                        using (var g = Graphics.FromImage(image))
                        {
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            form.DrawScene(g, width, height);
                            var bg = image.GetPixel(20, 20);
                            if (bg.R > 230 && bg.G > 230 && bg.B > 230)
                                throw new InvalidOperationException("Preview screenshot is white: " + scene);
                            if (form._hits.Count < 2)
                                throw new InvalidOperationException("Missing preview interaction regions: " + scene);
                            foreach (var hit in form._hits)
                                if (hit.Bounds.Right > width + 1 || hit.Bounds.Bottom > height + 1)
                                    throw new InvalidOperationException("Interactive control outside viewport: " + scene + ":" + hit.Action);
                            image.Save(System.IO.Path.Combine(outputFolder, "native-" + scene + ".png"), ImageFormat.Png);
                        }
                    }
                }
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 4;
            }
        }
    }
}
