using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueHubUiBridge
    {
        private static readonly MethodInfo ShowViewMethod = typeof(LeagueHubForm).GetMethod(
            "ShowView", BindingFlags.Instance | BindingFlags.NonPublic);
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

            QueueOpen(owner, string.Empty);
        }

        public static void RequestOpen(MainForm owner, string viewId)
        {
            if (_dialogOpen || _openPending || _module == null || owner == null || owner.IsDisposed || !owner.IsHandleCreated) return;
            QueueOpen(owner, NormalizeViewId(viewId));
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
                    if (!string.IsNullOrWhiteSpace(requestedViewId) && ShowViewMethod != null)
                    {
                        form.Shown += delegate
                        {
                            try
                            {
                                ShowViewMethod.Invoke(form, new object[] { requestedViewId, true });
                            }
                            catch (TargetInvocationException exception)
                            {
                                AppLog.Error("LOL Hub contextual navigation failed", exception.InnerException ?? exception);
                            }
                            catch (Exception exception)
                            {
                                AppLog.Error("LOL Hub contextual navigation failed", exception);
                            }
                        };
                    }
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
            return ShowViewMethod != null;
        }
    }
}
