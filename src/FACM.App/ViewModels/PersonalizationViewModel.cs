using System.ComponentModel;
using System.Runtime.CompilerServices;
using FACM.Core.Personalization;
using FACM.Core.Settings;

namespace FACM.App.ViewModels;

public sealed class PersonalizationViewModel : INotifyPropertyChanged
{
    private readonly ISettings2Repository _settings;
    private readonly IFacmThemeRuntime _themes;
    private DesktopPetPreferenceService? _desktopPets;
    private FacmThemeDefinition _selectedTheme = FacmThemeCatalog.Get(FacmThemeCatalog.DefaultThemeId);
    private FacmPetDefinition _selectedPet = FacmPetCatalog.Get(FacmPetCatalog.DefaultPetId);
    private bool _isPetEnabled;
    private bool _isBusy;
    private bool _isRecoveryReadOnly;
    private bool _initialized;
    private string _status = string.Empty;

    public PersonalizationViewModel(ISettings2Repository settings, IFacmThemeRuntime themes)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _themes = themes ?? throw new ArgumentNullException(nameof(themes));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<FacmThemeDefinition> ThemeOptions => FacmThemeCatalog.All;
    public IReadOnlyList<FacmPetDefinition> PetOptions => FacmPetCatalog.Visible;

    public FacmThemeDefinition SelectedTheme
    {
        get => _selectedTheme;
        private set => SetField(ref _selectedTheme, value);
    }

    public FacmPetDefinition SelectedPet
    {
        get => _selectedPet;
        private set => SetField(ref _selectedPet, value);
    }

    public bool IsPetEnabled
    {
        get => _isPetEnabled;
        private set => SetField(ref _isPetEnabled, value);
    }

    public bool CanControlDesktopPet => _desktopPets is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public bool IsRecoveryReadOnly
    {
        get => _isRecoveryReadOnly;
        private set => SetField(ref _isRecoveryReadOnly, value);
    }

    public bool IsInitialized => _initialized;

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public void ConfigureDesktopPetService(DesktopPetPreferenceService desktopPets)
    {
        _desktopPets = desktopPets ?? throw new ArgumentNullException(nameof(desktopPets));
        OnPropertyChanged(nameof(CanControlDesktopPet));
    }

    public void InitializeForStartup()
    {
        if (_initialized || IsBusy) return;
        IsBusy = true;
        try
        {
            var loaded = _settings.LoadAsync().GetAwaiter().GetResult();
            ApplyLoadedSettings(loaded);
        }
        catch
        {
            SelectedTheme = FacmThemeCatalog.Get(FacmThemeCatalog.DefaultThemeId);
            SelectedPet = FacmPetCatalog.Get(FacmPetCatalog.DefaultPetId);
            IsPetEnabled = false;
            _themes.Apply(SelectedTheme);
            Status = "fallback-default";
        }
        finally
        {
            _initialized = true;
            OnPropertyChanged(nameof(IsInitialized));
            IsBusy = false;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || IsBusy) return;
        IsBusy = true;
        try
        {
            var loaded = await _settings.LoadAsync(cancellationToken);
            ApplyLoadedSettings(loaded);
        }
        finally
        {
            _initialized = true;
            OnPropertyChanged(nameof(IsInitialized));
            IsBusy = false;
        }
    }

    public async Task<bool> SelectThemeAsync(string? themeId, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return false;
        var selected = FacmThemeCatalog.Get(themeId);
        IsBusy = true;
        try
        {
            // Persist the user's narrow theme intent before changing the live theme in normal mode.
            // In recovery mode the mutation contract deliberately returns session-only without touching
            // the damaged primary settings file.
            var updated = await _settings.UpdateAsync(
                settings => settings.Appearance.ThemeId = selected.Id,
                allowRecoveryRebuild: false,
                cancellationToken);
            IsRecoveryReadOnly = !updated.Persisted;
            SelectedTheme = selected;
            _themes.Apply(selected);
            Status = updated.Persisted ? "saved" : "applied-session-only";
            return true;
        }
        catch (OperationCanceledException)
        {
            Status = "cancelled";
            throw;
        }
        catch
        {
            Status = "failed";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> SelectPetAsync(string? petId, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return false;
        var selected = FacmPetCatalog.Get(petId);
        if (!selected.ShowInPicker) selected = FacmPetCatalog.Get(FacmPetCatalog.DefaultPetId);

        IsBusy = true;
        try
        {
            var updated = await _settings.UpdateAsync(
                settings => settings.Pets.StyleId = selected.Id,
                allowRecoveryRebuild: false,
                cancellationToken);
            IsRecoveryReadOnly = !updated.Persisted;
            SelectedPet = selected;
            IsPetEnabled = updated.Settings.Pets.Enabled;
            Status = updated.Persisted ? "pet-saved" : "pet-session-only";
            return true;
        }
        catch (OperationCanceledException)
        {
            Status = "cancelled";
            throw;
        }
        catch
        {
            Status = "failed";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> InitializeDesktopPetAsync(CancellationToken cancellationToken = default)
    {
        var service = _desktopPets;
        if (service is null || IsBusy) return false;
        IsBusy = true;
        try
        {
            var snapshot = await service.InitializeAsync(cancellationToken);
            ApplyDesktopPetSnapshot(snapshot);
            return true;
        }
        catch (OperationCanceledException)
        {
            Status = "cancelled";
            throw;
        }
        catch
        {
            IsPetEnabled = false;
            Status = "pet-runtime-failed";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> EnableSelectedPetAsync(CancellationToken cancellationToken = default)
    {
        var service = _desktopPets;
        if (service is null || IsBusy) return false;
        IsBusy = true;
        try
        {
            var snapshot = await service.SelectPetAsync(SelectedPet.Id, cancellationToken);
            ApplyDesktopPetSnapshot(snapshot);
            return snapshot.Enabled;
        }
        catch (OperationCanceledException)
        {
            Status = "cancelled";
            throw;
        }
        catch
        {
            IsPetEnabled = false;
            Status = "pet-runtime-failed";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RestoreDefaultLauncherAsync(CancellationToken cancellationToken = default)
    {
        var service = _desktopPets;
        if (service is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var snapshot = await service.RestoreDefaultLauncherAsync(cancellationToken);
            ApplyDesktopPetSnapshot(snapshot);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ResetDesktopPositionAsync(CancellationToken cancellationToken = default)
    {
        var service = _desktopPets;
        if (service is null || IsBusy) return;
        IsBusy = true;
        try
        {
            await service.ResetPositionAsync(cancellationToken);
            Status = "desktop-position-reset";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyLoadedSettings(Settings2LoadResult loaded)
    {
        IsRecoveryReadOnly = IsRecoveryOrigin(loaded.Origin);
        SelectedTheme = FacmThemeCatalog.Get(loaded.Settings.Appearance.ThemeId);
        SelectedPet = FacmPetCatalog.Get(loaded.Settings.Pets.StyleId);
        IsPetEnabled = loaded.Settings.Pets.Enabled;
        _themes.Apply(SelectedTheme);
        Status = IsRecoveryReadOnly ? "recovery-read-only" : "ready";
    }

    private void ApplyDesktopPetSnapshot(DesktopPetPreferenceSnapshot snapshot)
    {
        SelectedPet = snapshot.Pet;
        IsPetEnabled = snapshot.Enabled;
        IsRecoveryReadOnly = snapshot.RecoveryReadOnly;
        Status = snapshot.Detail;
    }

    private static bool IsRecoveryOrigin(SettingsLoadOrigin origin) =>
        origin is SettingsLoadOrigin.RecoveredLastKnownGood or SettingsLoadOrigin.RecoveryDefaults;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
