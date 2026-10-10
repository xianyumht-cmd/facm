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
        private readonly EscSettingsForm _escPanel;
        private readonly UiTextEditorPanel _textEditor;
        private readonly GgmanUnifiedSyncPanel _cloudCenter;
        private readonly FacmActionButton _refreshButton;
        private readonly FacmActionButton _privacyButton;
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
                    : _ui.Get(UiTextKeys.AccountManage),
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

            _rankingPanel = CreatePanel(new Rectangle(28, 306, 664, 122));
            _rankingPanel.Controls.Add(CreateCaption(
                _ui.Get(UiTextKeys.LeaguePersonalStatsRanking),
                new Point(16, 10),
                240));
            _percentileValue = CreateValue(new Point(16, 34), 630, 13F);
            _rankingPanel.Controls.Add(_percentileValue);
            _privacyButton = new FacmActionButton
            {
                Text = _ui.Get(UiTextKeys.RegisteredStatsPrivacy),
                Bounds = new Rectangle(430, 84, 105, 28),
                Tone = FacmButtonTone.Secondary
            };
            _refreshButton = new FacmActionButton
            {
                Text = _ui.Get(UiTextKeys.LeaguePersonalStatsRefresh),
                Bounds = new Rectangle(544, 84, 104, 28),
                Tone = FacmButtonTone.Secondary
            };
            _rankingPanel.Controls.Add(_privacyButton);
            _rankingPanel.Controls.Add(_refreshButton);
            _pageContent.Controls.Add(_rankingPanel);

            _escPanel = new EscSettingsForm(_ui, _settings.GamePath)
            {
                TopLevel = false,
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(28, 454),
                BackColor = FacmDesignSystem.Canvas
            };
            _pageContent.Controls.Add(_escPanel);
            _textEditor = new UiTextEditorPanel(_ui)
            {
                Location = new Point(28, 880)
            };
            _textEditor.ExpandedHeightChanged += delegate { LayoutPersonalStatsPage(); };
            _pageContent.Controls.Add(_textEditor);
            _cloudCenter = new GgmanUnifiedSyncPanel(_ui, _settings, _escPanel, _textEditor);
            _pageContent.Controls.Add(_cloudCenter);

            _scrollArea.Controls.Add(_pageContent);
            Controls.Add(_scrollArea);
            _scrollArea.ClientSizeChanged += delegate { LayoutPersonalStatsPage(); };
            LayoutPersonalStatsPage();

            _privacyButton.Click += delegate { OpenPrivacyDialog(); };
            _refreshButton.Click += async delegate { await RefreshRankingAsync(); };

            _module.StatsChanged += HandleStatsChanged;
            Shown += async delegate
            {
                ApplySnapshot();
                _escPanel.Show();
                if (GgmanAccountSession.Current != null) await RefreshRankingAsync();
            };
            FormClosed += delegate
            {
                _module.StatsChanged -= HandleStatsChanged;
                if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
                _lifetime.Dispose();
            };

            ApplySnapshot();
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
                    _ui.Get(UiTextKeys.AccountOpenFailed),
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
                        : _ui.Get(UiTextKeys.AccountManage);
                    _escPanel.RefreshAccountActions();
                    _textEditor.RefreshAccountActions();
                    _cloudCenter.RefreshAccountActions();
                    _ = _module.RefreshAfterSessionChangedAsync(_lifetime.Token);
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
                _cloudCenter.SetBounds(28, 454, cardWidth, 152);
                _escPanel.SetBounds(28, _cloudCenter.Bottom + 14, cardWidth, cardWidth >= 620 ? 410 : 460);
                _textEditor.SetBounds(28, _escPanel.Bottom + 14, cardWidth, _textEditor.Height);
                _pageContent.Height = _textEditor.Bottom + 16;
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
                _refreshButton.Left = cardWidth - _refreshButton.Width - 16;
                _privacyButton.Left = _refreshButton.Left - _privacyButton.Width - 10;
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
                if (cardWidth - 32 <= 0 || cardWidth < 2 * 104 + 44)
                    throw new InvalidOperationException("Personal stats actions lost their usable width.");
            }
            if (ResolvePageWidthForSmokeTest(420) - 160 - 30 <= 28 + 160)
                throw new InvalidOperationException("GGman account entry overlaps the page title at narrow width.");
            if (306 + 122 >= 454 || 454 + 152 >= 620)
                throw new InvalidOperationException("My GGman cloud center overlaps history or ESC area.");
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

        private void OpenPrivacyDialog()
        {
            var owner = TopLevelControl as Form;
            using (var dialog = new GgmanStatsPrivacyForm(_module, _ui))
            {
                if (owner != null && owner.Visible && owner.TopLevel)
                    dialog.ShowDialog(owner);
                else
                    dialog.ShowDialog();
            }
            if (!IsDisposed) ApplySnapshot();
        }

        private async Task RefreshRankingAsync()
        {
            if (IsDisposed || _lifetime.IsCancellationRequested) return;
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

            if (GgmanAccountSession.Current == null)
            {
                _percentileValue.Text = _ui.Get(UiTextKeys.RegisteredStatsRankSignedOut);
            }
            else if (!snapshot.CloudRankingEnabled)
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

            _refreshButton.Enabled = GgmanAccountSession.Current != null;
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
