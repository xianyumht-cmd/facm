using System.ComponentModel;
using System.Runtime.CompilerServices;
using FACM.Core.Online;
using FACM.Core.Settings;

namespace FACM.App.ViewModels;

public sealed class MaintenanceViewModel : INotifyPropertyChanged
{
    private readonly ISettings2Repository _settings;
    private readonly IUpdateManifestSource _updates;
    private readonly IAnnouncementSource _announcements;
    private readonly Version _currentVersion;
    private bool _initialized;
    private bool _isBusy;
    private bool _autoUpdateEnabled = true;
    private bool _loadedFromRecovery;
    private string _status = "not-initialized";
    private string _lastAnnouncementId = string.Empty;
    private UpdateDecision? _update;
    private UpdateManifestSnapshot? _manifest;
    private AnnouncementSnapshot? _announcement;

    public MaintenanceViewModel(
        ISettings2Repository settings,
        IUpdateManifestSource updates,
        IAnnouncementSource announcements,
        Version currentVersion)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _announcements = announcements ?? throw new ArgumentNullException(nameof(announcements));
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsInitialized => _initialized;
    public string CurrentVersion => _currentVersion.ToString();

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
            var loaded = await _settings.LoadAsync(cancellationToken);
            AutoUpdateEnabled = loaded.Settings.Online.AutoUpdateEnabled;
            _lastAnnouncementId = loaded.Settings.Online.LastAnnouncementId;
            LoadedFromRecovery = IsRecoveryOrigin(loaded.Origin);
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
            var loaded = await _settings.LoadAsync(cancellationToken);
            loaded.Settings.Online.AutoUpdateEnabled = enabled;
            await _settings.SaveAsync(loaded.Settings, cancellationToken);
            AutoUpdateEnabled = enabled;
            LoadedFromRecovery = false;
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

    // Manual check is deliberately independent from AutoUpdateEnabled. The toggle only controls
    // automatic startup network access; an explicit user request must always be honored.
    public async Task<UpdateDecision> ManualCheckAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return Update ?? UpdateDecisionService.Evaluate(_currentVersion, null);
        IsBusy = true;
        Status = "checking";
        try
        {
            Manifest = await _updates.GetAsync(cancellationToken);
            Update = UpdateDecisionService.Evaluate(_currentVersion, Manifest);
            Status = Update.Reason;
            return Update;
        }
        catch (OperationCanceledException)
        {
            Status = "cancelled";
            throw;
        }
        catch
        {
            Manifest = null;
            Update = UpdateDecisionService.Evaluate(_currentVersion, null);
            Status = Update.Reason;
            return Update;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> RefreshAnnouncementAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var announcement = await _announcements.GetAsync(cancellationToken);
            Announcement = announcement is null
                ? null
                : announcement with { LinkUrl = OnlineUriPolicy.NormalizeAbsoluteHttpsString(announcement.LinkUrl) };
            return Announcement is not null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            Announcement = null;
            return false;
        }
    }

    public async Task<bool> MarkAnnouncementSeenAsync(CancellationToken cancellationToken = default)
    {
        var id = Announcement?.Id?.Trim() ?? string.Empty;
        if (id.Length == 0 || string.Equals(id, _lastAnnouncementId, StringComparison.Ordinal)) return true;
        try
        {
            var loaded = await _settings.LoadAsync(cancellationToken);
            loaded.Settings.Online.LastAnnouncementId = id;
            await _settings.SaveAsync(loaded.Settings, cancellationToken);
            _lastAnnouncementId = id;
            LoadedFromRecovery = false;
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
