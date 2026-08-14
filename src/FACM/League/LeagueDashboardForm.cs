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
        private readonly System.Windows.Forms.Timer _timer;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
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

            var title = new Label { Text = _ui.Get(UiTextKeys.LeagueDashboardTitle), Location = new Point(28, 22), Size = new Size(440, 36), ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold) };
            var hint = new Label { Text = _ui.Get(UiTextKeys.LeagueDashboardHint), Location = new Point(30, 62), Size = new Size(560, 24), ForeColor = Color.FromArgb(146, 161, 188) };
            Controls.Add(title);
            Controls.Add(hint);

            _connectionValue = AddCard(UiTextKeys.LeagueDashboardConnection, new Rectangle(28, 102, 286, 80));
            _accountValue = AddCard(UiTextKeys.LeagueDashboardAccount, new Rectangle(336, 102, 286, 80));
            _platformValue = AddCard(UiTextKeys.LeagueDashboardPlatformRegion, new Rectangle(28, 198, 286, 80));
            _phaseValue = AddCard(UiTextKeys.LeagueDashboardGameflow, new Rectangle(336, 198, 286, 80));
            _performanceValue = AddCard(UiTextKeys.LeagueDashboardPerformance, new Rectangle(28, 294, 286, 80));
            _updatedValue = AddCard(UiTextKeys.LeagueDashboardLastUpdated, new Rectangle(336, 294, 286, 80));

            _refreshButton = CreateButton(UiTextKeys.LeagueDashboardRefresh, new Rectangle(432, 389, 92, 30), Color.FromArgb(55, 104, 214));
            _refreshButton.Click += async delegate { await RefreshAsync(true); };
            var close = CreateButton(UiTextKeys.Close, new Rectangle(530, 389, 92, 30), Color.FromArgb(35, 43, 60));
            close.Click += delegate { Close(); };
            Controls.Add(_refreshButton);
            Controls.Add(close);

            ShowEmptyState(null);
            _timer = new System.Windows.Forms.Timer { Interval = 5000 };
            _timer.Tick += async delegate { await RefreshAsync(false); };
            Shown += async delegate
            {
                await RefreshAsync(true);
                if (!IsDisposed) _timer.Start();
            };
            FormClosed += delegate
            {
                _timer.Stop();
                _timer.Dispose();
                if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
                _lifetime.Dispose();
            };
        }

        private Label AddCard(string key, Rectangle bounds)
        {
            var panel = new Panel { Bounds = bounds, BackColor = Color.FromArgb(22, 29, 44), BorderStyle = BorderStyle.FixedSingle };
            panel.Controls.Add(new Label { Text = _ui.Get(key), Location = new Point(15, 9), Size = new Size(252, 21), ForeColor = Color.FromArgb(139, 157, 190), Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold) });
            var value = new Label { Location = new Point(15, 36), Size = new Size(252, 28), AutoEllipsis = true, ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold) };
            panel.Controls.Add(value);
            Controls.Add(panel);
            return value;
        }

        private Button CreateButton(string key, Rectangle bounds, Color background)
        {
            var button = new Button { Text = _ui.Get(key), Bounds = bounds, FlatStyle = FlatStyle.Flat, BackColor = background, ForeColor = Color.White, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = Color.FromArgb(68, 79, 101);
            return button;
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
                var needDetails = phase.Connected && (forceDetails || _snapshot == null || !_snapshot.Connected || DateTime.UtcNow - _lastDetailsUtc >= TimeSpan.FromMinutes(1));
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
                if (!_lifetime.IsCancellationRequested) ShowEmptyState(_snapshot);
            }
            catch (Exception exception)
            {
                AppLog.Info("League Dashboard refresh skipped: " + exception.Message);
                if (!IsDisposed) ShowEmptyState(_snapshot);
            }
            finally
            {
                _refreshing = false;
                if (!IsDisposed) _refreshButton.Enabled = true;
            }
        }

        private void MergePhase(LeagueDashboardPhaseState phase)
        {
            if (_snapshot == null || !phase.Connected) _snapshot = new LeagueDashboardSnapshot();
            _snapshot.Connected = phase.Connected;
            _snapshot.ClientProcessDetected = phase.ClientProcessDetected;
            _snapshot.GameProcessDetected = phase.GameProcessDetected;
            _snapshot.Phase = phase.Phase;
            _snapshot.Activity = phase.Activity;
            _snapshot.BudgetName = phase.BudgetName;
            _snapshot.UpdatedAtUtc = phase.UpdatedAtUtc;
        }

        private void ApplySnapshot()
        {
            if (_snapshot == null || !_snapshot.Connected) { ShowEmptyState(_snapshot); return; }
            _connectionValue.Text = _ui.Get(UiTextKeys.LeagueDashboardConnected);
            _connectionValue.ForeColor = Color.FromArgb(103, 218, 166);
            var account = string.IsNullOrWhiteSpace(_snapshot.AccountName) ? _ui.Get(UiTextKeys.LeagueDashboardUnknown) : _snapshot.AccountName;
            if (_snapshot.SummonerLevel > 0) account += "  ·  " + _ui.Get(UiTextKeys.LeagueDashboardLevel) + " " + _snapshot.SummonerLevel;
            _accountValue.Text = account;
            var platform = FirstNonEmpty(_snapshot.PlatformName, _snapshot.PlatformId);
            if (!string.IsNullOrWhiteSpace(_snapshot.PlatformName) && !string.IsNullOrWhiteSpace(_snapshot.PlatformId) && !string.Equals(_snapshot.PlatformName, _snapshot.PlatformId, StringComparison.OrdinalIgnoreCase))
                platform = _snapshot.PlatformName + "  ·  " + _snapshot.PlatformId;
            _platformValue.Text = ValueOrUnknown(platform);
            _phaseValue.Text = ValueOrUnknown(_snapshot.Phase);
            _performanceValue.Text = ValueOrUnknown(_snapshot.BudgetName);
            _updatedValue.Text = _snapshot.UpdatedAtUtc == DateTime.MinValue ? _ui.Get(UiTextKeys.LeagueDashboardUnknown) : _snapshot.UpdatedAtUtc.ToLocalTime().ToString("HH:mm:ss");
        }

        private void ShowEmptyState(LeagueDashboardPhaseState state)
        {
            var unknown = _ui.Get(UiTextKeys.LeagueDashboardUnknown);
            var processDetected = state != null && (state.ClientProcessDetected || state.GameProcessDetected);
            _connectionValue.Text = processDetected ? unknown : _ui.Get(UiTextKeys.LeagueDashboardDisconnected);
            _connectionValue.ForeColor = Color.FromArgb(244, 169, 104);
            _accountValue.Text = processDetected ? unknown : _ui.Get(UiTextKeys.LeagueDashboardWaitingClient);
            _platformValue.Text = unknown;
            _phaseValue.Text = unknown;
            _performanceValue.Text = state == null ? unknown : ValueOrUnknown(state.BudgetName);
            _updatedValue.Text = state == null || state.UpdatedAtUtc == DateTime.MinValue
                ? unknown
                : state.UpdatedAtUtc.ToLocalTime().ToString("HH:mm:ss");
        }

        private string ValueOrUnknown(string value) { return string.IsNullOrWhiteSpace(value) ? _ui.Get(UiTextKeys.LeagueDashboardUnknown) : value; }

        private void AdjustTimerInterval()
        {
            if (!Visible || WindowState == FormWindowState.Minimized) { _timer.Interval = 10000; return; }
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
            foreach (var value in values) if (!string.IsNullOrWhiteSpace(value)) return value;
            return null;
        }
    }
}
