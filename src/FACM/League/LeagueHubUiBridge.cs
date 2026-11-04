using System;
using System.Linq;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueHubUiBridge
    {
        private static LeagueHubModule _module;
        private static bool _dialogOpen;
        private static bool _openPending;

        public static void Install(LeagueHubModule module)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
        }

        public static void Uninstall()
        {
            _module = null;
            _openPending = false;
        }

        public static void RequestOpen()
        {
            if (_module == null) return;
            var owner = Application.OpenForms.OfType<MainForm>().FirstOrDefault(form => !form.IsDisposed);
            if (owner == null || !owner.IsHandleCreated) return;
            if (owner.TryNavigateUnifiedRoute(string.Empty)) return;
            if (_dialogOpen || _openPending) return;
            QueueOpen(owner, string.Empty);
        }

        public static void RequestOpen(MainForm owner, string viewId)
        {
            if (_module == null || owner == null || owner.IsDisposed || !owner.IsHandleCreated) return;
            var route = NormalizeViewId(viewId);
            if (owner.TryNavigateUnifiedRoute(route)) return;
            if (_dialogOpen || _openPending) return;
            QueueOpen(owner, route);
        }

        private static void QueueOpen(MainForm owner, string requestedViewId)
        {
            _openPending = true;
            try
            {
                owner.BeginInvoke(new Action(delegate
                {
                    _openPending = false;
                    if (owner.IsDisposed) return;
                    Open(owner, requestedViewId);
                }));
            }
            catch
            {
                _openPending = false;
            }
        }

        internal static LeagueDashboardModule UnifiedDashboard
        {
            get { return _module == null ? null : _module.UnifiedDashboard; }
        }

        internal static LeagueGameRepairService UnifiedRepair
        {
            get { return _module == null ? null : _module.UnifiedRepair; }
        }

        internal static LeagueEfficiencyModule UnifiedEfficiency
        {
            get { return _module == null ? null : _module.UnifiedEfficiency; }
        }

        internal static LeagueHubForm CreateUnifiedEmbeddedForm(UiTextCatalog ui)
        {
            if (_module == null || ui == null) return null;
            return _module.CreateForm(ui) as LeagueHubForm;
        }

        private static void Open(MainForm owner, string requestedViewId)
        {
            if (_dialogOpen || _module == null || owner == null || owner.IsDisposed) return;
            _dialogOpen = true;
            try
            {
                owner.CloseMenu();
                var ui = UiTextCatalog.Load();
                using (var form = _module.CreateForm(ui))
                {
                    FACM.HoverDescriptionEnhancer.ApplyLeagueHub(form, ui);
                    var hub = form as LeagueHubForm;
                    if (hub != null) hub.SetInitialView(requestedViewId);
                    form.ShowDialog(owner);
                }
            }
            finally
            {
                _dialogOpen = false;
            }
        }

        private static string NormalizeViewId(string viewId)
        {
            if (string.IsNullOrWhiteSpace(viewId)) return string.Empty;
            return LeagueHubNavigation.Views.Any(item => string.Equals(item.Id, viewId, StringComparison.Ordinal))
                ? viewId
                : string.Empty;
        }

        internal static bool InstalledForSmokeTest()
        {
            return _module != null;
        }

        internal static bool ContextNavigationAvailableForSmokeTest()
        {
            return LeagueHubForm.ResolveInitialViewForSmokeTest(LeagueHubNavigation.Player) == LeagueHubNavigation.Player;
        }
    }
}
