using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueDashboardUiBridge
    {
        private const string MenuName = "FACM.LeagueDashboard";
        private static readonly FieldInfo TrayField = typeof(MainForm).GetField("_tray", BindingFlags.Instance | BindingFlags.NonPublic);
        private static LeagueDashboardModule _module;
        private static MainForm _owner;
        private static bool _dialogOpen;
        private static bool _installed;

        public static void Install(LeagueDashboardModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            _module = module;
            if (_installed) return;
            _installed = true;
            Application.Idle += AttachWhenReady;
            Application.ApplicationExit += delegate { Uninstall(); };
        }

        public static void Uninstall()
        {
            if (!_installed) return;
            _installed = false;
            Application.Idle -= AttachWhenReady;
            _owner = null;
            _module = null;
        }

        internal static bool HasTrayAccessForSmokeTest()
        {
            return TrayField != null && TrayField.FieldType == typeof(NotifyIcon);
        }

        private static void AttachWhenReady(object sender, EventArgs e)
        {
            if (_module == null) return;
            var owner = Application.OpenForms.OfType<MainForm>().FirstOrDefault(form => !form.IsDisposed);
            if (owner == null) return;
            if (ReferenceEquals(owner, _owner) && HasMenuItem(owner)) return;

            var tray = TrayField == null ? null : TrayField.GetValue(owner) as NotifyIcon;
            var menu = tray == null ? null : tray.ContextMenuStrip;
            if (menu == null || menu.IsDisposed) return;

            ShellMenuGroups.AddLeagueAction(
                menu,
                MenuName,
                UiTextRuntime.Text(UiTextKeys.LeagueDashboardMenu),
                ShellMenuGroups.DashboardOrder,
                delegate { Open(owner); });
            _owner = owner;
        }

        private static bool HasMenuItem(MainForm owner)
        {
            var tray = TrayField == null ? null : TrayField.GetValue(owner) as NotifyIcon;
            var menu = tray == null ? null : tray.ContextMenuStrip;
            return ShellMenuGroups.HasLeagueAction(menu, MenuName);
        }

        private static void Open(MainForm owner)
        {
            if (_dialogOpen || _module == null || owner == null || owner.IsDisposed) return;
            _dialogOpen = true;
            try
            {
                using (var form = _module.CreateDashboardForm(UiTextCatalog.Load()))
                {
                    form.TopMost = true;
                    form.ShowDialog(owner);
                }
            }
            finally
            {
                _dialogOpen = false;
            }
        }
    }
}
