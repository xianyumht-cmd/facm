using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueDashboardForm : Form
    {
        private readonly UiTextCatalog _ui;
        private readonly LeagueDashboardPhaseService _phaseService;
        private readonly LeagueDashboardDetailsService _detailsService;
        private readonly Label _connectionValue;
        private readonly Label _accountValue;
        private readonly Label _platformValue;
        private readonly Label _phaseValue;
        private readonly Label _performanceValue;
        private readonly Label _updatedValue;
        private readonly Button _refreshButton;
        private readonly Timer _timer;
        private CancellationTokenSource _lifetime = new CancellationTokenSource();
        private LeagueDashboardSnapshot _snapshot;
        private DateTime _lastDetailsUtc = DateTime.MinValue;
        private bool _refreshing;

        public LeagueDashboardForm(ILeagueClientApi client, PerformanceBudgetProvider budgets, UiTextCatalog ui)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (budgets == null) throw new ArgumentNullException(nameof(budgets));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _phaseService = new LeagueDashboardPhaseService(client, budgets);
            _detailsService = new LeagueDashboardDetailsService(client, budgets);

            Text = _ui.Get(UiTextKeys.LeagueDashboardWindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(650, 430);
            MinimumSize = new Size(650, 430);
            MaximizeBox = false;
            BackColor = Color.FromArgb(14, 19, 30);
            ForeColor = Color.FromArgb(238, 243, 252);
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = _ui.Get(UiTextKeys.LeagueDashboardTitle),
                Location = new Point(28, 22),
                Size = new Size(440, 36),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = _ui.Get(UiTextKeys.LeagueDashboardHint),
                Location = new Point(30, 62),
                Size = new Size(560, 24),
                ForeColor = Color.FromArgb(146, 161, 188)
            };

            var connectionCard = CreateCard(new Rectangle(28, 102, 286, 80));
            connectionCard.Controls.Add(CreateCaption(UiTextKeys.LeagueDashboardConnection));
            _connectionValue = CreateValueLabel(new Point(16, 36), 252);
            connectionCard.Controls.Add(_connectionValue);

            var accountCard = CreateCard(new Rectangle(336, 102, 286, 80));
            accountCard.Controls.Add(CreateCaption(UiTextKeys.LeagueDashboardAccount));
            _accountValue = CreateValueLabel(new Point(16, 36), 252);
            accountCard.Controls.Add(_accountValue);

            var platformCard = CreateCard(new Rectangle(28, 198, 286, 80));
            platformCard.Controls.Add(CreateCaption(UiTextKeys.LeagueDashboardPlatformRegion));
            _platformValue = CreateValueLabel(new Point(16, 36), 252);
            platformCard.Controls.Add(_platformValue);

            var phaseCard = CreateCard(new Rectangle(336, 198, 286, 80));
            phaseCard.Controls.Add(CreateCaption(UiTextKeys.LeagueDashboardGameflow));
            _phaseValue = CreateValueLabel(new Point(16, 36), 252);
            phaseCard.Controls.Add(_phaseValue);

            var performanceCard = CreateCard(new Rectangle(28, 294, 286, 80));
            performanceCard.Controls.Add(CreateCaption(UiTextKeys.LeagueDashboardPerformance));
            _performanceValue = CreateValueLabel(new Point(16, 36), 252);
            performanceCard.Controls.Add(_performanceValue);

            var updateCard = CreateCard(new Rectangle(336, 294, 286, 80));
            updateCard.Controls.Add(CreateCaption(UiTextKeys.LeagueDashboardLastUpdated));
            _updatedValue = CreateValueLabel(new Point(16, 36), 252);
            updateCard.Controls.Add(_updatedValue);

            _refreshButton = new Button
            {
                Text = _ui.Get(UiTextKeys.LeagueDashboardRefresh),
                Location = new Point(432, 389),
                Size = new Size(92, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 104, 214),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _refreshButton.FlatAppearance.BorderColor = Color.FromArgb(83, 133, 237);
            _refreshButton.Click += async delegate { await RefreshAsync(true); };

            var closeButton = new Button
            {
                Text = _ui.Get(UiTextKeys.Close),
                Location = new Point(530, 389),
                Size = new Size(92, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(35, 43, 60),
                ForeColor = Color.FromArgb(218, 225, 239),
                Cursor = Cursors.Hand
            };
            closeButton.FlatAppearance.BorderColor = Color.FromArgb(68, 79, 101);
            closeButton.Click += delegate { Close(); };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(connectionCard);
            Controls.Add(accountCard);
            Controls.Add(platformCard);
            Controls.Add(phaseCard);
            Controls.Add(performanceCard);
            Controls.Add(updateCard);
            Controls.Add(_refreshButton);
            Controls.Add(closeButton);

            ShowEmptyState();
            _timer = new Timer { Interval = 5000 };
            _timer.Tick += async delegate { await RefreshAsync(false); };
            Shown += async delegate
            {
                await RefreshAsync(true);
                if (!IsDisposed) _timer.Start();
            };
            FormClosed += HandleClosed;
        }

        private Panel CreateCard(Rectangle bounds)
        {
            return new Panel
            {
                Bounds = bounds,
                BackColor = Color.FromArgb(22, 29, 44),
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private Label CreateCaption(string key)
        {
            return new Label
            {
                Text = _ui.Get(key),
                Location = new Point(15, 10),
                Size = new Size(252, 20),
                ForeColor = Color.FromArgb(139, 157, 190),
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
            };
        }

        private static Label CreateValueLabel(Point location, int width)
        {
            return new Label
            {
                Location = location,
                Size = new Size(width, 28),
                AutoEllipsis = true,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold)
            };
        }

        private async Task RefreshAsync(bool forceDetails)
        {
            if (_refreshing || IsDisposed || _lifetime.IsCancellationRequested) return;
            _refreshing = true;
            _refreshButton.Enabled = false;
            try
            {
                var phase = await _phaseService.RefreshAsync(_lifetime.Token);
                if (IsDisposed) return;

                var needDetails = phase.Connected &&
                                  (forceDetails || _snapshot == null || !_snapshot.Connected ||
                                   DateTime.UtcNow - _lastDetailsUtc >= TimeSpan.FromMinutes(1));
                if (needDetails)
                {
                    _snapshot = await _detailsService.LoadAsync(phase, _lifetime.Token);
                    _lastDetailsUtc = DateTime.UtcNow;
                }
                else
                {
                    MergePhase(phase);
                }

                ApplySnapshot();
                AdjustTimerInterval();
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested) ShowEmptyState();
            }
            catch (Exception exception)
            {
                AppLog.Info("League Dashboard refresh skipped: " + exception.Message);
                if (!IsDisposed) ShowEmptyState();
            }
            finally
            {
                _refreshing = false;
                if (!IsDisposed) _refreshButton.Enabled = true;
            }
        }

        private void MergePhase(LeagueDashboardPhaseState phase)
        {
            if (phase == null) return;
            if (_snapshot == null || !phase.Connected)
                _snapshot = new LeagueDashboardSnapshot();
            _snapshot.Connected = phase.Connected;
            _snapshot.Phase = phase.Phase;
            _snapshot.Activity = phase.Activity;
            _snapshot.BudgetName = phase.BudgetName;
            _snapshot.UpdatedAtUtc = phase.UpdatedAtUtc;
        }

        private void ApplySnapshot()
        {
            if (_snapshot == null || !_snapshot.Connected)
            {
                ShowEmptyState();
                return;
            }

            _connectionValue.Text = _ui.Get(UiTextKeys.LeagueDashboardConnected);
            _connectionValue.ForeColor = Color.FromArgb(103, 218, 166);

            var account = string.IsNullOrWhiteSpace(_snapshot.AccountName)
                ? _ui.Get(UiTextKeys.LeagueDashboardUnknown)
                : _snapshot.AccountName;
            if (_snapshot.SummonerLevel > 0)
                account += "  ·  " + _ui.Get(UiTextKeys.LeagueDashboardLevel) + " " + _snapshot.SummonerLevel;
            _accountValue.Text = account;

            var platform = FirstNonEmpty(_snapshot.PlatformName, _snapshot.PlatformId);
            if (!string.IsNullOrWhiteSpace(_snapshot.PlatformName) && !string.IsNullOrWhiteSpace(_snapshot.PlatformId) &&
                !string.Equals(_snapshot.PlatformName, _snapshot.PlatformId, StringComparison.OrdinalIgnoreCase))
                platform = _snapshot.PlatformName + "  ·  " + _snapshot.PlatformId;
            _platformValue.Text = string.IsNullOrWhiteSpace(platform)
                ? _ui.Get(UiTextKeys.LeagueDashboardUnknown)
                : platform;

            _phaseValue.Text = string.IsNullOrWhiteSpace(_snapshot.Phase)
                ? _ui.Get(UiTextKeys.LeagueDashboardUnknown)
                : _snapshot.Phase;
            _performanceValue.Text = string.IsNullOrWhiteSpace(_snapshot.BudgetName)
                ? _ui.Get(UiTextKeys.LeagueDashboardUnknown)
                : _snapshot.BudgetName;
            _updatedValue.Text = _snapshot.UpdatedAtUtc == DateTime.MinValue
                ? _ui.Get(UiTextKeys.LeagueDashboardUnknown)
                : _snapshot.UpdatedAtUtc.ToLocalTime().ToString("HH:mm:ss");
        }

        private void ShowEmptyState()
        {
            var unknown = _ui.Get(UiTextKeys.LeagueDashboardUnknown);
            _connectionValue.Text = _ui.Get(UiTextKeys.LeagueDashboardDisconnected);
            _connectionValue.ForeColor = Color.FromArgb(244, 169, 104);
            _accountValue.Text = _ui.Get(UiTextKeys.LeagueDashboardWaitingClient);
            _platformValue.Text = unknown;
            _phaseValue.Text = unknown;
            _performanceValue.Text = unknown;
            _updatedValue.Text = unknown;
        }

        private void AdjustTimerInterval()
        {
            if (_timer == null) return;
            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                _timer.Interval = 10000;
                return;
            }

            switch (_snapshot == null ? LeagueActivityLevel.None : _snapshot.Activity)
            {
                case LeagueActivityLevel.ChampSelect: _timer.Interval = 2000; break;
                case LeagueActivityLevel.Queueing: _timer.Interval = 3000; break;
                case LeagueActivityLevel.InGame: _timer.Interval = 8000; break;
                default: _timer.Interval = 5000; break;
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return null;
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return null;
        }

        private void HandleClosed(object sender, FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
