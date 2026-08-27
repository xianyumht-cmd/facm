namespace FACM.Core.Online;

public sealed record UpdateManifestSnapshot(
    bool Enabled,
    string Version,
    string MinimumVersion,
    bool ForceUpdate,
    string DownloadUrl,
    string Sha256,
    string ReleaseNotes,
    string PublishedAt);

public sealed record UpdateDecision(
    Version CurrentVersion,
    Version? LatestVersion,
    bool UpdateAvailable,
    bool ForceUpdateRequired,
    string Reason);

public interface IUpdateManifestSource
{
    Task<UpdateManifestSnapshot?> GetAsync(CancellationToken cancellationToken);
}

public interface IUpdateInstaller
{
    Task InstallAsync(UpdateManifestSnapshot manifest, CancellationToken cancellationToken);
}

public static class UpdateDecisionService
{
    public static UpdateDecision Evaluate(Version currentVersion, UpdateManifestSnapshot? manifest)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        if (manifest is null) return new(currentVersion, null, false, false, "manifest-unavailable");
        if (!manifest.Enabled) return new(currentVersion, ParseVersion(manifest.Version), false, false, "updates-disabled");

        var latest = ParseVersion(manifest.Version);
        if (latest is null) return new(currentVersion, null, false, false, "invalid-version");

        var available = latest > currentVersion;
        var minimum = ParseVersion(manifest.MinimumVersion);
        var force = manifest.ForceUpdate && minimum is not null && currentVersion < minimum;
        return new(currentVersion, latest, available, force, available ? "update-available" : "up-to-date");
    }

    private static Version? ParseVersion(string? value) => Version.TryParse(value, out var version) ? version : null;
}
