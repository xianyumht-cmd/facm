using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost.Modules;
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
        private readonly Label _percentileValue;
        private readonly Label _statusValue;
        private readonly Label[] _historyRows;
        private readonly FacmToggleSwitch _localToggle;
        private readonly FacmToggleSwitch _rankingToggle;
        private readonly FacmActionButton _refreshButton;
        private bool _applying;
        private bool _savingPreferences;

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
            ClientSize = new Size(720, 680);
            MinimumSize = new Size(650, 640);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            var title = new Label
            {
                Text = _ui.Get(UiTextKeys.LeaguePersonalStatsTitle),
                Location = new Point(28, 22),
                Size = new Size(440, 32),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 17F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = _ui.Get(UiTextKeys.LeaguePersonalStatsHint),
                Location = new Point(30, 58),
                Size = new Size(630, 22),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent
            };
            Controls.Add(title);
            Controls.Add(hint);

            var summary = CreatePanel(new Rectangle(28, 94, 664, 112));
            _accountsValue = AddMetric(summary, UiTextKeys.LeaguePersonalStatsAccounts, 14);
            _daysValue = AddMetric(summary, UiTextKeys.LeaguePersonalStatsActiveDays, 230);
            _memberValue = AddMetric(summary, UiTextKeys.LeaguePersonalStatsMemberSince, 446);
            Controls.Add(summary);

            var history = CreatePanel(new Rectangle(28, 220, 664, 156));
            history.Controls.Add(CreateCaption(
                _ui.Get(UiTextKeys.LeaguePersonalStatsAccounts),
                new Point(16, 12),
                240));
            for (var index = 0; index < _historyRows.Length; index++)
            {
                var row = new Label
                {
                    Location = new Point(16, 38 + index * 28),
                    Size = new Size(632, 24),
                    ForeColor = FacmDesignSystem.Text,
                    BackColor = Color.Transparent,
                    Font = new Font(Font.FontFamily, 8.5F),
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _historyRows[index] = row;
                history.Controls.Add(row);
            }
            Controls.Add(history);

            var ranking = CreatePanel(new Rectangle(28, 390, 664, 104));
            ranking.Controls.Add(CreateCaption(
                _ui.Get(UiTextKeys.LeaguePersonalStatsRanking),
                new Point(16, 12),
                240));
            _percentileValue = CreateValue(new Point(16, 38), 630, 13F);
            ranking.Controls.Add(_percentileValue);
            Controls.Add(ranking);

            var preferences = CreatePanel(new Rectangle(28, 508, 664, 118));
            _localToggle = new FacmToggleSwitch
            {
                Text = _ui.Get(UiTextKeys.LeaguePersonalStatsLocalToggle),
                Location = new Point(16, 10),
                Size = new Size(632, 32)
            };
            _rankingToggle = new FacmToggleSwitch
            {
                Text = _ui.Get(UiTextKeys.LeaguePersonalStatsRankingToggle),
                Location = new Point(16, 44),
                Size = new Size(632, 32)
            };
            _refreshButton = new FacmActionButton
            {
                Text = _ui.Get(UiTextKeys.LeaguePersonalStatsRefresh),
                Bounds = new Rectangle(548, 82, 100, 28),
                Tone = FacmButtonTone.Secondary,
                Font = new Font(Font.FontFamily, 8.2F, FontStyle.Bold)
            };
            preferences.Controls.Add(_localToggle);
            preferences.Controls.Add(_rankingToggle);
            preferences.Controls.Add(_refreshButton);
            Controls.Add(preferences);

            _statusValue = new Label
            {
                Location = new Point(30, 636),
                Size = new Size(660, 22),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 8F)
            };
            Controls.Add(_statusValue);

            _localToggle.CheckedChanged += HandlePreferenceChanged;
            _rankingToggle.CheckedChanged += HandlePreferenceChanged;
            _refreshButton.Click += async delegate { await RefreshRankingAsync(); };

            _module.StatsChanged += HandleStatsChanged;
            Shown += async delegate
            {
                ApplySnapshot();
                if (_settings.LeagueCloudRankingEnabled) await RefreshRankingAsync();
            };
            FormClosed += delegate
            {
                _module.StatsChanged -= HandleStatsChanged;
                if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
                _lifetime.Dispose();
            };

            ApplySnapshot();
        }

        private Label AddMetric(Control parent, string key, int left)
        {
            parent.Controls.Add(CreateCaption(
                _ui.Get(key),
                new Point(left, 14),
                190));
            var value = CreateValue(new Point(left, 43), 190, 18F);
            parent.Controls.Add(value);
            return value;
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
