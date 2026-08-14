using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueBuildApplyUiBridge
    {
        private const string MenuName = "FACM.LeagueBuildApply";
        private const string AdvisorMenuName = "FACM.LeagueBuildAdvisor";
        private static readonly FieldInfo TrayField = typeof(MainForm).GetField("_tray", BindingFlags.Instance | BindingFlags.NonPublic);
        private static LeagueBuildAdvisorModule _module;
        private static MainForm _owner;
        private static bool _dialogOpen;
        private static bool _installed;

        public static void Install(LeagueBuildAdvisorModule module)
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
            if (menu.Items.Cast<ToolStripItem>().Any(item => string.Equals(item.Name, MenuName, StringComparison.Ordinal)))
            {
                _owner = owner;
                return;
            }

            var item = new ToolStripMenuItem(
                LeagueAdvisorText.Get(UiTextCatalog.Load(), LeagueBuildApplyUiTextKeys.Menu))
            {
                Name = MenuName
            };
            item.Click += delegate { Open(owner); };
            var advisorIndex = FindIndex(menu, AdvisorMenuName);
            var insertAt = advisorIndex >= 0 ? advisorIndex + 1 : Math.Min(8, menu.Items.Count);
            menu.Items.Insert(Math.Min(insertAt, menu.Items.Count), item);
            _owner = owner;
        }

        private static int FindIndex(ContextMenuStrip menu, string name)
        {
            for (var index = 0; index < menu.Items.Count; index++)
            {
                if (string.Equals(menu.Items[index].Name, name, StringComparison.Ordinal)) return index;
            }
            return -1;
        }

        private static bool HasMenuItem(MainForm owner)
        {
            var tray = TrayField == null ? null : TrayField.GetValue(owner) as NotifyIcon;
            var menu = tray == null ? null : tray.ContextMenuStrip;
            return menu != null && menu.Items.Cast<ToolStripItem>().Any(item => string.Equals(item.Name, MenuName, StringComparison.Ordinal));
        }

        private static void Open(MainForm owner)
        {
            if (_dialogOpen || _module == null || owner == null || owner.IsDisposed) return;
            _dialogOpen = true;
            try
            {
                using (var form = _module.CreateApplyForm(UiTextCatalog.Load()))
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
