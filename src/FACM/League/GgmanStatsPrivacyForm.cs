using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Online;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class GgmanStatsPrivacyForm : Form
    {
        private readonly LeaguePersonalStatsModule _module;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly FacmToggleSwitch _ranking;
        private readonly FacmToggleSwitch _usage;
        private readonly FacmActionButton _import;
        private readonly FacmActionButton _save;
        private readonly Label _status;
        private bool _busy;

        internal GgmanStatsPrivacyForm(LeaguePersonalStatsModule module, UiTextCatalog ui)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            Text = _ui.Get(UiTextKeys.RegisteredStatsPrivacy);
            ClientSize = new Size(550, 289);
            MinimumSize = Size;
            MaximumSize = Size;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;

            var hint = new Label
            {
                Text = _ui.Get(UiTextKeys.RegisteredStatsPrivacyHint),
                Bounds = new Rectangle(20, 19, 510, 50),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent
            };
            _ranking = new FacmToggleSwitch
            {
                Text = _ui.Get(UiTextKeys.RegisteredStatsRankOptIn),
                Bounds = new Rectangle(20, 83, 510, 30)
            };
            _usage = new FacmToggleSwitch
            {
                Text = _ui.Get(UiTextKeys.RegisteredStatsUsageConsent),
                Bounds = new Rectangle(20, 127, 510, 30)
            };
            _import = new FacmActionButton
            {
                Text = _ui.Get(UiTextKeys.RegisteredStatsImport),
                Bounds = new Rectangle(20, 186, 226, 36),
                Tone = FacmButtonTone.Secondary
            };
            _save = new FacmActionButton
            {
                Text = _ui.Get(UiTextKeys.RegisteredStatsSave),
                Bounds = new Rectangle(309, 186, 221, 36),
                Tone = FacmButtonTone.Primary
            };
            _status = new Label
            {
                Bounds = new Rectangle(20, 242, 510, 28),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            Controls.Add(hint);
            Controls.Add(_ranking);
            Controls.Add(_usage);
            Controls.Add(_import);
            Controls.Add(_save);
            Controls.Add(_status);

            _save.Click += async delegate { await SaveAsync(); };
            _import.Click += async delegate { await ImportAsync(); };
            Shown += delegate { RefreshState(); };
            FormClosing += delegate { _lifetime.Cancel(); };
            FormClosed += delegate { _lifetime.Dispose(); };
            RefreshState();
        }

        private void RefreshState()
        {
            var signedIn = GgmanAccountSession.Current != null;
            _ranking.Checked = signedIn && _module.GetSnapshot().CloudRankingEnabled;
            _ranking.Enabled = signedIn && _module.HasLoadedRegisteredRanking && !_busy;
            _usage.Checked = UsageTelemetryModule.IsEnabled();
            _import.Enabled = signedIn && !_busy;
            _save.Enabled = !_busy;
            if (!signedIn) _status.Text = _ui.Get(UiTextKeys.RegisteredStatsLoginRequired);
            else if (!_module.HasLoadedRegisteredRanking) _status.Text = _ui.Get(UiTextKeys.RegisteredStatsRankLoading);
        }

        private async Task SaveAsync()
        {
            if (_busy) return;
            SetBusy(true);
            try
            {
                UsageTelemetryModule.SetEnabled(_usage.Checked);
                if (GgmanAccountSession.Current != null && _module.HasLoadedRegisteredRanking)
                    await _module.SetRegisteredRankingVisibleAsync(_ranking.Checked, _lifetime.Token);
                if (!IsDisposed) _status.Text = _ui.Get(UiTextKeys.RegisteredStatsSaved);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!IsDisposed)
                    _status.Text = string.Format(_ui.Get(UiTextKeys.RegisteredStatsPrivacyError), error.Message);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private async Task ImportAsync()
        {
            if (_busy || GgmanAccountSession.Current == null) return;
            var local = _module.ReadLegacySummary();
            var prompt = string.Format(_ui.Get(UiTextKeys.RegisteredStatsImportPrompt),
                local.PlayedAccounts, local.ActiveDays.Count);
            if (MessageBox.Show(this, prompt, Text, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            SetBusy(true);
            try
            {
                await _module.ImportLegacyAsync(_lifetime.Token);
                if (!IsDisposed) _status.Text = _ui.Get(UiTextKeys.RegisteredStatsImportSuccess);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!IsDisposed)
                    _status.Text = string.Format(_ui.Get(UiTextKeys.RegisteredStatsPrivacyError), error.Message);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _ranking.Enabled = !busy && _module.HasLoadedRegisteredRanking;
            _usage.Enabled = !busy;
            _import.Enabled = !busy && GgmanAccountSession.Current != null;
            _save.Enabled = !busy;
        }
    }
}
