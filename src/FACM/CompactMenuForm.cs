using System;
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

        public CompactMenuForm(MainForm ownerBall, AppSettings settings, UiTextCatalog ui, CleanupModule cleanupModule)
        {
            _ownerBall = ownerBall;
            _settings = settings;
            _ui = ui ?? UiTextCatalog.Load();
            _cleanup = cleanupModule ?? throw new ArgumentNullException(nameof(cleanupModule));
            _theme = ThemeCatalog.Get(_settings.ThemeId);
            _scaleX = _theme.WindowSize.Width / (float)BaseWidth;
            _scaleY = _theme.WindowSize.Height / (float)BaseHeight;

            Text = _ui.AppName;
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
                Text = "F", // ui-text-contract: allow brand mark
                Location = ScalePoint(18, 14),
                Size = ScaleSize(44, 44),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = _theme.Accent,
                Font = new Font("Segoe UI", ScaleFont(18F), FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            ApplyShape(logo, _theme.ButtonRadius + 3, _theme.UsesAngularCorners);
            logo.Click += OpenPersonalizationMenu;

            var brand = new Label
            {
                Text = _ui.AppName,
                AutoSize = true,
                Location = ScalePoint(76, 10),
                ForeColor = _theme.TextPrimary,
                Font = new Font("Segoe UI", ScaleFont(16F), _theme.HeaderFontStyle),
                BackColor = Color.Transparent
            };
            var version = new Label
            {
                Text = MainForm.DisplayMajorMinorVersion() + "  " + _ui.ControlCenter,
                AutoSize = true,
                Location = ScalePoint(77, 42),
                ForeColor = _theme.TextMuted,
                Font = new Font(_theme.FontName, ScaleFont(8F), FontStyle.Bold),
                BackColor = Color.Transparent
            };

            var adminBadge = CreateButton(
                _cleanup.IsAdministrator ? _ui.Get(UiTextKeys.Administrator) : _ui.Get(UiTextKeys.StandardMode),
                new Rectangle(282, 20, 84, 28),
                false);
            adminBadge.ForeColor = _cleanup.IsAdministrator ? _theme.Success : _theme.TextMuted;
            adminBadge.Enabled = false;

            var close = new Label
            {
                Text = "×", // ui-text-contract: allow standard close glyph
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

            // Directory selection is a prerequisite, not a daily task. Keep it as status + one
            // management affordance and reveal detection/manual selection only when requested.
            var pathCard = CreatePanel(new Rectangle(16, 80, 388, 62), false);
            pathCard.Controls.Add(CreateCaption(_ui.Get(UiTextKeys.WorkDirectory), new Point(15, 8), 130));
            _pathValue = new Label
            {
                Location = ScaleChildPoint(15, 29),
                Size = ScaleChildSize(278, 24),
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = _theme.TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(8.4F), FontStyle.Bold)
            };
            var managePath = CreateButton(_ui.Get(UiTextKeys.ShellManageDirectory), new Rectangle(306, 17, 67, 31), false);
            managePath.Location = ScaleChildPoint(306, 17);
            managePath.Size = ScaleChildSize(67, 31);
            managePath.Click += OpenDirectoryMenu;
            pathCard.Controls.Add(_pathValue);
            pathCard.Controls.Add(managePath);
            RefreshPathLabel();

            var cleanup = CreatePanel(new Rectangle(16, 154, 388, 92), true);
            cleanup.Cursor = Cursors.Hand;
            var cleanupIcon = new Label
            {
                Text = _theme.Style == ThemeStyle.Luxury ? "✦" : "↻", // ui-text-contract: allow decorative glyph
                Location = ScaleChildPoint(15, 21),
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
                Location = ScaleChildPoint(74, 15),
                Size = ScaleChildSize(215, 29),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(13F), FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            var cleanupHint = new Label
            {
                Text = _ui.Get(UiTextKeys.CleanupHint),
                Location = ScaleChildPoint(75, 47),
                Size = ScaleChildSize(225, 22),
                ForeColor = Blend(Color.White, _theme.TextMuted, 0.35F),
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(8.2F)),
                Cursor = Cursors.Hand
            };
            var cleanupTag = CreateButton(
                _ui.Get(UiTextKeys.StartCleanup),
                new Rectangle(297, 28, 76, 34),
                true);
            cleanupTag.Location = ScaleChildPoint(297, 28);
            cleanupTag.Size = ScaleChildSize(76, 34);
            cleanupTag.Font = new Font(_theme.FontName, ScaleFont(7.8F), FontStyle.Bold);
            cleanup.Controls.Add(cleanupIcon);
            cleanup.Controls.Add(cleanupTitle);
            cleanup.Controls.Add(cleanupHint);
            cleanup.Controls.Add(cleanupTag);
            WireClick(cleanup, CleanEnvironment);

            var featureCard = CreatePanel(new Rectangle(16, 258, 388, 234), false);
            featureCard.Controls.Add(CreateCaption(_ui.Get(UiTextKeys.ShellFeatureCenter), new Point(15, 9), 180));

            var repair = CreateActionRow(
                _ui.Get(UiTextKeys.ShellRepairTools),
                _ui.Get(UiTextKeys.ShellRepairHint),
                new Rectangle(15, 38, 358, 57),
                OpenRepairMenu);
            var league = CreateActionRow(
                _ui.Get(UiTextKeys.ShellLeague),
                _ui.Get(UiTextKeys.ShellLeagueHint),
                new Rectangle(15, 103, 358, 57),
                OpenLeagueMenu);
            var personalize = CreateActionRow(
                _ui.Get(UiTextKeys.ShellPersonalization),
                _ui.Get(UiTextKeys.ShellPersonalizationHint),
                new Rectangle(15, 168, 358, 57),
                OpenPersonalizationMenu);
            featureCard.Controls.Add(repair);
            featureCard.Controls.Add(league);
            featureCard.Controls.Add(personalize);

            var more = CreateButton(
                _ui.Get(UiTextKeys.ShellMoreSettings) + "  " + _ui.Get(UiTextKeys.ShellArrow),
                new Rectangle(16, 506, 388, 44),
                false);
            more.Click += OpenMoreMenu;

            var footer = new Panel
            {
                Location = ScalePoint(0, 566),
                Size = ScaleSize(BaseWidth, 114),
                BackColor = Blend(_theme.Background, Color.Black, _theme.IsLight ? 0.04F : 0.22F)
            };
            _status = new Label
            {
                Text = _ui.Get(UiTextKeys.Ready) + " · " + _theme.Name,
                Location = ScaleChildPoint(17, 17),
                Size = ScaleChildSize(386, 22),
                AutoEllipsis = true,
                ForeColor = _theme.Style == ThemeStyle.Synthwave ? _theme.AccentSecondary : _theme.TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(8.5F), FontStyle.Bold)
            };
            var footerHint = new Label
            {
                Text = _ui.Get(UiTextKeys.ShellSimpleHint),
                Location = ScaleChildPoint(17, 45),
                Size = ScaleChildSize(386, 18),
                ForeColor = _theme.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(7.5F))
            };
            var moveHint = new Label
            {
                Text = "单击悬浮球收起  ·  拖动悬浮球调整位置",
                Location = ScaleChildPoint(17, 69),
                Size = ScaleChildSize(386, 18),
                ForeColor = _theme.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(7.5F))
            };
            footer.Controls.Add(_status);
            footer.Controls.Add(footerHint);
            footer.Controls.Add(moveHint);

            Controls.Add(header);
            Controls.Add(pathCard);
            Controls.Add(cleanup);
            Controls.Add(featureCard);
            Controls.Add(more);
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
                    graphics.DrawLine(pen, ScaleX(14), Height - ScaleY(115), Width - ScaleX(14), Height - ScaleY(115));
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

        private ThemedPanel CreateActionRow(string title, string hint, Rectangle bounds, EventHandler click)
        {
            var row = new ThemedPanel(_theme, false)
            {
                Location = ScaleChildPoint(bounds.X, bounds.Y),
                Size = ScaleChildSize(bounds.Width, bounds.Height),
                Cursor = Cursors.Hand
            };
            var titleLabel = new Label
            {
                Text = title,
                Location = ScaleChildPoint(15, 8),
                Size = ScaleChildSize(bounds.Width - 72, 22),
                ForeColor = _theme.TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(10F), FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            var hintLabel = new Label
            {
                Text = hint,
                Location = ScaleChildPoint(15, 31),
                Size = ScaleChildSize(bounds.Width - 72, 18),
                ForeColor = _theme.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, ScaleFont(7.7F)),
                Cursor = Cursors.Hand
            };
            var arrow = new Label
            {
                Text = _ui.Get(UiTextKeys.ShellArrow),
                Location = ScaleChildPoint(bounds.Width - 48, 8),
                Size = ScaleChildSize(32, 38),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = _theme.AccentSecondary,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", ScaleFont(18F), FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            row.Controls.Add(titleLabel);
            row.Controls.Add(hintLabel);
            row.Controls.Add(arrow);
            WireClick(row, click);
            return row;
        }

        private static void WireClick(Control parent, EventHandler click)
        {
            parent.Click += click;
            foreach (Control child in parent.Controls) child.Click += click;
        }

        private void OpenDirectoryMenu(object sender, EventArgs e)
        {
            ShowPopupMenu(sender as Control, delegate(ContextMenuStrip menu)
            {
                AddPopupItem(menu, _ui.Get(UiTextKeys.AutoDetect), delegate { DetectGamePath(this, EventArgs.Empty); });
                AddPopupItem(menu, _ui.Get(UiTextKeys.SelectDirectory), delegate { SelectGamePath(this, EventArgs.Empty); });
            });
        }

        private void OpenRepairMenu(object sender, EventArgs e)
        {
            ShowPopupMenu(sender as Control, delegate(ContextMenuStrip menu)
            {
                AddPopupItem(menu, _ui.ToolA, delegate { RunToolA(); });
                menu.Items.Add(new ToolStripSeparator());
                for (var mode = 1; mode <= 4; mode++)
                {
                    var captured = mode;
                    AddPopupItem(menu, _ui.ModeName(mode), delegate { RunFixMode(captured); });
                }
            });
        }

        private void OpenLeagueMenu(object sender, EventArgs e)
        {
            _dialogOpen = true;
            if (_ownerBall.ShowShellGroup(ShellMenuGroups.LeagueGroupName, sender as Control, EndPopupInteraction)) return;
            _dialogOpen = false;
            SetStatus(string.Format(
                _ui.Get(UiTextKeys.ShellStatusFormat),
                _ui.Get(UiTextKeys.ShellLeague),
                _ui.Get(UiTextKeys.ShellUnavailable)));
        }

        private void OpenPersonalizationMenu(object sender, EventArgs e)
        {
            ShowPopupMenu(sender as Control, delegate(ContextMenuStrip menu)
            {
                AddPopupItem(menu, _ui.Get(UiTextKeys.PanelTheme), delegate { _ownerBall.OpenPanelThemeSelector(); });
                AddPopupItem(menu, _ui.Get(UiTextKeys.DesktopPet), delegate { _ownerBall.OpenPetSelector(); });
                AddPopupItem(menu, _ui.Get(UiTextKeys.RestoreFloatingBall), delegate { _ownerBall.RestoreDefaultBall(); });
                AddPopupItem(menu, _ui.Get(UiTextKeys.PetReset), delegate { _ownerBall.ResetAnimalPet(); });
            });
        }

        private void OpenMoreMenu(object sender, EventArgs e)
        {
            ShowPopupMenu(sender as Control, delegate(ContextMenuStrip menu)
            {
                var autoCheck = AddPopupItem(menu, _ui.Get(UiTextKeys.AutoCheckAtStartup), null);
                autoCheck.Checked = _settings.AutoUpdateEnabled;
                autoCheck.CheckOnClick = true;
                autoCheck.Click += delegate
                {
                    _settings.AutoUpdateEnabled = autoCheck.Checked;
                    _settings.Save();
                    SetStatus(string.Format(
                        _ui.Get(UiTextKeys.ShellStatusFormat),
                        _ui.Get(UiTextKeys.AutoCheckAtStartup),
                        autoCheck.Checked ? _ui.Get(UiTextKeys.ShellEnabled) : _ui.Get(UiTextKeys.ShellDisabled)));
                };
                menu.Items.Add(new ToolStripSeparator());
                AddPopupItem(menu, _ui.CheckUpdate, delegate { _ownerBall.OpenUpdateCenter(); });
                AddPopupItem(menu, _ui.OpenLog, delegate { OpenLog(this, EventArgs.Empty); });
                menu.Items.Add(new ToolStripSeparator());
                AddPopupItem(menu, _ui.Exit, delegate { _ownerBall.ExitApplication(); });
            });
        }

        private ToolStripMenuItem AddPopupItem(ContextMenuStrip menu, string text, Action click)
        {
            var item = new ToolStripMenuItem(text)
            {
                ForeColor = _theme.TextPrimary
            };
            if (click != null) item.Click += delegate { click(); };
            menu.Items.Add(item);
            return item;
        }

        private void ShowPopupMenu(Control anchor, Action<ContextMenuStrip> populate)
        {
            if (populate == null) return;
            var menu = new ContextMenuStrip
            {
                Font = new Font(_theme.FontName, 9F),
                ShowImageMargin = false,
                BackColor = _theme.Surface,
                ForeColor = _theme.TextPrimary
            };
            populate(menu);
            _dialogOpen = true;
            menu.Closed += delegate
            {
                _dialogOpen = false;
                try
                {
                    _ownerBall.BeginInvoke(new Action(delegate
                    {
                        if (!menu.IsDisposed) menu.Dispose();
                        if (!IsDisposed) Activate();
                    }));
                }
                catch
                {
                    try { if (!menu.IsDisposed) menu.Dispose(); } catch { }
                }
            };
            var point = anchor != null && !anchor.IsDisposed
                ? anchor.PointToScreen(new Point(0, anchor.Height + 4))
                : Cursor.Position;
            menu.Show(point);
        }

        private void EndPopupInteraction()
        {
            _dialogOpen = false;
            if (IsDisposed) return;
            try { BeginInvoke(new Action(Activate)); } catch { }
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
            if (_cleanup.IsValidGameRoot(_settings.GamePath))
            {
                _pathValue.Text = string.Format(
                    _ui.Get(UiTextKeys.ShellDirectoryReadyFormat),
                    _ui.Get(UiTextKeys.ShellDirectoryReady),
                    _settings.GamePath);
                _pathValue.ForeColor = _theme.Success;
                return;
            }

            _pathValue.Text = string.Format(
                _ui.Get(UiTextKeys.ShellDirectoryMissingFormat),
                _ui.Get(UiTextKeys.ShellDirectoryMissing));
            _pathValue.ForeColor = _theme.Warning;
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
