using System.ComponentModel;
using FACM.Core.League;

namespace FACM.App.ViewModels;

public sealed class LeagueGameRepairViewModel : INotifyPropertyChanged
{
    private readonly ILeagueGameRepairService _repair;
    private bool _isBusy;
    private string _status = string.Empty;

    public LeagueGameRepairViewModel(ILeagueGameRepairService repair)
    {
        _repair = repair ?? throw new ArgumentNullException(nameof(repair));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
        }
    }

    public bool AutoRepairEnabled => _repair.AutoRepairEnabled;

    public string Status
    {
        get => _status;
        private set
        {
            if (string.Equals(_status, value, StringComparison.Ordinal)) return;
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public Task<LeagueGameRepairResult?> RepairWindowAsync(CancellationToken cancellationToken = default) =>
        RunAsync(_repair.RepairWindowAsync, cancellationToken);

    public LeagueGameRepairResult ToggleAutoRepair()
    {
        var result = _repair.SetAutoRepairEnabled(!_repair.AutoRepairEnabled);
        Status = result.Message;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoRepairEnabled)));
        return result;
    }

    public Task<LeagueGameRepairResult?> SkipSettlementAsync(CancellationToken cancellationToken = default) =>
        RunAsync(_repair.SkipSettlementAsync, cancellationToken);

    public Task<LeagueGameRepairResult?> RestartClientUxAsync(CancellationToken cancellationToken = default) =>
        RunAsync(_repair.RestartClientUxAsync, cancellationToken);

    public Task<LeagueGameRepairResult?> ExitGameAsync(CancellationToken cancellationToken = default) =>
        RunAsync(_repair.ExitGameAsync, cancellationToken);

    private async Task<LeagueGameRepairResult?> RunAsync(
        Func<CancellationToken, Task<LeagueGameRepairResult>> operation,
        CancellationToken cancellationToken)
    {
        if (IsBusy) return null;
        IsBusy = true;
        try
        {
            var result = await operation(cancellationToken).ConfigureAwait(false);
            Status = result.Message;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Status = "执行失败：" + exception.GetType().Name;
            return new LeagueGameRepairResult(false, false, "failed", Status);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
