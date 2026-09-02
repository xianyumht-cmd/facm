using FACM.Core.Personalization;
using FACM.Core.Settings;

internal static class PersonalizationSmoke
{
    public static void Run()
    {
        VerifyCatalogs();
        VerifyDesktopPetPreferenceFallbackAsync().GetAwaiter().GetResult();
    }

    private static void VerifyCatalogs()
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

    private static async Task VerifyDesktopPetPreferenceFallbackAsync()
    {
        var disabledSettings = Settings2Document.CreateDefault();
        var disabledRepo = new FakeSettingsRepository(disabledSettings, SettingsLoadOrigin.ExistingV2);
        var disabledRuntime = new FakeDesktopPetRuntime();
        var disabledService = new DesktopPetPreferenceService(disabledRepo, disabledRuntime);
        var disabled = await disabledService.InitializeAsync();
        True(!disabled.Enabled, "disabled preference keeps default launcher");
        Equal(1, disabledRuntime.ApplyCalls, "disabled preference runtime call count");
        True(!disabledRuntime.LastEnabled, "disabled preference runtime state");
        Equal(0, disabledRepo.SaveCalls, "disabled preference must not rewrite settings");

        var failingSettings = Settings2Document.CreateDefault();
        failingSettings.Pets.Enabled = true;
        failingSettings.Pets.StyleId = "vpet";
        var failingRepo = new FakeSettingsRepository(failingSettings, SettingsLoadOrigin.ExistingV2);
        var failingRuntime = new FakeDesktopPetRuntime { FailNextStart = true };
        var failingService = new DesktopPetPreferenceService(failingRepo, failingRuntime);
        var failed = await failingService.InitializeAsync();
        True(!failed.Enabled, "failed pet startup restores default launcher");
        True(!failingRepo.Document.Pets.Enabled, "failed pet startup repairs enabled preference");
        Equal(1, failingRepo.SaveCalls, "failed pet startup persists fallback");
        Equal(2, failingRuntime.ApplyCalls, "failed pet startup invokes explicit stop fallback");

        var runtimeLossSettings = Settings2Document.CreateDefault();
        runtimeLossSettings.Pets.Enabled = true;
        runtimeLossSettings.Pets.StyleId = "vpet";
        var runtimeLossRepo = new FakeSettingsRepository(runtimeLossSettings, SettingsLoadOrigin.ExistingV2);
        var runtimeLossRuntime = new FakeDesktopPetRuntime();
        var runtimeLossService = new DesktopPetPreferenceService(runtimeLossRepo, runtimeLossRuntime);
        var runtimeReady = await runtimeLossService.InitializeAsync();
        True(runtimeReady.Enabled, "ready pet startup keeps enabled preference");
        runtimeLossRuntime.TriggerRuntimeFailure();
        True(!runtimeLossRepo.Document.Pets.Enabled, "post-ready runtime failure repairs enabled preference");
        Equal(1, runtimeLossRepo.SaveCalls, "post-ready runtime failure persists launcher fallback");

        var recoverySettings = Settings2Document.CreateDefault();
        recoverySettings.Pets.StyleId = "greenfly";
        var recoveryRepo = new FakeSettingsRepository(recoverySettings, SettingsLoadOrigin.RecoveredLastKnownGood);
        var recoveryRuntime = new FakeDesktopPetRuntime();
        var recoveryService = new DesktopPetPreferenceService(recoveryRepo, recoveryRuntime);
        var recoverySelection = await recoveryService.SelectPetAsync("vpet");
        True(recoverySelection.Enabled, "recovery mode may enable pet for current session");
        True(recoverySelection.RecoveryReadOnly, "recovery mode surfaced as read-only");
        Equal(0, recoveryRepo.SaveCalls, "recovery mode pet choice must not overwrite damaged settings");

        var resetSettings = Settings2Document.CreateDefault();
        resetSettings.Pets.BallX = 123;
        resetSettings.Pets.BallY = 456;
        resetSettings.Pets.Enabled = true;
        var resetRepo = new FakeSettingsRepository(resetSettings, SettingsLoadOrigin.ExistingV2);
        var resetRuntime = new FakeDesktopPetRuntime();
        var resetService = new DesktopPetPreferenceService(resetRepo, resetRuntime);
        await resetService.RestoreDefaultLauncherAsync();
        True(!resetRepo.Document.Pets.Enabled, "restore default launcher disables pet preference");
        await resetService.ResetPositionAsync();
        Equal(int.MinValue, resetRepo.Document.Pets.BallX, "reset position BallX sentinel");
        Equal(int.MinValue, resetRepo.Document.Pets.BallY, "reset position BallY sentinel");
        Equal(1, resetRuntime.ResetCalls, "reset position runtime call count");
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

    private sealed class FakeSettingsRepository(Settings2Document document, SettingsLoadOrigin origin) : ISettings2Repository
    {
        public Settings2Document Document { get; private set; } = document;
        public int SaveCalls { get; private set; }

        public Task<Settings2LoadResult> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Settings2LoadResult(Document, origin));
        }

        public Task SaveAsync(Settings2Document settings, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Document = settings;
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDesktopPetRuntime : IDesktopPetRuntime
    {
        public DesktopPetRuntimeState Current { get; private set; } = new(false, false, string.Empty, "default-launcher");
        public event EventHandler<DesktopPetRuntimeState>? StateChanged;
        public bool FailNextStart { get; set; }
        public int ApplyCalls { get; private set; }
        public int ResetCalls { get; private set; }
        public bool LastEnabled { get; private set; }

        public Task<DesktopPetModeResult> ApplyAsync(bool enabled, FacmPetDefinition pet, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCalls++;
            LastEnabled = enabled;
            if (enabled && FailNextStart)
            {
                FailNextStart = false;
                Current = new(false, false, string.Empty, "planned-failure");
                StateChanged?.Invoke(this, Current);
                return Task.FromResult(new DesktopPetModeResult(false, false, "planned-failure"));
            }

            Current = enabled
                ? new(true, true, pet.Id, "ready")
                : new(false, false, string.Empty, "default-launcher");
            StateChanged?.Invoke(this, Current);
            return Task.FromResult(new DesktopPetModeResult(true, Current.PetVisible, Current.Detail));
        }

        public void TriggerRuntimeFailure()
        {
            Current = new(false, false, string.Empty, "runtime-failed:planned-exit");
            StateChanged?.Invoke(this, Current);
        }

        public Task ResetPositionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetCalls++;
            return Task.CompletedTask;
        }
    }
}
