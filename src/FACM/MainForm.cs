using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Models;
using FACM.Services;
using Microsoft.Win32;

namespace FACM
{
    internal sealed class MainForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(15, 20, 32);
        private static readonly Color Side = Color.FromArgb(20, 27, 42);
        private static readonly Color Card = Color.FromArgb(28, 37, 55);
        private static readonly Color Card2 = Color.FromArgb(35, 46, 68);
        private static readonly Color Line = Color.FromArgb(56, 70, 96);
        private static readonly Color Blue = Color.FromArgb(77, 129, 255);
        private static readonly Color BlueHover = Color.FromArgb(98, 147, 255);
        private static readonly Color White = Color.FromArgb(241, 245, 255);
        private static readonly Color Muted = Color.FromArgb(157, 170, 195);
        private static readonly Color Green = Color.FromArgb(70, 198, 142);
        private static readonly Color Yellow = Color.FromArgb(244, 184, 74);
        private static readonly Color Red = Color.FromArgb(241, 103, 111);

        private readonly CleanupService _cleanup = new CleanupService();
        private readonly Panel _content = new Panel();
        private readonly Label _pageTitle = new Label();
        private readonly Label _pageHint = new Label();
        private readonly Label _status = new Label();
        private readonly Button _homeNav = new Button();
        private readonly Button _cleanNav = new Button();
        private readonly Button _aboutNav = new Button();

        private TextBox _gamePath;
        private FlowLayoutPanel _results;
        private Button _scan;
        private Button _delete;
        private Label _summary;
        private ProgressBar _progress;
        private CancellationTokenSource _operation;
        private IReadOnlyList<CleanupItem> _items = Array.Empty<CleanupItem>();

        public MainForm()
        {
            Text = "FACM";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1040, 680);
            MinimumSize = new Size(940, 600);
            BackColor = Bg;
            ForeColor = White;
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.None;
            Icon = SystemIcons.Shield;
            DoubleBuffered = true;

            BuildWindow();
            ShowHome();
            Shown += delegate { TryRoundWindow(); };
            Resize += delegate { if (WindowState == FormWindowState.Normal) TryRoundWindow(); };
            FormClosing += ClosingApp;
        }

        private void BuildWindow()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Bg };
            top.MouseDown += DragWindow;

            var logo = new RoundPanel { Location = new Point(20, 14), Size = new Size(34, 34), BackColor = Blue, Radius = 10 };
            logo.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "F",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold)
            });
            logo.MouseDown += DragWindow;

            var title = new Label
            {
                AutoSize = true,
                Location = new Point(66, 13),
                Text = "FACM",
                ForeColor = White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold)
            };
            title.MouseDown += DragWindow;
            var caption = new Label
            {
                AutoSize = true,
                Location = new Point(66, 36),
                Text = "安全清理中心",
                ForeColor = Muted,
                Font = new Font("Microsoft YaHei UI", 8F)
            };
            caption.MouseDown += DragWindow;

            var close = CaptionButton("×");
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Location = new Point(ClientSize.Width - 52, 13);
            close.Click += delegate { Close(); };
            close.MouseEnter += delegate { close.BackColor = Red; };
            close.MouseLeave += delegate { close.BackColor = Color.Transparent; };

            var min = CaptionButton("—");
            min.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            min.Location = new Point(ClientSize.Width - 100, 13);
            min.Click += delegate { WindowState = FormWindowState.Minimized; };

            top.Controls.Add(logo);
            top.Controls.Add(title);
            top.Controls.Add(caption);
            top.Controls.Add(min);
            top.Controls.Add(close);

            var sidebar = new Panel { Dock = DockStyle.Left, Width = 215, BackColor = Side, Padding = new Padding(16, 22, 16, 18) };
            sidebar.Controls.Add(BuildPrivacyCard());
            ConfigureNav(_aboutNav, "03   关于与签名");
            ConfigureNav(_cleanNav, "02   垃圾清理");
            ConfigureNav(_homeNav, "01   概览");
            _aboutNav.Click += delegate { ShowAbout(); };
            _cleanNav.Click += delegate { ShowCleaner(); };
            _homeNav.Click += delegate { ShowHome(); };
            sidebar.Controls.Add(_aboutNav);
            sidebar.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
            sidebar.Controls.Add(_cleanNav);
            sidebar.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
            sidebar.Controls.Add(_homeNav);
            sidebar.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = "功能",
                Padding = new Padding(10, 0, 0, 0),
                ForeColor = Muted,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
            });

            var main = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(32, 22, 32, 26) };
            var header = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = Bg };
            _pageTitle.AutoSize = true;
            _pageTitle.Location = new Point(0, 0);
            _pageTitle.ForeColor = White;
            _pageTitle.Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold);
            _pageHint.AutoSize = true;
            _pageHint.Location = new Point(2, 44);
            _pageHint.ForeColor = Muted;
            _pageHint.Font = new Font("Microsoft YaHei UI", 9.5F);
            _status.Size = new Size(170, 34);
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _status.TextAlign = ContentAlignment.MiddleCenter;
            _status.BackColor = Card;
            _status.ForeColor = Muted;
            _status.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            header.Resize += delegate { _status.Location = new Point(header.ClientSize.Width - _status.Width, 4); };
            header.Controls.Add(_pageTitle);
            header.Controls.Add(_pageHint);
            header.Controls.Add(_status);

            _content.Dock = DockStyle.Fill;
            _content.BackColor = Bg;
            _content.AutoScroll = true;
            main.Controls.Add(_content);
            main.Controls.Add(header);

            Controls.Add(main);
            Controls.Add(sidebar);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(37, 47, 66) });
            Controls.Add(top);
        }

        private Control BuildPrivacyCard()
        {
            var panel = new RoundPanel
            {
                Dock = DockStyle.Bottom,
                Height = 112,
                BackColor = Card,
                Radius = 14,
                Padding = new Padding(14)
            };
            panel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "不联网、不上传、不静默执行。\r\n每次删除都需要你确认。",
                ForeColor = Muted,
                Font = new Font("Microsoft YaHei UI", 8.3F),
                Padding = new Padding(0, 32, 0, 0)
            });
            panel.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                Text = "本地运行",
                ForeColor = White,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold)
            });
            return panel;
        }

        private void ShowHome()
        {
            ActiveNav(_homeNav);
            Header("概览", "只处理你明确选择的残留，不做后台扫描。", "就绪", Muted);
            ClearPage();

            var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 390, ColumnCount = 2, RowCount = 2, BackColor = Bg };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            grid.Controls.Add(HomeCard("垃圾清理", "扫描系统与所选游戏目录中的指定残留，核对后再删除。", "开始扫描", Blue, delegate { ShowCleaner(); }), 0, 0);
            grid.Controls.Add(HomeCard("安全边界", "白名单路径 · 链接拦截\r\n运行中拒绝清理 · 本地日志", null, Green, null), 1, 0);
            grid.Controls.Add(HomeCard("更少误报", "不内嵌第三方 EXE，不下载文件，不注入进程，不创建服务。", null, Blue, null), 0, 1);
            grid.Controls.Add(HomeCard("数字签名", SignatureInspector.GetCurrentExecutableSignatureStatus() + "\r\n正式发布应使用受信任证书。", null, Yellow, null), 1, 1);
            _content.Controls.Add(grid);
        }

        private Control HomeCard(string title, string text, string action, Color accent, Action click)
        {
            var panel = new RoundPanel { Dock = DockStyle.Fill, BackColor = Card, Radius = 16, Margin = new Padding(8), Padding = new Padding(20) };
            panel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                ForeColor = Muted,
                Font = new Font("Microsoft YaHei UI", 9F),
                Padding = new Padding(0, 48, 0, action == null ? 0 : 45)
            });
            panel.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 42,
                Text = title,
                ForeColor = White,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                Padding = new Padding(0, 9, 0, 0)
            });
            panel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = accent });
            if (action != null)
            {
                var button = ActionButton(action, accent);
                button.Dock = DockStyle.Bottom;
                button.Width = 130;
                button.Click += delegate { click(); };
                panel.Controls.Add(button);
            }
            return panel;
        }

        private void ShowCleaner()
        {
            ActiveNav(_cleanNav);
            Header("垃圾清理", "先扫描，再核对完整路径，最后手动确认删除。", "等待扫描", Muted);
            ClearPage();

            var select = new RoundPanel { Dock = DockStyle.Top, Height = 138, BackColor = Card, Radius = 16, Padding = new Padding(20) };
            select.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                Text = "游戏目录（可选）",
                ForeColor = White,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            });
            select.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = "留空时只扫描 C:\\Program Files 与 C:\\ProgramData 下的固定目录。",
                ForeColor = Muted,
                Font = new Font("Microsoft YaHei UI", 8.5F)
            });
            var pathRow = new Panel { Dock = DockStyle.Fill, BackColor = Card, Padding = new Padding(0, 8, 0, 0) };
            var browse = ActionButton("选择目录", Card2);
            browse.Dock = DockStyle.Right;
            browse.Width = 120;
            browse.Click += Browse;
            _gamePath = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Card2,
                ForeColor = White,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                Text = LoadGamePath()
            };
            pathRow.Controls.Add(_gamePath);
            pathRow.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 12, BackColor = Card });
            pathRow.Controls.Add(browse);
            select.Controls.Add(pathRow);

            var buttons = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Bg, Padding = new Padding(0, 17, 0, 11) };
            _scan = ActionButton("扫描残留", Blue);
            _scan.Dock = DockStyle.Left;
            _scan.Width = 145;
            _scan.Click += async delegate { await ScanAsync(); };
            _delete = ActionButton("开始清理", Red);
            _delete.Dock = DockStyle.Left;
            _delete.Width = 145;
            _delete.Enabled = false;
            _delete.Click += async delegate { await DeleteAsync(); };
            _summary = new Label { Dock = DockStyle.Fill, Text = "尚未扫描", TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted, Padding = new Padding(22, 0, 0, 0) };
            buttons.Controls.Add(_summary);
            buttons.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 12, BackColor = Bg });
            buttons.Controls.Add(_delete);
            buttons.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 12, BackColor = Bg });
            buttons.Controls.Add(_scan);

            _progress = new ProgressBar { Dock = DockStyle.Top, Height = 4, Style = ProgressBarStyle.Marquee, Visible = false };
            var resultTitle = new Label { Dock = DockStyle.Top, Height = 42, Text = "扫描结果", TextAlign = ContentAlignment.MiddleLeft, ForeColor = White, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold) };
            _results = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 250, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Bg };
            _results.Resize += delegate { ResizeRows(); };
            var notice = Notice("重要提示", "清理后，游戏可能在下次启动时重新下载组件。FACM 不停止服务、不修改第三方注册表，也不会绕过运行中的保护程序。", Yellow);
            notice.Dock = DockStyle.Top;
            notice.Height = 90;

            _content.Controls.Add(notice);
            _content.Controls.Add(_results);
            _content.Controls.Add(resultTitle);
            _content.Controls.Add(_progress);
            _content.Controls.Add(buttons);
            _content.Controls.Add(select);
        }

        private void ShowAbout()
        {
            ActiveNav(_aboutNav);
            Header("关于与签名", "公开行为和可复现构建，比隐藏动作更重要。", "FACM 2.0", Blue);
            ClearPage();

            var about = new RoundPanel { Dock = DockStyle.Top, Height = 225, BackColor = Card, Radius = 16, Padding = new Padding(22) };
            about.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "重新设计的 Windows 清理工具。核心逻辑公开、路径白名单固定、所有操作可在日志中核对。\r\n\r\n" + SignatureInspector.GetCurrentExecutableSignatureStatus(),
                ForeColor = Muted,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                Padding = new Padding(0, 50, 0, 45)
            });
            about.Controls.Add(new Label { Dock = DockStyle.Top, Height = 46, Text = "FACM 2.0", ForeColor = White, Font = new Font("Segoe UI", 18F, FontStyle.Bold) });
            var logs = ActionButton("打开日志目录", Card2);
            logs.Dock = DockStyle.Bottom;
            logs.Width = 145;
            logs.Click += delegate { OpenLogs(); };
            about.Controls.Add(logs);

            var signing = Notice("关于误报与签名", "自签名证书不能直接建立 SmartScreen 信誉。正式分发应使用受信任代码签名证书，对最终 EXE 执行 SHA-256 签名并添加可信时间戳。", Blue);
            signing.Dock = DockStyle.Top;
            signing.Height = 105;
            var excluded = Notice("本版本明确不包含", "网络更新器、下载执行、驱动安装、服务创建、进程注入、隐藏命令行、计划任务、开机自启和捆绑的第三方可执行文件。", Green);
            excluded.Dock = DockStyle.Top;
            excluded.Height = 100;
            _content.Controls.Add(excluded);
            _content.Controls.Add(signing);
            _content.Controls.Add(about);
        }

        private async Task ScanAsync()
        {
            if (_operation != null) return;
            var path = (_gamePath.Text ?? string.Empty).Trim();
            if (path.Length > 0 && !Directory.Exists(path))
            {
                MessageBox.Show("所选游戏目录不存在。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveGamePath(path);
            Busy(true, "正在扫描");
            _results.Controls.Clear();
            _operation = new CancellationTokenSource();
            try
            {
                var token = _operation.Token;
                _items = await Task.Run(() => _cleanup.Scan(path, token), token);
                RenderItems();
                var found = _items.Count(x => x.State == CleanupItemState.Found);
                var bytes = _items.Where(x => x.State == CleanupItemState.Found).Sum(x => x.EstimatedBytes);
                _summary.Text = found == 0 ? "未发现可清理目录" : string.Format("发现 {0} 项，共约 {1}", found, Bytes(bytes));
                _delete.Enabled = found > 0;
                Header("垃圾清理", "先扫描，再核对完整路径，最后手动确认删除。", found > 0 ? "发现残留" : "未发现残留", found > 0 ? Yellow : Green);
            }
            catch (Exception ex)
            {
                AppLog.Error("Scan operation failed", ex);
                MessageBox.Show("扫描失败：" + ex.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _operation.Dispose();
                _operation = null;
                Busy(false, null);
            }
        }

        private async Task DeleteAsync()
        {
            if (_operation != null) return;
            var targets = _items.Where(x => x.State == CleanupItemState.Found).ToList();
            if (targets.Count == 0) return;

            var running = ProcessGuard.GetRunningRelatedProcesses();
            if (running.Count > 0)
            {
                MessageBox.Show("检测到相关程序仍在运行：\r\n\r\n" + string.Join("\r\n", running) + "\r\n\r\n请正常退出后再清理。FACM 不会强制结束进程。", "无法清理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var paths = string.Join("\r\n", targets.Select(x => "• " + x.Path));
            if (MessageBox.Show("将永久删除：\r\n\r\n" + paths + "\r\n\r\n不会进入回收站，确认继续吗？", "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            Busy(true, "正在清理");
            _operation = new CancellationTokenSource();
            try
            {
                var token = _operation.Token;
                await Task.Run(() => { foreach (var item in targets) _cleanup.Delete(item, token); }, token);
                RenderItems();
                var deleted = targets.Count(x => x.State == CleanupItemState.Deleted);
                var failed = targets.Count - deleted;
                _summary.Text = string.Format("已删除 {0} 项，失败或跳过 {1} 项", deleted, failed);
                Header("垃圾清理", "结果已写入本地日志。", failed == 0 ? "清理完成" : "部分完成", failed == 0 ? Green : Yellow);
                MessageBox.Show(failed == 0 ? "清理完成。" : "部分项目失败或被安全策略跳过，请查看结果和日志。", "FACM", MessageBoxButtons.OK, failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                AppLog.Error("Delete operation failed", ex);
                MessageBox.Show("清理失败：" + ex.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _operation.Dispose();
                _operation = null;
                Busy(false, null);
                _delete.Enabled = _items.Any(x => x.State == CleanupItemState.Found);
            }
        }

        private void RenderItems()
        {
            _results.SuspendLayout();
            _results.Controls.Clear();
            foreach (var item in _items)
            {
                var panel = new RoundPanel { Width = Math.Max(520, _results.ClientSize.Width - 24), Height = 82, BackColor = Card, Radius = 12, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(16, 10, 16, 10) };
                var color = item.State == CleanupItemState.Found ? Yellow : item.State == CleanupItemState.Deleted ? Green : item.State == CleanupItemState.Missing ? Muted : Red;
                panel.Controls.Add(new Label { Dock = DockStyle.Right, Width = 150, Text = StateText(item) + "\r\n" + (item.State == CleanupItemState.Found ? item.SizeText : item.Detail), TextAlign = ContentAlignment.MiddleRight, ForeColor = color, Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold) });
                panel.Controls.Add(new Label { Dock = DockStyle.Fill, Text = item.DisplayName + "\r\n" + item.Path, ForeColor = White, Font = new Font("Microsoft YaHei UI", 8.8F), Padding = new Padding(0, 5, 10, 0), AutoEllipsis = true });
                _results.Controls.Add(panel);
            }
            _results.ResumeLayout();
        }

        private void ResizeRows()
        {
            if (_results == null) return;
            foreach (Control row in _results.Controls) row.Width = Math.Max(520, _results.ClientSize.Width - 24);
        }

        private void Browse(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { Description = "选择游戏安装目录", ShowNewFolderButton = false })
            {
                if (Directory.Exists(_gamePath.Text)) dialog.SelectedPath = _gamePath.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _gamePath.Text = dialog.SelectedPath;
                    SaveGamePath(dialog.SelectedPath);
                }
            }
        }

        private void OpenLogs()
        {
            try
            {
                var dir = Path.GetDirectoryName(AppLog.CurrentLogPath);
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开日志目录：" + ex.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Busy(bool value, string text)
        {
            if (_progress != null) _progress.Visible = value;
            if (_scan != null) _scan.Enabled = !value;
            if (_delete != null && value) _delete.Enabled = false;
            if (!string.IsNullOrEmpty(text)) { _status.Text = text; _status.ForeColor = Blue; }
        }

        private void Header(string title, string hint, string status, Color color)
        {
            _pageTitle.Text = title;
            _pageHint.Text = hint;
            _status.Text = status;
            _status.ForeColor = color;
        }

        private void ClearPage()
        {
            _content.Controls.Clear();
        }

        private void ActiveNav(Button active)
        {
            foreach (var button in new[] { _homeNav, _cleanNav, _aboutNav })
            {
                button.BackColor = ReferenceEquals(button, active) ? Color.FromArgb(45, 69, 122) : Side;
                button.ForeColor = ReferenceEquals(button, active) ? White : Muted;
            }
        }

        private static void ConfigureNav(Button button, string text)
        {
            button.Dock = DockStyle.Top;
            button.Height = 52;
            button.Text = text;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(12, 0, 0, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Side;
            button.ForeColor = Muted;
            button.Cursor = Cursors.Hand;
            button.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        }

        private static Button ActionButton(string text, Color color)
        {
            var button = new Button
            {
                Text = text,
                Height = 42,
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                ForeColor = White,
                Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static Button CaptionButton(string text)
        {
            var button = new Button { Text = text, Size = new Size(42, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, ForeColor = Muted, Font = new Font("Segoe UI", 12F), Cursor = Cursors.Hand, TabStop = false };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static Control Notice(string title, string text, Color accent)
        {
            var panel = new RoundPanel { BackColor = Card, Radius = 14, Padding = new Padding(18), Margin = new Padding(0, 14, 0, 0) };
            panel.Controls.Add(new Label { Dock = DockStyle.Fill, Text = text, ForeColor = Muted, Font = new Font("Microsoft YaHei UI", 8.6F), Padding = new Padding(0, 30, 0, 0) });
            panel.Controls.Add(new Label { Dock = DockStyle.Top, Height = 28, Text = title, ForeColor = White, Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold) });
            panel.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 4, BackColor = accent });
            return panel;
        }

        private static string StateText(CleanupItem item)
        {
            switch (item.State)
            {
                case CleanupItemState.Found: return "可清理";
                case CleanupItemState.Deleted: return "已删除";
                case CleanupItemState.Blocked: return "已跳过";
                case CleanupItemState.Failed: return "失败";
                default: return "未发现";
            }
        }

        private static string Bytes(long value)
        {
            if (value <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = value;
            var index = 0;
            while (size >= 1024 && index < units.Length - 1) { size /= 1024; index++; }
            return string.Format(index == 0 ? "{0:0} {1}" : "{0:0.##} {1}", size, units[index]);
        }

        private static string LoadGamePath()
        {
            try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\FACM")) return Convert.ToString(key?.GetValue("GameDirectory")) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static void SaveGamePath(string path)
        {
            try { using (var key = Registry.CurrentUser.CreateSubKey(@"Software\FACM")) key?.SetValue("GameDirectory", path ?? string.Empty, RegistryValueKind.String); }
            catch (Exception ex) { AppLog.Error("Save game directory failed", ex); }
        }

        private void ClosingApp(object sender, FormClosingEventArgs e)
        {
            if (_operation != null && MessageBox.Show("任务正在执行，确定取消并退出吗？", "FACM", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            if (_operation != null) _operation.Cancel();
            AppLog.Info("FACM stopped");
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }

        private void TryRoundWindow()
        {
            try { var value = 2; DwmSetWindowAttribute(Handle, 33, ref value, sizeof(int)); } catch { }
            if (WindowState != FormWindowState.Normal) return;
            using (var path = Rounded(new Rectangle(0, 0, Width, Height), 12)) Region = new Region(path);
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

        private sealed class RoundPanel : Panel
        {
            public int Radius { get; set; } = 12;
            public RoundPanel() { DoubleBuffered = true; ResizeRedraw = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var path = Rounded(ClientRectangle, Radius)) Region = new Region(path);
            }
        }

        private static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var d = Math.Max(2, radius * 2);
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
