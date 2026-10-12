using System;
using System.Drawing;
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

            // Prepare is deferred until Load. Hub-embedded pages have TopLevel=false by then and
            // are intentionally skipped, while the Hub itself and standalone quick surfaces get
            // the shared FACM title bar and outside-click behavior.
            FacmWindowChrome.Prepare(form);

            // The Hub is now purpose-built at compact density. Legacy business forms still use
            // the conservative density pass so their existing layouts gain space without a rewrite.
            if (!(form is LeagueHubForm))
                LeagueCompactDensity.Apply(form);

            var efficiency = form as LeagueEfficiencyForm;
            if (efficiency != null && efficiency.Controls.Count > 0)
            {
                var scrollRoot = efficiency.Controls[0] as TableLayoutPanel;
                if (scrollRoot != null)
                    ConfigureEfficiencyScrollSurfaceForSmokeTest(scrollRoot);
            }

            FacmDesignSystem.ApplyLeagueSurface(form);
            return form;
        }

        internal static void ConfigureEfficiencyScrollSurfaceForSmokeTest(TableLayoutPanel scrollRoot)
        {
            if (scrollRoot == null) throw new ArgumentNullException(nameof(scrollRoot));

            // LeagueEfficiencyForm uses fixed-height business rows followed by a Percent spacer.
            // TableLayoutPanel does not reliably infer a vertical scroll extent from that mixture
            // once the form is embedded and Dock=Fill. Derive the extent from the actual compacted
            // absolute rows instead of hard-coding a screen or DPI-specific height.
            var height = Math.Max(0, scrollRoot.Padding.Vertical);
            foreach (RowStyle row in scrollRoot.RowStyles)
            {
                if (row.SizeType != SizeType.Absolute) continue;
                height += Math.Max(0, (int)Math.Ceiling(row.Height));
            }

            scrollRoot.AutoScroll = true;
            scrollRoot.AutoScrollMinSize = new Size(0, height);
        }
    }
}
