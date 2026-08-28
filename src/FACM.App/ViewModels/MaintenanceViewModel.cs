using System.ComponentModel;
using System.Runtime.CompilerServices;
using FACM.Core.Online;

namespace FACM.App.ViewModels;

public sealed class MaintenanceViewModel : INotifyPropertyChanged
{
    private readonly MaintenanceApplicationService _service;
    private bool _initialized;
    private bool _isBusy;
    private bool _autoUpdateEnabled = true;
    private bool _loadedFromRecovery;
    private string _status = "not-initialized";
    private string _lastAnnouncementId = string.Empty;
    private UpdateDecision? _update;
    private UpdateManifestSnapshot? _manifest;
    private AnnouncementSnapshot? _announcement;

    public MaintenanceViewModel(MaintenanceApplicationService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsInitialized => _initialized;
    public string CurrentVersion => _service.CurrentVersion.ToString();

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public bool AutoUpdateEnabled
    {
        get => _autoUpdateEnabled;
        private set => SetField(ref _autoUpdateEnabled, value);
    }

    public bool LoadedFromRecovery
    {
        get => _loadedFromRecovery;
        private set => SetField(ref _loadedFromRecovery, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public UpdateDecision? Update
    {
        get => _update;
        private set
        {
            if (!SetField(ref _update, value)) return;
            OnPropertyChanged(nameof(LatestVersion));
            OnPropertyChanged(nameof(UpdateAvailable));
            OnPropertyChanged(nameof(ForceUpdateRequired));
        }
    }

    public UpdateManifestSnapshot? Manifest
    {
        get => _manifest;
        private set
        {
            if (!SetField(ref _manifest, value)) return;
            OnPropertyChanged(nameof(ReleaseNotes));
        }
    }

    public AnnouncementSnapshot? Announcement
    {
        get => _announcement;
        private set
        {
            if (!SetField(ref _announcement, value)) return;
            OnPropertyChanged(nameof(HasAnnouncement));
            OnPropertyChanged(nameof(AnnouncementDetailUri));
            OnPropertyChanged(nameof(IsAnnouncementNew));
        }
    }

    public string LatestVersion => Update?.LatestVersion?.ToString() ?? string.Empty;
    public bool UpdateAvailable => Update?.UpdateAvailable == true;
    public bool ForceUpdateRequired => Update?.ForceUpdateRequired == true;
    public string ReleaseNotes => Manifest?.ReleaseNotes ?? string.Empty;
    public bool HasAnnouncement => Announcement is { Enabled: true };
    public Uri? AnnouncementDetailUri => Announcement?.DetailUri;
    public bool IsAnnouncementNew => HasAnnouncement &&
        !string.IsNullOrWhiteSpace(Announcement!.Id) &&
        !string.Equals(Announcement.Id, _lastAnnouncementId, StringComparison.Ordinal);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || IsBusy) return;
        IsBusy = true;
        try
        {
            ApplyPreferences(await _service.LoadPreferencesAsync(cancellationToken));
            Status = LoadedFromRecovery ? "recovery-loaded-no-save" : "ready";
        }
        finally
        {
            _initialized = true;
            OnPropertyChanged(nameof(IsInitialized));
            IsBusy = false;
        }
    }

    public async Task<bool> SetAutoUpdateEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return false;
        IsBusy = true;
        try
        {
            ApplyPreferences(await _service.SetAutoUpdateEnabledAsync(enabled, cancellationToken));
            Status = "auto-update-saved";
            return true;
        }
        catch (OperationCanceledException)
        {
            Status = "cancelled";
            throw;
        }
        catch
        {
            Status = "settings-save-failed";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<UpdateDecision> ManualCheckAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return Update ?? UpdateDecisionService.Evaluate(_service.CurrentVersion, null);
        IsBusy = true;
        Status = "checking";
        try
        {
            var result = await _service.CheckNowAsync(cancellationToken);
            Manifest = result.Manifest;
            Update = result.Decision;
            Status = result.Decision.Reason;
            return result.Decision;
        }
        catch (OperationCanceledException)
        {
            Status = "cancelled";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> RefreshAnnouncementAsync(CancellationToken cancellationToken = default)
    {
        Announcement = await _service.GetAnnouncementAsync(cancellationToken);
        return Announcement is not null;
    }

    public async Task<bool> MarkAnnouncementSeenAsync(CancellationToken cancellationToken = default)
    {
        var id = Announcement?.Id?.Trim() ?? string.Empty;
        if (id.Length == 0 || string.Equals(id, _lastAnnouncementId, StringComparison.Ordinal)) return true;
        try
        {
            ApplyPreferences(await _service.MarkAnnouncementSeenAsync(id, cancellationToken));
            OnPropertyChanged(nameof(IsAnnouncementNew));
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyPreferences(MaintenancePreferences preferences)
    {
        AutoUpdateEnabled = preferences.AutoUpdateEnabled;
        _lastAnnouncementId = preferences.LastAnnouncementId;
        LoadedFromRecovery = preferences.LoadedFromRecovery;
        OnPropertyChanged(nameof(IsAnnouncementNew));
    }

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
