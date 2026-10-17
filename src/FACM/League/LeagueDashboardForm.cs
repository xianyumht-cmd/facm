using System;
using System.Collections.Generic;
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
        private readonly FacmActionButton _closeButton;
        private readonly Panel _scrollRoot;
        private readonly Label _titleLabel;
        private readonly Label _hintLabel;
        private readonly FacmGlassPanel _overviewPanel;
        private readonly FacmGlassPanel _detailsPanel;
        private bool _arranging;
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
            MinimumSize = new Size(400, 280);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            MaximizeBox = false;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            _titleLabel = new Label
            {
                Text = _ui.Get(UiTextKeys.LeagueDashboardTitle),
                Location = new Point(28, 20),
                Size = new Size(440, 34),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 15F, FontStyle.Bold)
            };
            _hintLabel = new Label
            {
                Text = _ui.Get(UiTextKeys.LeagueDashboardHint),
                Location = new Point(30, 58),
                Size = new Size(560, 24),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent
            };
            Controls.Add(_titleLabel);
            Controls.Add(_hintLabel);

            _overviewPanel = CreatePanel(new Rectangle(28, 96, 594, 98));
            var overview = _overviewPanel;
            overview.Controls.Add(CreateCaption(UiTextKeys.LeagueDashboardConnection, new Point(16, 13), 250));
            _connectionValue = CreateOverviewValue(new Point(16, 39), 250);
            overview.Controls.Add(_connectionValue);
            overview.Controls.Add(CreateSeparator(new Rectangle(296, 14, 1, 68)));
            overview.Controls.Add(CreateCaption(UiTextKeys.LeagueDashboardGameflow, new Point(316, 13), 250));
            _phaseValue = CreateOverviewValue(new Point(316, 39), 250);
            overview.Controls.Add(_phaseValue);
            Controls.Add(overview);

            _detailsPanel = CreatePanel(new Rectangle(28, 210, 594, 164));
            var details = _detailsPanel;
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
            _closeButton = CreateButton(
                UiTextKeys.Close,
                new Rectangle(530, 389, 92, 30),
                FacmButtonTone.Secondary);
            _closeButton.Click += delegate { Close(); };
            Controls.Add(_refreshButton);
            Controls.Add(_closeButton);

            _scrollRoot = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = FacmDesignSystem.Canvas
            };
            var owned = new List<Control>();
            foreach (Control child in Controls) owned.Add(child);
            foreach (var child in owned)
            {
                Controls.Remove(child);
                _scrollRoot.Controls.Add(child);
            }
            Controls.Add(_scrollRoot);
            Resize += delegate { LayoutDashboard(); };
            FacmThemeRuntime.ThemeChanged += RefreshPilotTheme;
            ShowEmptyState(null);
            LayoutDashboard();
            _timer = new System.Windows.Forms.Timer { Interval = 5000 };
            _timer.Tick += async delegate { await RefreshAsync(false); };
            Shown += async delegate
            {
                await RefreshAsync(true);
                if (!IsDisposed) _timer.Start();
            };
            FormClosed += delegate
            {
                FacmThemeRuntime.ThemeChanged -= RefreshPilotTheme;
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
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
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
                Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F),
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
                Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F, FontStyle.Bold)
            };
        }

        private int Unit(int logical)
        {
            return Math.Max(1, (int)Math.Round(logical * Math.Max(96F, DeviceDpi) / 96F));
        }

        private static int LabelHeight(Label label, int width, int minimum)
        {
            return Math.Max(minimum, TextRenderer.MeasureText(
                label.Text ?? string.Empty, label.Font,
                new Size(Math.Max(1, width), 16384),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + 2);
        }

        private void LayoutDashboard()
        {
            if (_arranging || IsDisposed || _scrollRoot == null || _scrollRoot.IsDisposed) return;
            _arranging = true;
            try
            {
                var pad = _scrollRoot.ClientSize.Width < Unit(760) ? Unit(16) : Unit(24);
                var width = Math.Min(Unit(880), Math.Max(Unit(240),
                    _scrollRoot.ClientSize.Width - pad * 2 - SystemInformation.VerticalScrollBarWidth));
                var y = pad;
                var titleHeight = LabelHeight(_titleLabel, width, Unit(28));
                _titleLabel.SetBounds(pad, y, width, titleHeight);
                y += titleHeight + Unit(8);
                var hintHeight = LabelHeight(_hintLabel, width, Unit(24));
                _hintLabel.SetBounds(pad, y, width, hintHeight);
                y += hintHeight + Unit(16);

                var inset = Unit(16);
                var inner = width - inset * 2;
                var twoColumns = inner >= Unit(600);
                var overviewHeight = twoColumns ? Unit(104) : Unit(176);
                _overviewPanel.SetBounds(pad, y, width, overviewHeight);
                var connLabel = (Label)_overviewPanel.Controls[0];
                var divider = (Panel)_overviewPanel.Controls[2];
                var phaseLabel = (Label)_overviewPanel.Controls[3];
                if (twoColumns)
                {
                    var column = (inner - Unit(32)) / 2;
                    connLabel.SetBounds(inset, Unit(12), column, Unit(22));
                    _connectionValue.SetBounds(inset, Unit(38), column, Unit(48));
                    divider.Visible = true;
                    divider.SetBounds(inset + column + Unit(15), Unit(16), Unit(1), overviewHeight - Unit(32));
                    phaseLabel.SetBounds(inset + column + Unit(32), Unit(12), column, Unit(22));
                    _phaseValue.SetBounds(inset + column + Unit(32), Unit(38), column, Unit(48));
                }
                else
                {
                    divider.Visible = false;
                    connLabel.SetBounds(inset, Unit(10), inner, Unit(22));
                    _connectionValue.SetBounds(inset, Unit(33), inner, Unit(42));
                    phaseLabel.SetBounds(inset, Unit(90), inner, Unit(22));
                    _phaseValue.SetBounds(inset, Unit(112), inner, Unit(44));
                }
                y += overviewHeight + Unit(16);
                var detailsY = y;
                var rowTop = Unit(8);
                var narrow = inner < Unit(560);
                var values = new[] { _accountValue, _platformValue, _performanceValue, _updatedValue };
                for (var i = 0; i < values.Length; i++)
                {
                    var label = (Label)_detailsPanel.Controls[i * 3];
                    // Each detail row inserts a caption and value; the first three add a separator.
                    var value = values[i];
                    var rowHeight = Unit(36);
                    if (narrow)
                    {
                        var labelHeight = LabelHeight(label, inner, Unit(20));
                        var valueHeight = LabelHeight(value, inner, Unit(24));
                        label.SetBounds(inset, rowTop + Unit(4), inner, labelHeight);
                        value.SetBounds(inset, rowTop + Unit(5) + labelHeight, inner, valueHeight);
                        rowHeight = Math.Max(Unit(48), labelHeight + valueHeight + Unit(10));
                    }
                    else
                    {
                        var labelWidth = Unit(144);
                        var valueWidth = Math.Max(Unit(80), inner - labelWidth - Unit(8));
                        var labelHeight = LabelHeight(label, labelWidth, Unit(20));
                        var valueHeight = LabelHeight(value, valueWidth, Unit(22));
                        rowHeight = Math.Max(Unit(36), Math.Max(labelHeight, valueHeight) + Unit(10));
                        label.SetBounds(inset, rowTop + Unit(5), labelWidth, labelHeight);
                        value.SetBounds(inset + labelWidth + Unit(8),
                            rowTop + Unit(5), valueWidth, valueHeight);
                    }
                    if (i < 3)
                    {
                        var separator = (Panel)_detailsPanel.Controls[i * 3 + 2];
                        separator.SetBounds(inset, rowTop + rowHeight, inner, Unit(1));
                    }
                    rowTop += rowHeight + Unit(1);
                }
                _detailsPanel.SetBounds(pad, detailsY, width, rowTop + Unit(8));
                y = detailsY + _detailsPanel.Height + Unit(16);

                var buttonWidth = Unit(96);
                var buttonHeight = Unit(32);
                _closeButton.SetBounds(pad + width - buttonWidth, y, buttonWidth, buttonHeight);
                _refreshButton.SetBounds(pad + width - buttonWidth * 2 - Unit(8),
                    y, buttonWidth, buttonHeight);
                _scrollRoot.AutoScrollMinSize = new Size(0, y + buttonHeight + pad);
            }
            finally { _arranging = false; }
        }

        private void RefreshPilotTheme(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                if (IsHandleCreated)
                    BeginInvoke(new Action(() => RefreshPilotTheme(sender, e)));
                return;
            }
            BackColor = FacmDesignSystem.Canvas;
            _scrollRoot.BackColor = FacmDesignSystem.Canvas;
            _overviewPanel.BackColor = FacmDesignSystem.Surface;
            _detailsPanel.BackColor = FacmDesignSystem.Surface;
            _titleLabel.ForeColor = FacmDesignSystem.Text;
            _hintLabel.ForeColor = FacmDesignSystem.TextMuted;
            _titleLabel.Font = new Font(FacmThemeRuntime.Current.FontName, 15F, FontStyle.Bold);
            _hintLabel.Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            _connectionValue.ForeColor = _snapshot != null && _snapshot.Connected
                ? FacmDesignSystem.Success : FacmDesignSystem.Warning;
            LayoutDashboard();
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
                LayoutDashboard();
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
