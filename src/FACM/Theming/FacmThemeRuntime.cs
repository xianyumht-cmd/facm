using System;
using System.Drawing;
using System.Windows.Forms;

namespace FACM.Theming
{
    /// <summary>
    /// Process-wide FACM theme state. ThemeCatalog remains the single palette source; this runtime
    /// only publishes the active palette and refreshes already-open FACM-owned WinForms surfaces.
    /// System-owned dialogs/UAC are deliberately outside this boundary.
    /// </summary>
    internal static class FacmThemeRuntime
    {
        private static ThemeDefinition _current = ThemeCatalog.Get(ThemeCatalog.DefaultThemeId);

        public static ThemeDefinition Current
        {
            get { return _current ?? ThemeCatalog.Get(ThemeCatalog.DefaultThemeId); }
        }

        public static event EventHandler ThemeChanged;

        public static void Initialize(string themeId)
        {
            _current = ThemeCatalog.Get(themeId);
        }

        public static void SetCurrent(string themeId)
        {
            var next = ThemeCatalog.Get(themeId);
            var previous = Current;
            _current = next;
            ApplyToOpenForms(previous, next);
            var handler = ThemeChanged;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        public static void RefreshOpenForms()
        {
            ApplyToOpenForms(Current, Current);
        }

        private static void ApplyToOpenForms(ThemeDefinition previous, ThemeDefinition next)
        {
            if (Application.OpenForms.Count == 0) return;
            var forms = new Form[Application.OpenForms.Count];
            for (var index = 0; index < forms.Length; index++) forms[index] = Application.OpenForms[index];

            foreach (var form in forms)
            {
                if (form == null || form.IsDisposed) continue;
                ApplyControl(form, previous, next);
                FacmWindowChrome.RefreshTheme(form);
                form.Invalidate(true);
            }
        }

        private static void ApplyControl(Control control, ThemeDefinition previous, ThemeDefinition next)
        {
            if (control == null || control.IsDisposed) return;

            control.ForeColor = Remap(control.ForeColor, previous, next, false);
            if (control.BackColor != Color.Transparent)
                control.BackColor = Remap(control.BackColor, previous, next, true);

            var button = control as Button;
            if (button != null && !(button is FacmNavButton) && !(button is FacmPillButton))
            {
                button.ForeColor = next.TextPrimary;
                button.FlatAppearance.BorderColor = next.Border;
                button.FlatAppearance.MouseOverBackColor = FacmDesignSystem.SurfaceHover;
                button.FlatAppearance.MouseDownBackColor = FacmDesignSystem.Blend(FacmDesignSystem.SurfaceHover, next.Accent, 0.12F);
            }

            var textBox = control as TextBoxBase;
            if (textBox != null)
            {
                textBox.BackColor = next.Surface;
                textBox.ForeColor = next.TextPrimary;
            }

            var list = control as ListView;
            if (list != null)
            {
                list.BackColor = next.BackgroundSecondary;
                list.ForeColor = next.TextPrimary;
            }

            control.Invalidate();
            foreach (Control child in control.Controls) ApplyControl(child, previous, next);
        }

        private static Color Remap(Color value, ThemeDefinition previous, ThemeDefinition next, bool background)
        {
            if (previous == null || next == null) return value;

            if (value.ToArgb() == previous.TextPrimary.ToArgb()) return next.TextPrimary;
            if (value.ToArgb() == previous.TextMuted.ToArgb()) return next.TextMuted;
            if (value.ToArgb() == previous.Accent.ToArgb()) return next.Accent;
            if (value.ToArgb() == previous.AccentSecondary.ToArgb()) return next.AccentSecondary;
            if (value.ToArgb() == previous.Success.ToArgb()) return next.Success;
            if (value.ToArgb() == previous.Warning.ToArgb()) return next.Warning;

            if (!background) return value;
            if (value.ToArgb() == previous.Background.ToArgb()) return next.Background;
            if (value.ToArgb() == previous.BackgroundSecondary.ToArgb()) return next.BackgroundSecondary;
            if (value.ToArgb() == previous.Surface.ToArgb()) return next.Surface;
            if (value.ToArgb() == previous.SurfaceSecondary.ToArgb()) return next.SurfaceSecondary;
            if (value.ToArgb() == previous.Border.ToArgb()) return next.Border;
            return value;
        }
    }
}
