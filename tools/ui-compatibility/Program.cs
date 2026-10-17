using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ReaLTaiizor.Controls;
using ReaLTaiizor.Enum.Poison;

namespace GGman.UiCompatibility
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var watch = Stopwatch.StartNew();
            try
            {
                using (var form = new CompatibilityForm())
                {
                    if (args.Length == 2 && args[0] == "--smoke")
                    {
                        form.Show();
                        Application.DoEvents();
                        form.ValidateControls();
                        var assembly = typeof(PoisonButton).Assembly;
                        var process = Process.GetCurrentProcess();
                        process.Refresh();
                        File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(new
                        {
                            status = "PASS: control construction and theme/scroll smoke only",
                            visualAcceptance = "INCONCLUSIVE: no interactive Win10 evidence",
                            os = Environment.OSVersion.VersionString,
                            clr = Environment.Version.ToString(),
                            assembly = assembly.FullName,
                            references = assembly.GetReferencedAssemblies().Select(a => a.FullName).ToArray(),
                            dpi = form.DeviceDpi,
                            constructionAndSmokeMilliseconds = watch.Elapsed.TotalMilliseconds,
                            privateBytes = process.PrivateMemorySize64,
                            workingSetBytes = process.WorkingSet64,
                            handles = process.HandleCount
                        }));
                        form.Close();
                    }
                    else if (args.Length == 0)
                    {
                        Application.Run(form);
                    }
                    else
                    {
                        throw new ArgumentException("Expected no arguments, or --smoke REPORT_PATH.");
                    }
                }
                return 0;
            }
            catch (Exception error)
            {
                if (args.Length == 2 && args[0] == "--smoke")
                    File.WriteAllText(args[1], error.ToString());
                else
                    MessageBox.Show(error.Message, "GGman UI compatibility", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    internal sealed class CompatibilityForm : Form
    {
        private readonly PoisonPanel viewport;
        private readonly FlowLayoutPanel rows;
        private readonly PoisonTextBox input;
        private readonly Label status;
        private readonly Font bodyFont;
        private readonly Font titleFont;
        private bool dark = true;

        internal CompatibilityForm()
        {
            Text = "GGman · 离线界面兼容性验证";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(900, 620);
            MinimumSize = new Size(600, 380);
            StartPosition = FormStartPosition.CenterScreen;
            bodyFont = new Font("Microsoft YaHei UI", 10F);
            titleFont = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold);
            Font = bodyFont;

            var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 118, Padding = new Padding(24, 12, 24, 8), ColumnCount = 2, RowCount = 2 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            var title = new Label { Text = "游戏修复", Font = titleFont, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            var notice = new Label { Text = "离线布局样本：所有按钮只显示结果，不执行修复、联网或更改配置。", AutoSize = true, Dock = DockStyle.Fill };
            var theme = Button("切换明暗", "切换明暗主题");
            theme.Click += delegate { dark = !dark; ApplyTheme(); };
            header.Controls.Add(title, 0, 0);
            header.Controls.Add(theme, 1, 0);
            header.Controls.Add(notice, 0, 1);
            header.SetColumnSpan(notice, 2);

            status = new Label { Text = "等待操作 · 本程序不连接 GGman 服务", Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(24, 12, 24, 8) };
            viewport = new PoisonPanel { Dock = DockStyle.Fill, AutoScroll = true, HorizontalScrollbar = false, VerticalScrollbar = true, VerticalScrollbarSize = 12 };
            rows = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24, 12, 24, 24), Margin = Padding.Empty };
            input = new PoisonTextBox { Width = 500, Height = 36, UseCustomFont = true, Font = bodyFont, AccessibleName = "中文输入测试", Text = "中文输入、选中、复制与粘贴测试", Margin = new Padding(0, 0, 0, 20) };
            rows.Controls.Add(input);
            var titles = new[] { "恢复游戏窗口", "自动窗口修复", "客户端维护", "结束游戏", "重新检查状态" };
            for (var index = 0; index < titles.Length; index++)
            {
                var name = titles[index];
                var group = new TableLayoutPanel { Width = 780, AutoSize = true, ColumnCount = 1, Padding = new Padding(16), Margin = new Padding(0, 0, 0, 16) };
                group.Controls.Add(new Label { Text = name, AutoSize = true, Margin = new Padding(0, 0, 0, 8) });
                group.Controls.Add(new Label { Text = "检查完整中文说明是否可读；缩小窗口后应换行。此处为安全的展示数据，不会启动或结束任何游戏进程。", AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(0, 0, 0, 12) });
                var action = Button("验证显示", name);
                action.Click += delegate { status.Text = name + " · 已验证按钮事件，未执行业务操作"; };
                group.Controls.Add(action);
                rows.Controls.Add(group);
            }
            viewport.Controls.Add(rows);
            Controls.Add(viewport);
            Controls.Add(status);
            Controls.Add(header);
            viewport.SizeChanged += ResizeRows;
            ApplyTheme();
        }

        private PoisonButton Button(string text, string accessibleName)
        {
            return new PoisonButton
            {
                Text = text, AccessibleName = accessibleName, Width = 136, Height = 36,
                UseCustomFont = true, Font = bodyFont, DisplayFocus = true, UseSelectable = true, TabStop = true
            };
        }

        private void ResizeRows(object sender, EventArgs args)
        {
            var width = Math.Max(200, viewport.ClientSize.Width - viewport.VerticalScrollbarSize - 8);
            rows.MaximumSize = new Size(width, 0);
            rows.Width = width;
            foreach (Control row in rows.Controls)
            {
                row.Width = Math.Max(100, width - rows.Padding.Horizontal);
                foreach (var label in row.Controls.OfType<Label>())
                    label.MaximumSize = new Size(Math.Max(80, row.Width - row.Padding.Horizontal), 0);
            }
        }

        private void ApplyTheme()
        {
            var theme = dark ? ThemeStyle.Dark : ThemeStyle.Light;
            ApplyThemeTo(this, theme);
            viewport.VerticalPoisonScrollbar.Theme = theme;
            Invalidate(true);
        }

        private static void ApplyThemeTo(Control control, ThemeStyle theme)
        {
            control.BackColor = theme == ThemeStyle.Dark ? Color.FromArgb(24, 28, 34) : Color.FromArgb(245, 247, 250);
            control.ForeColor = theme == ThemeStyle.Dark ? Color.FromArgb(235, 239, 244) : Color.FromArgb(25, 32, 42);
            var button = control as PoisonButton;
            if (button != null) button.Theme = theme;
            var text = control as PoisonTextBox;
            if (text != null) text.Theme = theme;
            var panel = control as PoisonPanel;
            if (panel != null) panel.Theme = theme;
            foreach (Control child in control.Controls) ApplyThemeTo(child, theme);
        }

        internal void ValidateControls()
        {
            if (!viewport.IsHandleCreated || !input.IsHandleCreated)
                throw new InvalidOperationException("Native control handles were not created.");
            input.Text = "中文 IME fixture";
            for (var i = 0; i < 20; i++) { dark = !dark; ApplyTheme(); }
            if (input.Text != "中文 IME fixture") throw new InvalidOperationException("Theme switch changed text.");
            if (viewport.HorizontalScroll.Visible) throw new InvalidOperationException("Unexpected horizontal overflow.");
            viewport.AutoScrollPosition = new Point(0, 100);
            if (viewport.AutoScrollPosition.Y >= 0) throw new InvalidOperationException("Vertical content did not scroll.");
            viewport.AutoScrollPosition = Point.Empty;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && viewport != null) viewport.SizeChanged -= ResizeRows;
            base.Dispose(disposing);
            if (disposing) { bodyFont?.Dispose(); titleFont?.Dispose(); }
        }
    }
}
