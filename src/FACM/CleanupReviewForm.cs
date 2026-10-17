using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM
{
    internal sealed class CleanupReviewForm : Form
    {
        private static Color Surface { get { return FacmDesignSystem.Canvas; } }
        private static Color PanelColor { get { return FacmDesignSystem.Surface; } }
        private static Color Border { get { return FacmDesignSystem.BorderSoft; } }
        private static Color TextPrimary { get { return FacmDesignSystem.Text; } }
        private static Color TextMuted { get { return FacmDesignSystem.TextMuted; } }
        private static Color Accent { get { return FacmDesignSystem.Accent; } }
        private static Color Danger { get { return FacmDesignSystem.Error; } }

        private Point _dragStart;
        private bool _dragging;

        public bool Confirmed { get; private set; }

        public CleanupReviewForm(CleanupPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            Text = "FACM 清理预览";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(720, 540);
            MinimumSize = MaximumSize = Size;
            BackColor = Surface;
            ForeColor = TextPrimary;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F);
            DoubleBuffered = true;

            var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Surface };
            header.MouseDown += HeaderMouseDown;
            header.MouseMove += HeaderMouseMove;
            header.MouseUp += HeaderMouseUp;

            var title = new Label
            {
                Text = "确认清理范围",
                AutoSize = true,
                Location = new Point(24, 15),
                ForeColor = TextPrimary,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            var subtitle = new Label
            {
                Text = "只会处理下方列出的精确路径；保留目录不会出现在删除列表中。",
                AutoSize = true,
                Location = new Point(25, 46),
                ForeColor = TextMuted,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F),
                BackColor = Color.Transparent
            };
            var close = new Button
            {
                Text = "×",
                FlatStyle = FlatStyle.Flat,
                TabStop = true,
                AccessibleName = UiTextRuntime.Text(UiTextKeys.Close),
                Size = new Size(42, 42),
                Location = new Point(666, 9),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 20F),
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            close.FlatAppearance.BorderSize = 0;
            close.Click += delegate { Close(); };
            close.MouseEnter += delegate { close.ForeColor = TextPrimary; };
            close.MouseLeave += delegate { close.ForeColor = TextMuted; };
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(close);

            var summary = new Panel
            {
                Location = new Point(20, 78),
                Size = new Size(680, 78),
                BackColor = PanelColor
            };
            var countValue = new Label
            {
                Text = plan.DeletableTargets.Count.ToString(),
                Location = new Point(20, 14),
                Size = new Size(110, 32),
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            var countText = new Label
            {
                Text = "个清理目标",
                Location = new Point(21, 48),
                Size = new Size(110, 20),
                ForeColor = TextMuted,
                BackColor = Color.Transparent
            };
            var sizeValue = new Label
            {
                Text = SafeCleanupService.FormatBytes(plan.EstimatedBytes),
                Location = new Point(180, 16),
                Size = new Size(160, 28),
                ForeColor = TextPrimary,
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            var sizeText = new Label
            {
                Text = plan.FileCount + " 个文件  ·  " + plan.DirectoryCount + " 个文件夹",
                Location = new Point(181, 48),
                Size = new Size(230, 20),
                ForeColor = TextMuted,
                BackColor = Color.Transparent
            };
            var blocked = new Label
            {
                Text = plan.BlockedCount == 0 ? "安全检查通过" : plan.BlockedCount + " 项已自动阻止",
                Location = new Point(470, 22),
                Size = new Size(185, 34),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = plan.BlockedCount == 0 ? FacmDesignSystem.Success : Danger,
                BackColor = FacmDesignSystem.SurfaceRaised,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            summary.Controls.Add(countValue);
            summary.Controls.Add(countText);
            summary.Controls.Add(sizeValue);
            summary.Controls.Add(sizeText);
            summary.Controls.Add(blocked);

            var grid = new DataGridView
            {
                Location = new Point(20, 168),
                Size = new Size(680, 285),
                BackgroundColor = PanelColor,
                BorderStyle = BorderStyle.None,
                GridColor = FacmDesignSystem.BorderSoft,
                EnableHeadersVisualStyles = false,
                RowHeadersVisible = false,
                ColumnHeadersHeight = 38,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                ScrollBars = ScrollBars.Vertical
            };
            grid.RowTemplate.Height = 34;
            grid.ColumnHeadersDefaultCellStyle.BackColor = FacmDesignSystem.CanvasRaised;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = FacmDesignSystem.CanvasRaised;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold);
            grid.DefaultCellStyle.BackColor = PanelColor;
            grid.DefaultCellStyle.ForeColor = TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = FacmDesignSystem.SurfaceHover;
            grid.DefaultCellStyle.SelectionForeColor = TextPrimary;
            grid.DefaultCellStyle.Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "类别", Width = 105 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", Width = 96 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "大小", Width = 88 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "完整路径", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

            foreach (var target in plan.Targets)
            {
                var rowIndex = grid.Rows.Add(
                    target.Group,
                    target.Blocked ? "已阻止" : "将删除",
                    SafeCleanupService.FormatBytes(target.EstimatedBytes),
                    target.Path);
                if (target.Blocked)
                {
                    grid.Rows[rowIndex].DefaultCellStyle.ForeColor = Danger;
                    grid.Rows[rowIndex].Cells[1].ToolTipText = target.Detail;
                }
                else
                {
                    grid.Rows[rowIndex].Cells[1].Style.ForeColor = FacmDesignSystem.Success;
                }
            }

            var hint = new Label
            {
                Text = "删除不可撤销。执行前会再次校验每个路径与规则。",
                AutoSize = true,
                Location = new Point(24, 472),
                ForeColor = TextMuted,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F)
            };

            var cancel = CreateButton("取消", new Point(492, 468), new Size(92, 42), Color.FromArgb(39, 49, 69), TextPrimary);
            cancel.Click += delegate { Close(); };
            var execute = CreateButton("开始清理", new Point(592, 468), new Size(108, 42), Accent, Color.White);
            execute.Enabled = plan.DeletableTargets.Count > 0;
            execute.Click += delegate
            {
                Confirmed = true;
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(header);
            Controls.Add(summary);
            Controls.Add(grid);
            Controls.Add(hint);
            Controls.Add(cancel);
            Controls.Add(execute);
            CancelButton = cancel;

            Shown += delegate { ApplyRoundedRegion(); };
            Resize += delegate { ApplyRoundedRegion(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Border, 1F))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        private static Button CreateButton(string text, Point location, Size size, Color backColor, Color foreColor)
        {
            return new FacmActionButton
            {
                Text = text,
                Location = location,
                Size = size,
                Tone = backColor == Accent ? FacmButtonTone.Danger : FacmButtonTone.Secondary,
                Cursor = Cursors.Hand,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F, FontStyle.Bold),
                TabStop = true
            };
        }

        private void ApplyRoundedRegion()
        {
            using (var path = new GraphicsPath())
            {
                const int radius = 22;
                path.AddArc(0, 0, radius, radius, 180, 90);
                path.AddArc(Width - radius, 0, radius, radius, 270, 90);
                path.AddArc(Width - radius, Height - radius, radius, radius, 0, 90);
                path.AddArc(0, Height - radius, radius, radius, 90, 90);
                path.CloseFigure();
                var previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        private void HeaderMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragStart = e.Location;
        }

        private void HeaderMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Location = new Point(Left + e.X - _dragStart.X, Top + e.Y - _dragStart.Y);
        }

        private void HeaderMouseUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
        }
    }
}
