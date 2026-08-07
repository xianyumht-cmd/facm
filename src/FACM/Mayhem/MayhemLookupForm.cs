using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FACM.Mayhem
{
    internal sealed class MayhemLookupForm : Form
    {
        private readonly TextBox _query;
        private readonly Button _search;
        private readonly Label _status;
        private readonly Label _headline;
        private readonly Label _metrics;
        private readonly TextBox _balance;
        private readonly TextBox _skills;
        private readonly TextBox _items;
        private readonly TextBox _augments;
        private readonly DataGridView _topTen;
        private readonly LinkLabel _source;
        private CancellationTokenSource _queryCancellation;

        public MayhemLookupForm()
        {
            Text = "海斗排行榜查询 · OP.GG";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 680);
            ClientSize = new Size(980, 720);
            BackColor = Color.FromArgb(11, 16, 26);
            ForeColor = Color.FromArgb(240, 245, 255);
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = "海斗排行榜查询",
                Location = new Point(24, 18),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
                ForeColor = Color.White
            };
            var hint = new Label
            {
                Text = "输入英雄中文名、英文名或常用别名。数据来自 OP.GG ARAM: Mayhem。",
                Location = new Point(26, 56),
                Size = new Size(750, 24),
                ForeColor = Color.FromArgb(150, 166, 196)
            };

            _query = new TextBox
            {
                Location = new Point(24, 92),
                Size = new Size(530, 36),
                Font = new Font("Microsoft YaHei UI", 11F),
                BackColor = Color.FromArgb(28, 37, 56),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _query.KeyDown += QueryKeyDown;

            _search = new Button
            {
                Text = "查询",
                Location = new Point(566, 90),
                Size = new Size(112, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(69, 112, 255),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };
            _search.FlatAppearance.BorderColor = Color.FromArgb(114, 151, 255);
            _search.Click += async delegate { await SearchAsync(); };

            var examples = new Label
            {
                Text = "示例：寒冰 / 艾希 / Ashe / 琴女 / Sona / 火男",
                Location = new Point(696, 99),
                Size = new Size(250, 24),
                ForeColor = Color.FromArgb(126, 144, 177)
            };

            _status = new Label
            {
                Text = "准备查询",
                Location = new Point(24, 140),
                Size = new Size(930, 26),
                ForeColor = Color.FromArgb(99, 205, 166),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };

            var leftPanel = CreatePanel(new Rectangle(24, 174, 560, 502));
            var rightPanel = CreatePanel(new Rectangle(600, 174, 356, 502));

            _headline = new Label
            {
                Text = "尚未查询英雄",
                Location = new Point(18, 16),
                Size = new Size(522, 34),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                AutoEllipsis = true
            };
            _metrics = new Label
            {
                Text = "版本 / 排名 / 胜率将在此显示",
                Location = new Point(19, 52),
                Size = new Size(520, 28),
                ForeColor = Color.FromArgb(155, 174, 211)
            };

            var balanceLabel = CreateSectionLabel("当前版本 buff / debuff", 18, 88);
            _balance = CreateReadOnlyBox(new Rectangle(18, 112, 522, 56));
            var skillsLabel = CreateSectionLabel("技能加点", 18, 178);
            _skills = CreateReadOnlyBox(new Rectangle(18, 202, 522, 54));
            var itemsLabel = CreateSectionLabel("推荐出装", 18, 266);
            _items = CreateReadOnlyBox(new Rectangle(18, 290, 522, 76));
            var augmentsLabel = CreateSectionLabel("推荐强化符文", 18, 376);
            _augments = CreateReadOnlyBox(new Rectangle(18, 400, 522, 58));

            _source = new LinkLabel
            {
                Text = "打开 OP.GG 原始页面",
                Location = new Point(18, 468),
                Size = new Size(220, 24),
                LinkColor = Color.FromArgb(108, 163, 255),
                ActiveLinkColor = Color.FromArgb(151, 190, 255),
                DisabledLinkColor = Color.FromArgb(100, 110, 130),
                Enabled = false
            };
            _source.LinkClicked += OpenSource;

            leftPanel.Controls.Add(_headline);
            leftPanel.Controls.Add(_metrics);
            leftPanel.Controls.Add(balanceLabel);
            leftPanel.Controls.Add(_balance);
            leftPanel.Controls.Add(skillsLabel);
            leftPanel.Controls.Add(_skills);
            leftPanel.Controls.Add(itemsLabel);
            leftPanel.Controls.Add(_items);
            leftPanel.Controls.Add(augmentsLabel);
            leftPanel.Controls.Add(_augments);
            leftPanel.Controls.Add(_source);

            var rankTitle = new Label
            {
                Text = "当前版本总体胜率前十",
                Location = new Point(16, 15),
                Size = new Size(320, 30),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold)
            };
            var rankHint = new Label
            {
                Text = "按 OP.GG 返回的当前 Mayhem 排行展示",
                Location = new Point(17, 45),
                Size = new Size(320, 22),
                ForeColor = Color.FromArgb(132, 151, 184)
            };

            _topTen = new DataGridView
            {
                Location = new Point(14, 78),
                Size = new Size(328, 408),
                BackgroundColor = Color.FromArgb(15, 22, 35),
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                EnableHeadersVisualStyles = false
            };
            _topTen.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(32, 43, 65);
            _topTen.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _topTen.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            _topTen.DefaultCellStyle.BackColor = Color.FromArgb(20, 29, 45);
            _topTen.DefaultCellStyle.ForeColor = Color.FromArgb(225, 233, 249);
            _topTen.DefaultCellStyle.SelectionBackColor = Color.FromArgb(52, 78, 145);
            _topTen.DefaultCellStyle.SelectionForeColor = Color.White;
            _topTen.GridColor = Color.FromArgb(47, 59, 82);
            _topTen.RowTemplate.Height = 34;
            _topTen.Columns.Add("rank", "#");
            _topTen.Columns.Add("champion", "英雄");
            _topTen.Columns.Add("winRate", "胜率");
            _topTen.Columns.Add("tier", "梯队");
            _topTen.Columns[0].FillWeight = 18;
            _topTen.Columns[1].FillWeight = 48;
            _topTen.Columns[2].FillWeight = 28;
            _topTen.Columns[3].FillWeight = 24;

            rightPanel.Controls.Add(rankTitle);
            rightPanel.Controls.Add(rankHint);
            rightPanel.Controls.Add(_topTen);

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_query);
            Controls.Add(_search);
            Controls.Add(examples);
            Controls.Add(_status);
            Controls.Add(leftPanel);
            Controls.Add(rightPanel);

            AcceptButton = _search;
            FormClosed += delegate
            {
                if (_queryCancellation == null) return;
                _queryCancellation.Cancel();
                _queryCancellation.Dispose();
                _queryCancellation = null;
            };
        }

        private async Task SearchAsync()
        {
            var text = _query.Text.Trim();
            if (text.Length == 0)
            {
                MessageBox.Show("请输入英雄名称或别名。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _query.Focus();
                return;
            }

            if (_queryCancellation != null)
            {
                _queryCancellation.Cancel();
                _queryCancellation.Dispose();
            }
            _queryCancellation = new CancellationTokenSource();

            SetBusy(true, "正在连接 OP.GG 并读取当前版本数据...");
            try
            {
                var result = await OpggMayhemService.QueryAsync(text, _queryCancellation.Token);
                if (IsDisposed) return;
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    _status.Text = result.ErrorMessage;
                    _status.ForeColor = Color.FromArgb(255, 155, 120);
                    return;
                }
                RenderResult(result);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                {
                    _status.Text = "查询已取消";
                    _status.ForeColor = Color.FromArgb(170, 180, 200);
                }
            }
            catch (Exception exception)
            {
                if (!IsDisposed)
                {
                    _status.Text = "查询失败：" + exception.Message;
                    _status.ForeColor = Color.FromArgb(255, 155, 120);
                }
            }
            finally
            {
                if (!IsDisposed) SetBusy(false, _status.Text);
            }
        }

        private void RenderResult(MayhemChampionResult result)
        {
            _headline.Text = result.ChampionName + "  ·  ARAM: Mayhem";
            var metrics = new[]
            {
                string.IsNullOrWhiteSpace(result.Patch) ? "版本未知" : "版本 " + result.Patch,
                result.Rank.HasValue ? "排行 #" + result.Rank.Value : "排行待返回",
                result.WinRate.HasValue ? "胜率 " + result.WinRate.Value.ToString("0.00", CultureInfo.InvariantCulture) + "%" : "胜率待返回",
                string.IsNullOrWhiteSpace(result.Tier) ? null : result.Tier + " 梯队",
                result.PickRate.HasValue ? "选择率 " + result.PickRate.Value.ToString("0.00", CultureInfo.InvariantCulture) + "%" : null
            }.Where(value => !string.IsNullOrWhiteSpace(value));
            _metrics.Text = string.Join("  ·  ", metrics);
            _balance.Text = string.IsNullOrWhiteSpace(result.BalanceSummary) ? "OP.GG 当前页面未返回该项。" : result.BalanceSummary;
            _skills.Text = string.IsNullOrWhiteSpace(result.SkillOrder) ? "OP.GG 当前页面未返回该项。" : result.SkillOrder;
            _items.Text = result.CoreItems.Count == 0 ? "OP.GG 当前页面未解析到核心出装。" : string.Join("  →  ", result.CoreItems);
            _augments.Text = result.Augments.Count == 0 ? "OP.GG 当前页面未解析到强化符文。" : string.Join("  ·  ", result.Augments);

            _topTen.Rows.Clear();
            foreach (var champion in result.TopTen.OrderBy(item => item.Rank).Take(10))
            {
                _topTen.Rows.Add(
                    champion.Rank,
                    champion.Name,
                    champion.WinRate.HasValue ? champion.WinRate.Value.ToString("0.00", CultureInfo.InvariantCulture) + "%" : "—",
                    string.IsNullOrWhiteSpace(champion.Tier) ? "—" : champion.Tier);
            }
            if (result.TopTen.Count == 0) _topTen.Rows.Add("—", "OP.GG 暂未返回排行榜", "—", "—");

            _source.Tag = result.SourceUrl;
            _source.Enabled = !string.IsNullOrWhiteSpace(result.SourceUrl);
            _status.Text = (result.SourceNote ?? "查询完成") + "  ·  本地缓存 10 分钟";
            _status.ForeColor = Color.FromArgb(99, 205, 166);
        }

        private void SetBusy(bool busy, string status)
        {
            _search.Enabled = !busy;
            _query.Enabled = !busy;
            _search.Text = busy ? "查询中..." : "查询";
            if (!string.IsNullOrWhiteSpace(status)) _status.Text = status;
            if (busy) _status.ForeColor = Color.FromArgb(112, 165, 255);
            UseWaitCursor = busy;
        }

        private void QueryKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _ = SearchAsync();
        }

        private void OpenSource(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var url = _source.Tag as string;
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception exception)
            {
                MessageBox.Show("无法打开来源页面：" + exception.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static Panel CreatePanel(Rectangle bounds)
        {
            return new Panel
            {
                Location = bounds.Location,
                Size = bounds.Size,
                BackColor = Color.FromArgb(20, 28, 43),
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private static Label CreateSectionLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(300, 22),
                ForeColor = Color.FromArgb(142, 165, 207),
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
            };
        }

        private static TextBox CreateReadOnlyBox(Rectangle bounds)
        {
            return new TextBox
            {
                Location = bounds.Location,
                Size = bounds.Size,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(13, 20, 32),
                ForeColor = Color.FromArgb(230, 237, 250),
                Font = new Font("Microsoft YaHei UI", 9F)
            };
        }
    }
}
