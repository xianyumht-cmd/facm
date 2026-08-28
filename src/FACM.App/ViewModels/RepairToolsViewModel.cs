using System.ComponentModel;
using FACM.Core.Repair;
using FACM.Core.Text;

namespace FACM.App.ViewModels;

public sealed class RepairToolsViewModel : INotifyPropertyChanged
{
    private readonly IRepairToolService _repairTools;
    private string _statusTextKey = UiTextKeys.RepairToolsReady;
    private string _statusDetail = string.Empty;
    private bool _isBusy;

    public RepairToolsViewModel(IRepairToolService repairTools)
    {
        _repairTools = repairTools ?? throw new ArgumentNullException(nameof(repairTools));
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

    public string StatusTextKey
    {
        get => _statusTextKey;
        private set
        {
            if (string.Equals(_statusTextKey, value, StringComparison.Ordinal)) return;
            _statusTextKey = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusTextKey)));
        }
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set
        {
            if (string.Equals(_statusDetail, value, StringComparison.Ordinal)) return;
            _statusDetail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusDetail)));
        }
    }

    public RepairToolLaunchResult LaunchDriverCleanup()
    {
        if (IsBusy)
        {
            return new RepairToolLaunchResult(false, "busy", string.Empty);
        }

        IsBusy = true;
        try
        {
            var result = _repairTools.LaunchDriverCleanup();
            StatusTextKey = result.State switch
            {
                "started" => UiTextKeys.RepairDriverCleanupStarted,
                "cancelled" => UiTextKeys.RepairDriverCleanupCancelled,
                _ => UiTextKeys.RepairDriverCleanupFailed
            };
            StatusDetail = result.Message;
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
