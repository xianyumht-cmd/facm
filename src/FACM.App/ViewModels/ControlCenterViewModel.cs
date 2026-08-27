using FACM.Core.Online;
using FACM.Core.Settings;

namespace FACM.App.ViewModels;

public sealed class ControlCenterViewModel
{
    private readonly ISettingsRepository _settings;
    private readonly IUpdateManifestSource _updates;

    public ControlCenterViewModel(ISettingsRepository settings, IUpdateManifestSource updates)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
    }

    public string StatusText { get; private set; } = "准备就绪";
    public UpdateDecision? Update { get; private set; }

    public async Task RefreshAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var manifest = settings.AutoUpdateEnabled
            ? await _updates.GetAsync(cancellationToken).ConfigureAwait(false)
            : null;
        Update = settings.AutoUpdateEnabled
            ? UpdateDecisionService.Evaluate(currentVersion, manifest)
            : new UpdateDecision(currentVersion, null, false, false, "auto-update-disabled");
        StatusText = Update.UpdateAvailable ? "发现可用更新" : "准备就绪";
    }
}
