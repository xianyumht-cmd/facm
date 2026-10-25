using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace FACM.Presentation.Glass
{
    internal sealed class CompactApprovedLivePanelForm : Form
    {
        private const int HomeWidth = 380;
        private const int HomeHeight = 285;
        private const int DetailWidth = 548;
        private const int DetailHeight = 478;
        private readonly List<Hit> _hits = new List<Hit>();
        private readonly string[] _quickNames = { "亚索", "薇恩", "格温", "金克丝" }; // ui-text-contract: allow - owner-approved fixed mock UI copy
        private readonly string[] _tiers = { "T0", "A", "T1", "—" };
        private readonly string[] _rankNames = { "亚索", "薇恩", "格温", "金克丝", "永恩", "阿狸", "剑姬", "霞", "凯南", "卡莎" }; // ui-text-contract: allow - owner-approved fixed mock UI copy
        private readonly string[] _modeNames = { "海克斯大乱斗", "普通大乱斗", "召唤师峡谷" }; // ui-text-contract: allow - owner-approved fixed mock UI copy
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
        private string _currentModeLabel = "模式未识别"; // ui-text-contract: allow - frozen owner-approved compact copy
        private string _selectedChampionName = "尚未识别"; // ui-text-contract: allow - owner-approved fixed mock UI copy
        private bool _modeIsMayhem;
        private FACM.Mayhem.MayhemRankingSnapshot _liveRanking;
        private FACM.Mayhem.MayhemChampionResult _liveChampion;
        private string _rankingStatus = "尚未获取"; // ui-text-contract: allow - owner-approved fixed mock UI copy
        internal event Action<string> PageOpened;
        internal event Action<string> NavigationRequested;
        internal event Action<string> ChampionSelected;
        internal event Action ModeRefreshRequested;

        internal void UpdateGameMode(string modeName, bool isMayhem)
        {
            if (IsDisposed) return;
            _currentModeLabel = string.IsNullOrWhiteSpace(modeName) ? "模式未识别" : modeName; // ui-text-contract: allow - owner-approved fixed mock UI copy
            _modeIsMayhem = isMayhem;
            _liveRanking = null;
            _liveChampion = null;
            _selectedChampionName = "尚未识别"; // ui-text-contract: allow - frozen owner-approved compact copy
            UpdateQuickChampions(null);
            _rankingStatus = isMayhem ? "读取中" : "当前模式暂无排行"; // ui-text-contract: allow - owner-approved fixed mock UI copy
            Invalidate();
        }

        internal void UpdateRanking(FACM.Mayhem.MayhemRankingSnapshot ranking, string status)
        {
            if (IsDisposed) return;
            _liveRanking = _modeIsMayhem && FACM.Mayhem.MayhemRankingSourceService.IsComplete(ranking)
                ? ranking : null;
            _rankingStatus = string.IsNullOrWhiteSpace(status) ? "暂无可信数据" : status; // ui-text-contract: allow - owner-approved fixed mock UI copy
            Invalidate();
        }

        internal void UpdateQuickChampions(IReadOnlyList<string> names)
        {
            if (IsDisposed) return;
            for (int i = 0; i < _quickNames.Length; i++)
                _quickNames[i] = names != null && i < names.Count &&
                    !string.IsNullOrWhiteSpace(names[i]) ? names[i] : "—";
            Invalidate();
        }

        internal void UpdateSelectedChampionName(string name)
        {
            if (IsDisposed) return;
            _selectedChampionName = string.IsNullOrWhiteSpace(name) ? "尚未识别" : name; // ui-text-contract: allow - frozen owner-approved compact copy
            _liveChampion = null;
            Invalidate();
        }

        internal void UpdateChampion(FACM.Mayhem.MayhemChampionResult champion)
        {
            if (IsDisposed) return;
            _liveChampion = champion;
            Invalidate();
        }

        private string TierFor(string championName)
        {
            if (!_modeIsMayhem) return "—";
            var rank = FACM.Mayhem.MayhemRankingSourceService.FindRank(_liveRanking, championName);
            if (rank == null) return "—";
            // Ranking position is factual; a tier badge must never be inferred from position.
            return string.IsNullOrWhiteSpace(rank.Tier) ? "#" + rank.Rank : rank.Tier;
        }

        internal event EventHandler HideRequested;
        internal event EventHandler PlacementRequested;
        internal string CurrentPage { get { return _page; } }

        internal CompactApprovedLivePanelForm()
        {
            Text = "GGman"; // ui-text-contract: allow - approved compact shell
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
            if (page.StartsWith("nav:", StringComparison.Ordinal))
            {
                if (NavigationRequested != null) NavigationRequested(page);
                return;
            }
            if (page == "mode")
            {
                if (ModeRefreshRequested != null) ModeRefreshRequested();
                return;
            }
            if (page.StartsWith("hero:", StringComparison.Ordinal))
            {
                int index;
                if (int.TryParse(page.Substring(5), out index) && index >= 0 && index < _quickNames.Length)
                    _hero = index;
                Invalidate();
                if (ChampionSelected != null && _quickNames[_hero] != "—")
                    ChampionSelected(_quickNames[_hero]);
                return;
            }
            if (page != "mayhem" && page != "workbench" && page != "records" &&
                page != "tools" && page != "repair" && page != "settings") return;
            _page = page;
            ApplyPageSize();
            Invalidate();
            if (PageOpened != null) PageOpened(page);
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
                Write(g, "功能测试版", new RectangleF(w - 128, 13, 96, 15), 10f, _muted, false, StringAlignment.Far); // ui-text-contract: allow - verified pilot status // ui-text-contract: allow - owner-approved fixed mock UI copy
            else
                Write(g, "交互演示 · 无真实功能", new RectangleF(w - 205, 13, 172, 15), 10f, _muted, false, StringAlignment.Far); // ui-text-contract: allow - owner-approved fixed mock UI copy
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
            Write(g, _currentModeLabel, new RectangleF(48, 61, 200, 20), 12f, _text, true);
            Write(g, "切换模式 ›", new RectangleF(w - 123, 62, 102, 17), 10f, _muted, false, StringAlignment.Far); // ui-text-contract: allow - owner-approved fixed mock UI copy
            HitTest("mode", mode);
            var gap = 8f;
            var left = 13f;
            var width = Math.Max(116f, (w - 34f) / 2f);
            DrawTile(g, new RectangleF(left, 99, width, 64), "▣", "LOL 工作台", "workbench"); // ui-text-contract: allow - owner-approved fixed mock UI copy
            DrawTile(g, new RectangleF(left + width + gap, 99, width, 64), "▥", "战绩", "records"); // ui-text-contract: allow - owner-approved fixed mock UI copy
            DrawTile(g, new RectangleF(left, 171, width, 64), "✧", "海克斯攻略", "mayhem"); // ui-text-contract: allow - owner-approved fixed mock UI copy
            DrawTile(g, new RectangleF(left + width + gap, 171, width, 64), "⚙", "快捷工具", "tools"); // ui-text-contract: allow - owner-approved fixed mock UI copy
            using (var pen = new Pen(_line)) g.DrawLine(pen, 13, h - 42, w - 13, h - 42);
            Write(g, "清理修复", new RectangleF(16, h - 33, 104, 22), 10.6f, _muted, false); // ui-text-contract: allow - owner-approved fixed mock UI copy
            Write(g, "个性化", new RectangleF(w / 2 - 48, h - 33, 96, 22), 10.6f, _muted, false, StringAlignment.Center); // ui-text-contract: allow - owner-approved fixed mock UI copy
            Write(g, "更多 ›", new RectangleF(w - 111, h - 33, 92, 22), 10.6f, _muted, false, StringAlignment.Far); // ui-text-contract: allow - owner-approved fixed mock UI copy
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
            Write(g, "‹  返回", new RectangleF(15, 48, 80, 24), 11f, _accent, true); // ui-text-contract: allow - owner-approved fixed mock UI copy
            HitTest("back", 8, 43, 96, 33);
            var title = _page == "mayhem" ? _currentModeLabel : TitleFor(_page);
            Write(g, title, new RectangleF(125, 48, w - 145, 23), 12.2f, _text, true, StringAlignment.Far);
            if (_page == "mayhem") DrawMayhem(g, w, h);
            else DrawOther(g, w, h);
        }

        private void DrawMayhem(Graphics g, float w, float h)
        {
            Fill(g, new RectangleF(13, 79, w - 26, 59), _panel, 9f, _line);
            DrawPortrait(g, new RectangleF(22, 86, 45, 45), _selectedChampionName, _hero);
            Write(g, _selectedChampionName, new RectangleF(78, 89, 75, 22), 16, _text, true);
            Tier(g, new RectangleF(147, 89, 38, 21), TierFor(_selectedChampionName));
            Write(g, "英雄强度 · " + (_liveChampion != null && // ui-text-contract: allow - owner-approved fixed mock UI copy
                _liveChampion.Rank.HasValue ? "排名 #" + _liveChampion.Rank.Value : "待核验"), // ui-text-contract: allow - owner-approved fixed mock UI copy
                new RectangleF(78, 114, 155, 15), 10.2f, _muted, false);
            Write(g, "当前模式攻略 ›", new RectangleF(w - 169, 97, 153, 22), 11f, _accent, true, StringAlignment.Far); // ui-text-contract: allow - owner-approved fixed mock UI copy
            HitTest("hero:" + ((_hero + 1) % 4), w - 169, 81, 150, 53);

            Fill(g, new RectangleF(13, 147, w - 26, 163), _panel, 9f, _line);
            Write(g, "当前模式 TOP 10", new RectangleF(24, 156, 184, 20), 12.2f, _text, true); // ui-text-contract: allow - owner-approved fixed mock UI copy
            Write(g, _rankingStatus, new RectangleF(w - 235, 157, 211, 17), 10f, _muted, false, StringAlignment.Far);
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
                var rowData = _modeIsMayhem && _liveRanking != null && _liveRanking.TopTen != null &&
                    _liveRanking.TopTen.Count > i ? _liveRanking.TopTen[i] : null;
                Write(g, rowData == null ? "—" : rowData.Name,
                    new RectangleF(x + 32, y + 2, colWidth - 87, 19), 11f, _text, false);
                Tier(g, new RectangleF(x + colWidth - 48, y + 2, 35, 19),
                    rowData == null ? "—" : 
                        (string.IsNullOrWhiteSpace(rowData.Tier) ? "#" + rowData.Rank : rowData.Tier));
            }

            Fill(g, new RectangleF(13, 320, w - 26, 111), _panel, 9f, _line);
            Write(g, "快速选英雄", new RectangleF(24, 329, 154, 19), 12.2f, _text, true); // ui-text-contract: allow - owner-approved fixed mock UI copy
            Write(g, "头像上方显示评级", new RectangleF(w - 194, 329, 170, 19), 10f, _muted, false, StringAlignment.Far); // ui-text-contract: allow - owner-approved fixed mock UI copy
            float cellW = (w - 48) / 4f;
            for (int i = 0; i < 4; i++)
            {
                float x = 22 + cellW * i;
                Tier(g, new RectangleF(x + (cellW - 42) / 2, 351, 42, 18), TierFor(_quickNames[i]));
                DrawPortrait(g, new RectangleF(x + (cellW - 39) / 2, 372, 39, 39), _quickNames[i], i);
                Write(g, _quickNames[i], new RectangleF(x, 412, cellW, 16), 9.8f, _muted, false, StringAlignment.Center);
                HitTest("hero:" + i, x, 350, cellW, 80);
            }
            Write(g, _modeIsMayhem && _liveRanking != null
                ? "数据：" + _liveRanking.Source + " · " + (_liveRanking.Patch ?? "版本未核验") + " · 与国服最新版本尚未核对" // ui-text-contract: allow - owner-approved fixed mock UI copy
                : "没有经过版本核验的数据不会展示为当前排行", // ui-text-contract: allow - owner-approved fixed mock UI copy
                new RectangleF(15, h - 32, w - 30, 18), 10.2f, _muted, false, StringAlignment.Center);
        }

        private void DrawOther(Graphics g, float w, float h)
        {
            var title = TitleFor(_page);
            Fill(g, new RectangleF(13, 80, w - 26, 65), _panel, 9, _line);
            Write(g, title, new RectangleF(25, 94, w - 50, 24), 16f, _text, true);
            Write(g, "点击对应项目进入 GGman 现有功能", new RectangleF(25, 121, w - 50, 18), 10.5f, _muted, false); // ui-text-contract: allow - owner-approved fixed mock UI copy
            string[] items = _page == "workbench" ? new[] { "游戏状态与当前模式", "实时对局", "海克斯攻略", "个人战绩", "我的 GGman" } // ui-text-contract: allow - owner-approved fixed mock UI copy
                : _page == "records" ? new[] { "最近对局", "对局详情", "英雄表现", "历史统计", "数据来源状态" } // ui-text-contract: allow - owner-approved fixed mock UI copy
                : _page == "tools" ? new[] { "常用快捷工具", "游戏客户端辅助", "快捷键与热键", "操作记录", "高级设置" } // ui-text-contract: allow - owner-approved fixed mock UI copy
                : _page == "repair" ? new[] { "环境状态", "检查项目", "修复建议", "操作确认", "处理结果" } // ui-text-contract: allow - owner-approved fixed mock UI copy
                : new[] { "视觉主题", "字体及缩放", "界面密度", "悬浮球设置", "关于 GGman" }; // ui-text-contract: allow - owner-approved fixed mock UI copy
            Fill(g, new RectangleF(13, 155, w - 26, 266), _panel, 9f, _line);
            for (int i = 0; i < items.Length; i++)
            {
                float y = 164 + i * 50;
                Write(g, items[i], new RectangleF(26, y + 7, w - 102, 26), 12f, _text, i == 0);
                Write(g, "›", new RectangleF(w - 57, y + 5, 27, 26), 16f, _accent, false, StringAlignment.Far);
                if (i < items.Length - 1)
                    using (var pen = new Pen(_line)) g.DrawLine(pen, 27, y + 48, w - 27, y + 48);
                if (_page == "workbench" && i == 2) HitTest("mayhem", 14, y, w - 28, 48);
                else HitTest("nav:" + _page + ":" + i, 14, y, w - 28, 48);
            }
            Write(g, "页面布局已冻结 · 功能由现有 GGman 模块执行", // ui-text-contract: allow - owner-approved fixed mock UI copy
                new RectangleF(13, h - 33, w - 26, 20), 10.1f, _muted, false, StringAlignment.Center);
        }

        private static string TitleFor(string page)
        {
            switch (page)
            {
                case "workbench": return "LOL 工作台"; // ui-text-contract: allow - owner-approved fixed mock UI copy
                case "records": return "战绩"; // ui-text-contract: allow - owner-approved fixed mock UI copy
                case "tools": return "快捷工具"; // ui-text-contract: allow - owner-approved fixed mock UI copy
                case "repair": return "清理修复"; // ui-text-contract: allow - owner-approved fixed mock UI copy
                case "settings": return "个性化"; // ui-text-contract: allow - owner-approved fixed mock UI copy
                default: return "海克斯攻略"; // ui-text-contract: allow - owner-approved fixed mock UI copy
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

        internal static void ValidateModeRankingIsolationForSmokeTest()
        {
            using (var view = new CompactApprovedLivePanelForm())
            {
                var sample = new FACM.Mayhem.MayhemRankingSnapshot {
                    Source = "fixture", Patch = "26.21",
                    TopTen = new List<FACM.Mayhem.MayhemTopChampion>()
                };
                for (int i = 0; i < 10; i++)
                {
                    sample.TopTen.Add(new FACM.Mayhem.MayhemTopChampion {
                        Rank = i + 1, Name = i == 0 ? "亚索" : "Hero" + i, // ui-text-contract: allow - deterministic non-user-facing rank fixture
                        Slug = i == 0 ? "yasuo" : "hero" + i,
                        WinRate = 50.1, Tier = i == 0 ? "T0" : "A"
                    });
                }
                view.UpdateQuickChampions(new[] { "亚索", "薇恩" }); // ui-text-contract: allow - deterministic non-user-facing rank fixture
                if (view._quickNames[0] != "亚索" || view._quickNames[1] != "薇恩" || // ui-text-contract: allow - deterministic non-user-facing quick bench fixture
                    view._quickNames[2] != "—" || view._quickNames[3] != "—")
                    throw new InvalidOperationException("Quick bench placeholders contain fake champions.");
                sample.AllChampions = new List<FACM.Mayhem.MayhemTopChampion>(sample.TopTen);
                sample.AllChampions.Add(new FACM.Mayhem.MayhemTopChampion {
                    Rank = 11, Name = "薇恩", Slug = "vayne", WinRate = 45.2, Tier = null // ui-text-contract: allow - deterministic non-user-facing rank-only smoke fixture
                }); // ui-text-contract: allow - rank-only fixture
                view.UpdateGameMode("普通大乱斗", false); // ui-text-contract: allow - deterministic non-user-facing rank fixture
                view.UpdateRanking(sample, "not applicable");
                if (view.TierFor("亚索") != "—") // ui-text-contract: allow - deterministic non-user-facing rank fixture
                    throw new InvalidOperationException("Mayhem tier leaked to standard ARAM.");
                view.UpdateGameMode("海克斯大乱斗", true); // ui-text-contract: allow - deterministic non-user-facing rank fixture
                view.UpdateRanking(sample, "fixture");
                if (view.TierFor("亚索") != "T0" || view.TierFor("薇恩") != "#11") // ui-text-contract: allow - deterministic non-user-facing rank fixture
                    throw new InvalidOperationException("Mode-specific rank or factual rank-only badge was not projected.");
                view.UpdateGameMode("召唤师峡谷", false); // ui-text-contract: allow - deterministic non-user-facing rank fixture
                if (view.TierFor("亚索") != "—" || view._liveRanking != null) // ui-text-contract: allow - deterministic non-user-facing rank fixture
                    throw new InvalidOperationException("Rank data survived a mode switch.");
            }
        }

        // Offscreen GDI screenshots prove the preview can paint, unlike a successful C# build alone.
        // They are not a substitute for operator DPI/focus acceptance.
        internal static int RunSmokeTest(string outputFolder)
        {
            try
            {
                System.IO.Directory.CreateDirectory(outputFolder);
                using (var form = new CompactApprovedLivePanelForm())
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
                try { System.IO.File.WriteAllText(
                    System.IO.Path.Combine(outputFolder, "ERROR.txt"), exception.ToString()); }
                catch { }
                Console.Error.WriteLine(exception);
                return 4;
            }
        }
    }
}
