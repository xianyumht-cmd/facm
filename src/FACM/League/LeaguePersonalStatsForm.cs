using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Online;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeaguePersonalStatsForm : Form
    {
        private readonly LeaguePersonalStatsModule _module;
        private readonly AppSettings _settings;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private readonly Label _accountsValue;
        private readonly Label _daysValue;
        private readonly Label _memberValue;
        private readonly Label _activityValue;
        private readonly Label _percentileValue;
        private readonly Label _statusValue;
        private readonly Label[] _historyRows;
        private readonly Label[] _metricCaptions = new Label[3];
        private readonly Panel _scrollArea;
        private readonly Panel _pageContent;
        private readonly Label _titleLabel;
        private readonly FacmActionButton _accountButton;
        private readonly Label _hintLabel;
        private readonly FacmGlassPanel _summaryPanel;
        private readonly FacmGlassPanel _historyPanel;
        private readonly FacmGlassPanel _rankingPanel;
        private readonly FacmGlassPanel _preferencesPanel;
        private readonly FacmToggleSwitch _localToggle;
        private readonly FacmToggleSwitch _rankingToggle;
        private readonly FacmToggleSwitch _telemetryToggle;
        private readonly FacmActionButton _refreshButton;
        private bool _applying;
        private bool _savingPreferences;
        private bool _openingAccountDialog;

        public LeaguePersonalStatsForm(
            LeaguePersonalStatsModule module,
            AppSettings settings,
            UiTextCatalog ui)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _historyRows = new Label[4];

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = _ui.Get(UiTextKeys.LeaguePersonalStatsWindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 560);
            MinimumSize = new Size(650, 540);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            _pageContent = new Panel
            {
                Location = Point.Empty,
                Size = new Size(720, 584),
                BackColor = FacmDesignSystem.Canvas
            };
            _scrollArea = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = FacmDesignSystem.Canvas
            };

            _titleLabel = new Label
            {
                Text = _ui.Get(UiTextKeys.LeaguePersonalStatsTitle),
                Location = new Point(28, 16),
                Size = new Size(440, 30),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 17F, FontStyle.Bold)
            };
            _hintLabel = new Label
            {
                Text = _ui.Get(UiTextKeys.LeaguePersonalStatsHint),
                Location = new Point(30, 50),
                Size = new Size(630, 18),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent
            };
            _activityValue = new Label
            {
                Location = new Point(30, 68),
                Size = new Size(630, 18),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 8F),
                AutoEllipsis = true
            };
            _accountButton = new FacmActionButton
            {
                Text = GgmanAccountSession.Current == null
                    ? _ui.Get(UiTextKeys.AccountMenu)
                    : _ui.Get(UiTextKeys.AccountTitle),
                Bounds = new Rectangle(530, 15, 160, 32),
                Tone = FacmButtonTone.Secondary,
                Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold)
            };
            _accountButton.Click += delegate { OpenAccountDialog(); };
            _pageContent.Controls.Add(_accountButton);
            _pageContent.Controls.Add(_titleLabel);
            _pageContent.Controls.Add(_hintLabel);
            _pageContent.Controls.Add(_activityValue);

            _summaryPanel = CreatePanel(new Rectangle(28, 88, 664, 72));
            _accountsValue = AddMetric(_summaryPanel, UiTextKeys.LeaguePersonalStatsAccounts, 14, 0);
            _daysValue = AddMetric(_summaryPanel, UiTextKeys.LeaguePersonalStatsActiveDays, 230, 1);
            _memberValue = AddMetric(_summaryPanel, UiTextKeys.LeaguePersonalStatsMemberSince, 446, 2);
            _pageContent.Controls.Add(_summaryPanel);

            _historyPanel = CreatePanel(new Rectangle(28, 170, 664, 128));
            _historyPanel.Controls.Add(CreateCaption(
                _ui.Get(UiTextKeys.LeaguePersonalStatsAccounts),
                new Point(16, 10),
                240));
            for (var index = 0; index < _historyRows.Length; index++)
            {
                var row = new Label
                {
                    Location = new Point(16, 32 + index * 23),
                    Size = new Size(632, 21),
                    ForeColor = FacmDesignSystem.Text,
                    BackColor = Color.Transparent,
                    Font = new Font(Font.FontFamily, 8.5F),
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _historyRows[index] = row;
                _historyPanel.Controls.Add(row);
            }
            _pageContent.Controls.Add(_historyPanel);

            _rankingPanel = CreatePanel(new Rectangle(28, 306, 664, 84));
            _rankingPanel.Controls.Add(CreateCaption(
                _ui.Get(UiTextKeys.LeaguePersonalStatsRanking),
                new Point(16, 10),
                240));
            _percentileValue = CreateValue(new Point(16, 34), 630, 13F);
            _rankingPanel.Controls.Add(_percentileValue);
            _pageContent.Controls.Add(_rankingPanel);

            _preferencesPanel = CreatePanel(new Rectangle(28, 398, 664, 130));
            _localToggle = new FacmToggleSwitch
            {
                Text = _ui.Get(UiTextKeys.LeaguePersonalStatsLocalToggle),
                Location = new Point(16, 7),
                Size = new Size(632, 30)
            };
            _rankingToggle = new FacmToggleSwitch
            {
                Text = _ui.Get(UiTextKeys.LeaguePersonalStatsRankingToggle),
                Location = new Point(16, 37),
                Size = new Size(632, 30)
            };
            _telemetryToggle = new FacmToggleSwitch
            {
                Text = _ui.Get(UiTextKeys.LeaguePersonalStatsTelemetryToggle),
                Location = new Point(16, 67),
                Size = new Size(632, 30)
            };
            _refreshButton = new FacmActionButton
            {
                Text = _ui.Get(UiTextKeys.LeaguePersonalStatsRefresh),
                Bounds = new Rectangle(548, 98, 100, 26),
                Tone = FacmButtonTone.Secondary,
                Font = new Font(Font.FontFamily, 8.2F, FontStyle.Bold)
            };
            _preferencesPanel.Controls.Add(_localToggle);
            _preferencesPanel.Controls.Add(_rankingToggle);
            _preferencesPanel.Controls.Add(_telemetryToggle);
            _preferencesPanel.Controls.Add(_refreshButton);
            _pageContent.Controls.Add(_preferencesPanel);

            _statusValue = new Label
            {
                Location = new Point(30, 532),
                Size = new Size(660, 18),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 8F)
            };
            _pageContent.Controls.Add(_statusValue);
            _scrollArea.Controls.Add(_pageContent);
            Controls.Add(_scrollArea);
            _scrollArea.ClientSizeChanged += delegate { LayoutPersonalStatsPage(); };
            LayoutPersonalStatsPage();

            _localToggle.CheckedChanged += HandlePreferenceChanged;
            _rankingToggle.CheckedChanged += HandlePreferenceChanged;
            _telemetryToggle.CheckedChanged += HandleTelemetryChanged;
            _refreshButton.Click += async delegate { await RefreshRankingAsync(); };

            _module.StatsChanged += HandleStatsChanged;
            Shown += async delegate
            {
                ApplySnapshot();
                ApplyTelemetryState();
                if (_settings.LeagueCloudRankingEnabled) await RefreshRankingAsync();
            };
            FormClosed += delegate
            {
                _module.StatsChanged -= HandleStatsChanged;
                if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
                _lifetime.Dispose();
            };

            ApplySnapshot();
            ApplyTelemetryState();
        }

        private void OpenAccountDialog()
        {
            if (_openingAccountDialog || IsDisposed) return;
            _openingAccountDialog = true;
            _accountButton.Enabled = false;
            AppLog.Info("GGman account entry clicked.");
            var owner = TopLevelControl as Form;
            if (owner == this || owner == null || owner.IsDisposed || !owner.Visible || !owner.TopLevel)
                owner = null;
            try
            {
                using (var dialog = new GgmanAccountForm(_ui))
                {
                    dialog.Shown += delegate { AppLog.Info("GGman account dialog shown."); };
                    if (owner != null)
                        dialog.ShowDialog(owner);
                    else
                        dialog.ShowDialog();
                }
            }
            catch (Exception error)
            {
                AppLog.Warning("GGman account dialog failed; type=" + error.GetType().Name +
                    "; hresult=" + error.HResult.ToString("X8") + ".");
                MessageBox.Show(owner != null ? (IWin32Window)owner : this,
                    string.Format(_ui.Get(UiTextKeys.AccountError), "无法打开账号窗口，请查看 GGman 运行日志。"),
                    _ui.Get(UiTextKeys.AccountTitle), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _openingAccountDialog = false;
                if (!IsDisposed)
                {
                    _accountButton.Enabled = true;
                    _accountButton.Text = GgmanAccountSession.Current == null
                        ? _ui.Get(UiTextKeys.AccountMenu)
                        : _ui.Get(UiTextKeys.AccountTitle);
                }
            }
        }

        private Label AddMetric(Control parent, string key, int left, int index)
        {
            var caption = CreateCaption(_ui.Get(key), new Point(left, 7), 190);
            _metricCaptions[index] = caption;
            parent.Controls.Add(caption);
            var value = CreateValue(new Point(left, 29), 190, 18F);
            parent.Controls.Add(value);
            return value;
        }

        internal static int ResolvePageWidthForSmokeTest(int viewportWidth)
        {
            return Math.Max(480, viewportWidth - 2);
        }

        internal static Rectangle ResolveMetricBoundsForSmokeTest(int viewportWidth, int index)
        {
            var cardWidth = ResolvePageWidthForSmokeTest(viewportWidth) - 56;
            var slotWidth = (cardWidth - 28) / 3;
            return new Rectangle(14 + index * slotWidth, 29, slotWidth - 8, 40);
        }

        private void LayoutPersonalStatsPage()
        {
            if (_scrollArea == null || _scrollArea.IsDisposed || _pageContent == null || _pageContent.IsDisposed)
                return;
            var pageWidth = ResolvePageWidthForSmokeTest(_scrollArea.ClientSize.Width);
            var cardWidth = pageWidth - 56;

            _pageContent.SuspendLayout();
            try
            {
                _pageContent.Width = pageWidth;
                _accountButton.Left = pageWidth - _accountButton.Width - 30;
                _titleLabel.Width = Math.Max(160, _accountButton.Left - _titleLabel.Left - 12);
                _hintLabel.Width = pageWidth - 60;
                _activityValue.Width = pageWidth - 60;
                _summaryPanel.Width = cardWidth;
                _historyPanel.Width = cardWidth;
                _rankingPanel.Width = cardWidth;
                _preferencesPanel.Width = cardWidth;
                for (var index = 0; index < _metricCaptions.Length; index++)
                {
                    var bounds = ResolveMetricBoundsForSmokeTest(_scrollArea.ClientSize.Width, index);
                    _metricCaptions[index].SetBounds(bounds.Left, 7, bounds.Width, 20);
                    var value = index == 0 ? _accountsValue : index == 1 ? _daysValue : _memberValue;
                    value.Bounds = bounds;
                }

                foreach (var row in _historyRows)
                    row.Width = cardWidth - 32;
                _percentileValue.Width = cardWidth - 32;
                _localToggle.Width = cardWidth - 32;
                _rankingToggle.Width = cardWidth - 32;
                _telemetryToggle.Width = cardWidth - 32;
                _refreshButton.Left = cardWidth - _refreshButton.Width - 16;
                _statusValue.Width = cardWidth - 4;
            }
            finally
            {
                _pageContent.ResumeLayout(false);
            }
        }

        internal static void ValidateForSmokeTest()
        {
            foreach (var viewportWidth in new[] { 420, 480, 560, 680, 720, 1120 })
            {
                var pageWidth = ResolvePageWidthForSmokeTest(viewportWidth);
                var cardWidth = pageWidth - 56;
                if (pageWidth < 480 || cardWidth < 424 ||
                    (viewportWidth >= 482 && pageWidth > viewportWidth))
                    throw new InvalidOperationException("Personal stats content width does not fit its viewport.");

                var previousRight = 0;
                for (var index = 0; index < 3; index++)
                {
                    var metric = ResolveMetricBoundsForSmokeTest(viewportWidth, index);
                    if (metric.Width < 90 || metric.Left < previousRight || metric.Right > cardWidth - 10)
                        throw new InvalidOperationException("Personal stats summary metrics overlap or overflow.");
                    previousRight = metric.Right;
                }
                if (cardWidth - 32 <= 0 || cardWidth - 100 - 16 <= 0)
                    throw new InvalidOperationException("Personal stats history or privacy controls lost their usable width.");
            }
            if (ResolvePageWidthForSmokeTest(420) - 160 - 30 <= 28 + 160)
                throw new InvalidOperationException("GGman account entry overlaps the page title at narrow width.");
            if (398 + 130 > 532 || 532 + 18 > 584)
                throw new InvalidOperationException("Personal stats status or privacy controls are vertically clipped.");
        }

        private static FacmGlassPanel CreatePanel(Rectangle bounds)
        {
            return new FacmGlassPanel
            {
                Bounds = bounds,
                Radius = FacmDesignSystem.CardRadius,
                DrawBorder = true,
                BackColor = FacmDesignSystem.Surface
            };
        }

        private static Label CreateCaption(string text, Point location, int width)
        {
            return new Label
            {
                Text = text,
                Location = location,
                Size = new Size(width, 20),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8.2F, FontStyle.Bold)
            };
        }

        private static Label CreateValue(Point location, int width, float size)
        {
            return new Label
            {
                Location = location,
                Size = new Size(width, 40),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, size, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        private void HandleStatsChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(ApplySnapshot)); }
                catch { }
                return;
            }
            ApplySnapshot();
        }

        private async void HandlePreferenceChanged(object sender, EventArgs e)
        {
            if (_applying || _savingPreferences || IsDisposed) return;
            _savingPreferences = true;
            try
            {
                var local = _localToggle.Checked;
                var ranking = local && _rankingToggle.Checked;
                _rankingToggle.Enabled = local;
                await _module.ApplyPreferencesAsync(local, ranking, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _savingPreferences = false;
                if (!IsDisposed) ApplySnapshot();
            }
        }

        private void HandleTelemetryChanged(object sender, EventArgs e)
        {
            if (_applying || IsDisposed) return;
            UsageTelemetryModule.SetEnabled(_telemetryToggle.Checked);
        }

        private void ApplyTelemetryState()
        {
            _applying = true;
            try
            {
                _telemetryToggle.Checked = UsageTelemetryModule.IsEnabled();
            }
            finally
            {
                _applying = false;
            }
        }

        private async Task RefreshRankingAsync()
        {
            if (_savingPreferences || IsDisposed || _lifetime.IsCancellationRequested) return;
            _refreshButton.Enabled = false;
            try
            {
                await _module.RefreshCloudStateAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (!IsDisposed) _refreshButton.Enabled = true;
            }
        }

        private void ApplySnapshot()
        {
            var snapshot = _module.GetSnapshot();
            _accountsValue.Text = snapshot.PlayedAccounts.ToString();
            _daysValue.Text = snapshot.ActiveDays.ToString();
            _memberValue.Text = snapshot.FirstSeenUtc.HasValue
                ? snapshot.FirstSeenUtc.Value.ToLocalTime().ToString("yyyy-MM-dd")
                : "—";
            _activityValue.Text = string.Format(
                _ui.Get(UiTextKeys.LeaguePersonalStatsActivityFormat),
                snapshot.CurrentStreakDays,
                snapshot.Recent7ActiveDays,
                snapshot.Recent30ActiveDays,
                snapshot.NewAccountsThisMonth);
            RenderHistory(snapshot.RecentAccounts);

            if (!snapshot.CloudRankingEnabled)
            {
                _percentileValue.Text = _ui.Get(UiTextKeys.LeaguePersonalStatsRankingDisabled);
            }
            else if (snapshot.CloudRank <= 0 || snapshot.CloudRankedUsers <= 0)
            {
                _percentileValue.Text = _ui.Get(UiTextKeys.LeaguePersonalStatsRankingWaiting);
            }
            else
            {
                _percentileValue.Text = string.Format(
                    _ui.Get(UiTextKeys.LeaguePersonalStatsPercentileFormat),
                    snapshot.CloudPercentile);
            }

            _applying = true;
            try
            {
                _localToggle.Checked = snapshot.PersonalStatsEnabled;
                _rankingToggle.Checked = snapshot.PersonalStatsEnabled && snapshot.CloudRankingEnabled;
                _rankingToggle.Enabled = snapshot.PersonalStatsEnabled;
                _refreshButton.Enabled = snapshot.PersonalStatsEnabled && snapshot.CloudRankingEnabled && !_savingPreferences;
            }
            finally
            {
                _applying = false;
            }

            _statusValue.Text = snapshot.PersonalStatsEnabled
                ? string.Empty
                : _ui.Get(UiTextKeys.LeaguePersonalStatsPaused);
        }

        private void RenderHistory(IReadOnlyList<LeaguePersonalStatsAccountView> accounts)
        {
            for (var index = 0; index < _historyRows.Length; index++)
            {
                _historyRows[index].Text = string.Empty;
            }

            if (accounts == null || accounts.Count == 0)
            {
                _historyRows[0].Text = _ui.Get(UiTextKeys.LeagueDashboardUnknown);
                return;
            }

            for (var index = 0; index < _historyRows.Length && index < accounts.Count; index++)
            {
                var account = accounts[index];
                var name = string.IsNullOrWhiteSpace(account.DisplayName)
                    ? _ui.Get(UiTextKeys.LeagueDashboardUnknown)
                    : account.DisplayName;
                var region = string.IsNullOrWhiteSpace(account.Region)
                    ? _ui.Get(UiTextKeys.LeagueDashboardUnknown)
                    : account.Region;
                var anonymousId = string.IsNullOrWhiteSpace(account.AnonymousId)
                    ? _ui.Get(UiTextKeys.LeagueDashboardUnknown)
                    : account.AnonymousId;
                var firstSeen = account.FirstSeenUtc.HasValue
                    ? account.FirstSeenUtc.Value.ToLocalTime().ToString("MM-dd")
                    : "—";
                var lastSeen = account.LastSeenUtc.HasValue
                    ? account.LastSeenUtc.Value.ToLocalTime().ToString("MM-dd HH:mm")
                    : "—";

                _historyRows[index].Text = string.Format(
                    "{0}  ·  {1}  ·  #{2}  ·  ×{3}  ·  {4} → {5}",
                    name,
                    region,
                    anonymousId,
                    Math.Max(1, account.SeenCount),
                    firstSeen,
                    lastSeen);
            }
        }
    }
}
