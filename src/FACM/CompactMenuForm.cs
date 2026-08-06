using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using FACM.Configuration;
using FACM.Services;

namespace FACM
{
    internal sealed class CompactMenuForm : Form
    {
        private static readonly Color Background = Color.FromArgb(12, 17, 27);
        private static readonly Color Surface = Color.FromArgb(22, 29, 43);
        private static readonly Color SurfaceHover = Color.FromArgb(29, 39, 57);
        private static readonly Color Border = Color.FromArgb(48, 61, 84);
        private static readonly Color TextPrimary = Color.FromArgb(244, 247, 255);
        private static readonly Color TextMuted = Color.FromArgb(151, 165, 190);
        private static readonly Color Accent = Color.FromArgb(80, 126, 255);
        private static readonly Color AccentBright = Color.FromArgb(91, 205, 255);
        private static readonly Color Success = Color.FromArgb(92, 224, 166);

        private readonly MainForm _ownerBall;
        private readonly AppSettings _settings;
        private readonly Label _pathValue;
        private readonly Label _status;
        private readonly Label _adminBadge;
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
            ClientSize = new Size(408, 594);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Microsoft YaHei UI", 9F);
            DoubleBuffered = true;

            var header = new Panel { Location = new Point(0, 0), Size = new Size(408, 91), BackColor = Color.Transparent };
            var logo = new Label
            {
                Text = "F",
                Location = new Point(20, 18),
                Size = new Size(48, 48),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Accent,
                Font = new Font("Segoe UI", 19F, FontStyle.Bold)
            };
            MakeRound(logo, 15);
            var brand = new Label
            {
                Text = "FACM",
                AutoSize = true,
                Location = new Point(81, 17),
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 17F, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            var version = new Label
            {
                Text = "3.0  CONTROL CENTER",
                AutoSize = true,
                Location = new Point(83, 51),
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            _adminBadge = new Label
            {
                Text = ElevationService.IsAdministrator ? "管理员" : "标准模式",
                Location = new Point(277, 24),
                Size = new Size(82, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = ElevationService.IsAdministrator ? Success : TextMuted,
                BackColor = Color.FromArgb(25, 34, 50),
                Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold)
            };
            MakeRound(_adminBadge, 14);
            var close = new Label
            {
                Text = "×",
                Location = new Point(366, 16),
                Size = new Size(32, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18F),
                Cursor = Cursors.Hand
            };
            close.Click += delegate { Close(); };
            close.MouseEnter += delegate { close.ForeColor = TextPrimary; };
            close.MouseLeave += delegate { close.ForeColor = TextMuted; };
            header.Controls.Add(logo);
            header.Controls.Add(brand);
            header.Controls.Add(version);
            header.Controls.Add(_adminBadge);
            header.Controls.Add(close);

            var pathCard = new RoundedPanel
            {
                Location = new Point(16, 91),
                Size = new Size(376, 108),
                Radius = 18,
                FillColor = Surface,
                BorderColor = Border
            };
            var pathTitle = new Label
            {
                Text = "工作目录",
                AutoSize = true,
                Location = new Point(16, 13),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
            };
            _pathValue = new Label
            {
                Location = new Point(16, 36),
                Size = new Size(344, 26),
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            var detect = CreateSmallButton("自动识别", new Point(16, 70), 102);
            detect.Click += DetectGamePath;
            var choose = CreateSmallButton("选择目录", new Point(126, 70), 102);
            choose.Click += SelectGamePath;
            var config = new Label
            {
                Text = CleanupProfile.IsConfigured ? "●  清理规则已配置" : "●  等待开发者配置",
                Location = new Point(242, 72),
                Size = new Size(118, 24),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = CleanupProfile.IsConfigured ? Success : Color.FromArgb(255, 180, 92),
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 7.8F, FontStyle.Bold)
            };
            pathCard.Controls.Add(pathTitle);
            pathCard.Controls.Add(_pathValue);
            pathCard.Controls.Add(detect);
            pathCard.Controls.Add(choose);
            pathCard.Controls.Add(config);
            RefreshPathLabel();

            var cleanupCard = CreateActionCard(
                new Point(16, 211),
                new Size(376, 112),
                "清理环境",
                "扫描固定目录与已选择安装目录，预览后再执行",
                "CLEAN",
                true,
                CleanEnvironment);
            var cleanupTag = new Label
            {
                Text = "精确路径  ·  保留目录保护  ·  操作日志",
                Location = new Point(76, 78),
                Size = new Size(270, 20),
                ForeColor = Color.FromArgb(205, 222, 255),
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 7.8F)
            };
            cleanupCard.Controls.Add(cleanupTag);
            cleanupTag.Click += CleanEnvironment;

            var toolCard = CreateActionCard(
                new Point(16, 335),
                new Size(376, 74),
                "内置工具箱",
                "保留现有校验后释放与选择执行方式",
                "TOOLS",
                false,
                ShowFixMenu);

            var logCard = CreateMiniCard(new Point(16, 421), "操作日志", "查看每次扫描与删除结果", OpenLog);
            var aboutCard = CreateMiniCard(new Point(208, 421), "程序信息", "签名状态、版本与安全说明", ShowAbout);

            var footer = new Panel
            {
                Location = new Point(0, 519),
                Size = new Size(408, 75),
                BackColor = Color.FromArgb(9, 13, 21)
            };
            _status = new Label
            {
                Text = "准备就绪",
                Location = new Point(18, 13),
                Size = new Size(370, 24),
                AutoEllipsis = true,
                ForeColor = TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8.7F, FontStyle.Bold)
            };
            var footerHint = new Label
            {
                Text = "点击悬浮球收起  ·  右键悬浮球打开系统菜单",
                Location = new Point(18, 39),
                Size = new Size(370, 20),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 7.8F)
            };
            footer.Controls.Add(_status);
            footer.Controls.Add(footerHint);

            Controls.Add(header);
            Controls.Add(pathCard);
            Controls.Add(cleanupCard);
            Controls.Add(toolCard);
            Controls.Add(logCard);
            Controls.Add(aboutCard);
            Controls.Add(footer);

            Deactivate += delegate { if (!_dialogOpen) Close(); };
            Shown += delegate { ApplyRoundedRegion(); };
            Resize += delegate { ApplyRoundedRegion(); };
        }

        public void StartEnvironmentCleanup()
        {
            CleanEnvironment(this, EventArgs.Empty);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(15, 22, 36), Background, 125F))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
            using (var glow = new SolidBrush(Color.FromArgb(18, AccentBright)))
            {
                e.Graphics.FillEllipse(glow, -90, -150, 330, 300);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        private RoundedPanel CreateActionCard(Point location, Size size, string title, string description, string tag, bool primary, EventHandler click)
        {
            var card = new RoundedPanel
            {
                Location = location,
                Size = size,
                Radius = 18,
                FillColor = primary ? Color.FromArgb(43, 78, 171) : Surface,
                HoverColor = primary ? Color.FromArgb(50, 91, 197) : SurfaceHover,
                BorderColor = primary ? Color.FromArgb(99, 151, 255) : Border,
                Cursor = Cursors.Hand
            };
            var icon = new Label
            {
                Text = primary ? "↻" : "▦",
                Location = new Point(16, 16),
                Size = new Size(46, 46),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = primary ? Color.FromArgb(55, 255, 255, 255) : Color.FromArgb(39, 52, 75),
                Font = new Font("Segoe UI Symbol", 17F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            MakeRound(icon, 14);
            var titleLabel = new Label
            {
                Text = title,
                AutoSize = true,
                Location = new Point(76, 15),
                ForeColor = TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", primary ? 13F : 11F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            var descriptionLabel = new Label
            {
                Text = description,
                Location = new Point(77, primary ? 47 : 42),
                Size = new Size(260, 24),
                AutoEllipsis = true,
                ForeColor = primary ? Color.FromArgb(211, 224, 255) : TextMuted,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8.2F),
                Cursor = Cursors.Hand
            };
            var tagLabel = new Label
            {
                Text = tag,
                Location = new Point(306, 16),
                Size = new Size(52, 22),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = primary ? Color.White : AccentBright,
                BackColor = primary ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(30, 44, 68),
                Font = new Font("Segoe UI", 7F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            MakeRound(tagLabel, 10);

            card.Controls.Add(icon);
            card.Controls.Add(titleLabel);
            card.Controls.Add(descriptionLabel);
            card.Controls.Add(tagLabel);
            WireClick(card, click);
            return card;
        }

        private RoundedPanel CreateMiniCard(Point location, string title, string description, EventHandler click)
        {
            var card = new RoundedPanel
            {
                Location = location,
                Size = new Size(184, 86),
                Radius = 16,
                FillColor = Surface,
                HoverColor = SurfaceHover,
                BorderColor = Border,
                Cursor = Cursors.Hand
            };
            var titleLabel = new Label
            {
                Text = title,
                Location = new Point(14, 13),
                Size = new Size(150, 24),
                ForeColor = TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            var descriptionLabel = new Label
            {
                Text = description,
                Location = new Point(14, 42),
                Size = new Size(155, 34),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 7.7F),
                Cursor = Cursors.Hand
            };
            card.Controls.Add(titleLabel);
            card.Controls.Add(descriptionLabel);
            WireClick(card, click);
            return card;
        }

        private static Button CreateSmallButton(string text, Point location, int width)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Size = new Size(width, 27),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(35, 46, 66),
                ForeColor = TextPrimary,
                Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(57, 72, 98);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(44, 58, 82);
            return button;
        }

        private static void WireClick(Control parent, EventHandler click)
        {
            parent.Click += click;
            foreach (Control child in parent.Controls) child.Click += click;
        }

        private void CleanEnvironment(object sender, EventArgs e)
        {
            RunDialogAction(delegate
            {
                if (!CleanupProfile.IsConfigured)
                {
                    SetStatus("等待开发者填写清理规则");
                    MessageBox.Show(
                        "清理规则仍是占位配置。请先修改：\r\n\r\nsrc\\FACM\\Configuration\\CleanupProfile.cs\r\n\r\n填写后重新编译，程序才会启用删除功能。",
                        "需要开发者配置",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                if (!EnsureGamePath()) return;

                var running = ProcessGuard.GetRunningRelatedProcesses();
                if (running.Count > 0)
                {
                    SetStatus("检测到相关程序仍在运行");
                    MessageBox.Show(
                        "请先完全退出相关程序后再清理。\r\n\r\n正在运行：" + string.Join("、", running),
                        "暂不能清理",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (!ElevationService.IsAdministrator)
                {
                    var choice = MessageBox.Show(
                        "清理固定系统目录需要管理员权限。FACM 将以管理员身份重新启动，并自动继续本次清理。",
                        "需要管理员权限",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1);
                    if (choice != DialogResult.OK) return;
                    if (ElevationService.RestartElevatedForCleanup())
                    {
                        SetStatus("正在以管理员身份重新启动...");
                        _ownerBall.ExitApplication();
                    }
                    return;
                }

                SetStatus("正在生成清理预览...");
                var plan = SafeCleanupService.CreatePlan(_settings.GamePath);
                if (plan.Targets.Count == 0)
                {
                    SetStatus("没有发现可清理项目");
                    MessageBox.Show("没有发现符合开发者配置规则的文件或文件夹。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (var review = new CleanupReviewForm(plan))
                {
                    if (review.ShowDialog(this) != DialogResult.OK || !review.Confirmed)
                    {
                        SetStatus("已取消清理");
                        return;
                    }
                }

                SetStatus("正在清理...");
                var result = SafeCleanupService.Execute(plan);
                var summary = "清理完成。\r\n\r\n已删除文件：" + result.DeletedFiles +
                              "\r\n已删除文件夹：" + result.DeletedDirectories;
                if (result.Failures.Count > 0)
                {
                    summary += "\r\n未处理项目：" + result.Failures.Count + "\r\n\r\n详情已写入操作日志。";
                }
                SetStatus(result.Failures.Count == 0 ? "清理完成" : "清理完成，部分项目未处理");
                MessageBox.Show(summary, "FACM", MessageBoxButtons.OK,
                    result.Failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            });
        }

        private void DetectGamePath(object sender, EventArgs e)
        {
            RunDialogAction(delegate
            {
                if (!CleanupProfile.IsConfigured)
                {
                    MessageBox.Show("请先完成 CleanupProfile.cs 中的开发者配置。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                SetStatus("正在从进程与注册表识别目录...");
                var detected = GameLocator.FindGameRoot();
                if (string.IsNullOrEmpty(detected))
                {
                    SetStatus("未自动识别，请手动选择");
                    MessageBox.Show("未能自动识别目录，请手动选择标记文件夹的上级目录。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                SaveGamePath(detected);
                SetStatus("已自动识别工作目录");
            });
        }

        private void SelectGamePath(object sender, EventArgs e)
        {
            RunDialogAction(delegate
            {
                if (!CleanupProfile.IsConfigured)
                {
                    MessageBox.Show("请先完成 CleanupProfile.cs 中的开发者配置。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (var dialog = new FolderBrowserDialog
                {
                    Description = "请选择安装目录或标记文件夹的任意上级目录",
                    ShowNewFolderButton = false,
                    SelectedPath = Directory.Exists(_settings.GamePath) ? _settings.GamePath : string.Empty
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    var resolved = GameLocator.ResolveGameRoot(dialog.SelectedPath);
                    if (string.IsNullOrEmpty(resolved))
                    {
                        MessageBox.Show("所选范围内没有找到开发者配置的标记文件夹。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    SaveGamePath(resolved);
                    SetStatus("已保存工作目录");
                }
            });
        }

        private void ShowFixMenu(object sender, EventArgs e)
        {
            var menu = new ContextMenuStrip
            {
                Font = new Font("Microsoft YaHei UI", 9F),
                ShowImageMargin = false,
                BackColor = Color.FromArgb(28, 36, 52),
                ForeColor = TextPrimary
            };
            menu.Items.Add("运行模式 1", null, delegate { RunFixMode(1); });
            menu.Items.Add("运行模式 2", null, delegate { RunFixMode(2); });
            menu.Items.Add("运行模式 3", null, delegate { RunFixMode(3); });
            menu.Items.Add("运行模式 4", null, delegate { RunFixMode(4); });
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
                    SetStatus("已启动内置工具模式 " + mode);
                }
                catch (Exception exception)
                {
                    AppLog.Error("Built-in tool launch failed", exception);
                    MessageBox.Show("启动内置工具失败：" + exception.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    if (!File.Exists(path)) File.WriteAllText(path, string.Empty);
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    SetStatus("已打开操作日志");
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
                    "FACM 3.0\r\n\r\n" +
                    "签名状态：" + SignatureInspector.GetCurrentExecutableSignatureStatus() + "\r\n" +
                    "运行权限：" + (ElevationService.IsAdministrator ? "管理员" : "标准用户") + "\r\n" +
                    "清理配置：" + (CleanupProfile.IsConfigured ? "已配置" : "尚未配置") + "\r\n" +
                    "内置工具：固定版本资源，释放前校验 SHA-256\r\n\r\n" +
                    "程序不联网；清理前展示完整路径并要求确认。",
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
                SaveGamePath(detected);
                return true;
            }

            SelectGamePath(this, EventArgs.Empty);
            return GameLocator.IsValidGameRoot(_settings.GamePath);
        }

        private void SaveGamePath(string path)
        {
            _settings.GamePath = Path.GetFullPath(path);
            _settings.Save();
            RefreshPathLabel();
        }

        private void RefreshPathLabel()
        {
            _pathValue.Text = GameLocator.IsValidGameRoot(_settings.GamePath)
                ? _settings.GamePath
                : "尚未选择或未完成开发者配置";
        }

        private void SetStatus(string text)
        {
            _status.Text = text;
            AppLog.Info(text);
        }

        private void RunDialogAction(Action action)
        {
            _dialogOpen = true;
            try
            {
                action();
            }
            catch (Exception exception)
            {
                AppLog.Error("Control center action failed", exception);
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
            using (var path = RoundedRectangle(new Rectangle(0, 0, Width, Height), 24))
            {
                Region = new Region(path);
            }
        }

        private static void MakeRound(Control control, int radius)
        {
            Action apply = delegate
            {
                if (control.Width <= 0 || control.Height <= 0) return;
                using (var path = RoundedRectangle(new Rectangle(0, 0, control.Width, control.Height), radius))
                {
                    control.Region = new Region(path);
                }
            };
            control.SizeChanged += delegate { apply(); };
            apply();
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Max(2, radius * 2);
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class RoundedPanel : Panel
        {
            private bool _hovered;

            public int Radius { get; set; } = 16;
            public Color FillColor { get; set; } = Surface;
            public Color HoverColor { get; set; } = SurfaceHover;
            public Color BorderColor { get; set; } = Border;

            public RoundedPanel()
            {
                DoubleBuffered = true;
                BackColor = Color.Transparent;
                MouseEnter += delegate { _hovered = true; Invalidate(); };
                MouseLeave += delegate { _hovered = false; Invalidate(); };
                SizeChanged += delegate { ApplyRegion(); };
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
                using (var brush = new SolidBrush(_hovered ? HoverColor : FillColor))
                using (var pen = new Pen(BorderColor))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }
            }

            protected override void OnControlAdded(ControlEventArgs e)
            {
                base.OnControlAdded(e);
                e.Control.MouseEnter += delegate { _hovered = true; Invalidate(); };
                e.Control.MouseLeave += delegate { _hovered = false; Invalidate(); };
            }

            private void ApplyRegion()
            {
                if (Width <= 0 || Height <= 0) return;
                using (var path = RoundedRectangle(new Rectangle(0, 0, Width, Height), Radius))
                {
                    Region = new Region(path);
                }
            }
        }
    }
}
