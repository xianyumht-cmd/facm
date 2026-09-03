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
    /// Lightweight WinForms equivalent of the useful FACM 4 ChampSelect strip + automatic Mayhem guide.
    /// It stays on the legacy 3.5 runtime, reuses the existing Bench write service, and never writes builds.
    /// </summary>
    internal sealed class LeagueChampSelectAssistantForm : Form
    {
        private const int WidthPixels = 660;
        private const int CompactHeight = 116;
        private const int ExpandedHeight = 458;

        private readonly LeagueBenchQuickPickService _bench;
        private readonly ILeagueClientApi _leagueClient;
        private readonly MayhemAutomaticGuideService _guide;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly System.Windows.Forms.Timer _pollTimer;
        private readonly Label _status;
        private readonly FlowLayoutPanel _benchPanel;
        private readonly Button _guideToggle;
        private readonly Panel _guidePanel;
        private readonly PictureBox _championIcon;
        private readonly Label _championTitle;
        private readonly Label _championMeta;
        private readonly Label _skills;
        private readonly Label _spells;
        private readonly Label _items;
        private readonly Label _guideStatus;
        private readonly ListView _augments;
        private readonly ToolTip _toolTip;
        private readonly Dictionary<int, Bitmap> _benchIcons = new Dictionary<int, Bitmap>();

        private CancellationTokenSource _guideRequest;
        private bool _refreshing;
        private bool _benchConfirmed;
        private bool _guideExpanded;
        private int _guideChampionId;
        private int _guideGeneration;
        private bool _dragging;
        private Point _dragCursor;
        private Point _dragWindow;

        private sealed class BenchTarget
        {
            public int ChampionId { get; set; }
            public LeagueBenchSwapRoute Route { get; set; }
        }

        public LeagueChampSelectAssistantForm(
            LeagueBenchQuickPickService bench,
            ILeagueClientApi leagueClient)
        {
            _bench = bench ?? throw new ArgumentNullException(nameof(bench));
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _guide = new MayhemAutomaticGuideService(_leagueClient);
            _ui = UiTextCatalog.Load();

            Text = BuildWindowTitle();
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(14, 20, 32);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);
            DoubleBuffered = true;
            Opacity = 0d; // Do not flash on ordinary ranked ChampSelect before Bench has been confirmed.
            SetClientHeight(CompactHeight);

            var header = new Panel
            {
                Location = Point.Empty,
                Size = new Size(WidthPixels, 36),
                BackColor = Color.FromArgb(25, 34, 52),
                Cursor = Cursors.SizeAll
            };
            var title = new Label
            {
                Text = BuildWindowTitle(),
                Location = new Point(12, 8),
                Size = new Size(276, 22),
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = Color.White
            };
            _status = new Label
            {
                Text = BenchText(LeagueBenchQuickPickUiTextKeys.Waiting),
                Location = new Point(290, 9),
                Size = new Size(228, 20),
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(156, 176, 210)
            };
            _guideToggle = CreateButton(MayhemUiCopy.WindowTitle, new Rectangle(526, 5, 78, 26), Color.FromArgb(55, 73, 105));
            _guideToggle.Enabled = false;
            _guideToggle.Click += delegate { SetGuideExpanded(!_guideExpanded); };
            var close = CreateButton("×", new Rectangle(612, 5, 36, 26), Color.FromArgb(92, 48, 58));
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
                Size = new Size(640, 332),
                BackColor = Color.FromArgb(18, 26, 40),
                Visible = false
            };
            _championIcon = new PictureBox
            {
                Location = new Point(10, 10),
                Size = new Size(52, 52),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(30, 42, 62)
            };
            _championTitle = new Label
            {
                Text = MayhemUiCopy.WindowTitle,
                Location = new Point(72, 10),
                Size = new Size(360, 26),
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                ForeColor = Color.White
            };
            _championMeta = new Label
            {
                Text = MayhemUiCopy.ReadingHero,
                Location = new Point(73, 38),
                Size = new Size(545, 22),
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(150, 168, 198)
            };
            _guideStatus = new Label
            {
                Text = string.Empty,
                Location = new Point(10, 68),
                Size = new Size(610, 22),
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(111, 206, 165)
            };
            _skills = CreateGuideLine(BuildGuideLine(MayhemUiCopy.Skills, MayhemUiCopy.ReadingCache), 96);
            _spells = CreateGuideLine(BuildGuideLine(MayhemUiCopy.Summoner, MayhemUiCopy.ReadingCache), 120);
            _items = CreateGuideLine(BuildGuideLine(MayhemUiCopy.CompactBuild, MayhemUiCopy.ReadingCache), 144);
            var augmentTitle = new Label
            {
                Text = MayhemUiCopy.AugmentBoard,
                Location = new Point(10, 174),
                Size = new Size(260, 22),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(177, 195, 224)
            };
            _augments = new ListView
            {
                Location = new Point(10, 200),
                Size = new Size(610, 118),
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Color.FromArgb(22, 31, 47),
                ForeColor = Color.FromArgb(232, 238, 248),
                BorderStyle = BorderStyle.FixedSingle,
                ShowItemToolTips = true
            };
            _augments.Columns.Add("#", 38, HorizontalAlignment.Right);
            _augments.Columns.Add(MayhemUiCopy.PriorityAugment, 220, HorizontalAlignment.Left);
            _augments.Columns.Add(MayhemUiCopy.MetricQuality, 68, HorizontalAlignment.Left);
            _augments.Columns.Add(MayhemUiCopy.HeroWinRate, 78, HorizontalAlignment.Right);
            _augments.Columns.Add(MayhemUiCopy.PickRate, 82, HorizontalAlignment.Right);
            _augments.Columns.Add(MayhemUiCopy.Sample, 86, HorizontalAlignment.Right);

            _guidePanel.Controls.Add(_championIcon);
            _guidePanel.Controls.Add(_championTitle);
            _guidePanel.Controls.Add(_championMeta);
            _guidePanel.Controls.Add(_guideStatus);
            _guidePanel.Controls.Add(_skills);
            _guidePanel.Controls.Add(_spells);
            _guidePanel.Controls.Add(_items);
            _guidePanel.Controls.Add(augmentTitle);
            _guidePanel.Controls.Add(_augments);

            Controls.Add(header);
            Controls.Add(_benchPanel);
            Controls.Add(_guidePanel);

            _toolTip = new ToolTip { ShowAlways = true, AutomaticDelay = 120 };
            _pollTimer = new System.Windows.Forms.Timer { Interval = 650 };
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
                    _status.Text = BenchText(LeagueBenchQuickPickUiTextKeys.Waiting);
                    return;
                }

                // FACM 4 automatic guide is a Mayhem/ARAM guide. Do not surface it in normal ranked.
                if (!state.BenchEnabled)
                {
                    Close();
                    return;
                }

                if (!_benchConfirmed)
                {
                    _benchConfirmed = true;
                    Opacity = 1d;
                }

                _status.Text = state.ChampionIds.Count > 0
                    ? BenchText(LeagueBenchQuickPickUiTextKeys.Title) + ": " + state.ChampionIds.Count.ToString(CultureInfo.InvariantCulture)
                    : BenchText(LeagueBenchQuickPickUiTextKeys.Waiting);
                RenderBench(state);

                if (state.LocalChampionId > 0 && state.LocalChampionId != _guideChampionId)
                    StartAutomaticGuide(state.LocalChampionId);
            }
            catch (Exception exception)
            {
                AppLog.Info("ChampSelect assistant refresh skipped: " + exception.Message);
                if (!IsDisposed)
                    _status.Text = _benchConfirmed ? MayhemUiCopy.Failed : BenchText(LeagueBenchQuickPickUiTextKeys.Waiting);
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
                .Where(button => button.Tag is BenchTarget)
                .ToDictionary(button => ((BenchTarget)button.Tag).ChampionId, button => button);

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
                        Width = 54,
                        Height = 48,
                        Margin = new Padding(2),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Color.FromArgb(33, 47, 70),
                        ForeColor = Color.White,
                        Text = championId.ToString(CultureInfo.InvariantCulture),
                        ImageAlign = ContentAlignment.MiddleCenter,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Cursor = Cursors.Hand
                    };
                    button.FlatAppearance.BorderColor = Color.FromArgb(65, 90, 128);
                    button.Click += async delegate
                    {
                        var target = button.Tag as BenchTarget;
                        if (target != null) await SwapToAsync(target.ChampionId, target.Route);
                    };
                    _benchPanel.Controls.Add(button);
                    existing[championId] = button;
                    _ = LoadBenchIconAsync(button, championId);
                }

                button.Tag = new BenchTarget { ChampionId = championId, Route = state.SwapRoute };
                _toolTip.SetToolTip(
                    button,
                    BenchText(LeagueBenchQuickPickUiTextKeys.Tooltip) + " #" + championId.ToString(CultureInfo.InvariantCulture));
                button.FlatAppearance.BorderColor = championId == state.LocalChampionId
                    ? Color.FromArgb(92, 208, 155)
                    : Color.FromArgb(65, 90, 128);
            }
        }

        private async Task LoadBenchIconAsync(Button button, int championId)
        {
            Bitmap cached;
            if (_benchIcons.TryGetValue(championId, out cached))
            {
                if (!button.IsDisposed)
                {
                    button.Image = cached;
                    button.Text = string.Empty;
                }
                return;
            }

            try
            {
                var bytes = await _bench.LoadChampionIconAsync(championId, _lifetime.Token);
                if (bytes == null || bytes.Length == 0 || IsDisposed || button.IsDisposed) return;
                var bitmap = DecodeBitmap(bytes, new Size(34, 34));
                if (bitmap == null) return;
                _benchIcons[championId] = bitmap;
                button.Image = bitmap;
                button.Text = string.Empty;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Champion ID remains as a safe text fallback.
            }
        }

        private async Task SwapToAsync(int championId, LeagueBenchSwapRoute route)
        {
            if (championId <= 0 || IsDisposed) return;
            _status.Text = BenchText(LeagueBenchQuickPickUiTextKeys.Swapping);
            try
            {
                var result = await _bench.TrySwapAsync(championId, route, _lifetime.Token);
                if (IsDisposed) return;
                _status.Text = result.Success
                    ? BenchText(LeagueBenchQuickPickUiTextKeys.Success)
                    : DescribeSwapFailure(result.Status);
                _ = RefreshBenchAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Info("ChampSelect quick swap failed: " + exception.Message);
                if (!IsDisposed) _status.Text = BenchText(LeagueBenchQuickPickUiTextKeys.Rejected);
            }
        }

        private void StartAutomaticGuide(int championId)
        {
            CancelGuideRequest();
            _guideChampionId = championId;
            var generation = ++_guideGeneration;
            _guideRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _guideRequest.CancelAfter(TimeSpan.FromSeconds(15));
            _guideToggle.Enabled = true;
            SetGuideExpanded(true);
            ResetGuide(championId);
            _ = LoadAutomaticGuideAsync(generation, championId, _guideRequest);
        }

        private async Task LoadAutomaticGuideAsync(int generation, int championId, CancellationTokenSource request)
        {
            try
            {
                var result = await _guide.QueryForChampionIdAsync(championId, request.Token);
                if (!IsCurrentGuide(generation, championId, request)) return;
                if (result == null || !string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    _guideStatus.ForeColor = Color.FromArgb(245, 166, 126);
                    _guideStatus.Text = result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                        ? MayhemUiCopy.NoData
                        : result.ErrorMessage;
                    return;
                }
                RenderGuide(result, generation, request.Token);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && !_lifetime.IsCancellationRequested && generation == _guideGeneration)
                {
                    _guideStatus.ForeColor = Color.FromArgb(245, 166, 126);
                    _guideStatus.Text = MayhemUiCopy.TimeoutShort;
                }
            }
            catch (Exception exception)
            {
                AppLog.Info("Automatic Mayhem guide failed: " + exception.Message);
                if (!IsDisposed && generation == _guideGeneration)
                {
                    _guideStatus.ForeColor = Color.FromArgb(245, 166, 126);
                    _guideStatus.Text = MayhemUiCopy.Failed;
                }
            }
            finally
            {
                if (ReferenceEquals(_guideRequest, request))
                {
                    _guideRequest = null;
                    request.Dispose();
                }
            }
        }

        private bool IsCurrentGuide(int generation, int championId, CancellationTokenSource request)
        {
            return !IsDisposed && !request.IsCancellationRequested && generation == _guideGeneration && championId == _guideChampionId;
        }

        private void ResetGuide(int championId)
        {
            ReplacePicture(_championIcon, null);
            _championTitle.Text = MayhemUiCopy.ReadingHero;
            _championMeta.Text = _ui.Get(UiTextKeys.LeagueLiveChampion) + " " +
                                 championId.ToString(CultureInfo.InvariantCulture) + " · " +
                                 _ui.Get(UiTextKeys.LeagueLiveReadOnly);
            _guideStatus.ForeColor = Color.FromArgb(111, 206, 165);
            _guideStatus.Text = MayhemUiCopy.ReadingLatest;
            _skills.Text = BuildGuideLine(MayhemUiCopy.Skills, MayhemUiCopy.ReadingCache);
            _spells.Text = BuildGuideLine(MayhemUiCopy.Summoner, MayhemUiCopy.ReadingCache);
            _items.Text = BuildGuideLine(MayhemUiCopy.CompactBuild, MayhemUiCopy.ReadingCache);
            _augments.Items.Clear();
        }

        private void RenderGuide(MayhemChampionResult result, int generation, CancellationToken token)
        {
            _championTitle.Text = FirstNonEmpty(result.ChampionName, result.Query, MayhemUiCopy.Unknown);
            _championMeta.Text = BuildChampionMeta(result);
            _guideStatus.ForeColor = Color.FromArgb(111, 206, 165);
            _guideStatus.Text = MayhemUiCopy.Completed + " · " + _ui.Get(UiTextKeys.LeagueLiveReadOnly);
            _skills.Text = BuildGuideLine(MayhemUiCopy.Skills, BuildSkillText(result));
            _spells.Text = BuildGuideLine(MayhemUiCopy.Summoner, BuildSpellText(result));
            _items.Text = BuildGuideLine(MayhemUiCopy.CompactBuild, BuildItemText(result));

            _augments.BeginUpdate();
            try
            {
                _augments.Items.Clear();
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
                    item.ToolTipText = row.Description ?? string.Empty;
                    _augments.Items.Add(item);
                }
            }
            finally
            {
                _augments.EndUpdate();
            }

            if (!string.IsNullOrWhiteSpace(result.ChampionIconUrl))
                _ = LoadChampionPictureAsync(result.ChampionIconUrl, generation, token);
        }

        private async Task LoadChampionPictureAsync(string reference, int generation, CancellationToken token)
        {
            try
            {
                var bytes = await RiotGameDataService.DownloadImageAsync(reference, _leagueClient, token);
                if (bytes == null || bytes.Length == 0 || IsDisposed || generation != _guideGeneration) return;
                var bitmap = DecodeBitmap(bytes, new Size(52, 52));
                if (bitmap != null) ReplacePicture(_championIcon, bitmap);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Text guide remains complete without an icon.
            }
        }

        private void SetGuideExpanded(bool expanded)
        {
            if (expanded && !_guideToggle.Enabled) return;
            _guideExpanded = expanded;
            _guidePanel.Visible = expanded;
            _guideToggle.Text = expanded ? _ui.Get(UiTextKeys.Close) : MayhemUiCopy.WindowTitle;
            SetClientHeight(expanded ? ExpandedHeight : CompactHeight);
            KeepInsideWorkingArea();
        }

        private void SetClientHeight(int height)
        {
            MaximumSize = Size.Empty;
            MinimumSize = Size.Empty;
            ClientSize = new Size(WidthPixels, height);
            MinimumSize = Size;
            MaximumSize = Size;
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
            Location = new Point(_dragWindow.X + current.X - _dragCursor.X, _dragWindow.Y + current.Y - _dragCursor.Y);
        }

        private void EndDrag(object sender, MouseEventArgs e)
        {
            _dragging = false;
            KeepInsideWorkingArea();
        }

        private void KeepInsideWorkingArea()
        {
            var area = Screen.FromRectangle(Bounds).WorkingArea;
            Location = new Point(
                Math.Max(area.Left, Math.Min(Left, area.Right - Width)),
                Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }

        private void HandleClosed(object sender, FormClosedEventArgs e)
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();
            CancelGuideRequest();
            try { _lifetime.Cancel(); }
            catch { }
            _lifetime.Dispose();
            ReplacePicture(_championIcon, null);
            foreach (var bitmap in _benchIcons.Values) bitmap.Dispose();
            _benchIcons.Clear();
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

        private string BuildWindowTitle()
        {
            return UiTextRuntime.Text(UiTextKeys.AppName) + " · " + MayhemUiCopy.CardSubtitle;
        }

        private string BenchText(string key)
        {
            return LeagueBenchQuickPickText.Get(_ui, key);
        }

        private static string BuildGuideLine(string label, string value)
        {
            return (label ?? string.Empty) + ": " + (value ?? string.Empty);
        }

        private static Label CreateGuideLine(string text, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(10, y),
                Size = new Size(610, 22),
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(218, 226, 240)
            };
        }

        private static Button CreateButton(string text, Rectangle bounds, Color background)
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

        private static Bitmap DecodeBitmap(byte[] bytes, Size size)
        {
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var source = Image.FromStream(stream, true, true))
                    return new Bitmap(source, size);
            }
            catch
            {
                return null;
            }
        }

        private static void ReplacePicture(PictureBox picture, Image image)
        {
            var old = picture.Image;
            picture.Image = image;
            if (old != null && !ReferenceEquals(old, image)) old.Dispose();
        }

        private static string BuildSkillText(MayhemChampionResult result)
        {
            var values = (result.SkillPriority ?? new List<MayhemSkillPriority>())
                .Where(value => value != null)
                .Select(value => FirstNonEmpty(value.Key, value.Name))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(4)
                .ToArray();
            if (values.Length > 0) return string.Join(" → ", values);
            return string.IsNullOrWhiteSpace(result.SkillOrder) ? MayhemUiCopy.NoValue : result.SkillOrder;
        }

        private static string BuildSpellText(MayhemChampionResult result)
        {
            var values = (result.SummonerSpells ?? new List<MayhemBuildItem>())
                .Where(value => value != null)
                .Select(value => FirstNonEmpty(value.Name, value.Id))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(2)
                .ToArray();
            return values.Length == 0 ? MayhemUiCopy.NoValue : string.Join(" + ", values);
        }

        private static string BuildItemText(MayhemChampionResult result)
        {
            var values = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var buildItems = (result.StarterItems ?? new List<MayhemBuildItem>())
                .Concat(result.BootItems ?? new List<MayhemBuildItem>())
                .Concat(result.CoreBuilds != null && result.CoreBuilds.Count > 0 && result.CoreBuilds[0] != null
                    ? result.CoreBuilds[0].Items ?? new List<MayhemBuildItem>()
                    : new List<MayhemBuildItem>());
            foreach (var item in buildItems)
            {
                if (item == null) continue;
                var value = FirstNonEmpty(item.Name, item.Id);
                if (string.IsNullOrWhiteSpace(value) || !seen.Add(value)) continue;
                values.Add(value);
                if (values.Count >= 7) break;
            }
            if (values.Count == 0)
            {
                foreach (var value in (result.CoreItems ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    if (!seen.Add(value)) continue;
                    values.Add(value);
                    if (values.Count >= 7) break;
                }
            }
            return values.Count == 0 ? MayhemUiCopy.NoValue : string.Join(" → ", values);
        }

        private static string BuildChampionMeta(MayhemChampionResult result)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Tier)) parts.Add(result.Tier);
            if (result.Rank.HasValue) parts.Add(MayhemUiCopy.RankPrefix + result.Rank.Value.ToString(CultureInfo.InvariantCulture));
            if (result.WinRate.HasValue)
                parts.Add(MayhemUiCopy.Win + (result.WinRate.Value * 100d).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (!string.IsNullOrWhiteSpace(result.Patch)) parts.Add(MayhemUiCopy.PatchPrefix + result.Patch);
            return parts.Count == 0 ? MayhemUiCopy.CardSubtitle : string.Join(" · ", parts);
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

        private string DescribeSwapFailure(LeagueBenchSwapStatus status)
        {
            switch (status)
            {
                case LeagueBenchSwapStatus.TargetUnavailable:
                    return BenchText(LeagueBenchQuickPickUiTextKeys.Unavailable);
                case LeagueBenchSwapStatus.VerificationFailed:
                    return BenchText(LeagueBenchQuickPickUiTextKeys.VerifyFailed);
                case LeagueBenchSwapStatus.BenchDisabled:
                    return BenchText(LeagueBenchQuickPickUiTextKeys.Disabled);
                case LeagueBenchSwapStatus.SessionUnavailable:
                    return BenchText(LeagueBenchQuickPickUiTextKeys.Waiting);
                default:
                    return BenchText(LeagueBenchQuickPickUiTextKeys.Rejected);
            }
        }

        internal static void ValidateForSmokeTest()
        {
            var result = new MayhemChampionResult
            {
                ChampionName = "Seraphine",
                Tier = "S+",
                Rank = 3,
                WinRate = 0.5432,
                Patch = "26.18",
                SkillPriority = new List<MayhemSkillPriority>
                {
                    new MayhemSkillPriority { Key = "Q", Name = "Q" },
                    new MayhemSkillPriority { Key = "E", Name = "E" },
                    new MayhemSkillPriority { Key = "W", Name = "W" }
                },
                SummonerSpells = new List<MayhemBuildItem>
                {
                    new MayhemBuildItem { Name = "Flash" },
                    new MayhemBuildItem { Name = "Mark" }
                }
            };
            var meta = BuildChampionMeta(result);
            if (meta.IndexOf("S+", StringComparison.Ordinal) < 0 ||
                meta.IndexOf("#3", StringComparison.Ordinal) < 0 ||
                meta.IndexOf("54.3%", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("ChampSelect assistant guide summary projection is invalid.");
            if (!string.Equals(BuildSkillText(result), "Q → E → W", StringComparison.Ordinal))
                throw new InvalidOperationException("ChampSelect assistant skill projection is invalid.");
            if (!string.Equals(BuildSpellText(result), "Flash + Mark", StringComparison.Ordinal))
                throw new InvalidOperationException("ChampSelect assistant spell projection is invalid.");
        }
    }
}
