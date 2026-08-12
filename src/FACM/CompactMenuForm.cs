using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM
{
    internal sealed class CompactMenuForm : Form
    {
        private const int BaseWidth = 420;
        private const int BaseHeight = 680;

        private readonly MainForm _ownerBall;
        private readonly AppSettings _settings;
        private readonly UiTextCatalog _ui;
        private readonly CleanupModule _cleanup;
        private readonly ThemeDefinition _theme;
        private readonly float _scaleX;
        private readonly float _scaleY;
        private readonly Label _pathValue;
        private readonly Label _status;
        private bool _dialogOpen;

        public CompactMenuForm(MainForm ownerBall, AppSettings settings, UiTextCatalog ui, CleanupModule cleanup)
        {
            _ownerBall = ownerBall;
            _settings = settings;
            _ui = ui ?? UiTextCatalog.Load();
            _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
            _theme = ThemeCatalog.Get(_settings.ThemeId);
            _scaleX = _theme.WindowSize.Width / (float)BaseWidth;
            _scaleY = _theme.WindowSize.Height / (float)BaseHeight;

            Text = "FACM";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = _theme.WindowSize;
            BackColor = _theme.Background;
            ForeColor = _theme.TextPrimary;
            Font = new Font(_theme.FontName, 9F);
            DoubleBuffered = true;

            var header = new Panel
            {
                Location = Point.Empty,
                Size = ScaleSize(BaseWidth, 72),
                BackColor = Color.Transparent
            };

            var logo = new Label
            {
                Text = "F",
                Location = ScalePoint(18, 14),
                Size = ScaleSize(44, 44),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = _theme.Accent,
                Font = new Font("Segoe UI", ScaleFont(18F), FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            ApplyShape(logo, _theme.ButtonRadius + 3, _theme.UsesAngularCorners);
            logo.Click += OpenThemePicker;

            var brand = new Label
            {
                Text = "FACM",
                AutoSize = true,
                Location = ScalePoint(76, 10),
                ForeColor = _theme.TextPrimary,
                Font = new Font("Segoe UI", ScaleFont(16F), _theme.HeaderFontStyle),
                BackColor = Color.Transparent
            };
            var version = new Label
            {
                Text = "3.1  " + _ui.ControlCenter,
                AutoSize = true,
                Location = ScalePoint(77, 42),
                ForeColor = _theme.TextMuted,
                Font = new Font(_theme.FontName, ScaleFont(8F), FontStyle.Bold),
                BackColor = Color.Transparent
            };

            var adminBadge = CreateButton(
                _cleanup.IsAdministrator ? "管理员" : "标准模式",
                new Rectangle(282, 20, 84, 28),
                false);
            adminBadge.ForeColor = _cleanup.IsAdministrator ? _theme.Success : _theme.TextMuted;
            adminBadge.Enabled = false;

            var close = new Label
            {
                Text = "×",
                Location = ScalePoint(374, 14),
                Size = ScaleSize(32, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = _theme.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", ScaleFont(18F)),
                Cursor = Cursors.Hand
            };
            close.Click += delegate { Close(); };
            close.MouseEnter += delegate { close.ForeColor = _theme.TextPrimary; };
            close.MouseLeave += delegate { close.ForeColor = _theme.TextMuted; };

            header.Controls.Add(logo);
            header.Controls.Add(brand);
            header.Controls.Add(version);
            header.Controls.Add(adminBadge);
            header.Controls.Add(close);

            var pathCard = CreatePanel(new Rectangle(16, 80, 388, 96), false);
            pathCard.Controls.Add(CreateCaption("工作目录", new Point(15, 9), 120));
            _pathValue = new Label
            {
                Location = ScaleChildPoint(15, 31),
                Size = ScaleChildSize(358, 25),
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = _theme.TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(9F), FontStyle.Bold)
            };
            var detect = CreateButton("自动识别", new Rectangle(15, 62, 96, 26), false);
            detect.Location = ScaleChildPoint(15, 62);
            detect.Size = ScaleChildSize(96, 26);
            detect.Click += DetectGamePath;
            var choose = CreateButton("选择目录", new Rectangle(119, 62, 96, 26), false);
            choose.Location = ScaleChildPoint(119, 62);
            choose.Size = ScaleChildSize(96, 26);
            choose.Click += SelectGamePath;
            var config = new Label
            {
                Text = _cleanup.IsConfigured ? "● 规则已配置" : "● 等待配置",
                Location = ScaleChildPoint(224, 63),
                Size = ScaleChildSize(150, 23),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = _cleanup.IsConfigured ? _theme.Success : _theme.Warning,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(8F), FontStyle.Bold)
            };
            pathCard.Controls.Add(_pathValue);
            pathCard.Controls.Add(detect);
            pathCard.Controls.Add(choose);
            pathCard.Controls.Add(config);
            RefreshPathLabel();

            var cleanup = CreatePanel(new Rectangle(16, 188, 388, 82), true);
            cleanup.Cursor = Cursors.Hand;
            var cleanupIcon = new Label
            {
                Text = _theme.Style == ThemeStyle.Luxury ? "✦" : "↻",
                Location = ScaleChildPoint(15, 17),
                Size = ScaleChildSize(46, 46),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Blend(_theme.Accent, Color.White, 0.12F),
                Font = new Font("Segoe UI Symbol", ScaleFont(17F), FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            ApplyShape(cleanupIcon, _theme.ButtonRadius + 2, _theme.UsesAngularCorners);
            var cleanupTitle = new Label
            {
                Text = _ui.Cleanup,
                Location = ScaleChildPoint(74, 12),
                Size = ScaleChildSize(205, 29),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(13F), FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            var cleanupHint = new Label
            {
                Text = "先预览路径，再确认执行",
                Location = ScaleChildPoint(75, 43),
                Size = ScaleChildSize(225, 22),
                ForeColor = Blend(Color.White, _theme.TextMuted, 0.35F),
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(8.2F)),
                Cursor = Cursors.Hand
            };
            var cleanupTag = CreateButton(
                _theme.Style == ThemeStyle.Luxury ? "开始清理" : "CLEAN",
                new Rectangle(306, 25, 67, 31),
                true);
            cleanupTag.Location = ScaleChildPoint(306, 25);
            cleanupTag.Size = ScaleChildSize(67, 31);
            cleanupTag.Font = new Font(_theme.FontName, ScaleFont(7.4F), FontStyle.Bold);
            cleanup.Controls.Add(cleanupIcon);
            cleanup.Controls.Add(cleanupTitle);
            cleanup.Controls.Add(cleanupHint);
            cleanup.Controls.Add(cleanupTag);
            WireClick(cleanup, CleanEnvironment);

            var toolsCard = CreatePanel(new Rectangle(16, 282, 388, 184), false);
            toolsCard.Controls.Add(CreateCaption(_ui.ToolGroup, new Point(15, 9), 180));
            var toolA = CreateButton(_ui.ToolA, new Rectangle(15, 38, 358, 36), true);
            toolA.Location = ScaleChildPoint(15, 38);
            toolA.Size = ScaleChildSize(358, 36);
            toolA.Click += delegate { RunToolA(); };
            toolsCard.Controls.Add(toolA);
            for (var mode = 1; mode <= 4; mode++)
            {
                var captured = mode;
                var column = (mode - 1) % 2;
                var row = (mode - 1) / 2;
                var button = CreateButton(
                    _ui.ModeName(mode),
                    new Rectangle(15 + column * 181, 84 + row * 43, 177, 36),
                    false);
                button.Location = ScaleChildPoint(15 + column * 181, 84 + row * 43);
                button.Size = ScaleChildSize(177, 36);
                button.Click += delegate { RunFixMode(captured); };
                toolsCard.Controls.Add(button);
            }

            var onlineCard = CreatePanel(new Rectangle(16, 478, 388, 88), false);
            onlineCard.Controls.Add(CreateCaption("更新与公告", new Point(15, 9), 160));
            var autoUpdate = new CheckBox
            {
                Text = "启动时自动检查",
                Location = ScaleChildPoint(15, 42),
                Size = ScaleChildSize(178, 28),
                Checked = _settings.AutoUpdateEnabled,
                ForeColor = _theme.TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(_theme.FontName, ScaleFont(8.3F))
            };
            autoUpdate.CheckedChanged += delegate
            {
                _settings.AutoUpdateEnabled = autoUpdate.Checked;
                _settings.Save();
            };
            var update = CreateButton(_ui.CheckUpdate, new Rectangle(242, 35, 131, 36), true);
            update.Location = ScaleChildPoint(242, 35);
            update.Size = ScaleChildSize(131, 36);
            update.Click += delegate { _ownerBall.OpenUpdateCenter(); };
            onlineCard.Controls.Add(autoUpdate);
            onlineCard.Controls.Add(update);

            var bottomWidth = 120;
            var bottomGap = 14;
            var bottomX = 16;
            var logButton = CreateButton(_ui.OpenLog, new Rectangle(bottomX, 578, bottomWidth, 40), false);
            logButton.Click += OpenLog;
            var themeButton = CreateButton("主题设置", new Rectangle(bottomX + bottomWidth + bottomGap, 578, bottomWidth, 40), false);
            themeButton.Click += OpenThemePicker;
            var exitButton = CreateButton(_ui.Exit, new Rectangle(bottomX + (bottomWidth + bottomGap) * 2, 578, bottomWidth, 40), false);
            exitButton.Click += delegate { _ownerBall.ExitApplication(); };

            var footer = new Panel
            {
                Location = ScalePoint(0, 630),
                Size = ScaleSize(BaseWidth, 50),
                BackColor = Blend(_theme.Background, Color.Black, _theme.IsLight ? 0.04F : 0.22F)
            };
            _status = new Label
            {
                Text = "准备就绪 · " + _theme.Name,
                Location = ScaleChildPoint(17, 7),
                Size = ScaleChildSize(386, 19),
                AutoEllipsis = true,
                ForeColor = _theme.Style == ThemeStyle.Synthwave ? _theme.AccentSecondary : _theme.TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(8.5F), FontStyle.Bold)
            };
            var footerHint = new Label
            {
                Text = "单击悬浮球收起  ·  拖动悬浮球调整位置",
                Location = ScaleChildPoint(17, 27),
                Size = ScaleChildSize(386, 17),
                ForeColor = _theme.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(7.5F))
            };
            footer.Controls.Add(_status);
            footer.Controls.Add(footerHint);

            Controls.Add(header);
            Controls.Add(pathCard);
            Controls.Add(cleanup);
            Controls.Add(toolsCard);
            Controls.Add(onlineCard);
            Controls.Add(logButton);
            Controls.Add(themeButton);
            Controls.Add(exitButton);
            Controls.Add(footer);

            Deactivate += delegate { if (!_dialogOpen) Close(); };
            Shown += delegate { ApplyWindowRegion(); };
            Resize += delegate { ApplyWindowRegion(); };
        }

        public void StartEnvironmentCleanup()
        {
            CleanEnvironment(this, EventArgs.Empty);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new LinearGradientBrush(ClientRectangle, _theme.BackgroundSecondary, _theme.Background, 125F))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            DrawThemeDecoration(e.Graphics);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(_theme.Border, Math.Max(1F, _theme.BorderWidth)))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        private void DrawThemeDecoration(Graphics graphics)
        {
            if (_theme.Style == ThemeStyle.Cyber || _theme.Style == ThemeStyle.Rgb || _theme.Style == ThemeStyle.Synthwave)
            {
                using (var pen = new Pen(Color.FromArgb(35, _theme.AccentSecondary), 1F))
                {
                    for (var y = 74; y < Height; y += Math.Max(22, ScaleY(28)))
                    {
                        graphics.DrawLine(pen, 0, y, Width, y);
                    }
                    for (var x = -Height; x < Width; x += Math.Max(40, ScaleX(56)))
                    {
                        graphics.DrawLine(pen, x, Height, x + Height, 0);
                    }
                }
            }
            else if (_theme.Style == ThemeStyle.Aurora || _theme.Style == ThemeStyle.Holographic || _theme.Style == ThemeStyle.Glass)
            {
                using (var first = new SolidBrush(Color.FromArgb(25, _theme.Accent)))
                using (var second = new SolidBrush(Color.FromArgb(22, _theme.AccentSecondary)))
                {
                    graphics.FillEllipse(first, Width / 2, -Height / 6, Width, Height / 2);
                    graphics.FillEllipse(second, -Width / 3, Height / 2, Width, Height / 2);
                }
            }
            else if (_theme.Style == ThemeStyle.Brutalist)
            {
                using (var brush = new SolidBrush(_theme.AccentSecondary))
                {
                    graphics.FillRectangle(Width - ScaleX(104), 0, ScaleX(104), ScaleY(12));
                    graphics.FillRectangle(0, Height - ScaleY(12), ScaleX(150), ScaleY(12));
                }
            }
            else if (_theme.Style == ThemeStyle.Luxury)
            {
                using (var pen = new Pen(Color.FromArgb(115, _theme.AccentSecondary), 1F))
                {
                    graphics.DrawLine(pen, ScaleX(14), ScaleY(68), Width - ScaleX(14), ScaleY(68));
                    graphics.DrawLine(pen, ScaleX(14), Height - ScaleY(52), Width - ScaleX(14), Height - ScaleY(52));
                }
            }
        }

        private ThemedPanel CreatePanel(Rectangle bounds, bool primary)
        {
            return new ThemedPanel(_theme, primary)
            {
                Location = ScalePoint(bounds.X, bounds.Y),
                Size = ScaleSize(bounds.Width, bounds.Height)
            };
        }

        private ThemedButton CreateButton(string text, Rectangle bounds, bool primary)
        {
            return new ThemedButton(_theme, primary)
            {
                Text = text,
                Location = ScalePoint(bounds.X, bounds.Y),
                Size = ScaleSize(bounds.Width, bounds.Height),
                Font = new Font(_theme.FontName, ScaleFont(primary ? 8.8F : 8.2F), FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
        }

        private Label CreateCaption(string text, Point location, int width)
        {
            var color = _theme.Style == ThemeStyle.Brutalist ? _theme.TextPrimary : _theme.TextMuted;
            return new Label
            {
                Text = text,
                Location = ScaleChildPoint(location.X, location.Y),
                Size = ScaleChildSize(width, 23),
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(8.5F), FontStyle.Bold)
            };
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
                if (!_cleanup.IsConfigured)
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

                var running = _cleanup.GetRunningRelatedProcesses();
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

                if (!_cleanup.IsAdministrator)
                {
                    var choice = MessageBox.Show(
                        "清理固定系统目录需要管理员权限。FACM 将以管理员身份重新启动，并自动继续本次清理。",
                        "需要管理员权限",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1);
                    if (choice != DialogResult.OK) return;
                    if (_cleanup.RestartElevatedForCleanup())
                    {
                        SetStatus("正在以管理员身份重新启动...");
                        _ownerBall.ExitApplication();
                    }
                    return;
                }

                SetStatus("正在生成清理预览...");
                var plan = _cleanup.CreatePlan(_settings.GamePath);
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
                var result = _cleanup.Execute(plan);
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
                if (!_cleanup.IsConfigured)
                {
                    MessageBox.Show("请先完成 CleanupProfile.cs 中的开发者配置。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                SetStatus("正在从进程与注册表识别目录...");
                var detected = _cleanup.FindGameRoot();
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
                if (!_cleanup.IsConfigured)
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
                    var resolved = _cleanup.ResolveGameRoot(dialog.SelectedPath);
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

        private void OpenThemePicker(object sender, EventArgs e)
        {
            RunDialogAction(delegate
            {
                using (var picker = new ThemePickerForm(_settings.ThemeId))
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    if (string.Equals(_settings.ThemeId, picker.SelectedThemeId, StringComparison.OrdinalIgnoreCase)) return;
                    _settings.ThemeId = picker.SelectedThemeId;
                    _settings.Save();
                    AppLog.Info("Theme changed to " + picker.SelectedThemeId);
                    _ownerBall.BeginInvoke(new Action(_ownerBall.ApplyThemeSelection));
                }
            });
        }

        private bool EnsureGamePath()
        {
            if (_cleanup.IsValidGameRoot(_settings.GamePath)) return true;

            var detected = _cleanup.FindGameRoot();
            if (!string.IsNullOrEmpty(detected))
            {
                SaveGamePath(detected);
                return true;
            }

            SelectGamePath(this, EventArgs.Empty);
            return _cleanup.IsValidGameRoot(_settings.GamePath);
        }

        private void SaveGamePath(string path)
        {
            _settings.GamePath = Path.GetFullPath(path);
            _settings.Save();
            RefreshPathLabel();
        }

        private void RefreshPathLabel()
        {
            _pathValue.Text = _cleanup.IsValidGameRoot(_settings.GamePath)
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

        private void ApplyWindowRegion()
        {
            if (_theme.Style == ThemeStyle.Brutalist) return;
            using (var path = CreateShapePath(new Rectangle(0, 0, Width, Height), _theme.Radius, _theme.UsesAngularCorners))
            {
                Region = new Region(path);
            }
        }

        private void ApplyShape(Control control, int radius, bool angular)
        {
            Action apply = delegate
            {
                if (control.Width <= 0 || control.Height <= 0) return;
                using (var path = CreateShapePath(new Rectangle(0, 0, control.Width, control.Height), radius, angular))
                {
                    control.Region = new Region(path);
                }
            };
            control.SizeChanged += delegate { apply(); };
            apply();
        }

        private Point ScalePoint(int x, int y)
        {
            return new Point(ScaleX(x), ScaleY(y));
        }

        private Size ScaleSize(int width, int height)
        {
            return new Size(ScaleX(width), ScaleY(height));
        }

        private Point ScaleChildPoint(int x, int y)
        {
            return ScalePoint(x, y);
        }

        private Size ScaleChildSize(int width, int height)
        {
            return ScaleSize(width, height);
        }

        private int ScaleX(int value)
        {
            return Math.Max(1, (int)Math.Round(value * _scaleX));
        }

        private int ScaleY(int value)
        {
            return Math.Max(1, (int)Math.Round(value * _scaleY));
        }

        private float ScaleFont(float value)
        {
            var scale = Math.Min(_scaleX, _scaleY);
            return Math.Max(6F, value * (0.92F + (scale - 1F) * 0.45F));
        }

        private static Color Blend(Color first, Color second, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            return Color.FromArgb(
                (int)(first.A + (second.A - first.A) * amount),
                (int)(first.R + (second.R - first.R) * amount),
                (int)(first.G + (second.G - first.G) * amount),
                (int)(first.B + (second.B - first.B) * amount));
        }

        private static GraphicsPath CreateShapePath(Rectangle bounds, int radius, bool angular)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 1 || bounds.Height <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }

            if (angular)
            {
                var cut = Math.Max(4, Math.Min(16, radius + 7));
                path.AddPolygon(new[]
                {
                    new Point(bounds.Left + cut, bounds.Top),
                    new Point(bounds.Right - cut, bounds.Top),
                    new Point(bounds.Right, bounds.Top + cut),
                    new Point(bounds.Right, bounds.Bottom - cut),
                    new Point(bounds.Right - cut, bounds.Bottom),
                    new Point(bounds.Left + cut, bounds.Bottom),
                    new Point(bounds.Left, bounds.Bottom - cut),
                    new Point(bounds.Left, bounds.Top + cut)
                });
                path.CloseFigure();
                return path;
            }

            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                path.CloseFigure();
                return path;
            }

            var diameter = Math.Max(2, Math.Min(Math.Min(bounds.Width, bounds.Height), radius * 2));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class ThemedPanel : Panel
        {
            private readonly ThemeDefinition _theme;
            private readonly bool _primary;
            private bool _hovered;

            public ThemedPanel(ThemeDefinition theme, bool primary)
            {
                _theme = theme;
                _primary = primary;
                DoubleBuffered = true;
                BackColor = Color.Transparent;
                MouseEnter += delegate { _hovered = true; Invalidate(); };
                MouseLeave += delegate { _hovered = false; Invalidate(); };
                SizeChanged += delegate { ApplyRegion(); };
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
                using (var path = CreateShapePath(bounds, _theme.Radius, _theme.UsesAngularCorners))
                {
                    var first = _primary ? _theme.Accent : _theme.Surface;
                    var second = _primary ? _theme.AccentSecondary : _theme.SurfaceSecondary;
                    if (_hovered)
                    {
                        first = Blend(first, Color.White, _theme.IsLight ? 0.04F : 0.08F);
                        second = Blend(second, Color.White, _theme.IsLight ? 0.03F : 0.06F);
                    }
                    if (_theme.Style == ThemeStyle.Minimal && !_primary) second = first;
                    if (_theme.Style == ThemeStyle.Brutalist && !_primary) second = first;

                    using (var brush = new LinearGradientBrush(bounds, first, second, _theme.Style == ThemeStyle.Synthwave ? 0F : 18F))
                    using (var border = new Pen(_primary ? Blend(_theme.Border, Color.White, 0.2F) : _theme.Border, _theme.BorderWidth))
                    {
                        e.Graphics.FillPath(brush, path);
                        e.Graphics.DrawPath(border, path);
                    }

                    if (_theme.Style == ThemeStyle.Luxury)
                    {
                        var inner = Rectangle.Inflate(bounds, -4, -4);
                        using (var innerPath = CreateShapePath(inner, Math.Max(0, _theme.Radius - 3), false))
                        using (var innerPen = new Pen(Color.FromArgb(95, _theme.AccentSecondary), 1F))
                        {
                            e.Graphics.DrawPath(innerPen, innerPath);
                        }
                    }
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
                using (var path = CreateShapePath(new Rectangle(0, 0, Width, Height), _theme.Radius, _theme.UsesAngularCorners))
                {
                    Region = new Region(path);
                }
            }
        }

        private sealed class ThemedButton : Control
        {
            private readonly ThemeDefinition _theme;
            private readonly bool _primary;
            private bool _hovered;
            private bool _pressed;

            public ThemedButton(ThemeDefinition theme, bool primary)
            {
                _theme = theme;
                _primary = primary;
                DoubleBuffered = true;
                SetStyle(ControlStyles.Selectable, true);
                MouseEnter += delegate { _hovered = true; Invalidate(); };
                MouseLeave += delegate { _hovered = false; _pressed = false; Invalidate(); };
                MouseDown += delegate(object sender, MouseEventArgs e)
                {
                    if (e.Button != MouseButtons.Left || !Enabled) return;
                    _pressed = true;
                    Invalidate();
                };
                MouseUp += delegate { _pressed = false; Invalidate(); };
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                Invalidate();
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (!Enabled) return;
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                {
                    OnClick(EventArgs.Empty);
                    e.Handled = true;
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var offset = _pressed ? 1 : 0;
                var bounds = new Rectangle(1 + offset, 1 + offset, Width - 3, Height - 3);
                using (var path = CreateShapePath(bounds, _theme.ButtonRadius, _theme.UsesAngularCorners))
                {
                    var first = _primary ? _theme.Accent : _theme.SurfaceSecondary;
                    var second = _primary ? _theme.AccentSecondary : _theme.Surface;
                    if (_theme.Style == ThemeStyle.Brutalist)
                    {
                        first = _primary ? _theme.AccentSecondary : _theme.Surface;
                        second = first;
                    }
                    else if (_theme.Style == ThemeStyle.Minimal && !_primary)
                    {
                        first = _theme.Surface;
                        second = first;
                    }
                    if (_hovered && Enabled)
                    {
                        first = Blend(first, Color.White, _theme.IsLight ? 0.06F : 0.12F);
                        second = Blend(second, Color.White, _theme.IsLight ? 0.04F : 0.09F);
                    }
                    if (!Enabled)
                    {
                        first = Blend(first, _theme.Background, 0.45F);
                        second = Blend(second, _theme.Background, 0.45F);
                    }

                    using (var brush = new LinearGradientBrush(bounds, first, second, _theme.Style == ThemeStyle.Synthwave ? 0F : 12F))
                    using (var border = new Pen(_primary ? Blend(_theme.Border, Color.White, 0.18F) : _theme.Border, _theme.BorderWidth))
                    {
                        e.Graphics.FillPath(brush, path);
                        e.Graphics.DrawPath(border, path);
                    }

                    if (_theme.Style == ThemeStyle.Luxury)
                    {
                        var inner = Rectangle.Inflate(bounds, -3, -3);
                        using (var innerPath = CreateShapePath(inner, Math.Max(0, _theme.ButtonRadius - 2), false))
                        using (var pen = new Pen(Color.FromArgb(105, _theme.AccentSecondary), 1F))
                        {
                            e.Graphics.DrawPath(pen, innerPath);
                        }
                    }
                    else if (_theme.Style == ThemeStyle.Cyber || _theme.Style == ThemeStyle.Rgb)
                    {
                        using (var accentPen = new Pen(_theme.AccentSecondary, 1.4F))
                        {
                            e.Graphics.DrawLine(accentPen, bounds.Left + 8, bounds.Bottom - 2, bounds.Left + Math.Min(54, bounds.Width / 3), bounds.Bottom - 2);
                        }
                    }
                }

                var textColor = Enabled
                    ? (_primary || !_theme.IsLight ? Color.White : _theme.TextPrimary)
                    : _theme.TextMuted;
                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    bounds,
                    textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
