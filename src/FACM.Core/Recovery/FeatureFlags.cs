using FACM.Core.Cleanup;
using FACM.Core.League;
using FACM.Core.Observability;
using FACM.Core.Online;

namespace FACM.Core.Recovery;

public enum FacmFeatureCapability
{
    CleanupExecute,
    UpdateCheck,
    UpdateInstall,
    DiagnosticsExport,
    LeagueApplyMySelection,
    LeagueCreatePerkPage,
    LeagueUpdatePerkPage,
    LeagueSetCurrentPerkPage
}

public enum FeatureKillSwitchLoadOrigin
{
    Missing,
    Loaded,
    FailClosed
}

public sealed class FeatureKillSwitch
{
    private readonly HashSet<FacmFeatureCapability> _disabled;

    public FeatureKillSwitch(IEnumerable<FacmFeatureCapability> disabledCapabilities)
    {
        ArgumentNullException.ThrowIfNull(disabledCapabilities);
        _disabled = disabledCapabilities.ToHashSet();
    }

    public static FeatureKillSwitch None { get; } = new(Array.Empty<FacmFeatureCapability>());

    public IReadOnlyCollection<FacmFeatureCapability> DisabledCapabilities => _disabled.ToArray();

    public bool Disables(FacmFeatureCapability capability) => _disabled.Contains(capability);

    public static FeatureKillSwitch DisableAllApproved() => new(FeatureBaseline.GetApprovedCapabilities());
}

public sealed record FeatureKillSwitchLoadResult(
    FeatureKillSwitch KillSwitch,
    FeatureKillSwitchLoadOrigin Origin,
    string Reason);

public interface IFeatureKillSwitchSource
{
    Task<FeatureKillSwitchLoadResult> LoadAsync(CancellationToken cancellationToken = default);
}

public interface IFeaturePolicy
{
    bool IsEnabled(FacmFeatureCapability capability);
}

public sealed class FeaturePolicySnapshot : IFeaturePolicy
{
    private readonly HashSet<FacmFeatureCapability> _enabled;

    internal FeaturePolicySnapshot(IEnumerable<FacmFeatureCapability> enabledCapabilities)
    {
        _enabled = enabledCapabilities.ToHashSet();
    }

    public IReadOnlyCollection<FacmFeatureCapability> EnabledCapabilities => _enabled.ToArray();

    public bool IsEnabled(FacmFeatureCapability capability) => _enabled.Contains(capability);
}

public static class FeatureBaseline
{
    private static readonly FacmFeatureCapability[] ApprovedCapabilities =
    [
        FacmFeatureCapability.CleanupExecute,
        FacmFeatureCapability.UpdateCheck,
        FacmFeatureCapability.UpdateInstall,
        FacmFeatureCapability.DiagnosticsExport,
        FacmFeatureCapability.LeagueApplyMySelection,
        FacmFeatureCapability.LeagueCreatePerkPage,
        FacmFeatureCapability.LeagueUpdatePerkPage,
        FacmFeatureCapability.LeagueSetCurrentPerkPage
    ];

    public static IReadOnlyList<FacmFeatureCapability> GetApprovedCapabilities() =>
        ApprovedCapabilities.ToArray();
}

public static class FeaturePolicyEvaluator
{
    public static FeaturePolicySnapshot Evaluate(
        IEnumerable<FacmFeatureCapability> baselineCapabilities,
        FeatureKillSwitch killSwitch)
    {
        ArgumentNullException.ThrowIfNull(baselineCapabilities);
        ArgumentNullException.ThrowIfNull(killSwitch);

        var enabled = baselineCapabilities
            .Distinct()
            .Where(capability => !killSwitch.Disables(capability));
        return new FeaturePolicySnapshot(enabled);
    }

    public static bool IsNoMorePermissive(FeaturePolicySnapshot candidate, FeaturePolicySnapshot baseline)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(baseline);
        return candidate.EnabledCapabilities.All(baseline.IsEnabled);
    }
}

public static class LeagueFeatureCapabilities
{
    public static FacmFeatureCapability Map(LeagueWriteCapability capability) => capability switch
    {
        LeagueWriteCapability.ApplyMySelection => FacmFeatureCapability.LeagueApplyMySelection,
        LeagueWriteCapability.CreatePerkPage => FacmFeatureCapability.LeagueCreatePerkPage,
        LeagueWriteCapability.UpdatePerkPage => FacmFeatureCapability.LeagueUpdatePerkPage,
        LeagueWriteCapability.SetCurrentPerkPage => FacmFeatureCapability.LeagueSetCurrentPerkPage,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown League write capability.")
    };
}

public sealed class FeatureDisabledException(FacmFeatureCapability capability)
    : InvalidOperationException($"FACM capability is disabled: {capability}")
{
    public FacmFeatureCapability Capability { get; } = capability;
}

public sealed class FeatureGatedLeagueWriteGateway(
    ILeagueWriteGateway inner,
    IFeaturePolicy features) : ILeagueWriteGateway
{
    public Task<LeagueWriteResult?> ExecuteAsync(LeagueWriteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!features.IsEnabled(LeagueFeatureCapabilities.Map(command.Capability)))
            throw new FeatureDisabledException(LeagueFeatureCapabilities.Map(command.Capability));
        return inner.ExecuteAsync(command, cancellationToken);
    }
}

public sealed class FeatureGatedCleanupExecutor(
    ICleanupExecutor inner,
    IFeaturePolicy features) : ICleanupExecutor
{
    public Task<CleanupResult> ExecuteAsync(
        CleanupPlan plan,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!features.IsEnabled(FacmFeatureCapability.CleanupExecute))
            throw new FeatureDisabledException(FacmFeatureCapability.CleanupExecute);
        return inner.ExecuteAsync(plan, progress, cancellationToken);
    }
}

public sealed class FeatureGatedUpdateManifestSource(
    IUpdateManifestSource inner,
    IFeaturePolicy features) : IUpdateManifestSource
{
    public Task<UpdateManifestSnapshot?> GetAsync(CancellationToken cancellationToken)
    {
        if (!features.IsEnabled(FacmFeatureCapability.UpdateCheck))
            return Task.FromResult<UpdateManifestSnapshot?>(null);
        return inner.GetAsync(cancellationToken);
    }
}

public sealed class FeatureGatedUpdateInstaller(
    IUpdateInstaller inner,
    IFeaturePolicy features) : IUpdateInstaller
{
    public Task InstallAsync(UpdateManifestSnapshot manifest, CancellationToken cancellationToken)
    {
        if (!features.IsEnabled(FacmFeatureCapability.UpdateInstall))
            throw new FeatureDisabledException(FacmFeatureCapability.UpdateInstall);
        return inner.InstallAsync(manifest, cancellationToken);
    }
}

public sealed class FeatureGatedDiagnosticsBundleExporter(
    IDiagnosticsBundleExporter inner,
    IFeaturePolicy features) : IDiagnosticsBundleExporter
{
    public Task<DiagnosticsExportReceipt> ExportAsync(
        DiagnosticsSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (!features.IsEnabled(FacmFeatureCapability.DiagnosticsExport))
            throw new FeatureDisabledException(FacmFeatureCapability.DiagnosticsExport);
        return inner.ExportAsync(snapshot, cancellationToken);
    }
}
