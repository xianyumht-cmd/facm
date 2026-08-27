using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueGameRepairForm : Form
    {
        private readonly LeagueGameRepairService _repair;
        private readonly LeagueEfficiencyModule _efficiency;
        private readonly Label _status;
        private Button _autoButton;

        public LeagueGameRepairForm(LeagueGameRepairService repair, LeagueEfficiencyModule efficiency)
        {
            _repair = repair ?? throw new ArgumentNullException(nameof(repair));
            _efficiency = efficiency ?? throw new ArgumentNullException(nameof(efficiency));

            Text = LeagueGameRepairUiText.Title;
            FormBorderStyle = FormBorderStyle.None;
            TopLevel = false;
            Dock = DockStyle.Fill;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            var intro = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var title = Label(LeagueGameRepairUiText.Title, 13F, FontStyle.Bold, FacmDesignSystem.Text);
            title.Location = new Point(2, 0);
            title.Size = new Size(180, 26);
            var hint = Label(LeagueGameRepairUiText.Hint, 8.5F, FontStyle.Regular, FacmDesignSystem.TextMuted);
            hint.Location = new Point(2, 28);
            hint.Size = new Size(690, 22);
            intro.Controls.Add(title);
            intro.Controls.Add(hint);
            root.Controls.Add(intro, 0, 0);

            var windowCard = new FacmGlassPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), DrawBorder = true };
            var windowTitle = Label(LeagueGameRepairUiText.WindowGroup, 9.6F, FontStyle.Bold, FacmDesignSystem.Text);
            windowTitle.Location = new Point(16, 12);
            windowTitle.Size = new Size(180, 22);
            windowCard.Controls.Add(windowTitle);
            windowCard.Controls.Add(ActionButton(LeagueGameRepairUiText.FixNow, LeagueGameRepairUiText.FixNowHint, 16, 42, RepairWindowAsync, null));
            windowCard.Controls.Add(ActionButton(LeagueGameRepairUiText.FixAuto, LeagueGameRepairUiText.FixAutoHint, 350, 42, ToggleAutoRepairAsync, delegate(Button button) { _autoButton = button; }));
            root.Controls.Add(windowCard, 0, 1);

            var lobbyCard = new FacmGlassPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), DrawBorder = true };
            var lobbyTitle = Label(LeagueGameRepairUiText.LobbyGroup, 9.6F, FontStyle.Bold, FacmDesignSystem.Text);
            lobbyTitle.Location = new Point(16, 12);
            lobbyTitle.Size = new Size(180, 22);
            lobbyCard.Controls.Add(lobbyTitle);
            lobbyCard.Controls.Add(ActionButton(LeagueGameRepairUiText.SkipSettlement, LeagueGameRepairUiText.SkipSettlementHint, 16, 42, SkipSettlementAsync, null));
            lobbyCard.Controls.Add(ActionButton(LeagueGameRepairUiText.RestartUx, LeagueGameRepairUiText.RestartUxHint, 350, 42, RestartUxAsync, null));
            lobbyCard.Controls.Add(ActionButton(LeagueGameRepairUiText.ExitGame, LeagueGameRepairUiText.ExitGameHint, 16, 106, ExitGameAsync, null));
            root.Controls.Add(lobbyCard, 0, 2);

            var statusCard = new FacmGlassPanel { Dock = DockStyle.Fill, DrawBorder = true };
            _status = Label(LeagueGameRepairUiText.Ready, 8.7F, FontStyle.Bold, FacmDesignSystem.TextMuted);
            _status.Dock = DockStyle.Fill;
            _status.Padding = new Padding(16, 0, 16, 0);
            statusCard.Controls.Add(_status);
            root.Controls.Add(statusCard, 0, 3);

            Shown += delegate { RefreshAutoButton(); };
            VisibleChanged += delegate { if (Visible) RefreshAutoButton(); };
            FacmDesignSystem.ApplyLeagueSurface(this);
        }

        private Control ActionButton(
            string title,
            string hint,
            int x,
            int y,
            Func<Task> action,
            Action<Button> capture)
        {
            var panel = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(318, 58),
                BackColor = Color.Transparent
            };
            var button = new Button
            {
                Text = title,
                Location = new Point(0, 0),
                Size = new Size(126, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = FacmDesignSystem.SurfaceRaised,
                ForeColor = FacmDesignSystem.Text,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.BorderColor = FacmDesignSystem.Border;
            button.Click += async delegate
            {
                if (action == null || button.IsDisposed) return;
                button.Enabled = false;
                try { await action().ConfigureAwait(true); }
                catch (Exception exception)
                {
                    AppLog.Error("League native game repair action failed", exception);
                    SetStatus(LeagueGameRepairUiText.ActionFailed, FacmDesignSystem.Error);
                }
                finally
                {
                    if (!button.IsDisposed) button.Enabled = true;
                }
            };
            if (capture != null) capture(button);

            var description = Label(hint, 7.8F, FontStyle.Regular, FacmDesignSystem.TextMuted);
            description.Location = new Point(136, 0);
            description.Size = new Size(178, 55);
            panel.Controls.Add(button);
            panel.Controls.Add(description);
            return panel;
        }

        private async Task RepairWindowAsync()
        {
            var result = await _repair.RepairWindowAsync(CancellationToken.None).ConfigureAwait(true);
            ShowRepairResult(result);
        }

        private Task ToggleAutoRepairAsync()
        {
            var result = _repair.SetAutoRepairEnabled(!_repair.AutoRepairEnabled);
            RefreshAutoButton();
            ShowRepairResult(result);
            return Task.CompletedTask;
        }

        private async Task SkipSettlementAsync()
        {
            var result = await _repair.SkipSettlementAsync(CancellationToken.None).ConfigureAwait(true);
            ShowRepairResult(result);
        }

        private async Task RestartUxAsync()
        {
            var result = await _repair.RestartClientUxAsync(CancellationToken.None).ConfigureAwait(true);
            ShowRepairResult(result);
        }

        private async Task ExitGameAsync()
        {
            try
            {
                var result = await _efficiency.RunExitGameAsync().ConfigureAwait(true);
                if (result == null)
                {
                    SetStatus(LeagueGameRepairUiText.ExitFailed, FacmDesignSystem.Error);
                    return;
                }

                if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                    SetStatus(LeagueGameRepairUiText.ExitSuccess, FacmDesignSystem.Success);
                else if (string.Equals(result.Status, "no-target", StringComparison.OrdinalIgnoreCase))
                    SetStatus(LeagueGameRepairUiText.ExitNoTarget, FacmDesignSystem.Warning);
                else
                    SetStatus(LeagueGameRepairUiText.ExitFailed, FacmDesignSystem.Error);
            }
            catch (Exception exception)
            {
                AppLog.Error("League one-click exit game failed", exception);
                SetStatus(LeagueGameRepairUiText.ExitFailed, FacmDesignSystem.Error);
            }
        }

        private void RefreshAutoButton()
        {
            if (_autoButton == null || _autoButton.IsDisposed) return;
            _autoButton.Text = _repair.AutoRepairEnabled
                ? LeagueGameRepairUiText.FixAutoDisable
                : LeagueGameRepairUiText.FixAuto;
        }

        private void ShowRepairResult(LeagueGameRepairResult result)
        {
            if (result == null)
            {
                SetStatus(LeagueGameRepairUiText.ActionFailed, FacmDesignSystem.Error);
                return;
            }
            var color = result.Success
                ? (result.Changed ? FacmDesignSystem.Success : FacmDesignSystem.TextMuted)
                : FacmDesignSystem.Warning;
            SetStatus(result.Message, color);
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null || _status.IsDisposed) return;
            _status.Text = text ?? string.Empty;
            _status.ForeColor = color;
        }

        private static Label Label(string text, float size, FontStyle style, Color color)
        {
            return new Label
            {
                Text = text,
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, size, style),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }
    }
}
