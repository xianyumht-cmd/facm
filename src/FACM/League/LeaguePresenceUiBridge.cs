using System;
using System.Linq;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    /// <summary>
    /// Keeps the presence dialog behind the already-initialized League Dashboard module so the
    /// control center can launch it without creating another LCU connector or reaching into MainForm.
    /// </summary>
    internal static class LeaguePresenceUiBridge
    {
        private static LeagueDashboardModule _module;
        private static bool _dialogOpen;

        public static void Install(LeagueDashboardModule module)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
        }

        public static void Uninstall(LeagueDashboardModule module)
        {
            if (ReferenceEquals(_module, module)) _module = null;
        }

        public static bool RequestOpen(ThemeDefinition theme)
        {
            if (_dialogOpen || _module == null) return false;
            var owner = Application.OpenForms.OfType<MainForm>().FirstOrDefault(form => !form.IsDisposed);
            if (owner == null) return false;

            _dialogOpen = true;
            try
            {
                using (var form = _module.CreatePresenceForm(UiTextCatalog.Load(), theme))
                {
                    form.TopMost = true;
                    form.ShowDialog(owner);
                }
                return true;
            }
            finally
            {
                _dialogOpen = false;
            }
        }

        internal static bool IsInstalledForSmokeTest()
        {
            return _module != null;
        }
    }
}
