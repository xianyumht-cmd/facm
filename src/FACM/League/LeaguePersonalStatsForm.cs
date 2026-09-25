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
    internal static class LeaguePersonalStatsUiTextKeys
    {
        public const string WindowTitle = "LeaguePersonalStatsWindowTitle";
        public const string Title = "LeaguePersonalStatsTitle";
        public const string Hint = "LeaguePersonalStatsHint";
        public const string Accounts = "LeaguePersonalStatsAccounts";
        public const string ActiveDays = "LeaguePersonalStatsActiveDays";
        public const string MemberSince = "LeaguePersonalStatsMemberSince";
        public const string Ranking = "LeaguePersonalStatsRanking";
        public const string RankingDisabled = "LeaguePersonalStatsRankingDisabled";
        public const string RankingWaiting = "LeaguePersonalStatsRankingWaiting";
        public const string RankingFormat = "LeaguePersonalStatsRankingFormat";
        public const string PercentileFormat = "LeaguePersonalStatsPercentileFormat";
        public const string LocalToggle = "LeaguePersonalStatsLocalToggle";
        public const string RankingToggle = "LeaguePersonalStatsRankingToggle";
        public const string PrivacyHint = "LeaguePersonalStatsPrivacyHint";
        public const string Refresh = "LeaguePersonalStatsRefresh";
        public const string Paused = "LeaguePersonalStatsPaused";
    }

    internal static class LeaguePersonalStatsText
    {
        private static readonly Dictionary<string, string> Defaults =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { LeaguePersonalStatsUiTextKeys.WindowTitle, "GGman · 我的档案" },
                { LeaguePersonalStatsUiTextKeys.Title, "我的 GGman" },
                { LeaguePersonalStatsUiTextKeys.Hint, "把长期使用记录留在本机，需要时再选择加入匿名排行。" },
                { LeaguePersonalStatsUiTextKeys.Accounts, "玩过的账号" },
                { LeaguePersonalStatsUiTextKeys.ActiveDays, "活跃天数" },
                { LeaguePersonalStatsUiTextKeys.MemberSince, "加入 GGman" },
                { LeaguePersonalStatsUiTextKeys.Ranking, "匿名账号数排行" },
                { LeaguePersonalStatsUiTextKeys.RankingDisabled, "未参与" },
                { LeaguePersonalStatsUiTextKeys.RankingWaiting, "等待云端统计" },
                { LeaguePersonalStatsUiTextKeys.RankingFormat, "第 {0} / {1} 名" },
                { LeaguePersonalStatsUiTextKeys.PercentileFormat, "超过 {0:0.0}% 的参与玩家" },
                { LeaguePersonalStatsUiTextKeys.LocalToggle, "记录我的 GGman 使用足迹" },
                { LeaguePersonalStatsUiTextKeys.RankingToggle, "参与匿名账号数排行" },
                { LeaguePersonalStatsUiTextKeys.PrivacyHint, "账号历史只保存设备内派生的哈希；不上传 PUUID、账号名、密码或 LCU 凭据。排行可随时关闭。" },
                { LeaguePersonalStatsUiTextKeys.Refresh, "刷新排行" },
                { LeaguePersonalStatsUiTextKeys.Paused, "已暂停新增记录，已有本地历史不会删除。" }
            };

        public static string Get(UiTextCatalog ui, string key)
        {
            string fallback;
            if (!Defaults.TryGetValue(key ?? string.Empty, out fallback)) fallback = string.Empty;
            return ui == null ? fallback : ui.Get(key, fallback);
        }

        internal static IReadOnlyDictionary<string, string> DefaultsForSmokeTest()
        {
            return Defaults;
        }
    }

    internal sealed class LeaguePersonalStatsForm : Form
    {
        private readonly LeaguePersonalStatsModule _module;
        private readonly AppSettings _settings;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private readonly Label _accountsValue;
        private readonly Label _daysValue;
        private readonly Label _memberValue;
        private readonly Label _rankValue;
        private readonly Label _percentileValue;
        private readonly Label _statusValue;
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

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = LeaguePersonalStatsText.Get(_ui, LeaguePersonalStatsUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 500);
            MinimumSize = new Size(650, 470);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            var title = new Label
            {
                Text = LeaguePersonalStatsText.Get(_ui, LeaguePersonalStatsUiTextKeys.Title),
                Location = new Point(28, 22),
                Size = new Size(440, 32),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 17F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = LeaguePersonalStatsText.Get(_ui, LeaguePersonalStatsUiTextKeys.Hint),
                Location = new Point(30, 58),
                Size = new Size(630, 22),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent
            };
            Controls.Add(title);
            Controls.Add(hint);

            var summary = CreatePanel(new Rectangle(28, 94, 664, 112));
            _accountsValue = AddMetric(summary, LeaguePersonalStatsUiTextKeys.Accounts, 14);
            _daysValue = AddMetric(summary, LeaguePersonalStatsUiTextKeys.ActiveDays, 230);
            _memberValue = AddMetric(summary, LeaguePersonalStatsUiTextKeys.MemberSince, 446);
            Controls.Add(summary);

            var ranking = CreatePanel(new Rectangle(28, 220, 664, 104));
            ranking.Controls.Add(CreateCaption(
                LeaguePersonalStatsText.Get(_ui, LeaguePersonalStatsUiTextKeys.Ranking),
                new Point(16, 12),
                240));
            _rankValue = CreateValue(new Point(16, 38), 300, 13F);
            _percentileValue = CreateValue(new Point(320, 40), 326, 9.3F);
            _percentileValue.TextAlign = ContentAlignment.MiddleRight;
            ranking.Controls.Add(_rankValue);
            ranking.Controls.Add(_percentileValue);
            Controls.Add(ranking);

            var preferences = CreatePanel(new Rectangle(28, 338, 664, 118));
            _localToggle = new FacmToggleSwitch
            {
                Text = LeaguePersonalStatsText.Get(_ui, LeaguePersonalStatsUiTextKeys.LocalToggle),
                Location = new Point(16, 10),
                Size = new Size(632, 32)
            };
            _rankingToggle = new FacmToggleSwitch
            {
                Text = LeaguePersonalStatsText.Get(_ui, LeaguePersonalStatsUiTextKeys.RankingToggle),
                Location = new Point(16, 44),
                Size = new Size(632, 32)
            };
            var privacy = new Label
            {
                Text = LeaguePersonalStatsText.Get(_ui, LeaguePersonalStatsUiTextKeys.PrivacyHint),
                Location = new Point(16, 80),
                Size = new Size(520, 30),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 7.8F)
            };
            _refreshButton = new FacmActionButton
            {
                Text = LeaguePersonalStatsText.Get(_ui, LeaguePersonalStatsUiTextKeys.Refresh),
                Bounds = new Rectangle(548, 82, 100, 28),
                Tone = FacmButtonTone.Secondary,
                Font = new Font(Font.FontFamily, 8.2F, FontStyle.Bold)
            };
            preferences.Controls.Add(_localToggle);
            preferences.Controls.Add(_rankingToggle);
            preferences.Controls.Add(privacy);
            preferences.Controls.Add(_refreshButton);
            Controls.Add(preferences);

            _statusValue = new Label
            {
                Location = new Point(30, 466),
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
                LeaguePersonalStatsText.Get(_ui, key),
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

            if (!snapshot.CloudRankingEnabled)
            {
                _rankValue.Text = LeaguePersonalStatsText.Get(_ui, LeaguePersonalStatsUiTextKeys.RankingDisabled);
                _percentileValue.Text = string.Empty;
            }
            else if (snapshot.CloudRank <= 0 || snapshot.CloudRankedUsers <= 0)
            {
                _rankValue.Text = LeaguePersonalStatsText.Get(_ui, LeaguePersonalStatsUiTextKeys.RankingWaiting);
                _percentileValue.Text = string.Empty;
            }
            else
            {
                _rankValue.Text = string.Format(
                    LeaguePersonalStatsText.Get(_ui, LeaguePersonalStatsUiTextKeys.RankingFormat),
                    snapshot.CloudRank,
                    snapshot.CloudRankedUsers);
                _percentileValue.Text = string.Format(
                    LeaguePersonalStatsText.Get(_ui, LeaguePersonalStatsUiTextKeys.PercentileFormat),
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
                : LeaguePersonalStatsText.Get(_ui, LeaguePersonalStatsUiTextKeys.Paused);
        }
    }
}
