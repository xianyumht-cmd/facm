using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Mayhem;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    /// <summary>
    /// Narrow Champion Select Runtime Companion presentation surface.
    ///
    /// The Form renders controller snapshots. It owns no Gameflow reader, OP.GG transport, Mayhem
    /// request orchestration or raw LCU/filesystem write path. Existing FACM owners remain authoritative.
    /// </summary>
    internal sealed class LeagueRuntimeCompanionForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        internal const int DesignWidth = 320;
        internal const int HeaderHeight = 36;
        internal const int ContextHeight = 82;
        internal const int BenchHeight = 58;
        internal const int MinimumExpandedHeight = 420;
        internal const int MaximumExpandedHeight = 560;
        internal const int BodyContentWidth = 284;
        internal const int AugmentColumnTotalWidth = 268;
        private const int SectionWidth = 284;
        private const int RecommendationBaseHeight = 64;
        private const int AlternativeRowHeight = 36;
        private const int AugmentPageSize = 5;
        private const int AugmentRowHeight = 36;
        private const int GuideTokenSize = 28;
        private const int GuideTokenGap = 4;
        private const int BenchPageSize = 4;
        private const int SbHorz = 0;
        private const int SbVert = 1;

        private readonly LeagueRuntimeCompanionController _controller;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly System.Windows.Forms.Timer _pollTimer;
        private readonly ToolTip _toolTip;
        private readonly Dictionary<int, Bitmap> _championIcons = new Dictionary<int, Bitmap>();

        private readonly Panel _header;
        private readonly Label _title;
        private readonly Label _status;
        private readonly Button _quitButton;
        private readonly Button _pinButton;
        private readonly Button _collapseButton;
        private readonly Panel _context;
        private readonly Panel _benchHost;
        private readonly FlowLayoutPanel _benchPanel;
        private readonly Panel _body;
        private readonly FlowLayoutPanel _sections;
        private readonly PictureBox _championIcon;
        private readonly Label _championTitle;
        private readonly Label _championMeta;
        private readonly Label _championStats;
        private readonly Label _contextStatus;
        private readonly RecommendationSection _runes;
        private readonly RecommendationSection _spells;
        private readonly RecommendationSection _skills;
        private readonly RecommendationSection _starter;
        private readonly RecommendationSection _boots;
        private readonly RecommendationSection _core;
        private readonly Panel _aramBalanceSection;
        private readonly Label _aramBalanceText;
        private readonly Panel _mayhemSection;
        private readonly FlowLayoutPanel _augmentRows;
        private readonly Button _augmentPrevButton;
        private readonly Button _augmentNextButton;
        private readonly Label _augmentPageLabel;
        private readonly Button _augmentAllButton;
        private readonly Button _augmentPrismButton;
        private readonly Button _augmentGoldButton;
        private readonly Button _augmentSilverButton;

        private bool _refreshing;
        private bool _actionBusy;
        private bool _surfaceConfirmed;
        private bool _collapsed;
        private bool _pinned = true;
        private int _expandedClientWidth;
        private int _expandedClientHeight;
        private int _collapsedClientHeight;
        private int _renderedChampionIconId;
        private int _renderedGuideChampionId;
        private MayhemChampionResult _renderedGuide;
        private string _renderedBuildFingerprint;
        private Image _ownedGuideIcon;
        private bool _dragging;
        private Point _dragCursor;
        private Point _dragWindow;
        private IReadOnlyList<MayhemAugmentRow> _augmentSource = Array.Empty<MayhemAugmentRow>();
        private readonly List<Image> _ownedAugmentIcons = new List<Image>();
        private readonly List<Image> _ownedGuideSectionIcons = new List<Image>();
        private int _guideSectionRenderGeneration;
        private int _augmentPage;
        private int _augmentRenderGeneration;
        private string _augmentFilter = "all";
        private int _benchPage;
        private string _benchRosterFingerprint;
        private string _benchRenderFingerprint;

        private sealed class BenchTarget
        {
            public int ChampionId { get; set; }
            public LeagueBenchSwapRoute Route { get; set; }
        }

        private sealed class RecommendationSection
        {
            public string Category { get; set; }
            public Panel Host { get; set; }
            public Label Value { get; set; }
            public FlowLayoutPanel Visuals { get; set; }
            public Label Evidence { get; set; }
            public Button Action { get; set; }
            public Button More { get; set; }
            public Panel Alternatives { get; set; }
            public Panel Rule { get; set; }
            public bool Expanded { get; set; }
            public bool GuideFallback { get; set; }
            public IReadOnlyList<LeagueBuildAdvisorRow> Rows { get; set; } = Array.Empty<LeagueBuildAdvisorRow>();
        }

        public LeagueRuntimeCompanionForm(
            LeagueBenchQuickPickService bench,
            ILeagueClientApi leagueClient,
            LeagueBuildAdvisorDataService advisor = null,
            LeagueBuildApplyService apply = null,
            LeagueItemSetService itemSet = null)
        {
            if (bench == null) throw new ArgumentNullException(nameof(bench));
            if (leagueClient == null) throw new ArgumentNullException(nameof(leagueClient));
            _controller = new LeagueRuntimeCompanionController(bench, leagueClient, advisor, apply, itemSet);
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
            Opacity = 0d;

            _expandedClientWidth = DesignWidth;
            _expandedClientHeight = ResolveExpandedHeight(Screen.FromPoint(Cursor.Position).WorkingArea.Height);
            _collapsedClientHeight = HeaderHeight;
            SetFixedClientSize(_expandedClientWidth, _expandedClientHeight);

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
                Size = new Size(80, HeaderHeight),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.Text,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
            };
            _status = new Label
            {
                Text = char.ConvertFromUtf32(0x25CF),
                Location = new Point(158, 0),
                Size = new Size(18, HeaderHeight),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = false,
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.TextMuted
            };
            _quitButton = CreateChromeButton(CompanionText(LeagueRuntimeCompanionUiTextKeys.QuitChampSelectShort), 180, 32);
            _quitButton.ForeColor = FacmDesignSystem.Warning;
            _quitButton.Enabled = false;
            _quitButton.Click += async delegate { await QuitChampSelectAsync(); };
            _pinButton = CreateChromeButton("↑", 216, 30);
            _pinButton.ForeColor = FacmDesignSystem.Accent;
            _pinButton.Click += delegate { TogglePin(); };
            _collapseButton = CreateChromeButton("−", 248, 30);
            _collapseButton.Click += delegate { SetCollapsed(!_collapsed); };
            var close = CreateChromeButton("×", 280, 32);
            close.Font = new Font("Segoe UI", 13F, FontStyle.Regular);
            close.Click += delegate { Close(); };

            _header.Controls.Add(_title);
            _header.Controls.Add(_status);
            _header.Controls.Add(_quitButton);
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
                Padding = new Padding(8, 6, 8, 6)
            };
            _championIcon = new PictureBox
            {
                Location = new Point(8, 11),
                Size = new Size(44, 44),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = FacmDesignSystem.SurfaceRaised
            };
            FacmDesignSystem.Round(_championIcon, FacmDesignSystem.ControlRadius);
            _championTitle = new Label
            {
                Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ChampionWaiting),
                Location = new Point(60, 4),
                Size = new Size(252, 24),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 11F, FontStyle.Bold)
            };
            _championMeta = new Label
            {
                Text = string.Empty,
                Location = new Point(60, 29),
                Size = new Size(252, 16),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F)
            };
            _championStats = new Label
            {
                Text = string.Empty,
                Location = new Point(60, 46),
                Size = new Size(252, 16),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F)
            };
            _contextStatus = new Label
            {
                Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.BuildWaiting),
                Location = new Point(60, 63),
                Size = new Size(252, 15),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Accent,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F)
            };
            _context.Controls.Add(_championIcon);
            _context.Controls.Add(_championTitle);
            _context.Controls.Add(_championMeta);
            _context.Controls.Add(_championStats);
            _context.Controls.Add(_contextStatus);

            _benchHost = new Panel
            {
                Dock = DockStyle.Top,
                Height = BenchHeight,
                BackColor = FacmDesignSystem.Canvas,
                Padding = new Padding(8, 4, 8, 4),
                Visible = false
            };
            var benchLabel = new Label
            {
                Text = BenchText(LeagueBenchQuickPickUiTextKeys.Title),
                Dock = DockStyle.Top,
                Height = 16,
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
                AutoScroll = false,
                BackColor = FacmDesignSystem.Canvas,
                Padding = new Padding(0, 2, 0, 0),
                Margin = Padding.Empty
            };
            _benchHost.Controls.Add(_benchPanel);
            _benchHost.Controls.Add(benchLabel);

            _body = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = FacmDesignSystem.Canvas,
                Padding = new Padding(8, 6, 8, 8)
            };
            _sections = new FlowLayoutPanel
            {
                Location = Point.Empty,
                Width = SectionWidth,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = FacmDesignSystem.Canvas,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _runes = CreateRecommendationSection(
                "runes",
                LeagueRecommendationText.Get(_ui, LeagueRecommendationUiTextKeys.Runes),
                CompanionText(LeagueRuntimeCompanionUiTextKeys.ApplyShort));
            _spells = CreateRecommendationSection(
                "summoner-spells",
                LeagueRecommendationText.Get(_ui, LeagueRecommendationUiTextKeys.Spells),
                CompanionText(LeagueRuntimeCompanionUiTextKeys.ApplyShort));
            _skills = CreateRecommendationSection(
                "skills",
                LeagueRecommendationText.Get(_ui, LeagueRecommendationUiTextKeys.Skills),
                null);
            _starter = CreateRecommendationSection(
                "starter-items",
                CompanionText(LeagueRuntimeCompanionUiTextKeys.StarterItems),
                null);
            _boots = CreateRecommendationSection(
                "boots",
                CompanionText(LeagueRuntimeCompanionUiTextKeys.Boots),
                null);
            _core = CreateRecommendationSection(
                "core-items",
                CompanionText(LeagueRuntimeCompanionUiTextKeys.CoreItems),
                CompanionText(LeagueRuntimeCompanionUiTextKeys.ImportShort));

            _runes.Action.Click += async delegate { await ApplyLoadoutAsync(LeagueRuntimeCompanionApplyTarget.Runes, _runes); };
            _spells.Action.Click += async delegate { await ApplyLoadoutAsync(LeagueRuntimeCompanionApplyTarget.SummonerSpells, _spells); };
            _core.Action.Click += async delegate { await ImportItemSetAsync(); };

            foreach (var section in RecommendationSections()) _sections.Controls.Add(section.Host);

            _aramBalanceSection = new Panel
            {
                Width = SectionWidth,
                Height = 58,
                Margin = Padding.Empty,
                BackColor = FacmDesignSystem.Canvas,
                Visible = false
            };
            var aramBalanceTitle = new Label
            {
                Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.AramBaseBalance),
                Location = new Point(0, 8),
                Size = new Size(68, 18),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F, FontStyle.Bold)
            };
            _aramBalanceText = new Label
            {
                Text = string.Empty,
                Location = new Point(70, 4),
                Size = new Size(214, 46),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8.2F)
            };
            var aramBalanceRule = new Panel
            {
                Location = new Point(0, 57),
                Size = new Size(SectionWidth, 1),
                BackColor = FacmDesignSystem.BorderSoft
            };
            _aramBalanceSection.Controls.Add(aramBalanceTitle);
            _aramBalanceSection.Controls.Add(_aramBalanceText);
            _aramBalanceSection.Controls.Add(aramBalanceRule);
            _sections.Controls.Add(_aramBalanceSection);

            _mayhemSection = new Panel
            {
                Width = SectionWidth, Height = 226, Margin = Padding.Empty,
                BackColor = FacmDesignSystem.Canvas, Visible = false
            };
            var augmentTitle = new Label
            {
                Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.MayhemAugments),
                Location = new Point(0, 7), Size = new Size(176, 20), AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text, BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
            };
            _augmentPrevButton = CreateInlineButton("‹", new Point(184, 3), new Size(24, 24));
            _augmentPageLabel = new Label
            {
                Location = new Point(208, 5), Size = new Size(48, 20), TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = FacmDesignSystem.TextMuted, BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 7.8F)
            };
            _augmentNextButton = CreateInlineButton("›", new Point(258, 3), new Size(24, 24));
            _augmentPrevButton.Click += delegate { SetAugmentPage(_augmentPage - 1); };
            _augmentNextButton.Click += delegate { SetAugmentPage(_augmentPage + 1); };
            _augmentAllButton = CreateInlineButton(MayhemUiCopy.AugmentAll, new Point(0, 35), new Size(44, 22));
            _augmentPrismButton = CreateInlineButton(MayhemUiCopy.Prism, new Point(50, 35), new Size(44, 22));
            _augmentGoldButton = CreateInlineButton(MayhemUiCopy.Gold, new Point(100, 35), new Size(44, 22));
            _augmentSilverButton = CreateInlineButton(MayhemUiCopy.Silver, new Point(150, 35), new Size(44, 22));
            foreach (var filter in new[] { _augmentAllButton, _augmentPrismButton, _augmentGoldButton, _augmentSilverButton })
                filter.Visible = false;
            _augmentAllButton.Click += delegate { SetAugmentFilter("all"); };
            _augmentPrismButton.Click += delegate { SetAugmentFilter("prism"); };
            _augmentGoldButton.Click += delegate { SetAugmentFilter("gold"); };
            _augmentSilverButton.Click += delegate { SetAugmentFilter("silver"); };
            var augmentRule = new Panel
            {
                Location = new Point(0, 31), Size = new Size(SectionWidth, 1), BackColor = FacmDesignSystem.BorderSoft
            };
            _augmentRows = new FlowLayoutPanel
            {
                Location = new Point(0, 38), Size = new Size(SectionWidth, AugmentPageSize * AugmentRowHeight),
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false,
                Margin = Padding.Empty, Padding = Padding.Empty, BackColor = FacmDesignSystem.Canvas
            };
            _mayhemSection.Controls.Add(augmentTitle);
            _mayhemSection.Controls.Add(_augmentPrevButton);
            _mayhemSection.Controls.Add(_augmentPageLabel);
            _mayhemSection.Controls.Add(_augmentNextButton);
            _mayhemSection.Controls.Add(_augmentAllButton);
            _mayhemSection.Controls.Add(_augmentPrismButton);
            _mayhemSection.Controls.Add(_augmentGoldButton);
            _mayhemSection.Controls.Add(_augmentSilverButton);
            _mayhemSection.Controls.Add(augmentRule);
            _mayhemSection.Controls.Add(_augmentRows);
            _sections.Controls.Add(_mayhemSection);
            _body.Controls.Add(_sections);
            _body.HandleCreated += delegate { HideNativeBodyScrollBars(); };
            _body.Layout += delegate { HideNativeBodyScrollBars(); };
            _sections.SizeChanged += delegate { HideNativeBodyScrollBars(); };

            Controls.Add(_body);
            Controls.Add(_benchHost);
            Controls.Add(_context);
            Controls.Add(_header);

            _toolTip = new ToolTip { ShowAlways = true, AutomaticDelay = 120 };
            _toolTip.SetToolTip(_status, CompanionText(LeagueRuntimeCompanionUiTextKeys.BuildWaiting));
            _toolTip.SetToolTip(_quitButton, CompanionText(LeagueRuntimeCompanionUiTextKeys.QuitChampSelectTooltip));
            _toolTip.SetToolTip(_augmentPrevButton, CompanionText(LeagueRuntimeCompanionUiTextKeys.PreviousPage));
            _toolTip.SetToolTip(_augmentNextButton, CompanionText(LeagueRuntimeCompanionUiTextKeys.NextPage));
            _toolTip.SetToolTip(_pinButton, CompanionText(LeagueRuntimeCompanionUiTextKeys.Unpin));
            _toolTip.SetToolTip(_collapseButton, CompanionText(LeagueRuntimeCompanionUiTextKeys.Collapse));
            _pollTimer = new System.Windows.Forms.Timer { Interval = 650 };
            _pollTimer.Tick += async delegate { await RefreshSnapshotAsync(); };
            _controller.SnapshotChanged += HandleSnapshotChanged;
            Shown += HandleShown;
            FormClosed += HandleClosed;
            ResetRecommendationSections();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        internal static int ResolveExpandedHeight(int workingAreaHeight)
        {
            var usable = Math.Max(1, workingAreaHeight - 32);
            var compactCap = Math.Max(1, (int)Math.Round(workingAreaHeight * 0.70, MidpointRounding.AwayFromZero));
            var available = Math.Min(usable, compactCap);
            if (available < MinimumExpandedHeight) return available;
            return Math.Min(MaximumExpandedHeight, available);
        }

        private IEnumerable<RecommendationSection> RecommendationSections()
        {
            return new[] { _runes, _spells, _skills, _starter, _boots, _core };
        }

        private void HandleShown(object sender, EventArgs e)
        {
            _expandedClientWidth = ClientSize.Width;
            _expandedClientHeight = ClientSize.Height;
            _collapsedClientHeight = _header.Height;
            FacmDesignSystem.Round(this, FacmDesignSystem.WindowRadius);
            _pollTimer.Start();
            _ = RefreshSnapshotAsync();
        }

        private async Task RefreshSnapshotAsync()
        {
            if (_refreshing || IsDisposed || _lifetime.IsCancellationRequested) return;
            _refreshing = true;
            try
            {
                await _controller.RefreshAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Info("Runtime Companion refresh skipped: " + exception.Message);
                if (!IsDisposed)
                    SetStatus(CompanionText(LeagueRuntimeCompanionUiTextKeys.BuildUnavailable), FacmDesignSystem.Warning);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void HandleSnapshotChanged(object sender, LeagueRuntimeCompanionUpdateEventArgs e)
        {
            var snapshot = e == null ? null : e.Snapshot;
            if (snapshot == null || IsDisposed || _lifetime.IsCancellationRequested) return;
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(delegate { ApplySnapshot(snapshot); }));
                    return;
                }
                ApplySnapshot(snapshot);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ApplySnapshot(LeagueRuntimeCompanionSnapshot snapshot)
        {
            if (snapshot == null || IsDisposed) return;

            var hasContext = snapshot.HasVisibleChampSelectContext;
            if (hasContext && !_surfaceConfirmed)
            {
                _surfaceConfirmed = true;
                Opacity = 1d;
            }

            _benchHost.Visible = snapshot.BenchEnabled && !_collapsed;
            if (snapshot.BenchEnabled) RenderBench(snapshot);
            else ClearBenchButtons();

            if (snapshot.Build != null)
                RenderBuild(snapshot.Build, snapshot.BuildLoading, snapshot.BuildError);
            else if (snapshot.BuildLoading)
                SetBuildLoadingState(snapshot.LocalChampionId);
            else if (!string.IsNullOrWhiteSpace(snapshot.BuildError))
                SetBuildUnavailableState();
            else if (hasContext && snapshot.LocalChampionId <= 0 && _renderedBuildFingerprint == null)
                SetChampionWaitingState();

            if (snapshot.GuideChampionId > 0 && snapshot.GuideChampionId != _renderedGuideChampionId)
            {
                _renderedGuideChampionId = snapshot.GuideChampionId;
                _renderedGuide = null;
            }

            if (snapshot.GuideLoading && snapshot.Build == null)
            {
                _contextStatus.ForeColor = FacmDesignSystem.Accent;
                _contextStatus.Text = MayhemUiCopy.ReadingLatest;
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.GuideError) && snapshot.Build == null)
            {
                _contextStatus.ForeColor = FacmDesignSystem.Warning;
                _contextStatus.Text = snapshot.GuideError;
            }

            if (snapshot.HasGuide && !ReferenceEquals(_renderedGuide, snapshot.Guide))
            {
                _renderedGuide = snapshot.Guide;
                RenderGuideFallbackAndAugments(snapshot.Guide, snapshot.HasBuild);
            }
            else if (!snapshot.HasGuide && !snapshot.GuideLoading && _renderedGuide != null)
            {
                // Queue/mode changes or a failed version-bound refresh can withdraw a guide.
                // Clear only presentation that was owned by that guide so stale ARAM/Mayhem
                // content never survives into the next context.
                _renderedGuide = null;
                _renderedGuideChampionId = snapshot.GuideChampionId;
                ClearGuidePresentation();
            }

            if (!hasContext)
                SetStatus(CompanionText(LeagueRuntimeCompanionUiTextKeys.BuildWaiting), FacmDesignSystem.TextMuted);
        }

        private void RenderBuild(LeagueBuildAdvisorSnapshot build, bool loading, string error)
        {
            if (build == null) return;
            var fingerprint = BuildFingerprint(build);
            var changed = !string.Equals(_renderedBuildFingerprint, fingerprint, StringComparison.Ordinal);
            if (changed)
            {
                _renderedBuildFingerprint = fingerprint;
                ResetRecommendationSections();
            }

            var championId = build.ChampionId > 0 ? build.ChampionId : 0;
            if (championId > 0)
            {
                _championTitle.Text = FirstNonEmpty(build.ChampionName, FormatChampionId(championId));
                EnsureLocalChampionIcon(championId);
            }
            else
            {
                _championTitle.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ChampionWaiting);
                DetachChampionImage();
                _renderedChampionIconId = 0;
            }

            _championMeta.Text = BuildContextMeta(build);
            _championStats.Text = BuildStats(build.Recommendation);

            if (build.Recommendation != null)
            {
                foreach (var section in RecommendationSections())
                    ApplyBuildRows(section, FindBuildRows(build.Recommendation, section.Category));
            }
            SetActionButtonsEnabled(!_actionBusy);

            if (string.Equals(build.Status, "ready", StringComparison.OrdinalIgnoreCase))
            {
                var cacheSuffix = build.FromCache
                    ? " · " + CompanionText(LeagueRuntimeCompanionUiTextKeys.SourceCache)
                    : string.Empty;
                _contextStatus.ForeColor = FacmDesignSystem.Success;
                _contextStatus.Text = FirstNonEmpty(build.Source, LeagueRecommendationText.Get(_ui, LeagueRecommendationUiTextKeys.Ready)) + cacheSuffix;
                if (!_actionBusy) SetStatus(LeagueRecommendationText.Get(_ui, LeagueRecommendationUiTextKeys.Ready), FacmDesignSystem.Success);
            }
            else if (loading)
            {
                _contextStatus.ForeColor = FacmDesignSystem.Accent;
                _contextStatus.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.BuildLoading);
                if (!_actionBusy) SetStatus(CompanionText(LeagueRuntimeCompanionUiTextKeys.BuildLoading), FacmDesignSystem.Accent);
            }
            else if (string.Equals(build.Status, "waiting-champion", StringComparison.OrdinalIgnoreCase))
            {
                SetChampionWaitingState();
            }
            else if (!string.IsNullOrWhiteSpace(error) ||
                     string.Equals(build.Status, "opgg-unavailable", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(build.Status, "timeout", StringComparison.OrdinalIgnoreCase))
            {
                SetBuildUnavailableState();
            }
            else
            {
                _contextStatus.ForeColor = FacmDesignSystem.TextMuted;
                _contextStatus.Text = LeagueRecommendationText.Get(_ui, LeagueRecommendationUiTextKeys.Waiting);
                if (!_actionBusy) SetStatus(LeagueRecommendationText.Get(_ui, LeagueRecommendationUiTextKeys.Waiting), FacmDesignSystem.TextMuted);
            }
        }

        private async Task ApplyLoadoutAsync(
            LeagueRuntimeCompanionApplyTarget target,
            RecommendationSection section)
        {
            if (_actionBusy || section == null || section.Action == null || IsDisposed || _lifetime.IsCancellationRequested) return;
            _actionBusy = true;
            SetActionButtonsEnabled(false);
            section.Evidence.ForeColor = FacmDesignSystem.Accent;
            section.Evidence.Text = T(LeagueBuildApplyUiTextKeys.Preparing);
            SetStatus(T(LeagueBuildApplyUiTextKeys.Preparing), FacmDesignSystem.Accent);
            try
            {
                var preparation = await _controller.PrepareApplyAsync(target, _lifetime.Token);
                if (preparation == null || !preparation.IsUsable)
                {
                    section.Evidence.ForeColor = FacmDesignSystem.Warning;
                    section.Evidence.Text = T(LeagueBuildApplyUiTextKeys.NoLoadout);
                    SetStatus(T(LeagueBuildApplyUiTextKeys.NoLoadout), FacmDesignSystem.Warning);
                    return;
                }

                var plan = preparation.Plan;
                var skipped = LeagueRecommendationText.Get(_ui, LeagueRecommendationUiTextKeys.NotSelected);
                var spellPreview = target == LeagueRuntimeCompanionApplyTarget.SummonerSpells
                    ? FirstNonEmpty(plan.SpellPreview, plan.Spell1Id + " / " + plan.Spell2Id)
                    : skipped;
                var runePreview = target == LeagueRuntimeCompanionApplyTarget.Runes
                    ? FirstNonEmpty(plan.RunePreview, plan.PrimaryStyleId + " / " + plan.SecondaryStyleId)
                    : skipped;
                var confirmation = string.Format(
                    T(LeagueBuildApplyUiTextKeys.ConfirmFormat),
                    BuildApplyContext(preparation.SourceSnapshot),
                    spellPreview,
                    runePreview);
                var choice = MessageBox.Show(
                    this,
                    confirmation,
                    T(LeagueBuildApplyUiTextKeys.ConfirmTitle),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (choice != DialogResult.Yes)
                {
                    RestoreSectionEvidence(section);
                    SetStatus(T(LeagueBuildApplyUiTextKeys.Ready), FacmDesignSystem.TextMuted);
                    return;
                }

                var result = await _controller.ApplyPreparedAsync(preparation, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                RenderApplyResult(section, result);
                await _controller.RefreshAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested)
                {
                    section.Evidence.ForeColor = FacmDesignSystem.Warning;
                    section.Evidence.Text = T(LeagueBuildApplyUiTextKeys.ContextChanged);
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Runtime Companion loadout apply failed", exception);
                if (!IsDisposed)
                {
                    section.Evidence.ForeColor = FacmDesignSystem.Error;
                    section.Evidence.Text = T(LeagueBuildApplyUiTextKeys.WriteFailed);
                    SetStatus(T(LeagueBuildApplyUiTextKeys.WriteFailed), FacmDesignSystem.Error);
                }
            }
            finally
            {
                _actionBusy = false;
                if (!IsDisposed) SetActionButtonsEnabled(true);
            }
        }

        private async Task QuitChampSelectAsync()
        {
            if (_actionBusy || IsDisposed || _lifetime.IsCancellationRequested || !_controller.SupportsChampSelectQuit) return;
            _actionBusy = true;
            SetActionButtonsEnabled(false);
            _contextStatus.ForeColor = FacmDesignSystem.Warning;
            _contextStatus.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.QuitChampSelectBusy);
            SetStatus(_contextStatus.Text, FacmDesignSystem.Warning);
            try
            {
                var result = await _controller.QuitChampSelectAsync(_lifetime.Token);
                if (result != null && result.Success)
                {
                    _contextStatus.ForeColor = FacmDesignSystem.Success;
                    _contextStatus.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.QuitChampSelectSucceeded);
                    SetStatus(_contextStatus.Text, FacmDesignSystem.Success);
                    AppLog.Info("Runtime Companion quit Champion Select: success; phase=" + (result.PhaseAfter ?? "-") + "; lobbyPreserved=" + result.LobbyPreserved.ToString().ToLowerInvariant());
                    Close();
                    return;
                }

                var blocked = result != null && result.Status == LeagueChampSelectQuitStatus.NotInChampSelect;
                _contextStatus.ForeColor = blocked ? FacmDesignSystem.Warning : FacmDesignSystem.Error;
                _contextStatus.Text = CompanionText(blocked
                    ? LeagueRuntimeCompanionUiTextKeys.QuitChampSelectBlocked
                    : LeagueRuntimeCompanionUiTextKeys.QuitChampSelectFailed);
                SetStatus(_contextStatus.Text, _contextStatus.ForeColor);
                AppLog.Info("Runtime Companion quit Champion Select: " + (result == null ? "no-result" : result.Status.ToString()) +
                            "; http=" + (result == null ? "0" : result.StatusCode.ToString(CultureInfo.InvariantCulture)) +
                            "; lobbyPreserved=" + (result != null && result.LobbyPreserved).ToString().ToLowerInvariant());
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                _contextStatus.ForeColor = FacmDesignSystem.Error;
                _contextStatus.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.QuitChampSelectFailed);
                SetStatus(_contextStatus.Text, FacmDesignSystem.Error);
                AppLog.Info("Runtime Companion quit Champion Select failed: " + exception.Message);
            }
            finally
            {
                _actionBusy = false;
                if (!IsDisposed) SetActionButtonsEnabled(true);
            }
        }

        private async Task ImportItemSetAsync()
        {
            if (_actionBusy || IsDisposed || _lifetime.IsCancellationRequested || !_controller.SupportsItemSetApply) return;
            _actionBusy = true;
            SetActionButtonsEnabled(false);
            _core.Evidence.ForeColor = FacmDesignSystem.Accent;
            _core.Evidence.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ItemSetPreparing);
            SetStatus(CompanionText(LeagueRuntimeCompanionUiTextKeys.ItemSetPreparing), FacmDesignSystem.Accent);
            try
            {
                var source = _controller.CurrentSnapshot.Build;
                var plan = await _controller.PrepareItemSetAsync(_lifetime.Token);
                if (plan == null || !plan.HasItems)
                {
                    _core.Evidence.ForeColor = FacmDesignSystem.Warning;
                    _core.Evidence.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ItemSetUnavailable);
                    SetStatus(_core.Evidence.Text, FacmDesignSystem.Warning);
                    return;
                }

                var confirmation = string.Format(
                    CompanionText(LeagueRuntimeCompanionUiTextKeys.ItemSetConfirmFormat),
                    BuildApplyContext(source),
                    plan.ItemCount.ToString(CultureInfo.InvariantCulture));
                var choice = MessageBox.Show(
                    this,
                    confirmation,
                    CompanionText(LeagueRuntimeCompanionUiTextKeys.ItemSetConfirmTitle),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (choice != DialogResult.Yes)
                {
                    RestoreSectionEvidence(_core);
                    SetStatus(LeagueRecommendationText.Get(_ui, LeagueRecommendationUiTextKeys.Ready), FacmDesignSystem.TextMuted);
                    return;
                }

                var result = await _controller.ApplyItemSetAsync(plan, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                if (result != null && result.Succeeded)
                {
                    _core.Evidence.ForeColor = result.CleanupWarning ? FacmDesignSystem.Warning : FacmDesignSystem.Success;
                    _core.Evidence.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ItemSetSucceeded);
                    SetStatus(_core.Evidence.Text, result.CleanupWarning ? FacmDesignSystem.Warning : FacmDesignSystem.Success);
                }
                else if (result != null && string.Equals(result.Status, "blocked", StringComparison.OrdinalIgnoreCase))
                {
                    _core.Evidence.ForeColor = FacmDesignSystem.Warning;
                    _core.Evidence.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ItemSetBlocked);
                    SetStatus(_core.Evidence.Text, FacmDesignSystem.Warning);
                }
                else
                {
                    _core.Evidence.ForeColor = FacmDesignSystem.Error;
                    _core.Evidence.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ItemSetFailed);
                    SetStatus(_core.Evidence.Text, FacmDesignSystem.Error);
                }
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested)
                {
                    _core.Evidence.ForeColor = FacmDesignSystem.Warning;
                    _core.Evidence.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ItemSetBlocked);
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Runtime Companion item-set import failed", exception);
                if (!IsDisposed)
                {
                    _core.Evidence.ForeColor = FacmDesignSystem.Error;
                    _core.Evidence.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ItemSetFailed);
                    SetStatus(_core.Evidence.Text, FacmDesignSystem.Error);
                }
            }
            finally
            {
                _actionBusy = false;
                if (!IsDisposed) SetActionButtonsEnabled(true);
            }
        }

        private void RenderApplyResult(RecommendationSection section, LeagueBuildApplyResult result)
        {
            if (result == null)
            {
                section.Evidence.ForeColor = FacmDesignSystem.Error;
                section.Evidence.Text = T(LeagueBuildApplyUiTextKeys.WriteFailed);
                SetStatus(T(LeagueBuildApplyUiTextKeys.WriteFailed), FacmDesignSystem.Error);
                return;
            }
            if (string.Equals(result.Status, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                section.Evidence.ForeColor = FacmDesignSystem.Warning;
                section.Evidence.Text = T(LeagueBuildApplyUiTextKeys.ContextChanged);
                SetStatus(T(LeagueBuildApplyUiTextKeys.ContextChanged), FacmDesignSystem.Warning);
                return;
            }
            if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                section.Evidence.ForeColor = FacmDesignSystem.Success;
                section.Evidence.Text = T(LeagueBuildApplyUiTextKeys.Succeeded);
                SetStatus(T(LeagueBuildApplyUiTextKeys.Succeeded), FacmDesignSystem.Success);
                return;
            }
            if (result.RuneSkippedNoCapacity)
            {
                section.Evidence.ForeColor = FacmDesignSystem.Warning;
                section.Evidence.Text = T(LeagueBuildApplyUiTextKeys.RuneSlotFull);
                SetStatus(T(LeagueBuildApplyUiTextKeys.RuneSlotFull), FacmDesignSystem.Warning);
                return;
            }

            section.Evidence.ForeColor = result.AnyApplied ? FacmDesignSystem.Warning : FacmDesignSystem.Error;
            section.Evidence.Text = T(LeagueBuildApplyUiTextKeys.WriteFailed);
            SetStatus(
                T(result.AnyApplied ? LeagueBuildApplyUiTextKeys.Partial : LeagueBuildApplyUiTextKeys.WriteFailed),
                result.AnyApplied ? FacmDesignSystem.Warning : FacmDesignSystem.Error);
        }

        private void RestoreSectionEvidence(RecommendationSection section)
        {
            if (section == null || section.Rows == null || section.Rows.Count == 0) return;
            section.Evidence.ForeColor = FacmDesignSystem.TextMuted;
            section.Evidence.Text = section.Rows[0].Evidence ?? string.Empty;
        }

        private void SetActionButtonsEnabled(bool enabled)
        {
            if (_quitButton != null)
                _quitButton.Enabled = enabled && _controller.SupportsChampSelectQuit && _controller.CurrentSnapshot.SessionAvailable;
            if (_runes.Action != null)
                _runes.Action.Enabled = enabled && _controller.SupportsBuildApply && _runes.Host.Visible;
            if (_spells.Action != null)
                _spells.Action.Enabled = enabled && _controller.SupportsBuildApply && _spells.Host.Visible;
            if (_core.Action != null)
                _core.Action.Enabled = enabled && _controller.SupportsItemSetApply && _core.Host.Visible;
        }

        private void RenderGuideFallbackAndAugments(MayhemChampionResult result, bool hasBuildContext)
        {
            if (result == null) return;
            ResetGuideSectionIcons();
            RenderAramBaseBalance(result);
            if (!hasBuildContext)
            {
                if (!string.IsNullOrWhiteSpace(result.ChampionName))
                    _championTitle.Text = result.ChampionName;
                else if (_renderedGuideChampionId > 0)
                {
                    _championTitle.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ChampionResolving);
                    _ = ResolveGuideChampionNameAsync(_renderedGuideChampionId);
                }
                else
                    _championTitle.Text = MayhemUiCopy.Unknown;
                _championMeta.Text = BuildMayhemContextMeta(result);
                _championStats.Text = BuildMayhemStats(result);
                _contextStatus.ForeColor = FacmDesignSystem.Success;
                _contextStatus.Text = MayhemUiCopy.Completed;
                ApplyGuideFallbackIcons(_spells, result.SummonerSpells, BuildSpellText(result), 2);
                ApplyGuideFallbackSkills(_skills, result.SkillPriority, BuildSkillText(result));
                ApplyGuideFallbackIcons(_starter, result.StarterItems, BuildMayhemItemListText(result.StarterItems, 3), 3);
                ApplyGuideFallbackIcons(_boots, result.BootItems, BuildMayhemItemListText(result.BootItems, 2), 2);
                ApplyGuideFallbackIcons(_core, FirstMayhemCoreItems(result), BuildMayhemCoreText(result), 5);
                if (_renderedChampionIconId <= 0 && !string.IsNullOrWhiteSpace(result.ChampionIconUrl))
                    _ = LoadGuideChampionPictureAsync(result.ChampionIconUrl, _renderedGuideChampionId);
            }

            _augmentSource = (result.AugmentRows ?? new List<MayhemAugmentRow>())
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.Name))
                .OrderBy(value => value.Rank <= 0 ? int.MaxValue : value.Rank)
                .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .Take(40).ToList().AsReadOnly();
            SetAugmentPage(0);
        }

        private void SetAugmentPage(int page)
        {
            var source = FilteredAugments();
            var pages = Math.Max(1, (int)Math.Ceiling(source.Count / (double)AugmentPageSize));
            _augmentPage = Math.Max(0, Math.Min(page, pages - 1));
            RenderAugmentPage();
        }

        private void SetAugmentFilter(string filter)
        {
            var normalized = string.IsNullOrWhiteSpace(filter) ? "all" : filter.Trim().ToLowerInvariant();
            if (normalized != "all" && !(_augmentSource ?? Array.Empty<MayhemAugmentRow>()).Any(row => row != null && row.RarityKind == normalized))
                normalized = "all";
            _augmentFilter = normalized;
            _augmentPage = 0;
            RenderAugmentPage();
        }

        private IReadOnlyList<MayhemAugmentRow> FilteredAugments()
        {
            var source = _augmentSource ?? Array.Empty<MayhemAugmentRow>();
            if (string.Equals(_augmentFilter, "all", StringComparison.Ordinal)) return source;
            return source.Where(row => row != null && string.Equals(row.RarityKind, _augmentFilter, StringComparison.Ordinal)).ToList().AsReadOnly();
        }

        private void UpdateAugmentFilterControls()
        {
            var source = _augmentSource ?? Array.Empty<MayhemAugmentRow>();
            var hasPrism = source.Any(row => row != null && row.RarityKind == "prism");
            var hasGold = source.Any(row => row != null && row.RarityKind == "gold");
            var hasSilver = source.Any(row => row != null && row.RarityKind == "silver");
            var showFilters = hasPrism || hasGold || hasSilver;
            if (!showFilters) _augmentFilter = "all";
            if (_augmentFilter == "prism" && !hasPrism) _augmentFilter = "all";
            if (_augmentFilter == "gold" && !hasGold) _augmentFilter = "all";
            if (_augmentFilter == "silver" && !hasSilver) _augmentFilter = "all";

            foreach (var button in new[] { _augmentAllButton, _augmentPrismButton, _augmentGoldButton, _augmentSilverButton })
                button.Visible = showFilters;
            _augmentPrismButton.Enabled = hasPrism;
            _augmentGoldButton.Enabled = hasGold;
            _augmentSilverButton.Enabled = hasSilver;
            StyleAugmentFilterButton(_augmentAllButton, "all");
            StyleAugmentFilterButton(_augmentPrismButton, "prism");
            StyleAugmentFilterButton(_augmentGoldButton, "gold");
            StyleAugmentFilterButton(_augmentSilverButton, "silver");

            _augmentRows.Location = new Point(0, showFilters ? 64 : 38);
            _mayhemSection.Height = showFilters ? 252 : 226;
        }

        private void StyleAugmentFilterButton(Button button, string filter)
        {
            if (button == null) return;
            var selected = string.Equals(_augmentFilter, filter, StringComparison.Ordinal);
            button.ForeColor = selected ? FacmDesignSystem.Accent : FacmDesignSystem.TextMuted;
            button.FlatAppearance.BorderColor = selected ? FacmDesignSystem.Accent : FacmDesignSystem.BorderSoft;
        }

        private void RenderAugmentPage()
        {
            DisposeAugmentRows();
            if (_augmentSource == null || _augmentSource.Count == 0)
            {
                _augmentPageLabel.Text = string.Empty;
                _augmentPrevButton.Enabled = _augmentNextButton.Enabled = false;
                UpdateAugmentFilterControls();
                _mayhemSection.Visible = false;
                return;
            }
            UpdateAugmentFilterControls();
            var source = FilteredAugments();
            var pages = Math.Max(1, (int)Math.Ceiling(source.Count / (double)AugmentPageSize));
            _augmentPage = Math.Max(0, Math.Min(_augmentPage, pages - 1));
            _augmentPageLabel.Text = (_augmentPage + 1).ToString(CultureInfo.InvariantCulture) + "/" + pages.ToString(CultureInfo.InvariantCulture);
            _augmentPrevButton.Enabled = _augmentPage > 0;
            _augmentNextButton.Enabled = _augmentPage + 1 < pages;
            var generation = ++_augmentRenderGeneration;
            foreach (var row in source.Skip(_augmentPage * AugmentPageSize).Take(AugmentPageSize))
                _augmentRows.Controls.Add(CreateAugmentRow(row, generation));
            _mayhemSection.Visible = _augmentRows.Controls.Count > 0;
            HideNativeBodyScrollBars();
        }

        private Panel CreateAugmentRow(MayhemAugmentRow row, int generation)
        {
            var host = new Panel { Width = SectionWidth, Height = AugmentRowHeight, Margin = Padding.Empty, BackColor = FacmDesignSystem.Canvas };
            var icon = new PictureBox
            {
                Location = new Point(0, 5), Size = new Size(26, 26), SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = FacmDesignSystem.SurfaceRaised
            };
            FacmDesignSystem.Round(icon, Math.Min(4, FacmDesignSystem.ControlRadius));
            var title = new Label
            {
                Text = (row.Rank > 0 ? row.Rank.ToString(CultureInfo.InvariantCulture) + "  " : string.Empty) + row.Name,
                Location = new Point(34, 1), Size = new Size(178, 17), AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text, BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8.5F, FontStyle.Bold)
            };
            var rarityText = AugmentRarityLabel(row);
            var rarity = new Label
            {
                Text = rarityText, Location = new Point(216, 1), Size = new Size(68, 17), TextAlign = ContentAlignment.TopRight,
                AutoEllipsis = true, ForeColor = FacmDesignSystem.TextMuted, BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 7.4F)
            };
            var metrics = new Label
            {
                Text = BuildAugmentMetrics(row), Location = new Point(34, 18), Size = new Size(250, 16), AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted, BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 7.5F)
            };
            var rule = new Panel { Location = new Point(0, AugmentRowHeight - 1), Size = new Size(SectionWidth, 1), BackColor = FacmDesignSystem.BorderSoft };
            host.Controls.Add(icon); host.Controls.Add(title); host.Controls.Add(rarity); host.Controls.Add(metrics); host.Controls.Add(rule);
            var detail = string.IsNullOrWhiteSpace(row.Description) ? row.Name : row.Name + Environment.NewLine + row.Description;
            _toolTip.SetToolTip(host, detail); _toolTip.SetToolTip(title, detail); _toolTip.SetToolTip(metrics, detail);
            if (!string.IsNullOrWhiteSpace(row.IconUrl)) _ = LoadAugmentIconAsync(icon, row.IconUrl, generation);
            return host;
        }

        private static string AugmentRarityLabel(MayhemAugmentRow row)
        {
            if (row == null) return string.Empty;
            switch (row.RarityKind)
            {
                case "prism": return MayhemUiCopy.Prism;
                case "gold": return MayhemUiCopy.Gold;
                case "silver": return MayhemUiCopy.Silver;
                default: return string.Empty;
            }
        }

        private static string BuildAugmentMetrics(MayhemAugmentRow row)
        {
            if (row == null) return string.Empty;
            var parts = new List<string>();
            if (row.WinRate.HasValue) parts.Add(MayhemUiCopy.WinShort + FormatRate(row.WinRate));
            if (row.PickRate.HasValue) parts.Add(MayhemUiCopy.PickShort + FormatRate(row.PickRate));
            if (row.Games.HasValue) parts.Add(row.Games.Value.ToString("N0", CultureInfo.InvariantCulture) + MayhemUiCopy.GamesSuffix);
            return string.Join(MayhemUiCopy.SeparatorDot, parts);
        }

        private async Task LoadAugmentIconAsync(PictureBox target, string reference, int generation)
        {
            try
            {
                var bitmap = await _controller.LoadGuideAssetAsync(reference, _lifetime.Token);
                if (bitmap == null) return;
                if (IsDisposed || target.IsDisposed || generation != _augmentRenderGeneration) { bitmap.Dispose(); return; }
                _ownedAugmentIcons.Add(bitmap);
                target.Image = bitmap;
            }
            catch (OperationCanceledException) { } catch (ObjectDisposedException) { } catch { }
        }

        private void DisposeAugmentRows()
        {
            _augmentRenderGeneration++;
            var controls = _augmentRows.Controls.Cast<Control>().ToArray();
            _augmentRows.Controls.Clear();
            foreach (var control in controls) control.Dispose();
            foreach (var image in _ownedAugmentIcons) image.Dispose();
            _ownedAugmentIcons.Clear();
        }

        private void ClearAugments()
        {
            _augmentSource = Array.Empty<MayhemAugmentRow>();
            _augmentPage = 0;
            _augmentFilter = "all";
            DisposeAugmentRows();
            _augmentPageLabel.Text = string.Empty;
            _augmentPrevButton.Enabled = _augmentNextButton.Enabled = false;
        }

        private async Task ResolveGuideChampionNameAsync(int championId)
        {
            try
            {
                var name = await _controller.ResolveChampionNameAsync(championId, _lifetime.Token);
                if (!string.IsNullOrWhiteSpace(name) && !IsDisposed && championId == _renderedGuideChampionId)
                    _championTitle.Text = name;
            }
            catch (OperationCanceledException) { } catch (ObjectDisposedException) { } catch { }
        }

        private void HideNativeBodyScrollBars()
        {
            if (_body == null || !_body.IsHandleCreated) return;
            ShowScrollBar(_body.Handle, SbHorz, false);
            ShowScrollBar(_body.Handle, SbVert, false);
        }

        private void RenderAramBaseBalance(MayhemChampionResult result)
        {
            if (!ShouldShowAramBaseBalance(result))
            {
                _aramBalanceSection.Visible = false;
                _aramBalanceText.Text = string.Empty;
                return;
            }

            _aramBalanceText.Text = result.BaseBalanceSummary.Trim();
            _toolTip.SetToolTip(_aramBalanceText, _aramBalanceText.Text);
            var status = (result.BaseBalanceStatus ?? string.Empty).Trim();
            _aramBalanceText.ForeColor =
                string.Equals(status, "syncing", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "unavailable", StringComparison.OrdinalIgnoreCase)
                    ? FacmDesignSystem.Warning
                    : FacmDesignSystem.Text;
            _aramBalanceSection.Visible = true;
        }

        private static bool ShouldShowAramBaseBalance(MayhemChampionResult result)
        {
            return result != null && !string.IsNullOrWhiteSpace(result.BaseBalanceSummary);
        }

        private void SetBuildLoadingState(int championId)
        {
            if (championId > 0)
            {
                _championTitle.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ChampionResolving);
                EnsureLocalChampionIcon(championId);
            }
            else
            {
                _championTitle.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ChampionWaiting);
            }
            _championStats.Text = string.Empty;
            _contextStatus.ForeColor = FacmDesignSystem.Accent;
            _contextStatus.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.BuildLoading);
            if (!_actionBusy) SetStatus(CompanionText(LeagueRuntimeCompanionUiTextKeys.BuildLoading), FacmDesignSystem.Accent);
        }

        private void SetBuildUnavailableState()
        {
            _contextStatus.ForeColor = FacmDesignSystem.Warning;
            _contextStatus.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.BuildUnavailable);
            if (!_actionBusy) SetStatus(CompanionText(LeagueRuntimeCompanionUiTextKeys.BuildUnavailable), FacmDesignSystem.Warning);
        }

        private void SetChampionWaitingState()
        {
            _championTitle.Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ChampionWaiting);
            _championMeta.Text = string.Empty;
            _championStats.Text = string.Empty;
            _contextStatus.ForeColor = FacmDesignSystem.TextMuted;
            _contextStatus.Text = LeagueRecommendationText.Get(_ui, LeagueRecommendationUiTextKeys.Waiting);
            if (!_actionBusy) SetStatus(CompanionText(LeagueRuntimeCompanionUiTextKeys.BuildWaiting), FacmDesignSystem.TextMuted);
        }

        private RecommendationSection CreateRecommendationSection(string category, string title, string actionText)
        {
            var host = new Panel
            {
                Width = SectionWidth,
                Height = RecommendationBaseHeight,
                Margin = Padding.Empty,
                BackColor = FacmDesignSystem.Canvas,
                Visible = false
            };
            var caption = new Label
            {
                Text = title ?? string.Empty,
                Location = new Point(0, 9),
                Size = new Size(82, 20),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F, FontStyle.Bold)
            };
            var value = new Label
            {
                Location = new Point(70, 5),
                Size = new Size(actionText == null ? 214 : 168, 31),
                AutoEllipsis = false,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8.8F)
            };
            var visuals = new FlowLayoutPanel
            {
                Location = new Point(70, 4),
                Size = new Size(actionText == null ? 214 : 168, 32),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent,
                Visible = false
            };
            var evidence = new Label
            {
                Location = new Point(70, 38),
                Size = new Size(214, 16),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 7.8F)
            };
            Button action = null;
            if (!string.IsNullOrWhiteSpace(actionText))
            {
                action = CreateInlineButton(actionText, new Point(242, 6), new Size(42, 26));
                host.Controls.Add(action);
            }
            var more = CreateInlineButton(string.Empty, new Point(8, 36), new Size(54, 20));
            more.Font = new Font(FacmThemeRuntime.Current.FontName, 7.6F);
            more.Visible = false;
            var alternatives = new Panel
            {
                Location = new Point(70, RecommendationBaseHeight - 2),
                Size = new Size(214, 0),
                BackColor = FacmDesignSystem.Canvas,
                Visible = false
            };
            var rule = new Panel
            {
                Location = new Point(0, RecommendationBaseHeight - 1),
                Size = new Size(SectionWidth, 1),
                BackColor = FacmDesignSystem.BorderSoft
            };

            var section = new RecommendationSection
            {
                Category = category,
                Host = host,
                Value = value,
                Visuals = visuals,
                Evidence = evidence,
                Action = action,
                More = more,
                Alternatives = alternatives,
                Rule = rule
            };
            more.Click += delegate { SetAlternativesExpanded(section, !section.Expanded); };

            host.Controls.Add(caption);
            host.Controls.Add(value);
            host.Controls.Add(visuals);
            host.Controls.Add(evidence);
            host.Controls.Add(more);
            host.Controls.Add(alternatives);
            host.Controls.Add(rule);
            return section;
        }

        private Button CreateInlineButton(string text, Point location, Size size)
        {
            var button = new Button
            {
                Text = text ?? string.Empty,
                Location = location,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                BackColor = FacmDesignSystem.Surface,
                ForeColor = FacmDesignSystem.Text,
                Cursor = Cursors.Hand,
                TabStop = true,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F)
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = FacmDesignSystem.BorderSoft;
            button.FlatAppearance.MouseOverBackColor = FacmDesignSystem.SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = FacmDesignSystem.SurfaceRaised;
            FacmDesignSystem.Round(button, Math.Min(5, FacmDesignSystem.ControlRadius));
            return button;
        }

        private void ApplyBuildRows(RecommendationSection section, IReadOnlyList<LeagueBuildAdvisorRow> rows)
        {
            if (section == null) return;
            var usable = (rows ?? Array.Empty<LeagueBuildAdvisorRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Recommendation))
                .Take(3)
                .ToList()
                .AsReadOnly();
            section.Rows = usable;
            section.GuideFallback = false;
            ClearSectionVisuals(section);
            section.Value.Visible = true;
            if (usable.Count == 0)
            {
                HideRecommendationSection(section);
                return;
            }

            section.Host.Visible = true;
            section.Value.Text = usable[0].Recommendation;
            _toolTip.SetToolTip(section.Value, usable[0].Recommendation ?? string.Empty);
            if (!_actionBusy || section.Action == null)
                section.Evidence.Text = usable[0].Evidence ?? string.Empty;
            _toolTip.SetToolTip(section.Evidence, usable[0].Evidence ?? string.Empty);
            section.Evidence.ForeColor = FacmDesignSystem.TextMuted;
            section.More.Visible = usable.Count > 1;
            section.More.Text = section.Expanded
                ? CompanionText(LeagueRuntimeCompanionUiTextKeys.ShowLess)
                : CompanionText(LeagueRuntimeCompanionUiTextKeys.ShowMore) + " " + (usable.Count - 1).ToString(CultureInfo.InvariantCulture);
            RenderAlternatives(section);
        }

        private void SetAlternativesExpanded(RecommendationSection section, bool expanded)
        {
            if (section == null || section.Rows == null || section.Rows.Count <= 1) expanded = false;
            section.Expanded = expanded;
            section.More.Text = expanded
                ? CompanionText(LeagueRuntimeCompanionUiTextKeys.ShowLess)
                : CompanionText(LeagueRuntimeCompanionUiTextKeys.ShowMore) + " " + Math.Max(0, section.Rows.Count - 1).ToString(CultureInfo.InvariantCulture);
            RenderAlternatives(section);
            _sections.PerformLayout();
        }

        private void RenderAlternatives(RecommendationSection section)
        {
            if (section == null) return;
            var old = section.Alternatives.Controls.Cast<Control>().ToArray();
            section.Alternatives.Controls.Clear();
            foreach (var control in old) control.Dispose();

            if (!section.Expanded || section.Rows == null || section.Rows.Count <= 1)
            {
                section.Alternatives.Visible = false;
                section.Alternatives.Height = 0;
                section.Host.Height = RecommendationBaseHeight;
                section.Rule.Top = section.Host.Height - 1;
                return;
            }

            var alternatives = section.Rows.Skip(1).Take(2).ToArray();
            for (var index = 0; index < alternatives.Length; index++)
            {
                var row = alternatives[index];
                var y = index * AlternativeRowHeight;
                var divider = new Panel
                {
                    Location = new Point(0, y),
                    Size = new Size(214, 1),
                    BackColor = FacmDesignSystem.BorderSoft
                };
                var value = new Label
                {
                    Text = row.Recommendation ?? string.Empty,
                    Location = new Point(0, y + 3),
                    Size = new Size(214, 17),
                    AutoEllipsis = true,
                    ForeColor = FacmDesignSystem.Text,
                    BackColor = Color.Transparent,
                    Font = new Font(FacmThemeRuntime.Current.FontName, 8.2F)
                };
                var evidence = new Label
                {
                    Text = row.Evidence ?? string.Empty,
                    Location = new Point(0, y + 19),
                    Size = new Size(214, 15),
                    AutoEllipsis = true,
                    ForeColor = FacmDesignSystem.TextMuted,
                    BackColor = Color.Transparent,
                    Font = new Font(FacmThemeRuntime.Current.FontName, 7.6F)
                };
                section.Alternatives.Controls.Add(divider);
                section.Alternatives.Controls.Add(value);
                section.Alternatives.Controls.Add(evidence);
            }

            section.Alternatives.Height = alternatives.Length * AlternativeRowHeight;
            section.Alternatives.Visible = alternatives.Length > 0;
            section.Host.Height = RecommendationBaseHeight + section.Alternatives.Height;
            section.Rule.Top = section.Host.Height - 1;
        }

        private void HideRecommendationSection(RecommendationSection section)
        {
            section.Host.Visible = false;
            section.Value.Text = string.Empty;
            section.Value.Visible = true;
            ClearSectionVisuals(section);
            section.Evidence.Text = string.Empty;
            section.Rows = Array.Empty<LeagueBuildAdvisorRow>();
            section.Expanded = false;
            section.GuideFallback = false;
            section.More.Visible = false;
            section.Alternatives.Visible = false;
            section.Alternatives.Height = 0;
            section.Host.Height = RecommendationBaseHeight;
            section.Rule.Top = RecommendationBaseHeight - 1;
            if (section.Action != null) section.Action.Enabled = false;
        }

        private static void ApplyFallbackValue(RecommendationSection section, string value)
        {
            if (section == null || section.Host.Visible || string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, MayhemUiCopy.NoValue, StringComparison.Ordinal)) return;
            section.Value.Visible = true;
            section.Value.Text = value;
            section.Evidence.Text = string.Empty;
            section.Host.Visible = true;
            section.Rows = new List<LeagueBuildAdvisorRow>
            {
                new LeagueBuildAdvisorRow { Category = section.Category, Recommendation = value }
            }.AsReadOnly();
            section.GuideFallback = true;
        }

        private void ApplyGuideFallbackIcons(RecommendationSection section, IEnumerable<MayhemBuildItem> items, string fallbackText, int maxItems)
        {
            if (section == null || section.Host.Visible || items == null || maxItems <= 0)
            {
                ApplyFallbackValue(section, fallbackText);
                return;
            }

            var values = items
                .Where(item => item != null && !string.IsNullOrWhiteSpace(FirstNonEmpty(item.Name, item.Id)))
                .Take(maxItems)
                .ToList();
            if (values.Count == 0)
            {
                ApplyFallbackValue(section, fallbackText);
                return;
            }

            section.Value.Visible = false;
            section.Value.Text = string.Empty;
            ClearSectionVisuals(section);
            foreach (var item in values)
                section.Visuals.Controls.Add(CreateGuideToken(FirstNonEmpty(item.Name, item.Id), item.IconUrl, _guideSectionRenderGeneration));
            section.Visuals.Visible = section.Visuals.Controls.Count > 0;
            section.Evidence.Text = fallbackText ?? string.Empty;
            _toolTip.SetToolTip(section.Evidence, fallbackText ?? string.Empty);
            section.Host.Visible = true;
            section.Rows = new List<LeagueBuildAdvisorRow>
            {
                new LeagueBuildAdvisorRow { Category = section.Category, Recommendation = fallbackText ?? string.Empty }
            }.AsReadOnly();
            section.GuideFallback = true;
        }

        private void ApplyGuideFallbackSkills(RecommendationSection section, IEnumerable<MayhemSkillPriority> skills, string fallbackText)
        {
            if (section == null || section.Host.Visible || skills == null)
            {
                ApplyFallbackValue(section, fallbackText);
                return;
            }
            var values = skills.Where(skill => skill != null).Take(4).ToList();
            if (values.Count == 0)
            {
                ApplyFallbackValue(section, fallbackText);
                return;
            }

            section.Value.Visible = false;
            section.Value.Text = string.Empty;
            ClearSectionVisuals(section);
            foreach (var skill in values)
                section.Visuals.Controls.Add(CreateGuideToken(FirstNonEmpty(skill.Key, skill.Name), skill.IconUrl, _guideSectionRenderGeneration));
            section.Visuals.Visible = section.Visuals.Controls.Count > 0;
            section.Evidence.Text = fallbackText ?? string.Empty;
            _toolTip.SetToolTip(section.Evidence, fallbackText ?? string.Empty);
            section.Host.Visible = true;
            section.Rows = new List<LeagueBuildAdvisorRow>
            {
                new LeagueBuildAdvisorRow { Category = section.Category, Recommendation = fallbackText ?? string.Empty }
            }.AsReadOnly();
            section.GuideFallback = true;
        }

        private Control CreateGuideToken(string text, string iconReference, int generation)
        {
            var host = new Panel
            {
                Size = new Size(GuideTokenSize, GuideTokenSize),
                Margin = new Padding(0, 2, GuideTokenGap, 0),
                BackColor = FacmDesignSystem.SurfaceRaised
            };
            FacmDesignSystem.Round(host, Math.Min(4, FacmDesignSystem.ControlRadius));
            var picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            var fallback = new Label
            {
                Dock = DockStyle.Fill,
                Text = ShortGuideToken(text),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 7.5F, FontStyle.Bold)
            };
            host.Controls.Add(picture);
            host.Controls.Add(fallback);
            fallback.BringToFront();
            _toolTip.SetToolTip(host, text ?? string.Empty);
            _toolTip.SetToolTip(picture, text ?? string.Empty);
            _toolTip.SetToolTip(fallback, text ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(iconReference))
                _ = LoadGuideSectionIconAsync(picture, fallback, iconReference, generation);
            return host;
        }

        private async Task LoadGuideSectionIconAsync(PictureBox picture, Label fallback, string reference, int generation)
        {
            try
            {
                var bitmap = await _controller.LoadGuideAssetAsync(reference, _lifetime.Token);
                if (bitmap == null) return;
                if (IsDisposed || picture.IsDisposed || generation != _guideSectionRenderGeneration)
                {
                    bitmap.Dispose();
                    return;
                }
                _ownedGuideSectionIcons.Add(bitmap);
                picture.Image = bitmap;
                if (fallback != null && !fallback.IsDisposed) fallback.Visible = false;
            }
            catch (OperationCanceledException) { } catch (ObjectDisposedException) { } catch { }
        }

        private static string ShortGuideToken(string value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length <= 2) return text;
            return text.Substring(0, 1);
        }

        private static void ClearSectionVisuals(RecommendationSection section)
        {
            if (section == null || section.Visuals == null) return;
            var controls = section.Visuals.Controls.Cast<Control>().ToArray();
            section.Visuals.Controls.Clear();
            foreach (var control in controls) control.Dispose();
            section.Visuals.Visible = false;
        }

        private void ResetGuideSectionIcons()
        {
            _guideSectionRenderGeneration++;
            foreach (var section in RecommendationSections()) ClearSectionVisuals(section);
            foreach (var image in _ownedGuideSectionIcons) image.Dispose();
            _ownedGuideSectionIcons.Clear();
        }

        private void ClearGuidePresentation()
        {
            ResetGuideSectionIcons();
            _aramBalanceSection.Visible = false;
            _aramBalanceText.Text = string.Empty;
            _mayhemSection.Visible = false;
            ClearAugments();
            foreach (var section in RecommendationSections())
            {
                if (section.GuideFallback) HideRecommendationSection(section);
            }
        }

        private void ResetRecommendationSections()
        {
            ResetGuideSectionIcons();
            foreach (var section in RecommendationSections()) HideRecommendationSection(section);
            _aramBalanceSection.Visible = false;
            _aramBalanceText.Text = string.Empty;
            _mayhemSection.Visible = false;
            ClearAugments();
        }

        private static IReadOnlyList<LeagueBuildAdvisorRow> FindBuildRows(LeagueBuildRecommendation recommendation, string category)
        {
            if (recommendation == null || recommendation.Rows == null) return Array.Empty<LeagueBuildAdvisorRow>();
            return recommendation.Rows
                .Where(row => row != null && string.Equals(row.Category, category, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .AsReadOnly();
        }

        private void RenderBench(LeagueRuntimeCompanionSnapshot snapshot)
        {
            var ids = (snapshot.BenchChampionIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToArray();
            var rosterFingerprint = string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            if (!string.Equals(_benchRosterFingerprint, rosterFingerprint, StringComparison.Ordinal))
            {
                _benchRosterFingerprint = rosterFingerprint;
                _benchPage = 0;
                _benchRenderFingerprint = null;
            }
            var pages = Math.Max(1, (int)Math.Ceiling(ids.Length / (double)BenchPageSize));
            _benchPage = Math.Max(0, Math.Min(_benchPage, pages - 1));
            var fingerprint = rosterFingerprint + "|" + _benchPage + "|" + snapshot.LocalChampionId + "|" + snapshot.SwapRoute;
            if (string.Equals(_benchRenderFingerprint, fingerprint, StringComparison.Ordinal)) return;
            _benchRenderFingerprint = fingerprint;

            var old = _benchPanel.Controls.Cast<Control>().ToArray();
            _benchPanel.Controls.Clear();
            foreach (var control in old) control.Dispose();
            if (pages > 1) _benchPanel.Controls.Add(CreateBenchNavButton(false));
            foreach (var championId in ids.Skip(_benchPage * BenchPageSize).Take(BenchPageSize))
            {
                var button = CreateBenchButton(championId);
                button.Tag = new BenchTarget { ChampionId = championId, Route = snapshot.SwapRoute };
                button.Click += async delegate
                {
                    var target = button.Tag as BenchTarget;
                    if (target != null) await SwapToAsync(target.ChampionId, target.Route);
                };
                button.FlatAppearance.BorderColor = championId == snapshot.LocalChampionId ? FacmDesignSystem.Accent : FacmDesignSystem.BorderSoft;
                _toolTip.SetToolTip(button, BenchText(LeagueBenchQuickPickUiTextKeys.Tooltip) + " #" + championId.ToString(CultureInfo.InvariantCulture));
                _benchPanel.Controls.Add(button);
                _ = LoadBenchIconAsync(button, championId);
            }
            if (pages > 1) _benchPanel.Controls.Add(CreateBenchNavButton(true));
        }

        private Button CreateBenchNavButton(bool next)
        {
            var button = CreateInlineButton(next ? "›" : "‹", Point.Empty, new Size(20, 38));
            button.Margin = new Padding(0, 1, 4, 1);
            button.Click += delegate
            {
                var snapshot = _controller.CurrentSnapshot;
                var count = (snapshot.BenchChampionIds ?? Array.Empty<int>()).Count(id => id > 0);
                var pages = Math.Max(1, (int)Math.Ceiling(count / (double)BenchPageSize));
                _benchPage = next ? Math.Min(pages - 1, _benchPage + 1) : Math.Max(0, _benchPage - 1);
                _benchRenderFingerprint = null;
                RenderBench(snapshot);
            };
            _toolTip.SetToolTip(button, CompanionText(next ? LeagueRuntimeCompanionUiTextKeys.NextPage : LeagueRuntimeCompanionUiTextKeys.PreviousPage));
            return button;
        }

        private void ClearBenchButtons()
        {
            if (_benchPanel.Controls.Count == 0) return;
            var controls = _benchPanel.Controls.Cast<Control>().ToArray();
            _benchPanel.Controls.Clear();
            foreach (var control in controls) control.Dispose();
            _benchRosterFingerprint = null;
            _benchRenderFingerprint = null;
            _benchPage = 0;
        }

        private Button CreateBenchButton(int championId)
        {
            var button = new Button
            {
                Width = 44,
                Height = 38,
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
            try
            {
                var bitmap = await LoadChampionBitmapAsync(championId);
                if (bitmap == null || IsDisposed || button.IsDisposed) return;
                button.Image = bitmap;
                button.Text = string.Empty;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch
            {
            }
        }

        private void EnsureLocalChampionIcon(int championId)
        {
            if (championId <= 0 || championId == _renderedChampionIconId) return;
            _renderedChampionIconId = championId;
            DetachChampionImage();
            _ = LoadLocalChampionPictureAsync(championId);
        }

        private async Task LoadLocalChampionPictureAsync(int championId)
        {
            try
            {
                var bitmap = await LoadChampionBitmapAsync(championId);
                if (bitmap == null || IsDisposed || championId != _renderedChampionIconId) return;
                DisposeOwnedGuideIcon();
                _championIcon.Image = bitmap;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch
            {
            }
        }

        private async Task<Bitmap> LoadChampionBitmapAsync(int championId)
        {
            Bitmap cached;
            if (_championIcons.TryGetValue(championId, out cached)) return cached;
            var bytes = await _controller.LoadBenchChampionIconAsync(championId, _lifetime.Token);
            if (bytes == null || bytes.Length == 0 || IsDisposed) return null;
            var bitmap = DecodeBitmap(bytes, new Size(44, 44));
            if (bitmap == null) return null;
            _championIcons[championId] = bitmap;
            return bitmap;
        }

        private async Task SwapToAsync(int championId, LeagueBenchSwapRoute route)
        {
            if (championId <= 0 || IsDisposed) return;
            SetStatus(BenchText(LeagueBenchQuickPickUiTextKeys.Swapping), FacmDesignSystem.Accent);
            try
            {
                var result = await _controller.TrySwapAsync(championId, route, _lifetime.Token);
                if (IsDisposed) return;
                SetStatus(
                    result.Success ? BenchText(LeagueBenchQuickPickUiTextKeys.Success) : DescribeSwapFailure(result.Status),
                    result.Success ? FacmDesignSystem.Success : FacmDesignSystem.Warning);
                await _controller.RefreshAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Info("Runtime Companion quick swap failed: " + exception.Message);
                if (!IsDisposed)
                    SetStatus(BenchText(LeagueBenchQuickPickUiTextKeys.Rejected), FacmDesignSystem.Warning);
            }
        }

        private async Task LoadGuideChampionPictureAsync(string reference, int championId)
        {
            try
            {
                var bytes = await _controller.LoadGuideChampionIconAsync(reference, _lifetime.Token);
                if (bytes == null || bytes.Length == 0 || IsDisposed || championId != _renderedGuideChampionId) return;
                var bitmap = DecodeBitmap(bytes, new Size(44, 44));
                if (bitmap == null) return;
                DetachChampionImage();
                _ownedGuideIcon = bitmap;
                _championIcon.Image = bitmap;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch
            {
            }
        }

        private void DetachChampionImage()
        {
            _championIcon.Image = null;
            DisposeOwnedGuideIcon();
        }

        private void DisposeOwnedGuideIcon()
        {
            var owned = _ownedGuideIcon;
            _ownedGuideIcon = null;
            if (owned != null) owned.Dispose();
        }

        private void TogglePin()
        {
            _pinned = !_pinned;
            TopMost = _pinned;
            _pinButton.ForeColor = _pinned ? FacmDesignSystem.Accent : FacmDesignSystem.TextMuted;
            _toolTip.SetToolTip(
                _pinButton,
                CompanionText(_pinned ? LeagueRuntimeCompanionUiTextKeys.Unpin : LeagueRuntimeCompanionUiTextKeys.Pin));
            _pinButton.Invalidate();
        }

        private void SetCollapsed(bool collapsed)
        {
            if (_collapsed == collapsed) return;
            _collapsed = collapsed;
            _context.Visible = !collapsed;
            _benchHost.Visible = !collapsed && _controller.CurrentSnapshot.BenchEnabled;
            _body.Visible = !collapsed;
            _collapseButton.Text = collapsed ? "+" : "−";
            _toolTip.SetToolTip(
                _collapseButton,
                CompanionText(collapsed ? LeagueRuntimeCompanionUiTextKeys.Expand : LeagueRuntimeCompanionUiTextKeys.Collapse));
            SetFixedClientSize(
                _expandedClientWidth,
                collapsed ? _collapsedClientHeight : _expandedClientHeight);
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
            _status.Text = char.ConvertFromUtf32(0x25CF);
            _status.ForeColor = color;
            if (_toolTip != null) _toolTip.SetToolTip(_status, text ?? string.Empty);
        }

        private Button CreateChromeButton(string text, int x, int width)
        {
            var button = new Button
            {
                Text = text,
                Location = new Point(x, 4),
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
            _controller.SnapshotChanged -= HandleSnapshotChanged;
            try { _lifetime.Cancel(); }
            catch { }

            DetachChampionImage();
            ClearAugments();
            ResetGuideSectionIcons();
            foreach (var bitmap in _championIcons.Values) bitmap.Dispose();
            _championIcons.Clear();
            _toolTip.Dispose();
            _controller.Dispose();
            _lifetime.Dispose();
        }

        private string BuildWindowTitle()
        {
            return UiTextRuntime.Text(UiTextKeys.AppName) + " · " + LeagueRecommendationText.Get(_ui, LeagueRecommendationUiTextKeys.Title);
        }

        private string BenchText(string key)
        {
            return LeagueBenchQuickPickText.Get(_ui, key);
        }

        private string CompanionText(string key)
        {
            return LeagueRuntimeCompanionText.Get(_ui, key);
        }

        private string T(string key)
        {
            return LeagueAdvisorText.Get(_ui, key);
        }

        private static string BuildFingerprint(LeagueBuildAdvisorSnapshot build)
        {
            if (build == null) return null;
            return build.ChampionId.ToString(CultureInfo.InvariantCulture) + "|" +
                   (build.Mode ?? string.Empty) + "|" +
                   (build.Position ?? string.Empty) + "|" +
                   (build.Version ?? string.Empty) + "|" +
                   (build.Status ?? string.Empty);
        }

        private static string BuildContextMeta(LeagueBuildAdvisorSnapshot build)
        {
            if (build == null) return string.Empty;
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(build.Mode)) parts.Add(build.Mode.ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(build.Position) && !string.Equals(build.Position, "none", StringComparison.OrdinalIgnoreCase))
                parts.Add(build.Position.ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(build.Version)) parts.Add(build.Version);
            return string.Join(" · ", parts);
        }

        private string BuildStats(LeagueBuildRecommendation recommendation)
        {
            if (recommendation == null) return string.Empty;
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(recommendation.Tier)) parts.Add(recommendation.Tier);
            if (recommendation.Rank > 0)
                parts.Add(CompanionText(LeagueRuntimeCompanionUiTextKeys.RankShort) + " " + recommendation.Rank.ToString(CultureInfo.InvariantCulture));
            if (recommendation.WinRate.HasValue)
                parts.Add(CompanionText(LeagueRuntimeCompanionUiTextKeys.WinShort) + " " + FormatNormalizedRate(recommendation.WinRate));
            if (recommendation.PickRate.HasValue)
                parts.Add(CompanionText(LeagueRuntimeCompanionUiTextKeys.PickShort) + " " + FormatNormalizedRate(recommendation.PickRate));
            if (recommendation.BanRate.HasValue)
                parts.Add(CompanionText(LeagueRuntimeCompanionUiTextKeys.BanShort) + " " + FormatNormalizedRate(recommendation.BanRate));
            return string.Join(" · ", parts);
        }

        private static string BuildApplyContext(LeagueBuildAdvisorSnapshot snapshot)
        {
            if (snapshot == null) return string.Empty;
            var champion = string.IsNullOrWhiteSpace(snapshot.ChampionName)
                ? FormatChampionId(snapshot.ChampionId)
                : snapshot.ChampionName + " " + FormatChampionId(snapshot.ChampionId);
            return (snapshot.Phase ?? string.Empty) + " · " + champion + " · " +
                   (snapshot.Mode ?? string.Empty) + " / " + (snapshot.Position ?? string.Empty) + " · " +
                   (snapshot.Source ?? string.Empty) + " " + (snapshot.Version ?? string.Empty);
        }

        private static string BuildMayhemContextMeta(MayhemChampionResult result)
        {
            if (result == null) return MayhemUiCopy.CardSubtitle;
            var parts = new List<string> { MayhemUiCopy.CompactCardSubtitle };
            if (!string.IsNullOrWhiteSpace(result.Patch)) parts.Add(MayhemUiCopy.PatchPrefix + result.Patch);
            return string.Join(MayhemUiCopy.SeparatorDot, parts);
        }

        private static string BuildMayhemStats(MayhemChampionResult result)
        {
            if (result == null) return string.Empty;
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Tier)) parts.Add(result.Tier);
            if (result.Rank.HasValue) parts.Add(MayhemUiCopy.RankPrefix + result.Rank.Value.ToString(CultureInfo.InvariantCulture));
            if (result.WinRate.HasValue)
                parts.Add(MayhemUiCopy.Win + result.WinRate.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (result.PickRate.HasValue)
                parts.Add(MayhemUiCopy.Pick + result.PickRate.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            return string.Join(MayhemUiCopy.SeparatorDot, parts);
        }

        private static string FormatChampionId(int value)
        {
            return "#" + value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatNormalizedRate(double? rate)
        {
            if (!rate.HasValue) return string.Empty;
            var value = rate.Value;
            if (Math.Abs(value) <= 1.0) value *= 100.0;
            return value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
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

        private static string BuildMayhemItemListText(IEnumerable<MayhemBuildItem> items, int maxItems)
        {
            if (items == null || maxItems <= 0) return MayhemUiCopy.NoValue;
            var values = items
                .Where(item => item != null)
                .Select(item => FirstNonEmpty(item.Name, item.Id))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxItems)
                .ToArray();
            return values.Length == 0 ? MayhemUiCopy.NoValue : string.Join(MayhemUiCopy.CoreArrow, values);
        }

        private static IEnumerable<MayhemBuildItem> FirstMayhemCoreItems(MayhemChampionResult result)
        {
            if (result == null || result.CoreBuilds == null || result.CoreBuilds.Count == 0 || result.CoreBuilds[0] == null)
                return Array.Empty<MayhemBuildItem>();
            return result.CoreBuilds[0].Items ?? new List<MayhemBuildItem>();
        }

        private static string BuildMayhemCoreText(MayhemChampionResult result)
        {
            if (result == null) return MayhemUiCopy.NoValue;
            if (result.CoreBuilds != null && result.CoreBuilds.Count > 0 && result.CoreBuilds[0] != null)
            {
                var projected = BuildMayhemItemListText(result.CoreBuilds[0].Items, 5);
                if (!string.Equals(projected, MayhemUiCopy.NoValue, StringComparison.Ordinal)) return projected;
            }
            var legacy = (result.CoreItems ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();
            return legacy.Length == 0 ? MayhemUiCopy.NoValue : string.Join(MayhemUiCopy.CoreArrow, legacy);
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
            if (DesignWidth < 300 || DesignWidth > 340)
                throw new InvalidOperationException("Runtime Companion width left the compact design contract.");
            if (ResolveExpandedHeight(728) < MinimumExpandedHeight || ResolveExpandedHeight(728) > MaximumExpandedHeight)
                throw new InvalidOperationException("Runtime Companion 768p working-area height policy is invalid.");
            if (ResolveExpandedHeight(2160) != MaximumExpandedHeight)
                throw new InvalidOperationException("Runtime Companion maximum height cap drifted.");
            if (HeaderHeight + ContextHeight + BenchHeight >= MinimumExpandedHeight)
                throw new InvalidOperationException("Runtime Companion fixed regions leave no useful scroll body.");
            if (AugmentColumnTotalWidth > BodyContentWidth)
                throw new InvalidOperationException("Runtime Companion compact content width contract drifted.");
            if (AugmentPageSize != 5 || BenchPageSize != 4 || AugmentRowHeight < 32)
                throw new InvalidOperationException("Runtime Companion compact paging contract drifted.");
            if (new MayhemAugmentRow { Rarity = "kPrismatic" }.RarityKind != "prism" ||
                new MayhemAugmentRow { Rarity = "kGold" }.RarityKind != "gold" ||
                new MayhemAugmentRow { Rarity = "kSilver" }.RarityKind != "silver" ||
                new MayhemAugmentRow { Rarity = "kEventChoice" }.RarityKind != "other")
                throw new InvalidOperationException("Runtime Companion augment rarity mapping regressed.");
            if (RecommendationBaseHeight < 56 || SectionWidth != BodyContentWidth || AlternativeRowHeight > 48)
                throw new InvalidOperationException("Runtime Companion recommendation density contract drifted.");
            if (GuideTokenSize * 5 + GuideTokenGap * 4 > 214)
                throw new InvalidOperationException("Runtime Companion guide icon density no longer fits the compact recommendation row.");

            LeagueRuntimeCompanionController.ValidateForSmokeTest();
            if (LeagueRuntimeCompanionText.DefaultsForSmokeTest().Count < 25)
                throw new InvalidOperationException("Runtime Companion localized P0 copy is incomplete.");

            var recommendation = new LeagueBuildRecommendation
            {
                Tier = "T1",
                Rank = 3,
                WinRate = 0.5432,
                PickRate = 0.123,
                BanRate = 0.045
            };
            recommendation.Rows.Add(new LeagueBuildAdvisorRow { Category = "runes", Recommendation = "Conqueror", Evidence = "pick 60.0%" });
            recommendation.Rows.Add(new LeagueBuildAdvisorRow { Category = "runes", Recommendation = "Press the Attack", Evidence = "pick 20.0%" });
            recommendation.Rows.Add(new LeagueBuildAdvisorRow { Category = "starter-items", Recommendation = "Doran's Blade", Evidence = "pick 55.0%" });
            var build = new LeagueBuildAdvisorSnapshot
            {
                ChampionId = 58,
                ChampionName = "Renekton",
                Mode = "ranked",
                Position = "top",
                Version = "16.18",
                Status = "ready",
                Recommendation = recommendation
            };
            var meta = BuildContextMeta(build);
            if (meta.IndexOf("RANKED", StringComparison.Ordinal) < 0 ||
                meta.IndexOf("TOP", StringComparison.Ordinal) < 0 ||
                meta.IndexOf("16.18", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Runtime Companion context projection is invalid.");
            var rows = FindBuildRows(recommendation, "runes");
            if (rows.Count != 2 || rows[0].Recommendation != "Conqueror" || FindBuildRows(recommendation, "core-items").Count != 0)
                throw new InvalidOperationException("Runtime Companion alternative recommendation mapping is invalid.");
            if (!string.Equals(FormatNormalizedRate(recommendation.WinRate), "54.3%", StringComparison.Ordinal))
                throw new InvalidOperationException("Runtime Companion percentage formatting is invalid.");
            if (!string.Equals(FormatChampionId(58), "#58", StringComparison.Ordinal))
                throw new InvalidOperationException("Runtime Companion champion-id token formatting is invalid.");

            var mayhem = new MayhemChampionResult
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
            if (!string.Equals(BuildSkillText(mayhem), "Q → E → W", StringComparison.Ordinal))
                throw new InvalidOperationException("Runtime Companion Mayhem skill fallback is invalid.");
            if (!string.Equals(BuildSpellText(mayhem), "Flash + Mark", StringComparison.Ordinal))
                throw new InvalidOperationException("Runtime Companion Mayhem spell fallback is invalid.");
            mayhem.StarterItems = new List<MayhemBuildItem>
            {
                new MayhemBuildItem { Name = "Starter A" },
                new MayhemBuildItem { Name = "Starter B" }
            };
            mayhem.BootItems = new List<MayhemBuildItem>
            {
                new MayhemBuildItem { Name = "Boot A" }
            };
            mayhem.CoreBuilds = new List<MayhemBuildPath>
            {
                new MayhemBuildPath
                {
                    Rank = 1,
                    Items = new List<MayhemBuildItem>
                    {
                        new MayhemBuildItem { Name = "Core A" },
                        new MayhemBuildItem { Name = "Core B" }
                    }
                }
            };
            if (!string.Equals(BuildMayhemItemListText(mayhem.StarterItems, 3), "Starter A" + MayhemUiCopy.CoreArrow + "Starter B", StringComparison.Ordinal))
                throw new InvalidOperationException("Runtime Companion Mayhem starter-item projection is invalid.");
            if (!string.Equals(BuildMayhemItemListText(mayhem.BootItems, 2), "Boot A", StringComparison.Ordinal))
                throw new InvalidOperationException("Runtime Companion Mayhem boots projection is invalid.");
            if (!string.Equals(BuildMayhemCoreText(mayhem), "Core A" + MayhemUiCopy.CoreArrow + "Core B", StringComparison.Ordinal))
                throw new InvalidOperationException("Runtime Companion Mayhem core-build projection is invalid.");
            if (BuildMayhemStats(mayhem).IndexOf(MayhemUiCopy.RankPrefix, StringComparison.Ordinal) < 0 ||
                BuildMayhemStats(mayhem).IndexOf(MayhemUiCopy.Win, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Runtime Companion Mayhem headline statistics are incomplete.");
            if (ShouldShowAramBaseBalance(mayhem))
                throw new InvalidOperationException("Runtime Companion rendered an empty ARAM balance section.");
            mayhem.BaseBalanceSummary = "基础 ARAM（完整）：造成伤害 +5%"; // ui-text-contract: allow
            if (!ShouldShowAramBaseBalance(mayhem))
                throw new InvalidOperationException("Runtime Companion lost available ARAM balance presentation.");
        }
    }
}
