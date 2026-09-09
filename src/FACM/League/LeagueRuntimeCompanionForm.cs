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
using FACM.Theming;

namespace FACM.League
{
    /// <summary>
    /// Narrow Champion Select Runtime Companion.
    ///
    /// Phase 1 intentionally preserves the existing behavior boundary: this surface is shown only
    /// for Bench-enabled ARAM/Mayhem-style Champion Select, uses the existing Bench owner for swaps,
    /// uses the existing Mayhem automatic-guide service, and adds no League transport or write path.
    /// </summary>
    internal sealed class LeagueRuntimeCompanionForm : Form
    {
        internal const int DesignWidth = 388;
        internal const int HeaderHeight = 42;
        internal const int ContextHeight = 78;
        internal const int BenchHeight = 70;
        internal const int MinimumExpandedHeight = 480;
        internal const int MaximumExpandedHeight = 720;
        private const int BodyContentHeight = 520;

        private readonly LeagueBenchQuickPickService _bench;
        private readonly ILeagueClientApi _leagueClient;
        private readonly MayhemAutomaticGuideService _guide;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly System.Windows.Forms.Timer _pollTimer;
        private readonly ToolTip _toolTip;
        private readonly Dictionary<int, Bitmap> _benchIcons = new Dictionary<int, Bitmap>();

        private readonly Panel _header;
        private readonly Label _title;
        private readonly Label _status;
        private readonly Button _pinButton;
        private readonly Button _collapseButton;
        private readonly Panel _context;
        private readonly FlowLayoutPanel _benchPanel;
        private readonly Panel _body;
        private readonly PictureBox _championIcon;
        private readonly Label _championTitle;
        private readonly Label _championMeta;
        private readonly Label _guideStatus;
        private readonly Label _skills;
        private readonly Label _spells;
        private readonly Label _items;
        private readonly ListView _augments;

        private CancellationTokenSource _guideRequest;
        private bool _refreshing;
        private bool _benchConfirmed;
        private bool _collapsed;
        private bool _pinned = true;
        private int _guideChampionId;
        private int _guideGeneration;
        private int _expandedHeight;
        private bool _dragging;
        private Point _dragCursor;
        private Point _dragWindow;

        private sealed class BenchTarget
        {
            public int ChampionId { get; set; }
            public LeagueBenchSwapRoute Route { get; set; }
        }

        public LeagueRuntimeCompanionForm(
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
            ControlBox = false;
            TopMost = true;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Opacity = 0d; // Do not flash before the existing Bench owner confirms this mode.

            _expandedHeight = ResolveExpandedHeight(Screen.FromPoint(Cursor.Position).WorkingArea.Height);
            SetFixedClientSize(DesignWidth, _expandedHeight);

            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = HeaderHeight,
                BackColor = FacmDesignSystem.Canvas,
                Cursor = Cursors.SizeAll
            };
            _title = new Label
            {
                Text = UiTextRuntime.Text(UiTextKeys.AppName),
                Location = new Point(10, 0),
                Size = new Size(128, HeaderHeight),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.Text,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
            };
            _status = new Label
            {
                Text = BenchText(LeagueBenchQuickPickUiTextKeys.Waiting),
                Location = new Point(140, 0),
                Size = new Size(142, HeaderHeight),
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.TextMuted
            };
            _pinButton = CreateChromeButton("↑", 284, 30);
            _pinButton.Click += delegate { TogglePin(); };
            _collapseButton = CreateChromeButton("−", 316, 30);
            _collapseButton.Click += delegate { SetCollapsed(!_collapsed); };
            var close = CreateChromeButton("×", 348, 32);
            close.Font = new Font("Segoe UI", 13F, FontStyle.Regular);
            close.Click += delegate { Close(); };

            _header.Controls.Add(_title);
            _header.Controls.Add(_status);
            _header.Controls.Add(_pinButton);
            _header.Controls.Add(_collapseButton);
            _header.Controls.Add(close);
            WireDrag(_header);
            WireDrag(_title);

            _context = new Panel
            {
                Dock = DockStyle.Top,
                Height = ContextHeight,
                BackColor = FacmDesignSystem.CanvasRaised,
                Padding = new Padding(10, 8, 10, 8)
            };
            _championIcon = new PictureBox
            {
                Location = new Point(10, 10),
                Size = new Size(52, 52),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = FacmDesignSystem.SurfaceRaised
            };
            FacmDesignSystem.Round(_championIcon, FacmDesignSystem.ControlRadius);
            _championTitle = new Label
            {
                Text = MayhemUiCopy.ReadingHero,
                Location = new Point(74, 8),
                Size = new Size(294, 28),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 12F, FontStyle.Bold)
            };
            _championMeta = new Label
            {
                Text = MayhemUiCopy.CardSubtitle,
                Location = new Point(74, 35),
                Size = new Size(294, 19),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent
            };
            _guideStatus = new Label
            {
                Text = MayhemUiCopy.ReadingLatest,
                Location = new Point(74, 54),
                Size = new Size(294, 18),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Accent,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F)
            };
            _context.Controls.Add(_championIcon);
            _context.Controls.Add(_championTitle);
            _context.Controls.Add(_championMeta);
            _context.Controls.Add(_guideStatus);

            var benchHost = new Panel
            {
                Dock = DockStyle.Top,
                Height = BenchHeight,
                BackColor = FacmDesignSystem.Canvas,
                Padding = new Padding(10, 6, 10, 6)
            };
            var benchLabel = new Label
            {
                Text = BenchText(LeagueBenchQuickPickUiTextKeys.Title),
                Dock = DockStyle.Top,
                Height = 18,
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F, FontStyle.Bold)
            };
            _benchPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                BackColor = FacmDesignSystem.Canvas,
                Padding = new Padding(0, 2, 0, 0),
                Margin = Padding.Empty
            };
            benchHost.Controls.Add(_benchPanel);
            benchHost.Controls.Add(benchLabel);

            _body = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                AutoScrollMinSize = new Size(0, BodyContentHeight),
                BackColor = FacmDesignSystem.Canvas,
                Padding = new Padding(10, 0, 10, 10)
            };

            _skills = CreateGuideLine(MayhemUiCopy.Skills, 12, 48);
            _spells = CreateGuideLine(MayhemUiCopy.Summoner, 66, 48);
            _items = CreateGuideLine(MayhemUiCopy.CompactBuild, 120, 74);
            var separator = new Panel
            {
                Location = new Point(10, 201),
                Size = new Size(348, 1),
                BackColor = FacmDesignSystem.BorderSoft
            };
            var augmentTitle = new Label
            {
                Text = MayhemUiCopy.AugmentBoard,
                Location = new Point(10, 212),
                Size = new Size(348, 24),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
            };
            _augments = new ListView
            {
                Location = new Point(10, 240),
                Size = new Size(348, 258),
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text,
                BorderStyle = BorderStyle.None,
                ShowItemToolTips = true
            };
            _augments.Columns.Add("#", 34, HorizontalAlignment.Right);
            _augments.Columns.Add(MayhemUiCopy.PriorityAugment, 128, HorizontalAlignment.Left);
            _augments.Columns.Add(MayhemUiCopy.MetricQuality, 50, HorizontalAlignment.Left);
            _augments.Columns.Add(MayhemUiCopy.HeroWinRate, 58, HorizontalAlignment.Right);
            _augments.Columns.Add(MayhemUiCopy.PickRate, 58, HorizontalAlignment.Right);
            _augments.Columns.Add(MayhemUiCopy.Sample, 72, HorizontalAlignment.Right);

            _body.Controls.Add(_skills);
            _body.Controls.Add(_spells);
            _body.Controls.Add(_items);
            _body.Controls.Add(separator);
            _body.Controls.Add(augmentTitle);
            _body.Controls.Add(_augments);

            Controls.Add(_body);
            Controls.Add(benchHost);
            Controls.Add(_context);
            Controls.Add(_header);

            _toolTip = new ToolTip { ShowAlways = true, AutomaticDelay = 120 };
            _pollTimer = new System.Windows.Forms.Timer { Interval = 650 };
            _pollTimer.Tick += async delegate { await RefreshBenchAsync(); };
            Shown += delegate
            {
                FacmDesignSystem.Round(this, FacmDesignSystem.WindowRadius);
                _pollTimer.Start();
                _ = RefreshBenchAsync();
            };
            FormClosed += HandleClosed;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        internal static int ResolveExpandedHeight(int workingAreaHeight)
        {
            var available = Math.Max(MinimumExpandedHeight, workingAreaHeight - 40);
            return Math.Max(MinimumExpandedHeight, Math.Min(MaximumExpandedHeight, available));
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
                    SetStatus(BenchText(LeagueBenchQuickPickUiTextKeys.Waiting), FacmDesignSystem.TextMuted);
                    return;
                }

                // Phase 1 intentionally retains the old behavior boundary: no popup in modes
                // where the existing Bench owner says the Bench surface is not applicable.
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

                SetStatus(
                    state.ChampionIds.Count > 0
                        ? BenchText(LeagueBenchQuickPickUiTextKeys.Title) + ": " + state.ChampionIds.Count.ToString(CultureInfo.InvariantCulture)
                        : BenchText(LeagueBenchQuickPickUiTextKeys.Waiting),
                    FacmDesignSystem.TextMuted);
                RenderBench(state);

                if (state.LocalChampionId > 0 && state.LocalChampionId != _guideChampionId)
                    StartAutomaticGuide(state.LocalChampionId);
            }
            catch (Exception exception)
            {
                AppLog.Info("Runtime Companion refresh skipped: " + exception.Message);
                if (!IsDisposed)
                    SetStatus(
                        _benchConfirmed ? MayhemUiCopy.Failed : BenchText(LeagueBenchQuickPickUiTextKeys.Waiting),
                        _benchConfirmed ? FacmDesignSystem.Warning : FacmDesignSystem.TextMuted);
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
                    button = CreateBenchButton(championId);
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
                    ? FacmDesignSystem.Accent
                    : FacmDesignSystem.BorderSoft;
            }
        }

        private Button CreateBenchButton(int championId)
        {
            var button = new Button
            {
                Width = 48,
                Height = 42,
                Margin = new Padding(0, 1, 5, 1),
                FlatStyle = FlatStyle.Flat,
                BackColor = FacmDesignSystem.Surface,
                ForeColor = FacmDesignSystem.Text,
                Text = championId.ToString(CultureInfo.InvariantCulture),
                ImageAlign = ContentAlignment.MiddleCenter,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                TabStop = true,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F)
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = FacmDesignSystem.BorderSoft;
            button.FlatAppearance.MouseOverBackColor = FacmDesignSystem.SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = FacmDesignSystem.SurfaceRaised;
            button.Resize += delegate { FacmDesignSystem.Round(button, Math.Min(5, FacmDesignSystem.ControlRadius)); };
            FacmDesignSystem.Round(button, Math.Min(5, FacmDesignSystem.ControlRadius));
            return button;
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
                var bitmap = DecodeBitmap(bytes, new Size(32, 32));
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
                // Champion ID remains as a readable fallback if the image path fails.
            }
        }

        private async Task SwapToAsync(int championId, LeagueBenchSwapRoute route)
        {
            if (championId <= 0 || IsDisposed) return;
            SetStatus(BenchText(LeagueBenchQuickPickUiTextKeys.Swapping), FacmDesignSystem.Accent);
            try
            {
                var result = await _bench.TrySwapAsync(championId, route, _lifetime.Token);
                if (IsDisposed) return;
                SetStatus(
                    result.Success ? BenchText(LeagueBenchQuickPickUiTextKeys.Success) : DescribeSwapFailure(result.Status),
                    result.Success ? FacmDesignSystem.Success : FacmDesignSystem.Warning);
                _ = RefreshBenchAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Info("Runtime Companion quick swap failed: " + exception.Message);
                if (!IsDisposed)
                    SetStatus(BenchText(LeagueBenchQuickPickUiTextKeys.Rejected), FacmDesignSystem.Warning);
            }
        }

        private void StartAutomaticGuide(int championId)
        {
            CancelGuideRequest();
            _guideChampionId = championId;
            var generation = ++_guideGeneration;
            _guideRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _guideRequest.CancelAfter(TimeSpan.FromSeconds(15));
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
                    _guideStatus.ForeColor = FacmDesignSystem.Warning;
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
                    _guideStatus.ForeColor = FacmDesignSystem.Warning;
                    _guideStatus.Text = MayhemUiCopy.TimeoutShort;
                }
            }
            catch (Exception exception)
            {
                AppLog.Info("Runtime Companion automatic guide failed: " + exception.Message);
                if (!IsDisposed && generation == _guideGeneration)
                {
                    _guideStatus.ForeColor = FacmDesignSystem.Warning;
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
            _guideStatus.ForeColor = FacmDesignSystem.Accent;
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
            _guideStatus.ForeColor = FacmDesignSystem.Success;
            _guideStatus.Text = MayhemUiCopy.Completed + " · " + _ui.Get(UiTextKeys.LeagueLiveReadOnly);
            _skills.Text = BuildGuideLine(MayhemUiCopy.Skills, BuildSkillText(result));
            _spells.Text = BuildGuideLine(MayhemUiCopy.Summoner, BuildSpellText(result));
            _items.Text = BuildGuideLine(MayhemUiCopy.CompactBuild, BuildItemText(result));
            _toolTip.SetToolTip(_championMeta, _championMeta.Text);
            _toolTip.SetToolTip(_skills, _skills.Text);
            _toolTip.SetToolTip(_spells, _spells.Text);
            _toolTip.SetToolTip(_items, _items.Text);

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
                // Text remains complete when the optional icon is unavailable.
            }
        }

        private void TogglePin()
        {
            _pinned = !_pinned;
            TopMost = _pinned;
            _pinButton.ForeColor = _pinned ? FacmDesignSystem.Accent : FacmDesignSystem.TextMuted;
            _pinButton.Invalidate();
        }

        private void SetCollapsed(bool collapsed)
        {
            if (_collapsed == collapsed) return;
            _collapsed = collapsed;
            _context.Visible = !collapsed;
            foreach (Control control in Controls)
            {
                if (ReferenceEquals(control, _header) || ReferenceEquals(control, _context)) continue;
                control.Visible = !collapsed;
            }
            _collapseButton.Text = collapsed ? "+" : "−";
            SetFixedClientSize(DesignWidth, collapsed ? HeaderHeight : _expandedHeight);
            KeepInsideWorkingArea();
            FacmDesignSystem.Round(this, FacmDesignSystem.WindowRadius);
        }

        private void SetFixedClientSize(int width, int height)
        {
            MaximumSize = Size.Empty;
            MinimumSize = Size.Empty;
            ClientSize = new Size(width, height);
            MinimumSize = Size;
            MaximumSize = Size;
        }

        private void SetStatus(string text, Color color)
        {
            _status.Text = text ?? string.Empty;
            _status.ForeColor = color;
        }

        private Button CreateChromeButton(string text, int x, int width)
        {
            var button = new Button
            {
                Text = text,
                Location = new Point(x, 7),
                Size = new Size(width, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = FacmDesignSystem.Canvas,
                ForeColor = FacmDesignSystem.TextMuted,
                Cursor = Cursors.Hand,
                TabStop = false,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular)
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = FacmDesignSystem.SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = FacmDesignSystem.SurfaceRaised;
            return button;
        }

        private Label CreateGuideLine(string label, int y, int height)
        {
            return new Label
            {
                Text = BuildGuideLine(label, MayhemUiCopy.ReadingCache),
                Location = new Point(10, y),
                Size = new Size(348, height),
                AutoEllipsis = false,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8.8F)
            };
        }

        private void WireDrag(Control control)
        {
            control.MouseDown += BeginDrag;
            control.MouseMove += ContinueDrag;
            control.MouseUp += EndDrag;
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
                if (values.Count >= 12) break;
            }
            if (values.Count == 0)
            {
                foreach (var value in (result.CoreItems ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    if (!seen.Add(value)) continue;
                    values.Add(value);
                    if (values.Count >= 12) break;
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
                parts.Add(MayhemUiCopy.Win + result.WinRate.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (!string.IsNullOrWhiteSpace(result.Patch)) parts.Add(MayhemUiCopy.PatchPrefix + result.Patch);
            return parts.Count == 0 ? MayhemUiCopy.CardSubtitle : string.Join(" · ", parts);
        }

        private static string FormatRate(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—";
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
            if (DesignWidth < 360 || DesignWidth > 430)
                throw new InvalidOperationException("Runtime Companion width left the compact design contract.");
            if (ResolveExpandedHeight(768) < MinimumExpandedHeight || ResolveExpandedHeight(768) > MaximumExpandedHeight)
                throw new InvalidOperationException("Runtime Companion 768p height policy is invalid.");
            if (ResolveExpandedHeight(2160) != MaximumExpandedHeight)
                throw new InvalidOperationException("Runtime Companion maximum height cap drifted.");
            if (HeaderHeight + ContextHeight + BenchHeight >= MinimumExpandedHeight)
                throw new InvalidOperationException("Runtime Companion fixed regions leave no useful scroll body.");

            var result = new MayhemChampionResult
            {
                ChampionName = "Seraphine",
                Tier = "S+",
                Rank = 3,
                WinRate = 54.32,
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
                throw new InvalidOperationException("Runtime Companion guide summary projection is invalid.");
            if (!string.Equals(FormatRate(53.5), "53.5%", StringComparison.Ordinal))
                throw new InvalidOperationException("Runtime Companion percent-unit contract is invalid.");
            if (!string.Equals(BuildSkillText(result), "Q → E → W", StringComparison.Ordinal))
                throw new InvalidOperationException("Runtime Companion skill projection is invalid.");
            if (!string.Equals(BuildSpellText(result), "Flash + Mark", StringComparison.Ordinal))
                throw new InvalidOperationException("Runtime Companion spell projection is invalid.");
        }
    }
}
