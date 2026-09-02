using FACM.Core.Online;
using FACM.Core.Personalization;
using FACM.Core.Settings;
using FACM.Core.State;
using FACM.Core.Text;

namespace FACM.App.ViewModels;

public sealed class ControlCenterViewModel
{
    private readonly ISettings2Repository _settings;
    private readonly IUpdateManifestSource _updates;
    private readonly IProductStateReader _productState;

    public ControlCenterViewModel(
        ISettings2Repository settings,
        IUpdateManifestSource updates,
        IProductStateReader productState)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _productState = productState ?? throw new ArgumentNullException(nameof(productState));
    }

    public string StatusTextKey { get; private set; } = UiTextKeys.ShellStatusReady;
    public UpdateDecision? Update { get; private set; }
    public ProductStateSnapshot ProductState => _productState.Current;

    public PersonalizationViewModel CreatePersonalization(IFacmThemeRuntime themeRuntime) =>
        new(_settings, themeRuntime);

    public async Task RefreshAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        var loaded = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var autoUpdateEnabled = loaded.Settings.Online.AutoUpdateEnabled;
        var manifest = autoUpdateEnabled
            ? await _updates.GetAsync(cancellationToken).ConfigureAwait(false)
            : null;
        Update = autoUpdateEnabled
            ? UpdateDecisionService.Evaluate(currentVersion, manifest)
            : new UpdateDecision(currentVersion, null, false, false, "auto-update-disabled");
        StatusTextKey = Update.UpdateAvailable
            ? UiTextKeys.ShellStatusUpdateAvailable
            : UiTextKeys.ShellStatusReady;
    }
}
