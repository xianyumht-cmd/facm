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
    /// Narrow Champion Select Runtime Companion presentation surface.
    ///
    /// The Form renders controller snapshots. It owns no Gameflow reader, OP.GG transport, Mayhem
    /// request orchestration or raw LCU/filesystem write path. Existing FACM owners remain authoritative.
    /// </summary>
    internal sealed class LeagueRuntimeCompanionForm : Form
    {
        internal const int DesignWidth = 388;
        internal const int HeaderHeight = 42;
        internal const int ContextHeight = 96;
        internal const int BenchHeight = 70;
        internal const int MinimumExpandedHeight = 480;
        internal const int MaximumExpandedHeight = 720;
        internal const int BodyContentWidth = 348;
        internal const int AugmentColumnTotalWidth = 334;
        private const int SectionWidth = 348;
        private const int RecommendationBaseHeight = 74;
        private const int AlternativeRowHeight = 42;

        private readonly LeagueRuntimeCompanionController _controller;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly System.Windows.Forms.Timer _pollTimer;
        private readonly ToolTip _toolTip;
        private readonly Dictionary<int, Bitmap> _championIcons = new Dictionary<int, Bitmap>();

        private readonly Panel _header;
        private readonly Label _title;
        private readonly Label _status;
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
        private readonly ListView _augments;

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
                Size = new Size(126, HeaderHeight),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.Text,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
            };
            _status = new Label
            {
                Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.BuildWaiting),
                Location = new Point(138, 0),
                Size = new Size(144, HeaderHeight),
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.TextMuted
            };
            _pinButton = CreateChromeButton("↑", 284, 30);
            _pinButton.ForeColor = FacmDesignSystem.Accent;
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
                Location = new Point(10, 13),
                Size = new Size(52, 52),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = FacmDesignSystem.SurfaceRaised
            };
            FacmDesignSystem.Round(_championIcon, FacmDesignSystem.ControlRadius);
            _championTitle = new Label
            {
                Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.ChampionWaiting),
                Location = new Point(74, 5),
                Size = new Size(294, 27),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 12F, FontStyle.Bold)
            };
            _championMeta = new Label
            {
                Text = string.Empty,
                Location = new Point(74, 32),
                Size = new Size(294, 18),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F)
            };
            _championStats = new Label
            {
                Text = string.Empty,
                Location = new Point(74, 50),
                Size = new Size(294, 18),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F)
            };
            _contextStatus = new Label
            {
                Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.BuildWaiting),
                Location = new Point(74, 70),
                Size = new Size(294, 18),
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
                Padding = new Padding(10, 6, 10, 6),
                Visible = false
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
            _benchHost.Controls.Add(_benchPanel);
            _benchHost.Controls.Add(benchLabel);

            _body = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = FacmDesignSystem.Canvas,
                Padding = new Padding(10, 8, 10, 10)
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
                T(LeagueBuildApplyUiTextKeys.Apply));
            _spells = CreateRecommendationSection(
                "summoner-spells",
                LeagueRecommendationText.Get(_ui, LeagueRecommendationUiTextKeys.Spells),
                T(LeagueBuildApplyUiTextKeys.Apply));
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
                CompanionText(LeagueRuntimeCompanionUiTextKeys.ImportItems));

            _runes.Action.Click += async delegate { await ApplyLoadoutAsync(LeagueRuntimeCompanionApplyTarget.Runes, _runes); };
            _spells.Action.Click += async delegate { await ApplyLoadoutAsync(LeagueRuntimeCompanionApplyTarget.SummonerSpells, _spells); };
            _core.Action.Click += async delegate { await ImportItemSetAsync(); };

            foreach (var section in RecommendationSections()) _sections.Controls.Add(section.Host);

            _aramBalanceSection = new Panel
            {
                Width = SectionWidth,
                Height = 70,
                Margin = Padding.Empty,
                BackColor = FacmDesignSystem.Canvas,
                Visible = false
            };
            var aramBalanceTitle = new Label
            {
                Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.AramBaseBalance),
                Location = new Point(0, 9),
                Size = new Size(82, 20),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F, FontStyle.Bold)
            };
            _aramBalanceText = new Label
            {
                Text = string.Empty,
                Location = new Point(82, 5),
                Size = new Size(266, 56),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8.2F)
            };
            var aramBalanceRule = new Panel
            {
                Location = new Point(0, 69),
                Size = new Size(SectionWidth, 1),
                BackColor = FacmDesignSystem.BorderSoft
            };
            _aramBalanceSection.Controls.Add(aramBalanceTitle);
            _aramBalanceSection.Controls.Add(_aramBalanceText);
            _aramBalanceSection.Controls.Add(aramBalanceRule);
            _sections.Controls.Add(_aramBalanceSection);

            _mayhemSection = new Panel
            {
                Width = SectionWidth,
                Height = 310,
                Margin = Padding.Empty,
                BackColor = FacmDesignSystem.Canvas,
                Visible = false
            };
            var augmentTitle = new Label
            {
                Text = CompanionText(LeagueRuntimeCompanionUiTextKeys.MayhemAugments),
                Location = new Point(0, 10),
                Size = new Size(SectionWidth, 24),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
            };
            var augmentRule = new Panel
            {
                Location = new Point(0, 38),
                Size = new Size(SectionWidth, 1),
                BackColor = FacmDesignSystem.BorderSoft
            };
            _augments = new ListView
            {
                Location = new Point(0, 48),
                Size = new Size(SectionWidth, 252),
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text,
                BorderStyle = BorderStyle.None,
                ShowItemToolTips = true
            };
            _augments.Columns.Add("#", 30, HorizontalAlignment.Right);
            _augments.Columns.Add(MayhemUiCopy.PriorityAugment, 112, HorizontalAlignment.Left);
            _augments.Columns.Add(MayhemUiCopy.MetricQuality, 42, HorizontalAlignment.Left);
            _augments.Columns.Add(MayhemUiCopy.HeroWinRate, 48, HorizontalAlignment.Right);
            _augments.Columns.Add(MayhemUiCopy.PickRate, 48, HorizontalAlignment.Right);
            _augments.Columns.Add(MayhemUiCopy.Sample, 54, HorizontalAlignment.Right);
            _mayhemSection.Controls.Add(augmentTitle);
            _mayhemSection.Controls.Add(augmentRule);
            _mayhemSection.Controls.Add(_augments);
            _sections.Controls.Add(_mayhemSection);
            _body.Controls.Add(_sections);

            Controls.Add(_body);
            Controls.Add(_benchHost);
            Controls.Add(_context);
            Controls.Add(_header);

            _toolTip = new ToolTip { ShowAlways = true, AutomaticDelay = 120 };
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
            var available = Math.Max(MinimumExpandedHeight, workingAreaHeight - 40);
            return Math.Max(MinimumExpandedHeight, Math.Min(MaximumExpandedHeight, available));
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
                RenderGuideFallbackAndAugments(snapshot.Guide, snapshot.Build != null);
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
            RenderAramBaseBalance(result);
            if (!hasBuildContext)
            {
                _championTitle.Text = FirstNonEmpty(result.ChampionName, result.Query, MayhemUiCopy.Unknown);
                _championMeta.Text = BuildMayhemMeta(result);
                _championStats.Text = string.Empty;
                _contextStatus.ForeColor = FacmDesignSystem.Success;
                _contextStatus.Text = MayhemUiCopy.Completed;
                ApplyFallbackValue(_skills, BuildSkillText(result));
                ApplyFallbackValue(_spells, BuildSpellText(result));
                ApplyFallbackValue(_core, BuildItemText(result));
                if (_renderedChampionIconId <= 0 && !string.IsNullOrWhiteSpace(result.ChampionIconUrl))
                    _ = LoadGuideChampionPictureAsync(result.ChampionIconUrl, _renderedGuideChampionId);
            }

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
            _mayhemSection.Visible = _augments.Items.Count > 0;
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
                _championTitle.Text = FormatChampionId(championId);
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
                Location = new Point(82, 6),
                Size = new Size(actionText == null ? 266 : 200, 36),
                AutoEllipsis = false,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8.8F)
            };
            var evidence = new Label
            {
                Location = new Point(82, 43),
                Size = new Size(206, 18),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 7.8F)
            };
            Button action = null;
            if (!string.IsNullOrWhiteSpace(actionText))
            {
                action = CreateInlineButton(actionText, new Point(286, 8), new Size(62, 28));
                host.Controls.Add(action);
            }
            var more = CreateInlineButton(string.Empty, new Point(294, 42), new Size(54, 22));
            more.Font = new Font(FacmThemeRuntime.Current.FontName, 7.6F);
            more.Visible = false;
            var alternatives = new Panel
            {
                Location = new Point(82, RecommendationBaseHeight - 2),
                Size = new Size(266, 0),
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
                Evidence = evidence,
                Action = action,
                More = more,
                Alternatives = alternatives,
                Rule = rule
            };
            more.Click += delegate { SetAlternativesExpanded(section, !section.Expanded); };

            host.Controls.Add(caption);
            host.Controls.Add(value);
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
            if (usable.Count == 0)
            {
                HideRecommendationSection(section);
                return;
            }

            section.Host.Visible = true;
            section.Value.Text = usable[0].Recommendation;
            if (!_actionBusy || section.Action == null)
                section.Evidence.Text = usable[0].Evidence ?? string.Empty;
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
                    Size = new Size(266, 1),
                    BackColor = FacmDesignSystem.BorderSoft
                };
                var value = new Label
                {
                    Text = row.Recommendation ?? string.Empty,
                    Location = new Point(0, y + 3),
                    Size = new Size(266, 19),
                    AutoEllipsis = true,
                    ForeColor = FacmDesignSystem.Text,
                    BackColor = Color.Transparent,
                    Font = new Font(FacmThemeRuntime.Current.FontName, 8.2F)
                };
                var evidence = new Label
                {
                    Text = row.Evidence ?? string.Empty,
                    Location = new Point(0, y + 22),
                    Size = new Size(266, 17),
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
            section.Value.Text = value;
            section.Evidence.Text = string.Empty;
            section.Host.Visible = true;
            section.Rows = new List<LeagueBuildAdvisorRow>
            {
                new LeagueBuildAdvisorRow { Category = section.Category, Recommendation = value }
            }.AsReadOnly();
            section.GuideFallback = true;
        }

        private void ClearGuidePresentation()
        {
            _aramBalanceSection.Visible = false;
            _aramBalanceText.Text = string.Empty;
            _mayhemSection.Visible = false;
            _augments.Items.Clear();
            foreach (var section in RecommendationSections())
            {
                if (section.GuideFallback) HideRecommendationSection(section);
            }
        }

        private void ResetRecommendationSections()
        {
            foreach (var section in RecommendationSections()) HideRecommendationSection(section);
            _aramBalanceSection.Visible = false;
            _aramBalanceText.Text = string.Empty;
            _mayhemSection.Visible = false;
            _augments.Items.Clear();
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
            var ids = snapshot.BenchChampionIds ?? Array.Empty<int>();
            var wanted = new HashSet<int>(ids.Where(id => id > 0));
            var existing = _benchPanel.Controls.OfType<Button>()
                .Where(button => button.Tag is BenchTarget)
                .ToDictionary(button => ((BenchTarget)button.Tag).ChampionId, button => button);

            foreach (var pair in existing)
            {
                if (wanted.Contains(pair.Key)) continue;
                _benchPanel.Controls.Remove(pair.Value);
                pair.Value.Dispose();
            }

            foreach (var championId in ids.Where(id => id > 0))
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

                button.Tag = new BenchTarget { ChampionId = championId, Route = snapshot.SwapRoute };
                _toolTip.SetToolTip(
                    button,
                    BenchText(LeagueBenchQuickPickUiTextKeys.Tooltip) + " #" + championId.ToString(CultureInfo.InvariantCulture));
                button.FlatAppearance.BorderColor = championId == snapshot.LocalChampionId
                    ? FacmDesignSystem.Accent
                    : FacmDesignSystem.BorderSoft;
            }
        }

        private void ClearBenchButtons()
        {
            if (_benchPanel.Controls.Count == 0) return;
            var controls = _benchPanel.Controls.Cast<Control>().ToArray();
            _benchPanel.Controls.Clear();
            foreach (var control in controls) control.Dispose();
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
            var bitmap = DecodeBitmap(bytes, new Size(52, 52));
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
                var bitmap = DecodeBitmap(bytes, new Size(52, 52));
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

        private static string BuildMayhemMeta(MayhemChampionResult result)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Tier)) parts.Add(result.Tier);
            if (result.Rank.HasValue) parts.Add(MayhemUiCopy.RankPrefix + result.Rank.Value.ToString(CultureInfo.InvariantCulture));
            if (result.WinRate.HasValue)
                parts.Add(MayhemUiCopy.Win + result.WinRate.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (!string.IsNullOrWhiteSpace(result.Patch)) parts.Add(MayhemUiCopy.PatchPrefix + result.Patch);
            return parts.Count == 0 ? MayhemUiCopy.CardSubtitle : string.Join(" · ", parts);
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
            if (DesignWidth < 360 || DesignWidth > 430)
                throw new InvalidOperationException("Runtime Companion width left the compact design contract.");
            if (ResolveExpandedHeight(728) < MinimumExpandedHeight || ResolveExpandedHeight(728) > MaximumExpandedHeight)
                throw new InvalidOperationException("Runtime Companion 768p working-area height policy is invalid.");
            if (ResolveExpandedHeight(2160) != MaximumExpandedHeight)
                throw new InvalidOperationException("Runtime Companion maximum height cap drifted.");
            if (HeaderHeight + ContextHeight + BenchHeight >= MinimumExpandedHeight)
                throw new InvalidOperationException("Runtime Companion fixed regions leave no useful scroll body.");
            if (AugmentColumnTotalWidth > BodyContentWidth)
                throw new InvalidOperationException("Runtime Companion augment table would require horizontal scrolling at the design width.");
            if (RecommendationBaseHeight < 56 || SectionWidth != BodyContentWidth || AlternativeRowHeight > 48)
                throw new InvalidOperationException("Runtime Companion recommendation density contract drifted.");

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
            if (ShouldShowAramBaseBalance(mayhem))
                throw new InvalidOperationException("Runtime Companion rendered an empty ARAM balance section.");
            mayhem.BaseBalanceSummary = "基础 ARAM（完整）：造成伤害 +5%"; // ui-text-contract: allow
            if (!ShouldShowAramBaseBalance(mayhem))
                throw new InvalidOperationException("Runtime Companion lost available ARAM balance presentation.");
        }
    }
}
