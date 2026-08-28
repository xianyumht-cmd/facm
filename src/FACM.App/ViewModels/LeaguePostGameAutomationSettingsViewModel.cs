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
            var loaded = await _settings.LoadAsync(linked.Token).ConfigureAwait(false);
            var recoveryReadOnly = loaded.Origin is
                SettingsLoadOrigin.RecoveredLastKnownGood or SettingsLoadOrigin.RecoveryDefaults;
            SetRecoveryReadOnly(recoveryReadOnly);
            if (recoveryReadOnly)
            {
                // Match the rest of FACM 4.0 recovery semantics: never overwrite a damaged primary
                // settings file merely because a toggle was changed while using fallback settings.
                RaiseState();
                return false;
            }

            if (autoHonor.HasValue)
                loaded.Settings.League.AutoHonorTeammateEnabled = autoHonor.Value;
            if (autoReturnLobby.HasValue)
                loaded.Settings.League.AutoReturnLobbyEnabled = autoReturnLobby.Value;

            await _settings.SaveAsync(loaded.Settings, linked.Token).ConfigureAwait(false);
            _automation.Configure(
                loaded.Settings.League.AutoHonorTeammateEnabled,
                loaded.Settings.League.AutoReturnLobbyEnabled);
            RaiseState();
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
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
        _settingsGate.Dispose();
        _lifetime.Dispose();
    }
}
