using FACM.Core.Personalization;
using FACM.Core.Settings;

internal static class PersonalizationSmoke
{
    public static void Run()
    {
        Equal(10, FacmThemeCatalog.All.Count, "stable theme count");
        Equal(FacmThemeCatalog.DefaultThemeId, "glass-blue", "stable default theme id");
        Equal("glass-blue", FacmThemeCatalog.Get("unknown-theme").Id, "unknown theme fallback");
        Equal(10, FacmThemeCatalog.All.Select(theme => theme.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "unique theme ids");
        True(FacmThemeCatalog.All.All(theme => !string.IsNullOrWhiteSpace(theme.Name)), "theme names");
        True(FacmThemeCatalog.All.Any(theme => theme.IsLight && theme.Id == "cloud-light"), "light theme contract");

        Equal("greenfly", FacmPetCatalog.DefaultPetId, "stable default pet id");
        Equal("greenfly", FacmPetCatalog.Get("unknown-pet").Id, "unknown pet fallback");
        True(FacmPetCatalog.Visible.Any(pet => pet.Id == "vpet" && pet.Runtime == FacmPetRuntimeKind.VPetCore), "visible VPet Core route");
        True(FacmPetCatalog.Visible.Any(pet => pet.Id == "greenfly" && pet.Runtime == FacmPetRuntimeKind.FlyingSprite), "visible flying sprite route");
        True(FacmPetCatalog.Contains("cat"), "legacy pet id compatibility");
        True(FacmPetCatalog.Visible.All(pet => pet.Id != "cat"), "legacy pet hidden from picker");

        var defaults = Settings2Document.CreateDefault();
        Equal(FacmThemeCatalog.DefaultThemeId, defaults.Appearance.ThemeId, "Settings 2.0 default theme alignment");
        Equal(FacmPetCatalog.DefaultPetId, defaults.Pets.StyleId, "Settings 2.0 default pet alignment");
        True(!defaults.Pets.Enabled, "new installs must not auto-enable desktop pet");
        True(Settings2Validator.Validate(defaults).IsValid, "personalization defaults must validate");

        var invalidTheme = Settings2Document.CreateDefault();
        invalidTheme.Appearance.ThemeId = "not-a-theme";
        True(!Settings2Validator.Validate(invalidTheme).IsValid, "unsupported theme rejection");
        var invalidPet = Settings2Document.CreateDefault();
        invalidPet.Pets.StyleId = "not-a-pet";
        True(!Settings2Validator.Validate(invalidPet).IsValid, "unsupported pet rejection");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }
}
