using FACM.App.ViewModels;
using FACM.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;

namespace FACM.App;

public sealed partial class MainWindow
{
    private LeagueGameRepairViewModel? _gameRepair;

    internal void ConfigureGameRepair(LeagueGameRepairViewModel gameRepair)
    {
        _gameRepair = gameRepair ?? throw new ArgumentNullException(nameof(gameRepair));

        RepairGameTitle.Text = _text.Get(UiTextKeys.RepairGameRepair);
        RepairGameDescription.Text = _text.Get(UiTextKeys.RepairGameRepairHint);
        RepairFixWindowButton.Content = _text.Get(UiTextKeys.RepairFixWindow);
        RepairFixWindowHint.Text = _text.Get(UiTextKeys.RepairFixWindowHint);
        RepairAutoWindowHint.Text = _text.Get(UiTextKeys.RepairAutoWindowHint);
        RepairSkipSettlementButton.Content = _text.Get(UiTextKeys.RepairSkipSettlement);
        RepairSkipSettlementHint.Text = _text.Get(UiTextKeys.RepairSkipSettlementHint);
        RepairRestartClientUxButton.Content = _text.Get(UiTextKeys.RepairRestartClientUx);
        RepairRestartClientUxHint.Text = _text.Get(UiTextKeys.RepairRestartClientUxHint);
        RepairExitGameButton.Content = _text.Get(UiTextKeys.RepairExitGame);
        RepairExitGameHint.Text = _text.Get(UiTextKeys.RepairExitGameHint);

        AutomationProperties.SetName(RepairFixWindowButton, _text.Get(UiTextKeys.RepairFixWindow));
        AutomationProperties.SetHelpText(RepairFixWindowButton, _text.Get(UiTextKeys.RepairFixWindowHint));
        AutomationProperties.SetName(RepairAutoWindowButton, _text.Get(UiTextKeys.RepairAutoWindow));
        AutomationProperties.SetHelpText(RepairAutoWindowButton, _text.Get(UiTextKeys.RepairAutoWindowHint));
        AutomationProperties.SetName(RepairSkipSettlementButton, _text.Get(UiTextKeys.RepairSkipSettlement));
        AutomationProperties.SetHelpText(RepairSkipSettlementButton, _text.Get(UiTextKeys.RepairSkipSettlementHint));
        AutomationProperties.SetName(RepairRestartClientUxButton, _text.Get(UiTextKeys.RepairRestartClientUx));
        AutomationProperties.SetHelpText(RepairRestartClientUxButton, _text.Get(UiTextKeys.RepairRestartClientUxHint));
        AutomationProperties.SetName(RepairExitGameButton, _text.Get(UiTextKeys.RepairExitGame));
        AutomationProperties.SetHelpText(RepairExitGameButton, _text.Get(UiTextKeys.RepairExitGameHint));

        ApplyGameRepairState();
        InitializePersonalizationSurface();
    }

    private async void OnRepairFixWindowClick(object sender, RoutedEventArgs args) =>
        await RunGameRepairAsync(viewModel => viewModel.RepairWindowAsync());

    private void OnRepairAutoWindowClick(object sender, RoutedEventArgs args)
    {
        var gameRepair = _gameRepair;
        if (gameRepair is null || gameRepair.IsBusy) return;
        _ = gameRepair.ToggleAutoRepair();
        ApplyGameRepairState();
    }

    private async void OnRepairSkipSettlementClick(object sender, RoutedEventArgs args) =>
        await RunGameRepairAsync(viewModel => viewModel.SkipSettlementAsync());

    private async void OnRepairRestartClientUxClick(object sender, RoutedEventArgs args) =>
        await RunGameRepairAsync(viewModel => viewModel.RestartClientUxAsync());

    private async void OnRepairExitGameClick(object sender, RoutedEventArgs args) =>
        await RunGameRepairAsync(viewModel => viewModel.ExitGameAsync());

    private async Task RunGameRepairAsync(Func<LeagueGameRepairViewModel, Task<FACM.Core.League.LeagueGameRepairResult?>> operation)
    {
        var gameRepair = _gameRepair;
        if (gameRepair is null || gameRepair.IsBusy || _closed) return;
        try
        {
            var task = operation(gameRepair);
            ApplyGameRepairState();
            _ = await task;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            ApplyGameRepairState();
        }
    }

    private void ApplyGameRepairState()
    {
        var gameRepair = _gameRepair;
        if (gameRepair is null || _closed)
        {
            SetGameRepairButtonsEnabled(false);
            RepairGameStatus.Text = _text.Get(UiTextKeys.RepairGameRepairReady);
            return;
        }

        RepairAutoWindowButton.Content = _text.Get(
            gameRepair.AutoRepairEnabled
                ? UiTextKeys.RepairAutoWindowDisable
                : UiTextKeys.RepairAutoWindow);
        RepairGameStatus.Text = string.IsNullOrWhiteSpace(gameRepair.Status)
            ? _text.Get(UiTextKeys.RepairGameRepairReady)
            : gameRepair.Status;
        SetGameRepairButtonsEnabled(!gameRepair.IsBusy);
    }

    private void SetGameRepairButtonsEnabled(bool enabled)
    {
        RepairFixWindowButton.IsEnabled = enabled;
        RepairAutoWindowButton.IsEnabled = enabled;
        RepairSkipSettlementButton.IsEnabled = enabled;
        RepairRestartClientUxButton.IsEnabled = enabled;
        RepairExitGameButton.IsEnabled = enabled;
    }
}
