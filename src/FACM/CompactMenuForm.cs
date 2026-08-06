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
        private static readonly Color Background = Color.FromArgb(11, 16, 25);
        private static readonly Color Surface = Color.FromArgb(22, 29, 43);
        private static readonly Color SurfaceHover = Color.FromArgb(29, 39, 57);
        private static readonly Color Border = Color.FromArgb(48, 61, 84);
        private static readonly Color TextPrimary = Color.FromArgb(244, 247, 255);
        private static readonly Color TextMuted = Color.FromArgb(151, 165, 190);
        private static readonly Color Accent = Color.FromArgb(76, 126, 255);
        private static readonly Color AccentHover = Color.FromArgb(88, 142, 255);
        private static readonly Color Success = Color.FromArgb(92, 224, 166);

        private readonly MainForm _ownerBall;
        private readonly AppSettings _settings;
        private readonly UiTextCatalog _ui;
        private readonly Label _pathValue;
        private readonly Label _status;
        private bool _dialogOpen;

        public CompactMenuForm(MainForm ownerBall, AppSettings settings, UiTextCatalog ui)
        {
            _ownerBall = ownerBall;
            _settings = settings;
            _ui = ui ?? UiTextCatalog.Load();

            Text = "FACM";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(420, 680);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Microsoft YaHei UI", 9F);
            DoubleBuffered = true;

            var header = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(420, 72),
                BackColor = Color.Transparent
            };
            var logo = new Label
            {
                Text = "F",
                Location = new Point(18, 14),
                Size = new Size(44, 44),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Accent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold)
            };
            MakeRound(logo, 13);
            var brand = new Label
            {
                Text = "FACM",
                AutoSize = true,
                Location = new Point(76, 12),
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            var version = new Label
            {
                Text = "3.1  " + _ui.ControlCenter.ToUpperInvariant(),
                AutoSize = true,
                Location = new Point(77, 43),
                ForeColor = TextMuted,
                Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            var adminBadge = new Label
            {
                Text = ElevationService.IsAdministrator ? "管理员" : "标准模式",
                Location = new Point(282, 20),
                Size = new Size(84, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = ElevationService.IsAdministrator ? Success : TextMuted,
                BackColor = Color.FromArgb(25, 34, 50),
                Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold)
            };
            MakeRound(adminBadge, 14);
            var close = new Label
            {
                Text = "×",
                Location = new Point(374, 14),
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
            header.Controls.Add(adminBadge);
            header.Controls.Add(close);

            var pathCard = new RoundedPanel
            {
                Location = new Point(16, 80),
                Size = new Size(388, 96),
                Radius = 17,
                FillColor = Surface,
                BorderColor = Border
            };
            var pathTitle = CreateCaption("工作目录", new Point(15, 11), 100);
            _pathValue = new Label
            {
                Location = new Point(15, 32),
                Size = new Size(358, 25),
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            var detect = CreateSmallButton("自动识别", new Point(15, 62), 96);
            detect.Click += DetectGamePath;
            var choose = CreateSmallButton("选择目录", new Point(119, 62), 96);
            choose.Click += SelectGamePath;
            var config = new Label
            {
                Text = CleanupProfile.IsConfigured ? "● 规则已配置" : "● 等待配置",
                Location = new Point(234, 63),
                Size = new Size(140, 23),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = CleanupProfile.IsConfigured ? Success : Color.FromArgb(255, 180, 92),
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold)
            };
            pathCard.Controls.Add(pathTitle);
            pathCard.Controls.Add(_pathValue);
            pathCard.Controls.Add(detect);
            pathCard.Controls.Add(choose);
            pathCard.Controls.Add(config);
            RefreshPathLabel();

            var cleanup = new RoundedPanel
            {
                Location = new Point(16, 188),
                Size = new Size(388, 82),
                Radius = 18,
                FillColor = Color.FromArgb(43, 78, 171),
                HoverColor = Color.FromArgb(50, 91, 197),
                BorderColor = Color.FromArgb(99, 151, 255),
                Cursor = Cursors.Hand
            };
            var cleanupIcon = CreateIcon("↻", new Point(15, 17), true);
            var cleanupTitle = new Label
            {
                Text = _ui.Cleanup,
                Location = new Point(74, 13),
                Size = new Size(210, 29),
                ForeColor = TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            var cleanupHint = new Label
            {
                Text = "先预览路径，再确认执行",
                Location = new Point(75, 44),
                Size = new Size(230, 22),
                ForeColor = Color.FromArgb(215, 228, 255),
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8.2F),
                Cursor = Cursors.Hand
            };
            var cleanupTag = new Label
            {
                Text = "CLEAN",
                Location = new Point(311, 28),
                Size = new Size(58, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(48, 255, 255, 255),
                Font = new Font("Segoe UI", 7F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            MakeRound(cleanupTag, 11);
            cleanup.Controls.Add(cleanupIcon);
            cleanup.Controls.Add(cleanupTitle);
            cleanup.Controls.Add(cleanupHint);
            cleanup.Controls.Add(cleanupTag);
            WireClick(cleanup, CleanEnvironment);

            var toolsCard = new RoundedPanel
            {
                Location = new Point(16, 282),
                Size = new Size(388, 184),
                Radius = 17,
                FillColor = Surface,
                BorderColor = Border
            };
            toolsCard.Controls.Add(CreateCaption(_ui.ToolGroup, new Point(15, 11), 180));
            var toolA = CreateToolButton(_ui.ToolA, new Point(15, 38), 358, true);
            toolA.Click += delegate { RunToolA(); };
            toolsCard.Controls.Add(toolA);
            for (var mode = 1; mode <= 4; mode++)
            {
                var captured = mode;
                var column = (mode - 1) % 2;
                var row = (mode - 1) / 2;
                var button = CreateToolButton(
                    _ui.ModeName(mode),
                    new Point(15 + column * 181, 84 + row * 43),
                    177,
                    false);
                button.Click += delegate { RunFixMode(captured); };
                toolsCard.Controls.Add(button);
            }

            var onlineCard = new RoundedPanel
            {
                Location = new Point(16, 478),
                Size = new Size(388, 88),
                Radius = 17,
                FillColor = Surface,
                BorderColor = Border
            };
            onlineCard.Controls.Add(CreateCaption("更新与公告", new Point(15, 11), 160));
            var autoUpdate = new CheckBox
            {
                Text = "启动时自动检查",
                Location = new Point(15, 43),
                Size = new Size(160, 28),
                Checked = _settings.AutoUpdateEnabled,
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8.3F)
            };
            autoUpdate.CheckedChanged += delegate
            {
                _settings.AutoUpdateEnabled = autoUpdate.Checked;
                _settings.Save();
            };
            var update = CreateToolButton(_ui.CheckUpdate, new Point(242, 35), 131, true);
            update.Click += delegate { _ownerBall.OpenUpdateCenter(); };
            onlineCard.Controls.Add(autoUpdate);
            onlineCard.Controls.Add(update);

            var logButton = CreateBottomButton(_ui.OpenLog, new Point(16, 578));
            logButton.Click += OpenLog;
            var aboutButton = CreateBottomButton(_ui.About, new Point(114, 578));
            aboutButton.Click += ShowAbout;
            var textButton = CreateBottomButton(_ui.EditText, new Point(212, 578));
            textButton.Click += OpenTextConfig;
            var exitButton = CreateBottomButton(_ui.Exit, new Point(310, 578));
            exitButton.Click += delegate { _ownerBall.ExitApplication(); };

            var footer = new Panel
            {
                Location = new Point(0, 630),
                Size = new Size(420, 50),
                BackColor = Color.FromArgb(8, 12, 19)
            };
            _status = new Label
            {
                Text = "准备就绪",
                Location = new Point(17, 8),
                Size = new Size(386, 19),
                AutoEllipsis = true,
                ForeColor = TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
            };
            var footerHint = new Label
            {
                Text = "单击悬浮球收起  ·  拖动悬浮球调整位置",
                Location = new Point(17, 28),
                Size = new Size(386, 17),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 7.5F)
            };
            footer.Controls.Add(_status);
            footer.Controls.Add(footerHint);

            Controls.Add(header);
            Controls.Add(pathCard);
            Controls.Add(cleanup);
            Controls.Add(toolsCard);
            Controls.Add(onlineCard);
            Controls.Add(logButton);
            Controls.Add(aboutButton);
            Controls.Add(textButton);
            Controls.Add(exitButton);
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
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        private static Label CreateCaption(string text, Point location, int width)
        {
            return new Label
            {
                Text = text,
                Location = location,
                Size = new Size(width, 23),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
            };
        }

        private static Label CreateIcon(string text, Point location, bool primary)
        {
            var icon = new Label
            {
                Text = text,
                Location = location,
                Size = new Size(46, 46),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = primary ? Color.FromArgb(55, 255, 255, 255) : Color.FromArgb(39, 52, 75),
                Font = new Font("Segoe UI Symbol", 17F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            MakeRound(icon, 14);
            return icon;
        }

        private static Button CreateSmallButton(string text, Point location, int width)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Size = new Size(width, 26),
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

        private static Button CreateToolButton(string text, Point location, int width, bool primary)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Size = new Size(width, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.FromArgb(35, 46, 66),
                ForeColor = TextPrimary,
                Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(104, 153, 255) : Color.FromArgb(57, 72, 98);
            button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(44, 58, 82);
            return button;
        }

        private static Button CreateBottomButton(string text, Point location)
        {
            var button = CreateToolButton(text, location, 94, false);
            button.Size = new Size(94, 40);
            button.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold);
            return button;
        }

        private static void WireClick(Control parent, EventHandler click)
        {
            parent.Click += click;
            foreach (Control child in parent.Controls) child.Click += click;
        }

        private void RunToolA()
        {
            RunDialogAction(delegate
            {
                _ownerBall.RunToolA();
                SetStatus("已启动：" + _ui.ToolA);
            });
        }

        private void RunFixMode(int mode)
        {
            RunDialogAction(delegate
            {
                _ownerBall.RunToolMode(mode);
                SetStatus("已启动：" + _ui.ModeName(mode));
            });
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

        private void OpenLog(object sender, EventArgs e)
        {
            _ownerBall.OpenLogFile();
            SetStatus("已打开操作日志");
        }

        private void OpenTextConfig(object sender, EventArgs e)
        {
            RunDialogAction(delegate
            {
                UiTextCatalog.OpenConfig();
                MessageBox.Show(
                    "修改等号右侧文字并保存，然后重新启动 FACM。\r\n\r\n配置文件：\r\n" + UiTextCatalog.ConfigPath,
                    "界面文字",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        private void ShowAbout(object sender, EventArgs e)
        {
            RunDialogAction(delegate
            {
                MessageBox.Show(
                    "FACM 3.1\r\n\r\n" +
                    "签名状态：" + SignatureInspector.GetCurrentExecutableSignatureStatus() + "\r\n" +
                    "运行权限：" + (ElevationService.IsAdministrator ? "管理员" : "标准用户") + "\r\n" +
                    "清理配置：" + (CleanupProfile.IsConfigured ? "已配置" : "尚未配置") + "\r\n" +
                    "工具资源：已嵌入 FACM.exe，运行时校验后按需释放\r\n" +
                    "更新与公告：支持手动检查、自动提示和强制更新",
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
            using (var path = RoundedRectangle(new Rectangle(0, 0, Width, Height), 22))
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
