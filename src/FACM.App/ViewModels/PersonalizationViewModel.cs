using System.ComponentModel;
using System.Runtime.CompilerServices;
using FACM.Core.Personalization;
using FACM.Core.Settings;

namespace FACM.App.ViewModels;

public sealed class PersonalizationViewModel : INotifyPropertyChanged
{
    private readonly ISettings2Repository _settings;
    private readonly IFacmThemeRuntime _themes;
    private FacmThemeDefinition _selectedTheme = FacmThemeCatalog.Get(FacmThemeCatalog.DefaultThemeId);
    private bool _isBusy;
    private bool _isRecoveryReadOnly;
    private string _status = string.Empty;

    public PersonalizationViewModel(ISettings2Repository settings, IFacmThemeRuntime themes)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _themes = themes ?? throw new ArgumentNullException(nameof(themes));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<FacmThemeDefinition> ThemeOptions => FacmThemeCatalog.All;

    public FacmThemeDefinition SelectedTheme
    {
        get => _selectedTheme;
        private set => SetField(ref _selectedTheme, value);
    }

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

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var loaded = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            IsRecoveryReadOnly = IsRecoveryOrigin(loaded.Origin);
            SelectedTheme = FacmThemeCatalog.Get(loaded.Settings.Appearance.ThemeId);
            _themes.Apply(SelectedTheme);
            Status = IsRecoveryReadOnly ? "recovery-read-only" : "ready";
        }
        finally
        {
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
            var loaded = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            IsRecoveryReadOnly = IsRecoveryOrigin(loaded.Origin);
            SelectedTheme = selected;
            _themes.Apply(selected);

            if (IsRecoveryReadOnly)
            {
                Status = "applied-session-only";
                return true;
            }

            loaded.Settings.Appearance.ThemeId = selected.Id;
            await _settings.SaveAsync(loaded.Settings, cancellationToken).ConfigureAwait(false);
            Status = "saved";
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

    private static bool IsRecoveryOrigin(SettingsLoadOrigin origin) =>
        origin is SettingsLoadOrigin.RecoveredLastKnownGood or SettingsLoadOrigin.RecoveryDefaults;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
