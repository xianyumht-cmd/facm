using System;
using System.Windows.Forms;

namespace FACM.Theming
{
    /// <summary>
    /// Some FACM surfaces already have purpose-built borderless chrome. They do not need the
    /// shared title bar, but normal transient dialogs should still follow the control center's
    /// outside-click contract. Persistent shell/pet surfaces are explicitly excluded.
    /// </summary>
    internal static class FacmBorderlessOutsideClose
    {
        private static bool _installed;

        public static void InstallGlobal()
        {
            if (_installed) return;
            _installed = true;
            Application.Idle += HandleIdle;
        }

        private static void HandleIdle(object sender, EventArgs e)
        {
            var count = Application.OpenForms.Count;
            var forms = new Form[count];
            for (var index = 0; index < count; index++)
                forms[index] = Application.OpenForms[index];

            foreach (var form in forms)
            {
                if (form == null || form.IsDisposed || !form.TopLevel || !form.Visible) continue;
                if (form.FormBorderStyle != FormBorderStyle.None) continue;
                if (IsPersistentSurface(form)) continue;
                FacmWindowChrome.EnableOutsideClose(form);
            }
        }

        private static bool IsPersistentSurface(Form form)
        {
            var type = form.GetType();
            var name = type.Name ?? string.Empty;
            var ns = type.Namespace ?? string.Empty;
            if (string.Equals(name, "MainForm", StringComparison.Ordinal) ||
                string.Equals(name, "CompactMenuForm", StringComparison.Ordinal))
                return true;
            if (ns.StartsWith("FACM.Pets", StringComparison.Ordinal) && name.EndsWith("Window", StringComparison.Ordinal))
                return true;
            return false;
        }
    }
}
