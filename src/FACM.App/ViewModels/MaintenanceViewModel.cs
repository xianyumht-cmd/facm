using System.ComponentModel;
using System.Runtime.CompilerServices;
using FACM.Core.Maintenance;
using FACM.Core.Online;

namespace FACM.App.ViewModels;

public sealed class MaintenanceViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MaintenanceApplicationService _service;
    private readonly IPreparedUpdateInstaller? _installer;
    private readonly ILogFileOpener? _logOpener;
    private bool _initialized;
    private bool _isBusy;
    private bool _autoUpdateEnabled = true;
    private bool _loadedFromRecovery;
    private string _status = "not-initialized";
    private string _lastAnnouncementId = string.Empty;
    private UpdateDecision? _update;
    private UpdateManifestSnapshot? _manifest;
    private AnnouncementSnapshot? _announcement;
    private PreparedUpdatePackage? _preparedUpdate;
    private CancellationTokenSource? _downloadCancellation;
    private int _activeInstallerOperations;
    private int _installerDisposed;
    private int _updateProgressPercent;
    private string _updateProgressStage = string.Empty;
    private bool _disposed;

    public MaintenanceViewModel(MaintenanceApplicationService service)
        : this(service, null, null)
    {
    }

    public MaintenanceViewModel(
        MaintenanceApplicationService service,
        IPreparedUpdateInstaller? installer,
        ILogFileOpener? logOpener)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _installer = installer;
        _logOpener = logOpener;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsInitialized => _initialized;
    public string CurrentVersion => _service.CurrentVersion.ToString();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanPrepareUpdate));
        }
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
            OnPropertyChanged(nameof(CanPrepareUpdate));
        }
    }

    public UpdateManifestSnapshot? Manifest
    {
        get => _manifest;
        private set
        {
            if (!SetField(ref _manifest, value)) return;
            OnPropertyChanged(nameof(ReleaseNotes));
            OnPropertyChanged(nameof(CanPrepareUpdate));
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
    public bool CanOpenLog => _logOpener is not null;
    public bool CanPrepareUpdate => _installer is not null && Manifest is not null && UpdateAvailable && !IsBusy;
    public bool HasPreparedUpdate => _preparedUpdate is not null;
    public int UpdateProgressPercent
    {
        get => _updateProgressPercent;
        private set => SetField(ref _updateProgressPercent, Math.Clamp(value, 0, 100));
    }
    public string UpdateProgressStage
    {
        get => _updateProgressStage;
        private set => SetField(ref _updateProgressStage, value ?? string.Empty);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_initialized || IsBusy) return;
        IsBusy = true;
        try
        {
            ApplyPreferences(await _service.LoadPreferencesAsync(cancellationToken));
            _initialized = true;
            OnPropertyChanged(nameof(IsInitialized));
            Status = LoadedFromRecovery ? "recovery-loaded-no-save" : "ready";
        }
        catch (OperationCanceledException)
        {
            Status = "cancelled";
            throw;
        }
        catch
        {
            // A transient settings/filesystem failure must not permanently poison the presenter. Keep
            // IsInitialized=false so startup or the More Settings surface can retry in the same process.
            Status = "initialization-failed";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> SetAutoUpdateEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
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
        ThrowIfDisposed();
        if (IsBusy) return Update ?? UpdateDecisionService.Evaluate(_service.CurrentVersion, null);
        IsBusy = true;
        Status = "checking";
        try
        {
            var result = await _service.CheckNowAsync(cancellationToken);
            Manifest = result.Manifest;
            Update = result.Decision;
            _preparedUpdate = null;
            OnPropertyChanged(nameof(HasPreparedUpdate));
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
        ThrowIfDisposed();
        Announcement = await _service.GetAnnouncementAsync(cancellationToken);
        return Announcement is not null;
    }

    public async Task<bool> MarkAnnouncementSeenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
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

    public async Task<LogOpenResult> OpenLogAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_logOpener is null) return new LogOpenResult(false, string.Empty, "log-opener-unavailable");
        var result = await _logOpener.OpenAsync(cancellationToken);
        Status = result.Started ? "log-opened" : result.Reason;
        return result;
    }

    public async Task<bool> PrepareUpdateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_installer is null || Manifest is null || !UpdateAvailable || IsBusy) return false;
        IsBusy = true;
        Status = "update-downloading";
        UpdateProgressPercent = 0;
        UpdateProgressStage = "connecting";

        // The in-flight operation owns the linked CTS. Shutdown may cancel it, but only this method's
        // finally disposes it after installer awaits have unwound. This avoids disposing a token source
        // underneath callbacks/registrations still used by the downloader.
        _downloadCancellation?.Dispose();
        var downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _downloadCancellation = downloadCancellation;
        var progress = new Progress<UpdateDownloadProgress>(item =>
        {
            UpdateProgressPercent = item.Percent;
            UpdateProgressStage = item.Stage;
        });
        EnterInstallerOperation();
        try
        {
            _preparedUpdate = await _installer.PrepareAsync(Manifest, progress, downloadCancellation.Token);
            Status = "update-prepared";
            OnPropertyChanged(nameof(HasPreparedUpdate));
            return true;
        }
        catch (OperationCanceledException)
        {
            Status = "update-download-cancelled";
            UpdateProgressStage = "cancelled";
            return false;
        }
        catch
        {
            Status = "update-download-failed";
            UpdateProgressStage = "failed";
            return false;
        }
        finally
        {
            if (ReferenceEquals(_downloadCancellation, downloadCancellation))
                _downloadCancellation = null;
            downloadCancellation.Dispose();
            ExitInstallerOperation();
            IsBusy = false;
        }
    }

    public void CancelUpdateDownload()
    {
        if (_disposed) return;
        _downloadCancellation?.Cancel();
    }

    public async Task<UpdateReplacementResult> StartPreparedReplacementAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_installer is null || _preparedUpdate is null)
            return new UpdateReplacementResult(false, "prepared-update-missing");
        if (IsBusy) return new UpdateReplacementResult(false, "maintenance-busy");
        IsBusy = true;
        Status = "update-starting";
        EnterInstallerOperation();
        try
        {
            var result = await _installer.StartReplacementAsync(_preparedUpdate, cancellationToken);
            Status = result.Reason;
            if (result.Started)
            {
                _preparedUpdate = null;
                OnPropertyChanged(nameof(HasPreparedUpdate));
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            Status = "cancelled";
            throw;
        }
        catch
        {
            Status = "update-start-failed";
            return new UpdateReplacementResult(false, "update-start-failed");
        }
        finally
        {
            ExitInstallerOperation();
            IsBusy = false;
        }
    }

    private void ApplyPreferences(MaintenancePreferences preferences)
    {
        AutoUpdateEnabled = preferences.AutoUpdateEnabled;
        _lastAnnouncementId = preferences.LastAnnouncementId;
        LoadedFromRecovery = preferences.LoadedFromRecovery;
        OnPropertyChanged(nameof(IsAnnouncementNew));
    }

    private void EnterInstallerOperation() => Interlocked.Increment(ref _activeInstallerOperations);

    private void ExitInstallerOperation()
    {
        if (Interlocked.Decrement(ref _activeInstallerOperations) == 0 && _disposed)
            DisposeInstallerOnce();
    }

    private void DisposeInstallerOnce()
    {
        if (_installer is not IDisposable disposable) return;
        if (Interlocked.Exchange(ref _installerDisposed, 1) != 0) return;
        disposable.Dispose();
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var downloadCancellation = _downloadCancellation;
        _downloadCancellation = null;
        try { downloadCancellation?.Cancel(); } catch { }
        // The active operation owns the CTS and installer until its finally runs. If no installer
        // operation is active, dispose immediately; otherwise ExitInstallerOperation owns the handoff.
        if (Volatile.Read(ref _activeInstallerOperations) == 0)
        {
            downloadCancellation?.Dispose();
            DisposeInstallerOnce();
        }
        PropertyChanged = null;
    }
}
