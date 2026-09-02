namespace FACM.Core.Online;

public sealed record UpdateManifestSnapshot(
    bool Enabled,
    string Version,
    string MinimumVersion,
    bool ForceUpdate,
    string DownloadUrl,
    string Sha256,
    string ReleaseNotes,
    string PublishedAt,
    string BootstrapManifestUrl = "");

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

public static class UpdateManifestPolicy
{
    public static bool IsApprovedReleaseUrl(Uri uri, Version version)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(version);
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrWhiteSpace(uri.Query) ||
            !string.IsNullOrWhiteSpace(uri.Fragment)) return false;

        var normalizedVersion = version.ToString();
        var githubPrefix = "/xianyumht-cmd/facm/releases/download/v" + normalizedVersion + "/";
        var giteePrefix = "/xymhtcmd/facm/releases/download/v" + normalizedVersion + "/";
        var prefix = string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            ? githubPrefix
            : string.Equals(uri.Host, "gitee.com", StringComparison.OrdinalIgnoreCase)
                ? giteePrefix
                : string.Empty;
        return prefix.Length > 0 && HasSingleAssetPath(uri.AbsolutePath, prefix);
    }

    public static bool IsApprovedReleaseManifestUrl(Uri uri, Version version)
    {
        return IsApprovedReleaseUrl(uri, version) &&
               uri.AbsolutePath.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSingleAssetPath(string path, string prefix)
    {
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               path.Length > prefix.Length && !path[prefix.Length..].Contains('/');
    }
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
        var belowMinimum = minimum is not null && currentVersion < minimum;
        var force = available && (manifest.ForceUpdate || belowMinimum);
        return new(currentVersion, latest, available, force, available ? "update-available" : "up-to-date");
    }

    public static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];
        return Version.TryParse(normalized, out var version) ? version : null;
    }
}
