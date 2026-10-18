#if REALTAIIZOR_REPAIR
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
        private readonly GameRepairView _view;
        private readonly Button _autoButton;
        private FacmStatusTone _statusTone;
        private string _statusText = LeagueGameRepairUiText.Ready;

        public LeagueGameRepairForm(LeagueGameRepairService repair, LeagueEfficiencyModule efficiency)
        {
            _repair = repair ?? throw new ArgumentNullException(nameof(repair));
            _efficiency = efficiency ?? throw new ArgumentNullException(nameof(efficiency));
            AutoScaleMode = AutoScaleMode.None;
            Text = LeagueGameRepairUiText.Title;
            FormBorderStyle = FormBorderStyle.None;
            TopLevel = false;
            Dock = DockStyle.Fill;
            _view = new GameRepairView();
            _autoButton = _view.Actions[1];
            _view.ActionRequested += RunAction;
            Controls.Add(_view);
            VisibleChanged += RefreshVisibility;
            FacmThemeRuntime.ThemeChanged += RefreshTheme;
            RefreshTheme(this, EventArgs.Empty);
            RefreshAutoButton();
        }

        private async void RunAction(int index)
        {
            var button = _view.Actions[index];
            button.Enabled = false;
            try {
                switch (index) {
                    case 0: await RepairWindowAsync(); break;
                    case 1: await ToggleAutoRepairAsync(); break;
                    case 2: await SkipSettlementAsync(); break;
                    case 3: await RestartUxAsync(); break;
                    case 4: await ExitGameAsync(); break;
                }
            } catch (Exception exception) {
                AppLog.Error("League native game repair action failed", exception);
                SetStatus(LeagueGameRepairUiText.ActionFailed, FacmStatusTone.Error);
            } finally { if (!button.IsDisposed) button.Enabled = true; }
        }

        private void RefreshVisibility(object sender, EventArgs args)
        {
            if (Visible) { RefreshAutoButton(); RefreshTheme(sender, args); }
        }

        private void RefreshTheme(object sender, EventArgs args)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) {
                if (IsHandleCreated) BeginInvoke(new Action(() => RefreshTheme(sender, args)));
                return;
            }
            var light = FacmThemeRuntime.Current.IsLight;
            _view.ApplyPalette(light, FacmDesignSystem.Canvas, FacmDesignSystem.Surface,
                FacmDesignSystem.ReadableTextOn(FacmDesignSystem.Surface, FacmDesignSystem.Text),
                FacmDesignSystem.ReadableTextOn(FacmDesignSystem.Surface, FacmDesignSystem.TextMuted),
                FacmDesignSystem.Accent, FacmDesignSystem.ReadableTextOn(FacmDesignSystem.Surface, FacmDesignSystem.Error));
            _view.SetStatus(_statusText, ResolveStatusColor());
        }

        private Color ResolveStatusColor()
        {
            switch (_statusTone) {
                case FacmStatusTone.Success: return FacmDesignSystem.Success;
                case FacmStatusTone.Warning: return FacmDesignSystem.Warning;
                case FacmStatusTone.Error: return FacmDesignSystem.Error;
                default: return FacmDesignSystem.TextMuted;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                FacmThemeRuntime.ThemeChanged -= RefreshTheme;
                VisibleChanged -= RefreshVisibility;
                if (_view != null) _view.ActionRequested -= RunAction;
            }
            base.Dispose(disposing);
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
                    SetStatus(LeagueGameRepairUiText.ExitFailed, FacmStatusTone.Error);
                    return;
                }

                if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                    SetStatus(LeagueGameRepairUiText.ExitSuccess, FacmStatusTone.Success);
                else if (string.Equals(result.Status, "no-target", StringComparison.OrdinalIgnoreCase))
                    SetStatus(LeagueGameRepairUiText.ExitNoTarget, FacmStatusTone.Warning);
                else
                    SetStatus(LeagueGameRepairUiText.ExitFailed, FacmStatusTone.Error);
            }
            catch (Exception exception)
            {
                AppLog.Error("League one-click exit game failed", exception);
                SetStatus(LeagueGameRepairUiText.ExitFailed, FacmStatusTone.Error);
            }
        }

        private void RefreshAutoButton()
        {
            if (_autoButton == null || _autoButton.IsDisposed) return;
            _autoButton.Text = _repair.AutoRepairEnabled
                ? LeagueGameRepairUiText.FixAutoDisable
                : LeagueGameRepairUiText.FixAuto;
            _autoButton.AccessibleName = _autoButton.Text;

        }

        private void ShowRepairResult(LeagueGameRepairResult result)
        {
            if (result == null)
            {
                SetStatus(LeagueGameRepairUiText.ActionFailed, FacmStatusTone.Error);
                return;
            }
            var tone = result.Success
                ? (result.Changed ? FacmStatusTone.Success : FacmStatusTone.Neutral)
                : FacmStatusTone.Warning;
            SetStatus(result.Message, tone);
        }

        private void SetStatus(string text, FacmStatusTone tone)
        {
            if (_view.IsDisposed) return;
            _statusTone = tone;
            _statusText = text ?? string.Empty;
            _view.SetStatus(_statusText, ResolveStatusColor());

        }

    }
}
#endif
