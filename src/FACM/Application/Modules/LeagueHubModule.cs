using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.League;
using FACM.Performance;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class LeagueHubModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[]
        {
            LeagueDashboardModule.ModuleId,
            LeaguePlayerModule.ModuleId,
            LeagueLiveModule.ModuleId,
            LeagueBuildAdvisorModule.ModuleId,
            LeagueEfficiencyModule.ModuleId,
            MayhemModule.ModuleId
        };

        private readonly LeagueDashboardModule _dashboard;
        private readonly LeaguePlayerModule _player;
        private readonly LeagueLiveModule _live;
        private readonly LeagueBuildAdvisorModule _advisor;
        private readonly LeagueEfficiencyModule _efficiency;
        private readonly MayhemModule _mayhem;
        private LeagueHubForm _hubForm;
        private Timer _champSelectPopupTimer;
        private Form _automaticLivePopup;
        private bool _champSelectEpisode;
        private bool _surfacePresentedForEpisode;
        private bool _dismissedForEpisode;
        private bool _closingAutomaticPopup;

        public LeagueHubModule(
            LeagueDashboardModule dashboard,
            LeaguePlayerModule player,
            LeagueLiveModule live,
            LeagueBuildAdvisorModule advisor,
            LeagueEfficiencyModule efficiency,
            MayhemModule mayhem)
        {
            _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _live = live ?? throw new ArgumentNullException(nameof(live));
            _advisor = advisor ?? throw new ArgumentNullException(nameof(advisor));
            _efficiency = efficiency ?? throw new ArgumentNullException(nameof(efficiency));
            _mayhem = mayhem ?? throw new ArgumentNullException(nameof(mayhem));
        }

        public const string ModuleId = "league-hub";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        public void Initialize()
        {
            LeagueHubNavigation.ValidateForSmokeTest();
            LeagueHubUiBridge.Install(this);

            // UI-only observer: it reads LeagueDashboardModule's cached phase and never creates a
            // second LCU session/gameflow monitor. LeagueHub remains the owner of League navigation.
            Application.Idle += StartChampSelectPopupObserver;
        }

        public Form CreateForm(UiTextCatalog ui)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            var form = LeagueSoftGlassSkin.Apply(new LeagueHubForm(
                ui,
                CreateDashboard,
                CreatePlayer,
                CreateLive,
                CreateMayhem,
                CreateRecommendation,
                CreateEfficiency,
                CreatePresence));
            _hubForm = form;
            form.UpdateGameflowContext(_dashboard.CurrentGameflowState);
            form.FormClosed += HandleHubFormClosed;
            return form;
        }

        private Form CreateDashboard(UiTextCatalog ui) { return Skin(_dashboard.CreateDashboardForm(ui)); }
        private Form CreatePlayer(UiTextCatalog ui) { return Skin(_player.CreatePlayerForm(ui)); }
        private Form CreateLive(UiTextCatalog ui) { return _live.CreateLiveForm(ui); }
        private Form CreateMayhem(UiTextCatalog ui) { return Skin(_mayhem.CreateLookupForm()); }
        private Form CreateRecommendation(UiTextCatalog ui) { return Skin(_advisor.CreateRecommendationForm(ui)); }
        private Form CreateEfficiency(UiTextCatalog ui) { return Skin(_efficiency.CreateForm(ui)); }
        private Form CreatePresence(UiTextCatalog ui) { return Skin(_dashboard.CreatePresenceForm(ui, null)); }

        private static Form Skin(Form form)
        {
            return LeagueSoftGlassSkin.Apply(form);
        }

        private void HandleHubFormClosed(object sender, FormClosedEventArgs e)
        {
            var form = sender as LeagueHubForm;
            if (form != null) form.FormClosed -= HandleHubFormClosed;
            if (ReferenceEquals(_hubForm, form)) _hubForm = null;
        }

        private void StartChampSelectPopupObserver(object sender, EventArgs e)
        {
            Application.Idle -= StartChampSelectPopupObserver;
            if (_champSelectPopupTimer != null) return;

            _champSelectPopupTimer = new Timer { Interval = 650 };
            _champSelectPopupTimer.Tick += HandleChampSelectPopupTick;
            _champSelectPopupTimer.Start();
            HandleChampSelectPopupTick(null, EventArgs.Empty);
        }

        private void HandleChampSelectPopupTick(object sender, EventArgs e)
        {
            var state = _dashboard.CurrentGameflowState;
            if (_hubForm != null && !_hubForm.IsDisposed)
                _hubForm.UpdateGameflowContext(state);

            var inChampSelect = state != null && state.Connected && state.Activity == LeagueActivityLevel.ChampSelect;

            if (!inChampSelect)
            {
                _champSelectEpisode = false;
                _surfacePresentedForEpisode = false;
                _dismissedForEpisode = false;
                CloseAutomaticLivePopup();
                return;
            }

            if (!_champSelectEpisode)
            {
                _champSelectEpisode = true;
                _surfacePresentedForEpisode = false;
                _dismissedForEpisode = false;
            }

            if (_hubForm != null && !_hubForm.IsDisposed &&
                string.Equals(_hubForm.CurrentViewIdForSmokeTest, LeagueHubNavigation.Live, StringComparison.Ordinal))
            {
                CloseAutomaticLivePopup();
                if (!_surfacePresentedForEpisode)
                {
                    if (!_hubForm.Visible) _hubForm.Show();
                    _hubForm.BringToFront();
                    _surfacePresentedForEpisode = true;
                }
                return;
            }

            if (_dismissedForEpisode || _automaticLivePopup != null || _surfacePresentedForEpisode) return;
            ShowAutomaticLivePopup();
        }

        private void ShowAutomaticLivePopup()
        {
            Form form = null;
            try
            {
                var ui = UiTextCatalog.Load();
                form = CreateLive(ui);
                form.Text = ui.Get(UiTextKeys.LeagueLiveWindowTitle) + " · 选人快捷";
                form.TopMost = true;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.ClientSize = new Size(820, 620);

                var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                form.Location = new Point(
                    Math.Max(area.Left + 12, area.Right - form.Width - 18),
                    area.Top + 18);

                form.FormClosed += HandleAutomaticLivePopupClosed;
                _automaticLivePopup = form;
                _surfacePresentedForEpisode = true;
                form.Show();
                form.BringToFront();
                AppLog.Info("League Live popup opened for ChampSelect episode.");
            }
            catch (Exception exception)
            {
                if (form != null && !form.IsDisposed) form.Dispose();
                _automaticLivePopup = null;
                _surfacePresentedForEpisode = false;
                _dismissedForEpisode = true;
                AppLog.Info("League Live automatic popup skipped: " + exception.Message);
            }
        }

        private void HandleAutomaticLivePopupClosed(object sender, FormClosedEventArgs e)
        {
            var form = sender as Form;
            if (form != null) form.FormClosed -= HandleAutomaticLivePopupClosed;
            if (ReferenceEquals(_automaticLivePopup, form)) _automaticLivePopup = null;
            if (!_closingAutomaticPopup && _champSelectEpisode)
                _dismissedForEpisode = true;
        }

        private void CloseAutomaticLivePopup()
        {
            var form = _automaticLivePopup;
            _automaticLivePopup = null;
            if (form == null) return;

            _closingAutomaticPopup = true;
            try
            {
                form.FormClosed -= HandleAutomaticLivePopupClosed;
                if (!form.IsDisposed) form.Close();
                if (!form.IsDisposed) form.Dispose();
            }
            finally
            {
                _closingAutomaticPopup = false;
            }
        }

        public void Dispose()
        {
            Application.Idle -= StartChampSelectPopupObserver;
            if (_champSelectPopupTimer != null)
            {
                _champSelectPopupTimer.Stop();
                _champSelectPopupTimer.Tick -= HandleChampSelectPopupTick;
                _champSelectPopupTimer.Dispose();
                _champSelectPopupTimer = null;
            }

            CloseAutomaticLivePopup();
            if (_hubForm != null)
                _hubForm.FormClosed -= HandleHubFormClosed;
            _hubForm = null;
            LeagueHubUiBridge.Uninstall();
        }
    }
}
