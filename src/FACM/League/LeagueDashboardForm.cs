using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Performance;
using FACM.Services;
using FACM.Theming;

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
        private readonly FacmActionButton _refreshButton;
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
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            var title = new Label
            {
                Text = _ui.Get(UiTextKeys.LeagueDashboardTitle),
                Location = new Point(28, 20),
                Size = new Size(440, 34),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 17F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = _ui.Get(UiTextKeys.LeagueDashboardHint),
                Location = new Point(30, 58),
                Size = new Size(560, 24),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent
            };
            Controls.Add(title);
            Controls.Add(hint);

            var overview = CreatePanel(new Rectangle(28, 96, 594, 98));
            overview.Controls.Add(CreateCaption(UiTextKeys.LeagueDashboardConnection, new Point(16, 13), 250));
            _connectionValue = CreateOverviewValue(new Point(16, 39), 250);
            overview.Controls.Add(_connectionValue);
            overview.Controls.Add(CreateSeparator(new Rectangle(296, 14, 1, 68)));
            overview.Controls.Add(CreateCaption(UiTextKeys.LeagueDashboardGameflow, new Point(316, 13), 250));
            _phaseValue = CreateOverviewValue(new Point(316, 39), 250);
            overview.Controls.Add(_phaseValue);
            Controls.Add(overview);

            var details = CreatePanel(new Rectangle(28, 210, 594, 164));
            _accountValue = AddDetailRow(details, UiTextKeys.LeagueDashboardAccount, 10, true);
            _platformValue = AddDetailRow(details, UiTextKeys.LeagueDashboardPlatformRegion, 48, true);
            _performanceValue = AddDetailRow(details, UiTextKeys.LeagueDashboardPerformance, 86, true);
            _updatedValue = AddDetailRow(details, UiTextKeys.LeagueDashboardLastUpdated, 124, false);
            Controls.Add(details);

            _refreshButton = CreateButton(
                UiTextKeys.LeagueDashboardRefresh,
                new Rectangle(432, 389, 92, 30),
                FacmButtonTone.Primary);
            _refreshButton.Click += async delegate { await RefreshAsync(true); };
            var close = CreateButton(
                UiTextKeys.Close,
                new Rectangle(530, 389, 92, 30),
                FacmButtonTone.Secondary);
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

        private Label CreateCaption(string key, Point location, int width)
        {
            return new Label
            {
                Text = _ui.Get(key),
                Location = location,
                Size = new Size(width, 20),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8.4F, FontStyle.Bold)
            };
        }

        private static Label CreateOverviewValue(Point location, int width)
        {
            return new Label
            {
                Location = location,
                Size = new Size(width, 36),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 13F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private Label AddDetailRow(Control parent, string key, int top, bool separator)
        {
            parent.Controls.Add(CreateCaption(key, new Point(16, top), 146));
            var value = new Label
            {
                Location = new Point(170, top - 1),
                Size = new Size(400, 24),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9.3F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            parent.Controls.Add(value);
            if (separator) parent.Controls.Add(CreateSeparator(new Rectangle(16, top + 28, 554, 1)));
            return value;
        }

        private static Panel CreateSeparator(Rectangle bounds)
        {
            return new Panel
            {
                Bounds = bounds,
                BackColor = FacmDesignSystem.BorderSoft,
                TabStop = false
            };
        }

        private FacmActionButton CreateButton(string key, Rectangle bounds, FacmButtonTone tone)
        {
            return new FacmActionButton
            {
                Text = _ui.Get(key),
                Bounds = bounds,
                Tone = tone,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8.6F, FontStyle.Bold)
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
            _connectionValue.ForeColor = FacmDesignSystem.Success;
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
            _connectionValue.ForeColor = FacmDesignSystem.Warning;
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
