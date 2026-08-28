using System.ComponentModel;
using System.Runtime.CompilerServices;
using FACM.Core.Cleanup;
using FACM.Core.Settings;
using FACM.Core.Text;

namespace FACM.App.ViewModels;

public sealed class CleanupViewModel : INotifyPropertyChanged
{
    private readonly ISettings2Repository _settings;
    private readonly CleanupApplicationService _cleanup;
    private readonly ICleanupEnvironment _environment;

    private string _gamePath = string.Empty;
    private bool _isGamePathValid;
    private bool _isBusy;
    private bool _isRecoveryReadOnly;
    private string _statusTextKey = UiTextKeys.CleanupDirectoryMissing;
    private string _statusDetail = string.Empty;
    private CleanupPlan? _currentPlan;
    private CleanupResult? _lastResult;
    private IReadOnlyList<string> _runningProcesses = Array.Empty<string>();

    public CleanupViewModel(
        ISettings2Repository settings,
        CleanupApplicationService cleanup,
        ICleanupEnvironment environment)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string GamePath
    {
        get => _gamePath;
        private set => SetField(ref _gamePath, value);
    }

    public bool IsGamePathValid
    {
        get => _isGamePathValid;
        private set => SetField(ref _isGamePathValid, value);
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

    public string StatusTextKey
    {
        get => _statusTextKey;
        private set => SetField(ref _statusTextKey, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetField(ref _statusDetail, value);
    }

    public CleanupPlan? CurrentPlan
    {
        get => _currentPlan;
        private set
        {
            if (ReferenceEquals(_currentPlan, value)) return;
            _currentPlan = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RequiresElevation));
            OnPropertyChanged(nameof(DeletableTargetCount));
            OnPropertyChanged(nameof(BlockedTargetCount));
        }
    }

    public CleanupResult? LastResult
    {
        get => _lastResult;
        private set => SetField(ref _lastResult, value);
    }

    public IReadOnlyList<string> RunningProcesses
    {
        get => _runningProcesses;
        private set => SetField(ref _runningProcesses, value);
    }

    public bool IsAdministrator => _environment.IsAdministrator;

    public bool RequiresElevation =>
        CurrentPlan?.DeletableTargets.Any(target =>
            target.Rule is CleanupRuleKind.ProgramFilesDirectory or CleanupRuleKind.ProgramDataDirectory) == true &&
        !_environment.IsAdministrator;

    public int DeletableTargetCount => CurrentPlan?.DeletableTargets.Count ?? 0;
    public int BlockedTargetCount => CurrentPlan?.BlockedTargets.Count ?? 0;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var loaded = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            IsRecoveryReadOnly = loaded.Origin is SettingsLoadOrigin.RecoveredLastKnownGood or SettingsLoadOrigin.RecoveryDefaults;
            GamePath = loaded.Settings.Environment.GamePath?.Trim() ?? string.Empty;
            IsGamePathValid = !string.IsNullOrWhiteSpace(GamePath) && _environment.IsValidGameRoot(GamePath);
            StatusTextKey = IsGamePathValid ? UiTextKeys.CleanupDirectoryReady : UiTextKeys.CleanupDirectoryMissing;
            StatusDetail = IsRecoveryReadOnly ? UiTextKeys.CleanupPathRecoveryReadOnly : string.Empty;
            RefreshProcessState();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> DetectAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return false;
        IsBusy = true;
        StatusTextKey = UiTextKeys.CleanupScanning;
        StatusDetail = string.Empty;
        try
        {
            var detected = await _environment.FindGameRootAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(detected) || !_environment.IsValidGameRoot(detected))
            {
                IsGamePathValid = false;
                CurrentPlan = null;
                StatusTextKey = UiTextKeys.CleanupInvalidDirectory;
                return false;
            }

            await ApplyResolvedPathAsync(detected, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusTextKey = IsGamePathValid ? UiTextKeys.CleanupDirectoryReady : UiTextKeys.CleanupDirectoryMissing;
            throw;
        }
        catch (Exception exception)
        {
            CurrentPlan = null;
            StatusTextKey = UiTextKeys.CleanupFailed;
            StatusDetail = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> SetSelectedPathAsync(
        string selectedPath,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(selectedPath)) return false;
        IsBusy = true;
        StatusTextKey = UiTextKeys.CleanupScanning;
        StatusDetail = string.Empty;
        try
        {
            var resolved = await _environment.ResolveGameRootAsync(selectedPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resolved) || !_environment.IsValidGameRoot(resolved))
            {
                IsGamePathValid = false;
                CurrentPlan = null;
                StatusTextKey = UiTextKeys.CleanupInvalidDirectory;
                return false;
            }

            await ApplyResolvedPathAsync(resolved, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusTextKey = IsGamePathValid ? UiTextKeys.CleanupDirectoryReady : UiTextKeys.CleanupDirectoryMissing;
            throw;
        }
        catch (Exception exception)
        {
            CurrentPlan = null;
            StatusTextKey = UiTextKeys.CleanupFailed;
            StatusDetail = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<CleanupPlan?> PreviewAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || !IsGamePathValid || string.IsNullOrWhiteSpace(GamePath)) return null;
        IsBusy = true;
        LastResult = null;
        CurrentPlan = null;
        StatusTextKey = UiTextKeys.CleanupScanning;
        StatusDetail = string.Empty;
        try
        {
            RefreshProcessState();
            if (RunningProcesses.Count > 0)
            {
                StatusTextKey = UiTextKeys.CleanupRunningProcesses;
                StatusDetail = string.Join("、", RunningProcesses);
                return null;
            }

            var plan = await _cleanup.PreviewAsync(GamePath, cancellationToken).ConfigureAwait(false);
            CurrentPlan = plan;
            StatusTextKey = plan.DeletableTargets.Count == 0
                ? UiTextKeys.CleanupNoTargets
                : UiTextKeys.CleanupPreviewTitle;
            return plan;
        }
        catch (OperationCanceledException)
        {
            StatusTextKey = UiTextKeys.CleanupDirectoryReady;
            throw;
        }
        catch (Exception exception)
        {
            StatusTextKey = UiTextKeys.CleanupFailed;
            StatusDetail = exception.Message;
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<CleanupResult?> ExecuteConfirmedAsync(
        bool confirmed,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy || CurrentPlan is null) return null;
        IsBusy = true;
        LastResult = null;
        StatusTextKey = UiTextKeys.CleanupExecuting;
        StatusDetail = string.Empty;
        try
        {
            RefreshProcessState();
            if (RunningProcesses.Count > 0)
            {
                StatusTextKey = UiTextKeys.CleanupRunningProcesses;
                StatusDetail = string.Join("、", RunningProcesses);
                return null;
            }
            if (RequiresElevation)
            {
                StatusTextKey = UiTextKeys.CleanupRequiresAdmin;
                return null;
            }

            var result = await _cleanup.ExecuteConfirmedAsync(
                CurrentPlan,
                confirmed,
                progress,
                cancellationToken).ConfigureAwait(false);
            LastResult = result;
            StatusTextKey = result.Failures.Count == 0 ? UiTextKeys.CleanupComplete : UiTextKeys.CleanupFailed;
            StatusDetail = result.Failures.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, result.Failures.Take(8));
            CurrentPlan = null;
            return result;
        }
        catch (OperationCanceledException)
        {
            StatusTextKey = UiTextKeys.CleanupDirectoryReady;
            throw;
        }
        catch (Exception exception)
        {
            StatusTextKey = UiTextKeys.CleanupFailed;
            StatusDetail = exception.Message;
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool RestartElevatedForCleanup() => _environment.RestartElevatedForCleanup();

    public void RefreshProcessState()
    {
        RunningProcesses = _environment.GetRunningRelatedProcesses();
    }

    private async Task ApplyResolvedPathAsync(string resolvedPath, CancellationToken cancellationToken)
    {
        var normalized = resolvedPath.Trim();
        GamePath = normalized;
        IsGamePathValid = true;
        CurrentPlan = null;
        LastResult = null;
        StatusTextKey = UiTextKeys.CleanupDirectoryReady;

        var loaded = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        IsRecoveryReadOnly = loaded.Origin is SettingsLoadOrigin.RecoveredLastKnownGood or SettingsLoadOrigin.RecoveryDefaults;
        if (IsRecoveryReadOnly)
        {
            StatusDetail = UiTextKeys.CleanupPathRecoveryReadOnly;
            return;
        }

        loaded.Settings.Environment.GamePath = normalized;
        await _settings.SaveAsync(loaded.Settings, cancellationToken).ConfigureAwait(false);
        StatusDetail = string.Empty;
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
