using System.ComponentModel;
using FACM.Core.League;
using FACM.Core.Settings;

namespace FACM.App.ViewModels;

/// <summary>
/// WinUI-facing settings/intent owner for post-game automation. It never reads LCU or executes
/// writes directly; the process-wide automation service remains the only behavior owner.
/// </summary>
public sealed class LeaguePostGameAutomationSettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISettings2Repository _settings;
    private readonly ILeaguePostGameAutomationService _automation;
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isBusy;
    private bool _recoveryReadOnly;
    private bool _disposed;

    public LeaguePostGameAutomationSettingsViewModel(
        ISettings2Repository settings,
        ILeaguePostGameAutomationService automation)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _automation = automation ?? throw new ArgumentNullException(nameof(automation));
        _automation.StatusChanged += OnAutomationStatusChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool AutoHonorEnabled => _automation.AutoHonorEnabled;
    public bool AutoReturnLobbyEnabled => _automation.AutoReturnLobbyEnabled;
    public LeagueHonorAttemptStatus? LastHonorStatus => _automation.LastHonorStatus;
    public bool IsBusy => _isBusy;
    public bool RecoveryReadOnly => _recoveryReadOnly;

    public Task<bool> SetAutoHonorEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        SaveAsync(enabled, null, cancellationToken);

    public Task<bool> SetAutoReturnLobbyEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        SaveAsync(null, enabled, cancellationToken);

    private async Task<bool> SaveAsync(
        bool? autoHonor,
        bool? autoReturnLobby,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _settingsGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            SetBusy(true);
            var updated = await _settings.UpdateAsync(
                settings =>
                {
                    if (autoHonor.HasValue)
                        settings.League.AutoHonorTeammateEnabled = autoHonor.Value;
                    if (autoReturnLobby.HasValue)
                        settings.League.AutoReturnLobbyEnabled = autoReturnLobby.Value;
                },
                allowRecoveryRebuild: false,
                cancellationToken: linked.Token).ConfigureAwait(false);

            // Persisted=false is currently only produced for recovery reads when rebuild is forbidden,
            // but keep the origin check explicit so a future non-persist outcome is not mislabeled as
            // recovery mode in the UI.
            SetRecoveryReadOnly(IsRecoveryOrigin(updated.Origin) && !updated.Persisted);
            if (!updated.Persisted)
            {
                RaiseState();
                return false;
            }

            _automation.Configure(
                updated.Settings.League.AutoHonorTeammateEnabled,
                updated.Settings.League.AutoReturnLobbyEnabled);
            RaiseState();
            return true;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            RaiseState();
            return false;
        }
        finally
        {
            SetBusy(false);
            _settingsGate.Release();
        }
    }

    private static bool IsRecoveryOrigin(SettingsLoadOrigin origin) =>
        origin is SettingsLoadOrigin.RecoveredLastKnownGood or SettingsLoadOrigin.RecoveryDefaults;

    private void OnAutomationStatusChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(LastHonorStatus));
        OnPropertyChanged(nameof(AutoHonorEnabled));
        OnPropertyChanged(nameof(AutoReturnLobbyEnabled));
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(AutoHonorEnabled));
        OnPropertyChanged(nameof(AutoReturnLobbyEnabled));
        OnPropertyChanged(nameof(LastHonorStatus));
    }

    private void SetBusy(bool busy)
    {
        if (_isBusy == busy) return;
        _isBusy = busy;
        OnPropertyChanged(nameof(IsBusy));
    }

    private void SetRecoveryReadOnly(bool value)
    {
        if (_recoveryReadOnly == value) return;
        _recoveryReadOnly = value;
        OnPropertyChanged(nameof(RecoveryReadOnly));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _automation.StatusChanged -= OnAutomationStatusChanged;
        _lifetime.Cancel();
        // Do not dispose the coordination primitives here. A toggle may already be inside an await
        // and must be able to release its semaphore during synchronous window teardown.
        PropertyChanged = null;
    }
}
