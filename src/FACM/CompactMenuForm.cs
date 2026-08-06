using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;

namespace FACM
{
    internal sealed class CompactMenuForm : Form
    {
        private static readonly Color Background = Color.FromArgb(20, 25, 36);
        private static readonly Color Card = Color.FromArgb(29, 36, 50);
        private static readonly Color CardHover = Color.FromArgb(38, 49, 68);
        private static readonly Color White = Color.FromArgb(244, 247, 255);
        private static readonly Color Muted = Color.FromArgb(153, 165, 186);
        private static readonly Color Blue = Color.FromArgb(66, 133, 255);
        private static readonly Color Green = Color.FromArgb(74, 211, 151);

        private readonly MainForm _ownerBall;
        private readonly AppSettings _settings;
        private readonly Label _pathLabel;
        private readonly Label _status;
        private bool _dialogOpen;

        public CompactMenuForm(MainForm ownerBall, AppSettings settings)
        {
            _ownerBall = ownerBall;
            _settings = settings;
            Text = "FACM";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(326, 438);
            BackColor = Background;
            ForeColor = White;
            Font = new Font("Microsoft YaHei UI", 9F);
            DoubleBuffered = true;

            var header = new Panel { Dock = DockStyle.Top, Height = 82, Padding = new Padding(18, 14, 18, 8), BackColor = Background };
            var brand = new Label { Text = "FACM 2.1", AutoSize = true, Location = new Point(18, 13), ForeColor = White, Font = new Font("Segoe UI", 14F, FontStyle.Bold) };
            var local = new Label { Text = "●  本地悬浮工具", AutoSize = true, Location = new Point(19, 43), ForeColor = Green, Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold) };
            var close = new Label { Text = "×", Size = new Size(34, 34), Location = new Point(278, 10), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted, Font = new Font("Segoe UI", 17F), Cursor = Cursors.Hand };
            close.Click += delegate { Close(); };
            close.MouseEnter += delegate { close.ForeColor = White; };
            close.MouseLeave += delegate { close.ForeColor = Muted; };
            header.Controls.Add(brand);
            header.Controls.Add(local);
            header.Controls.Add(close);

            var body = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(14, 0, 14, 0),
                AutoScroll = false,
                BackColor = Background
            };

            _pathLabel = new Label
            {
                Width = 298,
                Height = 42,
                Padding = new Padding(10, 5, 10, 4),
                BackColor = Color.FromArgb(24, 30, 43),
                ForeColor = Muted,
                Font = new Font("Microsoft YaHei UI", 8F),
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            RefreshPathLabel();

            body.Controls.Add(_pathLabel);
            body.Controls.Add(Spacer(6));
            body.Controls.Add(ActionButton("清理日志与缓存", "清理 LeagueClient 顶层 .log 与 FACM 临时文件", "清", CleanSafeFiles));
            body.Controls.Add(ActionButton("识别游戏目录", "从已运行进程和常见注册表位置识别", "识", DetectGamePath));
            body.Controls.Add(ActionButton("选择游戏目录", "手动选择包含 Game 的游戏根目录", "选", SelectGamePath));
            body.Controls.Add(ActionButton("修复客户端窗口", "内置 Fix-LCU-Window，提供四种模式", "修", ShowFixMenu));
            body.Controls.Add(ActionButton("打开操作日志", "查看 FACM 当天的本地执行记录", "记", OpenLog));

            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                Padding = new Padding(18, 0, 18, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "就绪  ·  右键悬浮球可退出",
                ForeColor = Muted,
                BackColor = Color.FromArgb(17, 22, 32),
                Font = new Font("Microsoft YaHei UI", 8F)
            };
            var about = new Label
            {
                Text = "关于 / 签名",
                AutoSize = true,
                Cursor = Cursors.Hand,
                ForeColor = Muted,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                Location = new Point(234, 14)
            };
            about.Click += ShowAbout;
            _status.Controls.Add(about);

            Controls.Add(body);
            Controls.Add(_status);
            Controls.Add(header);

            Deactivate += delegate { if (!_dialogOpen) Close(); };
            Shown += delegate { ApplyRoundedRegion(); };
            Resize += delegate { ApplyRoundedRegion(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(53, 64, 83))) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        private Control ActionButton(string title, string description, string glyph, EventHandler click)
        {
            var panel = new Panel
            {
                Width = 298,
                Height = 57,
                Margin = new Padding(0, 3, 0, 3),
                BackColor = Card,
                Cursor = Cursors.Hand
            };
            var icon = new Label
            {
                Text = glyph,
                Location = new Point(10, 10),
                Size = new Size(36, 36),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(36, 83, 165),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };
            var titleLabel = new Label { Text = title, AutoSize = true, Location = new Point(58, 9), ForeColor = White, Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold) };
            var descriptionLabel = new Label { Text = description, AutoSize = false, Size = new Size(225, 20), AutoEllipsis = true, Location = new Point(58, 31), ForeColor = Muted, Font = new Font("Microsoft YaHei UI", 7.7F) };
            var arrow = new Label { Text = "›", Size = new Size(18, 40), Location = new Point(273, 8), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted, Font = new Font("Segoe UI", 16F) };

            panel.Controls.Add(icon);
            panel.Controls.Add(titleLabel);
            panel.Controls.Add(descriptionLabel);
            panel.Controls.Add(arrow);
            foreach (Control control in panel.Controls) control.Cursor = Cursors.Hand;
            panel.Click += click;
            foreach (Control control in panel.Controls) control.Click += click;
            panel.MouseEnter += delegate { panel.BackColor = CardHover; };
            panel.MouseLeave += delegate { panel.BackColor = Card; };
            foreach (Control control in panel.Controls)
            {
                control.MouseEnter += delegate { panel.BackColor = CardHover; };
                control.MouseLeave += delegate { panel.BackColor = Card; };
            }
            return panel;
        }

        private static Control Spacer(int height)
        {
            return new Panel { Width = 298, Height = height, Margin = Padding.Empty, BackColor = Background };
        }

        private void CleanSafeFiles(object sender, EventArgs e)
        {
            RunDialogAction(delegate
            {
                if (!EnsureGamePath()) return;
                var running = ProcessGuard.GetRunningRelatedProcesses();
                if (running.Any(name => name.StartsWith("League", StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("请先完全退出游戏和客户端后再清理。\r\n\r\n正在运行：" + string.Join("、", running), "FACM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var preview = SafeCleanupService.Preview(_settings.GamePath);
                if (preview.Files.Count == 0)
                {
                    SetStatus("没有发现可清理的日志或临时文件");
                    MessageBox.Show("当前没有发现可安全清理的日志或 FACM 临时文件。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var message = "即将删除 " + preview.Files.Count + " 个日志/临时文件，共 " + SafeCleanupService.FormatBytes(preview.Bytes) + "。\r\n\r\n不会删除游戏组件、驱动或安全程序。是否继续？";
                if (MessageBox.Show(message, "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                var deleted = SafeCleanupService.Execute(preview);
                SetStatus("已清理 " + deleted + " 个文件");
                MessageBox.Show("清理完成，共删除 " + deleted + " 个文件。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        private void DetectGamePath(object sender, EventArgs e)
        {
            RunDialogAction(delegate
            {
                SetStatus("正在识别游戏目录...");
                var detected = GameLocator.FindGameRoot();
                if (string.IsNullOrEmpty(detected))
                {
                    SetStatus("未自动识别，请手动选择");
                    MessageBox.Show("没有从当前进程或常见注册表位置识别到游戏目录，请手动选择。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _settings.GamePath = detected;
                _settings.Save();
                RefreshPathLabel();
                SetStatus("已识别游戏目录");
            });
        }

        private void SelectGamePath(object sender, EventArgs e)
        {
            RunDialogAction(delegate
            {
                using (var dialog = new FolderBrowserDialog { Description = "请选择包含 Game 文件夹的游戏根目录", ShowNewFolderButton = false, SelectedPath = _settings.GamePath })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    if (!GameLocator.IsValidGameRoot(dialog.SelectedPath))
                    {
                        MessageBox.Show("所选目录不是有效的游戏根目录。应当直接包含 Game，并包含 LeagueClient 或 Launcher。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    _settings.GamePath = Path.GetFullPath(dialog.SelectedPath);
                    _settings.Save();
                    RefreshPathLabel();
                    SetStatus("已保存游戏目录");
                }
            });
        }

        private void ShowFixMenu(object sender, EventArgs e)
        {
            var menu = new ContextMenuStrip { Font = new Font("Microsoft YaHei UI", 9F), ShowImageMargin = false };
            menu.Items.Add("模式 1  立即恢复窗口", null, delegate { RunFixMode(1); });
            menu.Items.Add("模式 2  常驻自动恢复", null, delegate { RunFixMode(2); });
            menu.Items.Add("模式 3  跳过结算页面", null, delegate { RunFixMode(3); });
            menu.Items.Add("模式 4  热重载客户端", null, delegate { RunFixMode(4); });
            _dialogOpen = true;
            menu.Closed += delegate { _dialogOpen = false; Activate(); menu.Dispose(); };
            menu.Show(Cursor.Position);
        }

        private void RunFixMode(int mode)
        {
            RunDialogAction(delegate
            {
                try
                {
                    ToolRunner.RunFixLcu(mode);
                    SetStatus("已启动窗口修复模式 " + mode);
                }
                catch (Exception exception)
                {
                    AppLog.Error("Fix-LCU launch failed", exception);
                    MessageBox.Show("启动内置修复工具失败：" + exception.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
        }

        private void OpenLog(object sender, EventArgs e)
        {
            RunDialogAction(delegate
            {
                try
                {
                    var path = AppLog.CurrentLogPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (!File.Exists(path)) File.WriteAllText(path, string.Empty);
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    SetStatus("已打开日志");
                }
                catch (Exception exception)
                {
                    MessageBox.Show("无法打开日志：" + exception.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
        }

        private void ShowAbout(object sender, EventArgs e)
        {
            RunDialogAction(delegate
            {
                MessageBox.Show(
                    "FACM 2.1 悬浮球版\r\n\r\n" +
                    "签名状态：" + SignatureInspector.GetCurrentExecutableSignatureStatus() + "\r\n" +
                    "内置工具：Fix-LCU-Window 1.1.2（运行前 SHA-256 校验）\r\n\r\n" +
                    "FACM 不联网、不注入进程，不静默执行。",
                    "关于 FACM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        private bool EnsureGamePath()
        {
            if (GameLocator.IsValidGameRoot(_settings.GamePath)) return true;
            var detected = GameLocator.FindGameRoot();
            if (!string.IsNullOrEmpty(detected))
            {
                _settings.GamePath = detected;
                _settings.Save();
                RefreshPathLabel();
                return true;
            }
            SelectGamePath(this, EventArgs.Empty);
            return GameLocator.IsValidGameRoot(_settings.GamePath);
        }

        private void RefreshPathLabel()
        {
            _pathLabel.Text = GameLocator.IsValidGameRoot(_settings.GamePath) ? "游戏目录  " + _settings.GamePath : "游戏目录  尚未选择";
        }

        private void SetStatus(string text)
        {
            _status.Text = text;
            AppLog.Info(text);
        }

        private void RunDialogAction(Action action)
        {
            _dialogOpen = true;
            try { action(); }
            catch (Exception exception)
            {
                AppLog.Error("Menu action failed", exception);
                MessageBox.Show("操作失败：" + exception.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _dialogOpen = false;
                if (!IsDisposed) Activate();
            }
        }

        private void ApplyRoundedRegion()
        {
            using (var path = new GraphicsPath())
            {
                var radius = 18;
                path.AddArc(0, 0, radius, radius, 180, 90);
                path.AddArc(Width - radius, 0, radius, radius, 270, 90);
                path.AddArc(Width - radius, Height - radius, radius, radius, 0, 90);
                path.AddArc(0, Height - radius, radius, radius, 90, 90);
                path.CloseFigure();
                Region = new Region(path);
            }
        }
    }
}
