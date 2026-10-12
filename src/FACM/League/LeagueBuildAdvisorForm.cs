using System;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueBuildAdvisorForm : Form
    {
        private readonly LeagueBuildAdvisorDataService _service;
        private readonly UiTextCatalog _ui;
        private readonly Label _contextLabel;
        private readonly Label _statsLabel;
        private readonly Label _sourceLabel;
        private readonly Label _statusLabel;
        private readonly ListView _rows;
        private readonly Button _refreshButton;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _refreshing;
        private LeagueActivityLevel _lastActivity = LeagueActivityLevel.None;

        public LeagueBuildAdvisorForm(LeagueBuildAdvisorDataService service, UiTextCatalog ui)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));

            Text = LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorWindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(940, 650);
            MinimumSize = new Size(820, 570);
            BackColor = Color.FromArgb(14, 19, 30);
            ForeColor = Color.FromArgb(238, 243, 252);
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorTitle),
                Location = new Point(28, 20),
                Size = new Size(560, 36),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorHint),
                Location = new Point(30, 60),
                Size = new Size(870, 24),
                ForeColor = Color.FromArgb(146, 161, 188),
                AutoEllipsis = true
            };

            _contextLabel = CreateInfoLabel(new Point(30, 98), new Size(880, 24), true);
            _statsLabel = CreateInfoLabel(new Point(30, 128), new Size(880, 24), false);
            _sourceLabel = CreateInfoLabel(new Point(30, 156), new Size(880, 24), false);
            _statusLabel = CreateInfoLabel(new Point(30, 184), new Size(880, 24), false);

            _rows = new ListView
            {
                Location = new Point(28, 220),
                Size = new Size(884, 366),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(22, 29, 44),
                ForeColor = Color.FromArgb(238, 243, 252),
                BorderStyle = BorderStyle.FixedSingle,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = true,
                View = View.Details
            };
            _rows.Columns.Add(LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorCategory), 150);
            _rows.Columns.Add(LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorRecommendation), 540);
            _rows.Columns.Add(LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorEvidence), 170);

            _refreshButton = CreateButton(_ui.Get(UiTextKeys.LeagueLiveRefresh), Color.FromArgb(55, 104, 214));
            _refreshButton.Location = new Point(720, 604);
            _refreshButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _refreshButton.Click += async delegate { await RefreshOnceAsync(true); };

            var close = CreateButton(_ui.Get(UiTextKeys.Close), Color.FromArgb(35, 43, 60));
            close.Location = new Point(820, 604);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.Click += delegate { Close(); };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_contextLabel);
            Controls.Add(_statsLabel);
            Controls.Add(_sourceLabel);
            Controls.Add(_statusLabel);
            Controls.Add(_rows);
            Controls.Add(_refreshButton);
            Controls.Add(close);

            ApplyWaitingState();
            Shown += async delegate { await RunLoopAsync(); };
            FormClosed += delegate
            {
                if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
                _lifetime.Dispose();
            };
        }

        private Label CreateInfoLabel(Point location, Size size, bool bold)
        {
            return new Label
            {
                Location = location,
                Size = size,
                ForeColor = bold ? Color.White : Color.FromArgb(176, 191, 216),
                Font = new Font("Microsoft YaHei UI", bold ? 10F : 9F, bold ? FontStyle.Bold : FontStyle.Regular),
                AutoEllipsis = true
            };
        }

        private Button CreateButton(string text, Color background)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(92, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = background,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(68, 79, 101);
            return button;
        }

        private async Task RunLoopAsync()
        {
            while (!_lifetime.IsCancellationRequested && !IsDisposed)
            {
                await RefreshOnceAsync(false);
                if (_lifetime.IsCancellationRequested || IsDisposed) break;
                var delay = LeagueLivePolling.ResolveDelay(_lastActivity, WindowState == FormWindowState.Minimized);
                try { await Task.Delay(delay, _lifetime.Token); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task RefreshOnceAsync(bool force)
        {
            if (_refreshing || IsDisposed || _lifetime.IsCancellationRequested) return;
            _refreshing = true;
            _refreshButton.Enabled = false;
            try
            {
                var snapshot = await _service.RefreshAsync(force, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                ApplySnapshot(snapshot);
            }
            catch (OperationCanceledException)
            {
                // Form close owns cancellation.
            }
            catch (Exception exception)
            {
                AppLog.Info("League build advisor refresh skipped: " + exception.Message);
                if (!IsDisposed) _statusLabel.Text = LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorOpggUnavailable);
            }
            finally
            {
                _refreshing = false;
                if (!IsDisposed) _refreshButton.Enabled = true;
            }
        }

        private void ApplySnapshot(LeagueBuildAdvisorSnapshot snapshot)
        {
            if (snapshot == null)
            {
                ApplyWaitingState();
                return;
            }

            _lastActivity = snapshot.Activity;
            var unknown = _ui.Get(UiTextKeys.LeagueLiveUnknown);
            var champion = !string.IsNullOrWhiteSpace(snapshot.ChampionName)
                ? snapshot.ChampionName + " #" + snapshot.ChampionId
                : (snapshot.ChampionId > 0 ? "#" + snapshot.ChampionId : unknown);
            var mode = string.IsNullOrWhiteSpace(snapshot.Mode) ? unknown : snapshot.Mode;
            var position = string.IsNullOrWhiteSpace(snapshot.Position) ? unknown : snapshot.Position;
            var phase = string.IsNullOrWhiteSpace(snapshot.Phase) ? unknown : snapshot.Phase;

            _contextLabel.Text = LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorContext) + ": " +
                                 phase + "  ·  " + champion + "  ·  " + mode + " / " + position;
            _sourceLabel.Text = LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorSource) + ": " +
                                (string.IsNullOrWhiteSpace(snapshot.Source) ? "OP.GG Global" : snapshot.Source) +
                                "  ·  " + LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorVersion) + ": " +
                                (string.IsNullOrWhiteSpace(snapshot.Version) ? unknown : snapshot.Version) +
                                "  ·  " + _ui.Get(UiTextKeys.LeagueLivePerformance) + ": " +
                                (string.IsNullOrWhiteSpace(snapshot.BudgetName) ? unknown : snapshot.BudgetName);

            ApplyRecommendation(snapshot.Recommendation);
            _statusLabel.Text = StatusText(snapshot.Status, snapshot.FromCache);
        }

        private void ApplyRecommendation(LeagueBuildRecommendation recommendation)
        {
            _rows.BeginUpdate();
            try
            {
                _rows.Items.Clear();
                if (recommendation == null)
                {
                    _statsLabel.Text = LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorStats) + ": " + _ui.Get(UiTextKeys.LeagueLiveUnknown);
                    return;
                }

                var tier = string.IsNullOrWhiteSpace(recommendation.Tier) ? "--" : recommendation.Tier;
                var rank = recommendation.Rank > 0 ? " #" + recommendation.Rank : string.Empty;
                _statsLabel.Text = LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorStats) + ": " +
                                   tier + rank + "  ·  Win " + FormatRate(recommendation.WinRate) +
                                   "  ·  Pick " + FormatRate(recommendation.PickRate) +
                                   "  ·  Ban " + FormatRate(recommendation.BanRate);

                foreach (var row in recommendation.Rows)
                {
                    var item = new ListViewItem(new[]
                    {
                        CategoryText(row.Category),
                        row.Recommendation ?? string.Empty,
                        row.Evidence ?? string.Empty
                    });
                    _rows.Items.Add(item);
                }
            }
            finally
            {
                _rows.EndUpdate();
            }
        }

        private string CategoryText(string category)
        {
            switch ((category ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "summoner-spells": return _ui.Get(UiTextKeys.LeagueLiveSpells);
                case "runes": return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorRunes);
                case "starter-items": return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorStarterItems);
                case "boots": return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorBoots);
                case "core-items": return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorCoreItems);
                case "skills": return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorSkills);
                case "counters": return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorCounters);
                default: return category ?? string.Empty;
            }
        }

        private string StatusText(string status, bool fromCache)
        {
            switch ((status ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "client-required": return _ui.Get(UiTextKeys.LeaguePlayerClientRequired);
                case "waiting-champion": return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorWaitingChampion);
                case "waiting-champ-select": return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorWaitingChampSelect);
                case "unsupported-mode": return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorUnsupportedMode);
                case "opgg-unavailable": return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorOpggUnavailable);
                case "in-game-cache": return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorInGameCache);
                case "in-game-no-cache": return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorInGameNoCache);
                case "timeout": return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorTimeout);
                case "ready":
                    return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorReady) +
                           (fromCache ? " · cache" : string.Empty) + "  ·  " +
                           LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorReadOnly);
                default: return LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorReadOnly);
            }
        }

        private void ApplyWaitingState()
        {
            _lastActivity = LeagueActivityLevel.None;
            _contextLabel.Text = LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorContext) + ": " + _ui.Get(UiTextKeys.LeagueLiveUnknown);
            _statsLabel.Text = LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorStats) + ": " + _ui.Get(UiTextKeys.LeagueLiveUnknown);
            _sourceLabel.Text = LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorSource) + ": OP.GG Global";
            _statusLabel.Text = LeagueAdvisorText.Get(_ui, UiTextKeys.LeagueAdvisorWaitingChampSelect);
            _rows.Items.Clear();
        }

        private static string FormatRate(double? rate)
        {
            if (!rate.HasValue) return "--";
            var value = rate.Value;
            if (Math.Abs(value) <= 1.0) value *= 100.0;
            return value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
