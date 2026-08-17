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
        private static readonly Color Background = Color.FromArgb(10, 15, 25);
        private static readonly Color Surface = Color.FromArgb(18, 27, 43);
        private static readonly Color TextPrimary = Color.FromArgb(238, 243, 252);
        private static readonly Color TextMuted = Color.FromArgb(139, 157, 190);
        private static readonly Color NeonCyan = Color.FromArgb(73, 218, 255);
        private static readonly Color NeonBlue = Color.FromArgb(91, 142, 255);
        private static readonly Color NeonPurple = Color.FromArgb(154, 106, 255);

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
            ClientSize = new Size(840, 620);
            MinimumSize = new Size(700, 520);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Microsoft YaHei UI", 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(30, 22, 30, 22),
                ColumnCount = 2,
                RowCount = 7,
                BackColor = Background
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            var titlePanel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Background };
            titlePanel.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 4, BackColor = NeonCyan });
            titlePanel.Controls.Add(new Label
            {
                Text = _ui.Get(UiTextKeys.LeagueDashboardTitle),
                Location = new Point(14, 0),
                Size = new Size(440, 38),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold)
            });
            titlePanel.Controls.Add(new Label
            {
                Text = "LEAGUE // LIVE",
                Dock = DockStyle.Right,
                Width = 180,
                ForeColor = NeonPurple,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Consolas", 9F, FontStyle.Bold)
            });
            root.Controls.Add(titlePanel, 0, 0);
            root.SetColumnSpan(titlePanel, 2);

            var hint = new Label
            {
                Text = _ui.Get(UiTextKeys.LeagueDashboardHint),
                Dock = DockStyle.Fill,
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(1, 2, 0, 0)
            };
            root.Controls.Add(hint, 0, 1);
            root.SetColumnSpan(hint, 2);

            _connectionValue = AddCard(root, 0, 2, UiTextKeys.LeagueDashboardConnection, NeonCyan);
            _accountValue = AddCard(root, 1, 2, UiTextKeys.LeagueDashboardAccount, NeonPurple);
            _platformValue = AddCard(root, 0, 3, UiTextKeys.LeagueDashboardPlatformRegion, NeonBlue);
            _phaseValue = AddCard(root, 1, 3, UiTextKeys.LeagueDashboardGameflow, NeonCyan);
            _performanceValue = AddCard(root, 0, 4, UiTextKeys.LeagueDashboardPerformance, NeonPurple);
            _updatedValue = AddCard(root, 1, 4, UiTextKeys.LeagueDashboardLastUpdated, NeonBlue);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Background
            };
            var close = CreateButton(UiTextKeys.Close, Color.FromArgb(35, 43, 60), 100);
            close.Click += delegate { Close(); };
            _refreshButton = CreateButton(UiTextKeys.LeagueDashboardRefresh, Color.FromArgb(55, 104, 214), 112);
            _refreshButton.Click += async delegate { await RefreshAsync(true); };
            actions.Controls.Add(close);
            actions.Controls.Add(_refreshButton);
            root.Controls.Add(actions, 0, 6);
            root.SetColumnSpan(actions, 2);

            Controls.Add(root);

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

        private Label AddCard(TableLayoutPanel parent, int column, int row, string key, Color accent)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(column == 0 ? 0 : 8, 6, column == 0 ? 8 : 0, 6),
                BackColor = Surface
            };
            panel.Controls.Add(new Panel
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = accent
            });
            panel.Controls.Add(new Label
            {
                Text = _ui.Get(key),
                Location = new Point(18, 14),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Size = new Size(330, 22),
                ForeColor = TextMuted,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
            });
            var value = new Label
            {
                Location = new Point(18, 45),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Size = new Size(330, 36),
                AutoEllipsis = true,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold)
            };
            panel.Controls.Add(value);
            parent.Controls.Add(panel, column, row);
            panel.Resize += delegate
            {
                var width = Math.Max(80, panel.ClientSize.Width - 34);
                foreach (Control control in panel.Controls)
                {
                    var label = control as Label;
                    if (label != null) label.Width = width;
                }
            };
            return value;
        }

        private Button CreateButton(string key, Color background, int width)
        {
            var button = new Button
            {
                Text = _ui.Get(key),
                Size = new Size(width, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = background,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Margin = new Padding(8, 4, 0, 4),
                TabStop = false
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(68, 79, 101);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 83, 123);
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
