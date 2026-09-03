using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Mayhem;
using FACM.Services;

namespace FACM.League
{
    /// <summary>
    /// Lightweight WinForms equivalent of the useful FACM 4 ChampSelect strip/automatic-guide UX.
    /// The assistant is intentionally limited to bench-enabled ChampSelect sessions (ARAM/Mayhem),
    /// reuses the one existing LeagueBenchQuickPickService, and never writes rune/build settings.
    /// </summary>
    internal sealed class LeagueChampSelectAssistantForm : Form
    {
        private const int CompactHeight = 116;
        private const int ExpandedHeight = 470;
        private readonly LeagueBenchQuickPickService _bench;
        private readonly MayhemAutomaticGuideService _guide;
        private readonly ILeagueClientApi _leagueClient;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Timer _pollTimer;
        private readonly Label _status;
        private readonly FlowLayoutPanel _benchPanel;
        private readonly Button _guideToggle;
        private readonly Panel _guidePanel;
        private readonly PictureBox _championIcon;
        private readonly Label _championTitle;
        private readonly Label _championMeta;
        private readonly FlowLayoutPanel _skillFlow;
        private readonly FlowLayoutPanel _spellFlow;
        private readonly FlowLayoutPanel _itemFlow;
        private readonly ListView _augmentList;
        private readonly Label _guideStatus;
        private readonly ToolTip _toolTip;
        private readonly Dictionary<int, Bitmap> _benchIconCache = new Dictionary<int, Bitmap>();
        private CancellationTokenSource _guideRequest;
        private bool _refreshing;
        private bool _benchWasAvailable;
        private bool _guideExpanded;
        private int _guideChampionId;
        private int _guideGeneration;
        private Point _dragCursor;
        private Point _dragWindow;
        private bool _dragging;

        public LeagueChampSelectAssistantForm(
            LeagueBenchQuickPickService bench,
            ILeagueClientApi leagueClient)
        {
            _bench = bench ?? throw new ArgumentNullException(nameof(bench));
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _guide = new MayhemAutomaticGuideService(_leagueClient);

            Text = "FACM · 选人助手";
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(660, CompactHeight);
            MinimumSize = MaximumSize = Size;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(14, 20, 32);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);
            DoubleBuffered = true;

            var header = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(660, 36),
                BackColor = Color.FromArgb(25, 34, 52),
                Cursor = Cursors.SizeAll
            };
            var title = new Label
            {
                Text = "FACM · 海克斯大乱斗选人助手",
                Location = new Point(12, 8),
                Size = new Size(290, 22),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = Color.White
            };
            _status = new Label
            {
                Text = "正在读取替补席…",
                Location = new Point(304, 9),
                Size = new Size(250, 20),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(156, 176, 210)
            };
            _guideToggle = CreateFlatButton("攻略", new Rectangle(560, 5, 52, 26), Color.FromArgb(55, 73, 105));
            _guideToggle.Enabled = false;
            _guideToggle.Click += delegate { SetGuideExpanded(!_guideExpanded); };
            var close = CreateFlatButton("×", new Rectangle(618, 5, 30, 26), Color.FromArgb(92, 48, 58));
            close.Click += delegate { Close(); };
            header.Controls.Add(title);
            header.Controls.Add(_status);
            header.Controls.Add(_guideToggle);
            header.Controls.Add(close);
            header.MouseDown += BeginDrag;
            header.MouseMove += ContinueDrag;
            header.MouseUp += EndDrag;
            title.MouseDown += BeginDrag;
            title.MouseMove += ContinueDrag;
            title.MouseUp += EndDrag;

            _benchPanel = new FlowLayoutPanel
            {
                Location = new Point(10, 43),
                Size = new Size(640, 62),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.FromArgb(18, 26, 40),
                Padding = new Padding(4, 4, 4, 1)
            };

            _guidePanel = new Panel
            {
                Location = new Point(10, 116),
                Size = new Size(640, 344),
                BackColor = Color.FromArgb(18, 26, 40),
                Visible = false,
                AutoScroll = false
            };
            _championIcon = new PictureBox
            {
                Location = new Point(10, 10),
                Size = new Size(54, 54),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(30, 42, 62)
            };
            _championTitle = new Label
            {
                Text = "自动攻略",
                Location = new Point(74, 10),
                Size = new Size(360, 26),
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                ForeColor = Color.White
            };
            _championMeta = new Label
            {
                Text = "等待读取当前英雄…",
                Location = new Point(75, 38),
                Size = new Size(540, 22),
                ForeColor = Color.FromArgb(150, 168, 198)
            };
            _guideStatus = new Label
            {
                Text = string.Empty,
                Location = new Point(10, 70),
                Size = new Size(610, 22),
                ForeColor = Color.FromArgb(111, 206, 165)
            };

            var skillTitle = CreateSectionLabel("技能", 10, 98);
            _skillFlow = CreateTokenFlow(62, 96, 170, 62);
            var spellTitle = CreateSectionLabel("召唤师技能", 242, 98);
            _spellFlow = CreateTokenFlow(242, 118, 160, 62);
            var itemTitle = CreateSectionLabel("出装", 412, 98);
            _itemFlow = CreateTokenFlow(412, 118, 208, 62);

            var augmentTitle = CreateSectionLabel("强化符文 · 完整排行", 10, 190);
            _augmentList = new ListView
            {
                Location = new Point(10, 214),
                Size = new Size(610, 116),
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Color.FromArgb(22, 31, 47),
                ForeColor = Color.FromArgb(232, 238, 248),
                BorderStyle = BorderStyle.FixedSingle
            };
            _augmentList.Columns.Add("#", 38, HorizontalAlignment.Right);
            _augmentList.Columns.Add("强化", 218, HorizontalAlignment.Left);
            _augmentList.Columns.Add("品质", 64, HorizontalAlignment.Left);
            _augmentList.Columns.Add("胜率", 78, HorizontalAlignment.Right);
            _augmentList.Columns.Add("选择率", 82, HorizontalAlignment.Right);
            _augmentList.Columns.Add("场次", 86, HorizontalAlignment.Right);

            _guidePanel.Controls.Add(_championIcon);
            _guidePanel.Controls.Add(_championTitle);
            _guidePanel.Controls.Add(_championMeta);
            _guidePanel.Controls.Add(_guideStatus);
            _guidePanel.Controls.Add(skillTitle);
            _guidePanel.Controls.Add(_skillFlow);
            _guidePanel.Controls.Add(spellTitle);
            _guidePanel.Controls.Add(_spellFlow);
            _guidePanel.Controls.Add(itemTitle);
            _guidePanel.Controls.Add(_itemFlow);
            _guidePanel.Controls.Add(augmentTitle);
            _guidePanel.Controls.Add(_augmentList);

            Controls.Add(header);
            Controls.Add(_benchPanel);
            Controls.Add(_guidePanel);

            _toolTip = new ToolTip { ShowAlways = true, AutomaticDelay = 120 };
            _pollTimer = new Timer { Interval = 650 };
            _pollTimer.Tick += async delegate { await RefreshBenchAsync(); };
            Shown += delegate
            {
                _pollTimer.Start();
                _ = RefreshBenchAsync();
            };
            FormClosed += HandleClosed;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        private async Task RefreshBenchAsync()
        {
            if (_refreshing || IsDisposed || _lifetime.IsCancellationRequested) return;
            _refreshing = true;
            try
            {
                LeagueBenchQuickPickState state;
                try
                {
                    state = await _bench.RefreshAsync(_lifetime.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                if (state == null || !state.SessionAvailable)
                {
                    _status.Text = "等待客户端选人数据…";
                    return;
                }

                // Only ARAM/Mayhem-style bench sessions should keep the automatic assistant alive.
                // Ordinary ranked ChampSelect must not display an ARAM Mayhem guide.
                if (!state.BenchEnabled)
                {
                    _status.Text = "当前选人模式没有替补席";
                    Close();
                    return;
                }

                _benchWasAvailable = true;
                _status.Text = state.ChampionIds.Count > 0
                    ? "替补席 " + state.ChampionIds.Count.ToString(CultureInfo.InvariantCulture) + " 个英雄"
                    : "替补席暂时为空";
                RenderBench(state);

                if (state.LocalChampionId > 0 && state.LocalChampionId != _guideChampionId)
                    StartAutomaticGuide(state.LocalChampionId);
            }
            catch (Exception exception)
            {
                AppLog.Info("ChampSelect assistant refresh skipped: " + exception.Message);
                if (!IsDisposed) _status.Text = _benchWasAvailable ? "替补席刷新暂时失败" : "正在等待替补席…";
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void RenderBench(LeagueBenchQuickPickState state)
        {
            var wanted = new HashSet<int>(state.ChampionIds.Where(id => id > 0));
            var existing = _benchPanel.Controls.OfType<Button>()
                .Where(button => button.Tag is int)
                .ToDictionary(button => (int)button.Tag, button => button);

            foreach (var pair in existing)
            {
                if (wanted.Contains(pair.Key)) continue;
                _benchPanel.Controls.Remove(pair.Value);
                pair.Value.Dispose();
            }

            foreach (var championId in state.ChampionIds.Where(id => id > 0))
            {
                Button button;
                if (!existing.TryGetValue(championId, out button))
                {
                    button = new Button
                    {
                        Tag = championId,
                        Width = 54,
                        Height = 48,
                        Margin = new Padding(2),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Color.FromArgb(33, 47, 70),
                        ForeColor = Color.White,
                        Text = championId.ToString(CultureInfo.InvariantCulture),
                        TextImageRelation = TextImageRelation.ImageBeforeText,
                        ImageAlign = ContentAlignment.MiddleLeft,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Cursor = Cursors.Hand
                    };
                    button.FlatAppearance.BorderColor = Color.FromArgb(65, 90, 128);
                    button.Click += async delegate { await SwapToAsync((int)button.Tag, state.SwapRoute); };
                    _toolTip.SetToolTip(button, "点击换到英雄 ID " + championId.ToString(CultureInfo.InvariantCulture));
                    _benchPanel.Controls.Add(button);
                    existing[championId] = button;
                    _ = LoadBenchIconAsync(button, championId);
                }

                button.FlatAppearance.BorderColor = championId == state.LocalChampionId
                    ? Color.FromArgb(92, 208, 155)
                    : Color.FromArgb(65, 90, 128);
            }
        }

        private async Task LoadBenchIconAsync(Button button, int championId)
        {
            if (button == null || button.IsDisposed) return;
            Bitmap cached;
            if (_benchIconCache.TryGetValue(championId, out cached))
            {
                button.Image = cached;
                button.Text = string.Empty;
                return;
            }

            try
            {
                var bytes = await _bench.LoadChampionIconAsync(championId, _lifetime.Token);
                if (bytes == null || bytes.Length == 0 || IsDisposed || button.IsDisposed) return;
                var image = DecodeBitmap(bytes, new Size(34, 34));
                if (image == null) return;
                _benchIconCache[championId] = image;
                button.Image = image;
                button.Text = string.Empty;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Text fallback remains usable.
            }
        }

        private async Task SwapToAsync(int championId, LeagueBenchSwapRoute route)
        {
            if (championId <= 0 || IsDisposed) return;
            _status.Text = "正在换人…";
            try
            {
                var result = await _bench.TrySwapAsync(championId, route, _lifetime.Token);
                if (IsDisposed) return;
                _status.Text = result.Success
                    ? "换人成功"
                    : DescribeSwapFailure(result.Status);
                _ = RefreshBenchAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Info("ChampSelect quick swap failed: " + exception.Message);
                if (!IsDisposed) _status.Text = "换人失败，请稍后重试";
            }
        }

        private void StartAutomaticGuide(int championId)
        {
            if (championId <= 0 || IsDisposed) return;
            CancelGuideRequest();
            _guideChampionId = championId;
            _guideGeneration++;
            var generation = _guideGeneration;
            _guideRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _guideRequest.CancelAfter(TimeSpan.FromSeconds(15));
            _guideToggle.Enabled = true;
            SetGuideExpanded(true);
            ResetGuideUi(championId);
            _ = LoadAutomaticGuideAsync(generation, championId, _guideRequest);
        }

        private async Task LoadAutomaticGuideAsync(int generation, int championId, CancellationTokenSource request)
        {
            try
            {
                var result = await _guide.QueryForChampionIdAsync(championId, request.Token);
                if (IsDisposed || request.IsCancellationRequested || generation != _guideGeneration || championId != _guideChampionId) return;
                if (result == null || !string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    _guideStatus.ForeColor = Color.FromArgb(245, 166, 126);
                    _guideStatus.Text = result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                        ? "自动攻略暂时没有结果；替补席仍可正常使用。"
                        : result.ErrorMessage + "；替补席仍可正常使用。";
                    return;
                }
                RenderGuide(result, generation, request.Token);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && !_lifetime.IsCancellationRequested && generation == _guideGeneration)
                {
                    _guideStatus.ForeColor = Color.FromArgb(245, 166, 126);
                    _guideStatus.Text = "自动攻略读取超时；替补席仍可正常使用。";
                }
            }
            catch (Exception exception)
            {
                AppLog.Info("Automatic Mayhem guide failed: " + exception.Message);
                if (!IsDisposed && generation == _guideGeneration)
                {
                    _guideStatus.ForeColor = Color.FromArgb(245, 166, 126);
                    _guideStatus.Text = "自动攻略读取失败；替补席仍可正常使用。";
                }
            }
            finally
            {
                if (ReferenceEquals(_guideRequest, request))
                {
                    request.Dispose();
                    _guideRequest = null;
                }
            }
        }

        private void ResetGuideUi(int championId)
        {
            DisposePicture(_championIcon);
            _championTitle.Text = "正在识别当前英雄…";
            _championMeta.Text = "英雄 ID " + championId.ToString(CultureInfo.InvariantCulture) + " · 自动攻略只读，不会修改符文或配置";
            _guideStatus.ForeColor = Color.FromArgb(111, 206, 165);
            _guideStatus.Text = "正在读取技能、出装和强化符文排行…";
            ClearTokenFlow(_skillFlow);
            ClearTokenFlow(_spellFlow);
            ClearTokenFlow(_itemFlow);
            _augmentList.Items.Clear();
        }

        private void RenderGuide(MayhemChampionResult result, int generation, CancellationToken token)
        {
            _championTitle.Text = string.IsNullOrWhiteSpace(result.ChampionName)
                ? (string.IsNullOrWhiteSpace(result.Query) ? "当前英雄" : result.Query)
                : result.ChampionName;
            _championMeta.Text = BuildChampionMeta(result);
            _guideStatus.ForeColor = Color.FromArgb(111, 206, 165);
            _guideStatus.Text = "攻略已加载 · 仅提供参考，不会自动应用任何配置";

            var skills = result.SkillPriority == null
                ? new List<MayhemSkillPriority>()
                : result.SkillPriority.Where(item => item != null).Take(4).ToList();
            if (skills.Count > 0)
            {
                foreach (var skill in skills)
                    AddToken(_skillFlow, FirstNonEmpty(skill.Key, skill.Name), skill.Name, skill.IconUrl, generation, token);
            }
            else if (!string.IsNullOrWhiteSpace(result.SkillOrder))
            {
                AddTextToken(_skillFlow, result.SkillOrder, result.SkillOrder);
            }

            foreach (var spell in (result.SummonerSpells ?? new List<MayhemBuildItem>()).Where(item => item != null).Take(2))
                AddToken(_spellFlow, FirstNonEmpty(spell.Name, spell.Id), spell.Name, spell.IconUrl, generation, token);

            var itemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in (result.StarterItems ?? new List<MayhemBuildItem>())
                .Concat(result.BootItems ?? new List<MayhemBuildItem>())
                .Concat(result.CoreBuilds != null && result.CoreBuilds.Count > 0 && result.CoreBuilds[0] != null
                    ? result.CoreBuilds[0].Items ?? new List<MayhemBuildItem>()
                    : new List<MayhemBuildItem>()))
            {
                if (item == null) continue;
                var key = FirstNonEmpty(item.Id, item.Name);
                if (string.IsNullOrWhiteSpace(key) || !itemKeys.Add(key)) continue;
                AddToken(_itemFlow, FirstNonEmpty(item.Name, item.Id), item.Name, item.IconUrl, generation, token);
                if (_itemFlow.Controls.Count >= 7) break;
            }
            if (_itemFlow.Controls.Count == 0)
            {
                foreach (var itemName in (result.CoreItems ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Take(5))
                    AddTextToken(_itemFlow, itemName, itemName);
            }

            _augmentList.BeginUpdate();
            try
            {
                _augmentList.Items.Clear();
                foreach (var row in (result.AugmentRows ?? new List<MayhemAugmentRow>())
                    .Where(value => value != null && !string.IsNullOrWhiteSpace(value.Name))
                    .OrderBy(value => value.Rank <= 0 ? int.MaxValue : value.Rank)
                    .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(40))
                {
                    var item = new ListViewItem(row.Rank > 0 ? row.Rank.ToString(CultureInfo.InvariantCulture) : "—");
                    item.SubItems.Add(row.Name);
                    item.SubItems.Add(string.IsNullOrWhiteSpace(row.Rarity) ? "—" : row.Rarity);
                    item.SubItems.Add(FormatRate(row.WinRate));
                    item.SubItems.Add(FormatRate(row.PickRate));
                    item.SubItems.Add(row.Games.HasValue ? row.Games.Value.ToString("N0", CultureInfo.InvariantCulture) : "—");
                    if (!string.IsNullOrWhiteSpace(row.Description)) _toolTip.SetToolTip(_augmentList, row.Description);
                    _augmentList.Items.Add(item);
                }
            }
            finally
            {
                _augmentList.EndUpdate();
            }

            if (!string.IsNullOrWhiteSpace(result.ChampionIconUrl))
                _ = LoadPictureAsync(_championIcon, result.ChampionIconUrl, generation, token, new Size(54, 54));
        }

        private void AddToken(
            FlowLayoutPanel host,
            string text,
            string tooltip,
            string reference,
            int generation,
            CancellationToken token)
        {
            var panel = new Panel
            {
                Width = 48,
                Height = 54,
                Margin = new Padding(1),
                BackColor = Color.FromArgb(28, 39, 59)
            };
            var picture = new PictureBox
            {
                Location = new Point(7, 3),
                Size = new Size(34, 34),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(34, 47, 70)
            };
            var label = new Label
            {
                Text = Shorten(text, 7),
                Location = new Point(2, 39),
                Size = new Size(44, 13),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 7F),
                ForeColor = Color.FromArgb(218, 226, 240)
            };
            panel.Controls.Add(picture);
            panel.Controls.Add(label);
            host.Controls.Add(panel);
            _toolTip.SetToolTip(panel, FirstNonEmpty(tooltip, text));
            _toolTip.SetToolTip(picture, FirstNonEmpty(tooltip, text));
            _toolTip.SetToolTip(label, FirstNonEmpty(tooltip, text));
            if (!string.IsNullOrWhiteSpace(reference))
                _ = LoadPictureAsync(picture, reference, generation, token, new Size(34, 34));
        }

        private static void AddTextToken(FlowLayoutPanel host, string text, string tooltip)
        {
            var label = new Label
            {
                Text = text,
                AutoEllipsis = true,
                Width = Math.Min(host.Width - 4, 150),
                Height = 48,
                Margin = new Padding(1),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(28, 39, 59),
                ForeColor = Color.FromArgb(218, 226, 240)
            };
            host.Controls.Add(label);
        }

        private async Task LoadPictureAsync(
            PictureBox picture,
            string reference,
            int generation,
            CancellationToken token,
            Size size)
        {
            try
            {
                var bytes = await RiotGameDataService.DownloadImageAsync(reference, _leagueClient, token);
                if (bytes == null || bytes.Length == 0 || IsDisposed || picture.IsDisposed || generation != _guideGeneration) return;
                var bitmap = DecodeBitmap(bytes, size);
                if (bitmap == null) return;
                var old = picture.Image;
                picture.Image = bitmap;
                if (old != null) old.Dispose();
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Text guide remains usable without images.
            }
        }

        private void SetGuideExpanded(bool expanded)
        {
            if (expanded && !_guideToggle.Enabled) return;
            _guideExpanded = expanded;
            _guidePanel.Visible = expanded;
            _guideToggle.Text = expanded ? "收起" : "攻略";
            var height = expanded ? ExpandedHeight : CompactHeight;
            ClientSize = new Size(660, height);
            MinimumSize = MaximumSize = Size;
            KeepInsideWorkingArea();
        }

        private void KeepInsideWorkingArea()
        {
            var area = Screen.FromRectangle(Bounds).WorkingArea;
            var x = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            var y = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            Location = new Point(x, y);
        }

        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragCursor = Cursor.Position;
            _dragWindow = Location;
        }

        private void ContinueDrag(object sender, MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            var current = Cursor.Position;
            Location = new Point(
                _dragWindow.X + current.X - _dragCursor.X,
                _dragWindow.Y + current.Y - _dragCursor.Y);
        }

        private void EndDrag(object sender, MouseEventArgs e)
        {
            _dragging = false;
            KeepInsideWorkingArea();
        }

        private void HandleClosed(object sender, FormClosedEventArgs e)
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();
            CancelGuideRequest();
            try { _lifetime.Cancel(); }
            catch { }
            _lifetime.Dispose();
            DisposePicture(_championIcon);
            ClearTokenFlow(_skillFlow);
            ClearTokenFlow(_spellFlow);
            ClearTokenFlow(_itemFlow);
            foreach (var bitmap in _benchIconCache.Values) bitmap.Dispose();
            _benchIconCache.Clear();
            _toolTip.Dispose();
        }

        private void CancelGuideRequest()
        {
            var request = _guideRequest;
            _guideRequest = null;
            if (request == null) return;
            try { request.Cancel(); }
            catch { }
            request.Dispose();
        }

        private static void ClearTokenFlow(FlowLayoutPanel host)
        {
            if (host == null) return;
            foreach (Control control in host.Controls)
            {
                var panel = control as Panel;
                if (panel != null)
                {
                    foreach (var picture in panel.Controls.OfType<PictureBox>()) DisposePicture(picture);
                }
            }
            host.Controls.Clear();
        }

        private static void DisposePicture(PictureBox picture)
        {
            if (picture == null) return;
            var image = picture.Image;
            picture.Image = null;
            if (image != null) image.Dispose();
        }

        private static Bitmap DecodeBitmap(byte[] bytes, Size size)
        {
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var source = Image.FromStream(stream, true, true))
                {
                    return new Bitmap(source, size);
                }
            }
            catch
            {
                return null;
            }
        }

        private static Label CreateSectionLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(190, 20),
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(177, 195, 224)
            };
        }

        private static FlowLayoutPanel CreateTokenFlow(int x, int y, int width, int height)
        {
            return new FlowLayoutPanel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
        }

        private static Button CreateFlatButton(string text, Rectangle bounds, Color background)
        {
            var button = new Button
            {
                Text = text,
                Location = bounds.Location,
                Size = bounds.Size,
                FlatStyle = FlatStyle.Flat,
                BackColor = background,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static string BuildChampionMeta(MayhemChampionResult result)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Tier)) parts.Add(result.Tier);
            if (result.Rank.HasValue) parts.Add("排行 #" + result.Rank.Value.ToString(CultureInfo.InvariantCulture));
            if (result.WinRate.HasValue) parts.Add("胜率 " + (result.WinRate.Value * 100d).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (!string.IsNullOrWhiteSpace(result.Patch)) parts.Add("版本 " + result.Patch);
            return parts.Count == 0 ? "海克斯大乱斗自动攻略" : string.Join(" · ", parts);
        }

        private static string FormatRate(double? value)
        {
            return value.HasValue ? (value.Value * 100d).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return string.Empty;
            foreach (var value in values)
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            return string.Empty;
        }

        private static string Shorten(string value, int length)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length <= length) return text;
            return text.Substring(0, Math.Max(1, length - 1)) + "…";
        }

        private static string DescribeSwapFailure(LeagueBenchSwapStatus status)
        {
            switch (status)
            {
                case LeagueBenchSwapStatus.TargetUnavailable: return "目标已被换走，请重新选择";
                case LeagueBenchSwapStatus.VerificationFailed: return "客户端尚未确认换人，请查看游戏内结果";
                case LeagueBenchSwapStatus.BenchDisabled: return "当前替补席不可用";
                case LeagueBenchSwapStatus.SessionUnavailable: return "选人会话暂时不可用";
                default: return "换人请求未成功";
            }
        }

        internal static void ValidateForSmokeTest()
        {
            var result = new MayhemChampionResult
            {
                ChampionName = "萨勒芬妮",
                Tier = "S+",
                Rank = 3,
                WinRate = 0.5432,
                Patch = "26.18"
            };
            var meta = BuildChampionMeta(result);
            if (meta.IndexOf("S+", StringComparison.Ordinal) < 0 ||
                meta.IndexOf("#3", StringComparison.Ordinal) < 0 ||
                meta.IndexOf("54.3%", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("ChampSelect assistant guide summary projection is invalid.");

            if (DescribeSwapFailure(LeagueBenchSwapStatus.TargetUnavailable).IndexOf("换走", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("ChampSelect assistant lost quick-swap failure guidance.");
        }
    }
}
