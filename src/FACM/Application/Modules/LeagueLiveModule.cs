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
    internal sealed class LeagueLiveModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[]
        {
            LeagueClientModule.ModuleId,
            PerformanceModule.ModuleId,
            LeagueDashboardModule.ModuleId
        };

        private readonly LeagueClientModule _leagueClient;
        private readonly PerformanceModule _performance;
        private readonly LeagueDashboardModule _dashboard;
        private LeagueLiveDataService _service;
        private LeagueBenchQuickPickService _benchQuickPick;
        private Timer _champSelectPopupTimer;
        private LeagueLiveForm _automaticPopup;
        private bool _champSelectEpisode;
        private bool _dismissedForEpisode;
        private bool _closingAutomaticPopup;

        public LeagueLiveModule(
            LeagueClientModule leagueClient,
            PerformanceModule performance,
            LeagueDashboardModule dashboard)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _performance = performance ?? throw new ArgumentNullException(nameof(performance));
            _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        }

        public const string ModuleId = "league-live";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        public void Initialize()
        {
            _service = new LeagueLiveDataService(_leagueClient, _performance.Budgets);
            _benchQuickPick = new LeagueBenchQuickPickService(_service, (ILeagueBenchSwapWriteApi)_leagueClient);

            // Reuse LeagueDashboardModule's existing cached gameflow monitor. The popup observer is a
            // UI-only timer and never creates another LCU poller/session.
            Application.Idle += StartChampSelectPopupObserver;
        }

        public Form CreateLiveForm(UiTextCatalog ui)
        {
            if (_service == null || _benchQuickPick == null)
                throw new InvalidOperationException("League Live module is not initialized.");
            return LeagueSoftGlassSkin.Apply(new LeagueLiveForm(_service, _benchQuickPick, ui));
        }

        private void StartChampSelectPopupObserver(object sender, EventArgs e)
        {
            Application.Idle -= StartChampSelectPopupObserver;
            if (_champSelectPopupTimer != null || _service == null || _benchQuickPick == null) return;

            _champSelectPopupTimer = new Timer { Interval = 650 };
            _champSelectPopupTimer.Tick += HandleChampSelectPopupTick;
            _champSelectPopupTimer.Start();
            HandleChampSelectPopupTick(null, EventArgs.Empty);
        }

        private void HandleChampSelectPopupTick(object sender, EventArgs e)
        {
            var state = _dashboard.CurrentGameflowState;
            var inChampSelect = state != null && state.Connected && state.Activity == LeagueActivityLevel.ChampSelect;

            if (!inChampSelect)
            {
                _champSelectEpisode = false;
                _dismissedForEpisode = false;
                CloseAutomaticPopup();
                return;
            }

            if (!_champSelectEpisode)
            {
                _champSelectEpisode = true;
                _dismissedForEpisode = false;
            }

            if (_dismissedForEpisode || _automaticPopup != null || _service == null || _benchQuickPick == null)
                return;

            ShowAutomaticPopup();
        }

        private void ShowAutomaticPopup()
        {
            var ui = UiTextCatalog.Load();
            var form = LeagueSoftGlassSkin.Apply(new LeagueLiveForm(_service, _benchQuickPick, ui));
            form.Text = ui.Get(UiTextKeys.LeagueLiveWindowTitle) + " · 选人快捷";
            form.TopMost = true;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.ClientSize = new Size(820, 620);

            var area = Screen.PrimaryScreen == null
                ? new Rectangle(0, 0, 1280, 720)
                : Screen.PrimaryScreen.WorkingArea;
            form.Location = new Point(
                Math.Max(area.Left + 12, area.Right - form.Width - 18),
                Math.Max(area.Top + 12, area.Top + 18));

            form.FormClosed += HandleAutomaticPopupClosed;
            _automaticPopup = form;
            form.Show();
            form.BringToFront();
            AppLog.Info("League Live popup opened for ChampSelect episode.");
        }

        private void HandleAutomaticPopupClosed(object sender, FormClosedEventArgs e)
        {
            var form = sender as LeagueLiveForm;
            if (form != null) form.FormClosed -= HandleAutomaticPopupClosed;
            if (ReferenceEquals(_automaticPopup, form)) _automaticPopup = null;
            if (!_closingAutomaticPopup && _champSelectEpisode)
                _dismissedForEpisode = true;
        }

        private void CloseAutomaticPopup()
        {
            var form = _automaticPopup;
            _automaticPopup = null;
            if (form == null) return;

            _closingAutomaticPopup = true;
            try
            {
                form.FormClosed -= HandleAutomaticPopupClosed;
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

            CloseAutomaticPopup();
            _benchQuickPick = null;
            _service = null;
        }
    }
}
