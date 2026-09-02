namespace FACM.Core.Cleanup;

public sealed record CleanupProfileSnapshot(
    string ProgramFilesFolderName,
    string ProgramDataFolderName,
    string GameRootMarkerFolderName,
    string CleanupContainerRelativePath,
    string PreservedChildFolderName,
    IReadOnlyList<string> ExtraFolderRelativePaths,
    string LogFolderRelativePath,
    string LogSearchPattern,
    string RegistryDisplayNameKeyword,
    IReadOnlyList<string> RelatedProcessNames,
    int MaxMarkerSearchDepth);

public static class CleanupProfileContract
{
    public static CleanupProfileSnapshot Facm35 { get; } = CreateFacm35();

    public static void Validate(CleanupProfileSnapshot profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateFolderName(profile.ProgramFilesFolderName, nameof(profile.ProgramFilesFolderName));
        ValidateFolderName(profile.ProgramDataFolderName, nameof(profile.ProgramDataFolderName));
        ValidateFolderName(profile.GameRootMarkerFolderName, nameof(profile.GameRootMarkerFolderName));
        ValidateFolderName(profile.PreservedChildFolderName, nameof(profile.PreservedChildFolderName));
        _ = NormalizeRelativePath(profile.CleanupContainerRelativePath, nameof(profile.CleanupContainerRelativePath));
        _ = NormalizeRelativePath(profile.LogFolderRelativePath, nameof(profile.LogFolderRelativePath));
        if (profile.ExtraFolderRelativePaths is null || profile.ExtraFolderRelativePaths.Count == 0)
            throw new InvalidOperationException("Cleanup extra-folder allowlist is empty.");
        for (var index = 0; index < profile.ExtraFolderRelativePaths.Count; index++)
            _ = NormalizeRelativePath(profile.ExtraFolderRelativePaths[index], $"ExtraFolderRelativePaths[{index}]");
        if (!string.Equals(profile.LogSearchPattern, "*.log", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cleanup log search pattern must remain *.log.");
        if (string.IsNullOrWhiteSpace(profile.RegistryDisplayNameKeyword) || IsPlaceholder(profile.RegistryDisplayNameKeyword))
            throw new InvalidOperationException("Cleanup registry display-name keyword is not configured.");
        if (NormalizeProcessNames(profile.RelatedProcessNames).Count == 0)
            throw new InvalidOperationException("Cleanup related-process denylist is empty.");
        if (profile.MaxMarkerSearchDepth < 0 || profile.MaxMarkerSearchDepth > 16)
            throw new InvalidOperationException("Cleanup marker search depth is outside the approved range.");
    }

    public static string NormalizeRelativePath(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || IsPlaceholder(value))
            throw new InvalidOperationException(fieldName + " is not configured.");

        var normalized = value.Trim().Trim('"')
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Trim(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized))
            throw new InvalidOperationException(fieldName + " must be relative.");

        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment =>
                segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidOperationException(fieldName + " contains an invalid path segment.");

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    public static IReadOnlyList<string> NormalizeProcessNames(IEnumerable<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        return processNames
            .Where(value => !string.IsNullOrWhiteSpace(value) && !IsPlaceholder(value))
            .Select(value => Path.GetFileNameWithoutExtension(value.Trim()))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static CleanupProfileSnapshot CreateFacm35()
    {
        var profile = new CleanupProfileSnapshot(
            "AntiCheatExpert",
            "AntiCheatExpert",
            "Game",
            @"Game",
            "DATA",
            [@"Launcher\AntiCheatExpert", @"LeagueClient\AntiCheatExpert"],
            @"LeagueClient",
            "*.log",
            "英雄联盟",
            ["LeagueClient", "LeagueClientUx", "LeagueClientUxRender", "League of Legends", "RiotClientServices"],
            5);
        Validate(profile);
        return profile;
    }

    private static void ValidateFolderName(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || IsPlaceholder(value))
            throw new InvalidOperationException(fieldName + " is not configured.");
        var trimmed = value.Trim();
        if (trimmed is "." or ".." ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
            trimmed.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            throw new InvalidOperationException(fieldName + " must be a valid single folder name.");
    }

    private static bool IsPlaceholder(string value) =>
        value.Contains("REPLACE_", StringComparison.OrdinalIgnoreCase);
}

public interface ICleanupEnvironment
{
    Task<string?> FindGameRootAsync(CancellationToken cancellationToken = default);
    Task<string?> ResolveGameRootAsync(string selectedOrCandidatePath, CancellationToken cancellationToken = default);
    bool IsValidGameRoot(string path);
    IReadOnlyList<string> GetRunningRelatedProcesses();
    bool IsAdministrator { get; }
    bool RestartElevatedForCleanup();
}
