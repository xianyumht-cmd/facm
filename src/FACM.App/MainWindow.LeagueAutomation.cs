using FACM.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;

namespace FACM.App;

public sealed partial class MainWindow
{
    private bool _leagueAutomationUiApplying;
    private string _leagueAutomationStatusTextKey = UiTextKeys.LeagueAutomationSettingsReady;

    private void InitializeLeagueAutomationSurface()
    {
        LeagueAutoMatchmakingToggle.Header = _text.Get(UiTextKeys.LeagueAutoMatchmaking);
        LeagueAutoMatchmakingHint.Text = _text.Get(UiTextKeys.LeagueAutoMatchmakingHint);
        LeagueAutoAcceptToggle.Header = _text.Get(UiTextKeys.LeagueAutoAccept);
        LeagueAutoAcceptHint.Text = _text.Get(UiTextKeys.LeagueAutoAcceptHint);

        AutomationProperties.SetName(LeagueAutoMatchmakingToggle, _text.Get(UiTextKeys.LeagueAutoMatchmaking));
        AutomationProperties.SetHelpText(LeagueAutoMatchmakingToggle, _text.Get(UiTextKeys.LeagueAutoMatchmakingHint));
        AutomationProperties.SetName(LeagueAutoAcceptToggle, _text.Get(UiTextKeys.LeagueAutoAccept));
        AutomationProperties.SetHelpText(LeagueAutoAcceptToggle, _text.Get(UiTextKeys.LeagueAutoAcceptHint));

        ApplyLeagueAutomationSettingsSurface();
    }

    private void ApplyLeagueAutomationSettingsSurface()
    {
        if (_closed) return;

        _leagueAutomationUiApplying = true;
        try
        {
            LeagueAutoMatchmakingToggle.IsOn = _leagueWorkbench.AutoMatchmakingEnabled;
            LeagueAutoAcceptToggle.IsOn = _leagueWorkbench.AutoAcceptEnabled;
            var enabled = _leagueWorkbench.HasMatchmakingAutomation && !_leagueWorkbench.IsAutomationSettingsBusy;
            LeagueAutoMatchmakingToggle.IsEnabled = enabled;
            LeagueAutoAcceptToggle.IsEnabled = enabled;
            LeagueAutomationSettingsStatus.Text = _text.Get(_leagueAutomationStatusTextKey);
        }
        finally
        {
            _leagueAutomationUiApplying = false;
        }
    }

    private async void OnLeagueAutoMatchmakingToggled(object sender, RoutedEventArgs args)
    {
        if (_leagueAutomationUiApplying || _closed || _leagueWorkbench.IsAutomationSettingsBusy) return;
        var requested = LeagueAutoMatchmakingToggle.IsOn;
        var saved = false;
        try
        {
            saved = await _leagueWorkbench.SetAutoMatchmakingEnabledAsync(requested);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _leagueAutomationStatusTextKey = saved
                ? UiTextKeys.LeagueAutomationSettingsSaved
                : UiTextKeys.LeagueAutomationSettingsFailed;
            ApplyLeagueAutomationSettingsSurface();
        }
    }

    private async void OnLeagueAutoAcceptToggled(object sender, RoutedEventArgs args)
    {
        if (_leagueAutomationUiApplying || _closed || _leagueWorkbench.IsAutomationSettingsBusy) return;
        var requested = LeagueAutoAcceptToggle.IsOn;
        var saved = false;
        try
        {
            saved = await _leagueWorkbench.SetAutoAcceptEnabledAsync(requested);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _leagueAutomationStatusTextKey = saved
                ? UiTextKeys.LeagueAutomationSettingsSaved
                : UiTextKeys.LeagueAutomationSettingsFailed;
            ApplyLeagueAutomationSettingsSurface();
        }
    }
}
