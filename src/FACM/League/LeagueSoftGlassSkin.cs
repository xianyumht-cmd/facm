using System.Windows.Forms;
using FACM.Theming;

namespace FACM.League
{
    /// <summary>
    /// League compatibility entry point for the shared FACM design system.
    /// Business forms keep their existing ownership and lifecycle; this layer only applies
    /// compact density to legacy pages, then normalizes material/controls through FACM tokens.
    /// </summary>
    internal static class LeagueSoftGlassSkin
    {
        public static T Apply<T>(T form) where T : Form
        {
            if (form == null || form.IsDisposed) return form;

            // The Hub is now purpose-built at compact density. Legacy business forms still use
            // the conservative density pass so their existing layouts gain space without a rewrite.
            if (!(form is LeagueHubForm))
                LeagueCompactDensity.Apply(form);

            FacmDesignSystem.ApplyLeagueSurface(form);
            return form;
        }
    }
}
