using System.ComponentModel;
using FACM.Core.League;
using FACM.Core.Settings;

namespace FACM.App.ViewModels;

/// <summary>
/// WinUI-facing settings/intent owner for recommended setup automation. It only persists the toggle
/// and forwards Configure to the process-scoped service; it never reads League or performs writes.
/// </summary>
public sealed class LeagueRecommendedAutoApplySettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISettings2Repository _settings;
    private readonly ILeagueRecommendedAutoApplyService _automation;
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isBusy;
    private bool _recoveryReadOnly;
    private bool _disposed;

    public LeagueRecommendedAutoApplySettingsViewModel(
        ISettings2Repository settings,
        ILeagueRecommendedAutoApplyService automation)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _automation = automation ?? throw new ArgumentNullException(nameof(automation));
        _automation.StatusChanged += OnAutomationStatusChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Enabled => _automation.Enabled;
    public LeagueRecommendedAutoApplyStatus LastStatus => _automation.LastStatus;
    public bool IsBusy => _isBusy;
    public bool RecoveryReadOnly => _recoveryReadOnly;

    public async Task<bool> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
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
                RaiseState();
                return false;
            }

            loaded.Settings.League.AutoApplyRecommended = enabled;
            await _settings.SaveAsync(loaded.Settings, linked.Token).ConfigureAwait(false);
            _automation.Configure(enabled);
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

    private void OnAutomationStatusChanged(object? sender, LeagueRecommendedAutoApplyStatusChangedEventArgs args)
    {
        OnPropertyChanged(nameof(LastStatus));
        OnPropertyChanged(nameof(Enabled));
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(LastStatus));
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
