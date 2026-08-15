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
            if (_dialogOpen || _openPending || _module == null) return;
            var owner = Application.OpenForms.OfType<MainForm>().FirstOrDefault(form => !form.IsDisposed);
            if (owner == null || !owner.IsHandleCreated) return;

            _openPending = true;
            try
            {
                owner.BeginInvoke(new Action(delegate
                {
                    _openPending = false;
                    Open(owner);
                }));
            }
            catch
            {
                _openPending = false;
            }
        }

        private static void Open(MainForm owner)
        {
            if (_dialogOpen || _module == null || owner == null || owner.IsDisposed) return;
            _dialogOpen = true;
            try
            {
                owner.CloseMenu();
                using (var form = _module.CreateForm(UiTextCatalog.Load()))
                {
                    form.ShowDialog(owner);
                }
            }
            finally
            {
                _dialogOpen = false;
            }
        }

        internal static bool InstalledForSmokeTest()
        {
            return _module != null;
        }
    }
}
