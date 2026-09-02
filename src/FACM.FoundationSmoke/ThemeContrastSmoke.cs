using FACM.Core.Personalization;

internal static class ThemeContrastSmoke
{
    public static void Run()
    {
        foreach (var theme in FacmThemeCatalog.All)
        {
            Check(theme, theme.Accent, "accent");
            Check(theme, theme.SurfaceSecondary, "surface-secondary");
            Check(theme, theme.Success, "success");
            Check(theme, theme.Warning, "warning");
        }
    }

    private static void Check(FacmThemeDefinition theme, string background, string surface)
    {
        var foreground = FacmThemeContrast.ReadableForeground(background);
        if (FacmThemeContrast.ContrastRatio(foreground, background) < 4.5d)
            throw new InvalidOperationException($"{theme.Id} {surface} contrast is below 4.5:1.");
    }
}
