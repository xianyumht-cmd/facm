namespace FACM.Core.Runtime;

public sealed record RuntimePathLayout(
    string DistributionDirectory,
    string SettingsPath,
    string Settings2Path,
    string UiTextPath,
    string LogsDirectory,
    string RuntimeDirectory,
    string CacheDirectory,
    string PetHostDataDirectory,
    string UpdatesDirectory)
{
    public string DataRootDirectory => Path.GetDirectoryName(Settings2Path) ?? DistributionDirectory;
    public bool IsModular => !string.Equals(
        Path.GetFullPath(Settings2Path),
        Path.Combine(DistributionDirectory, "settings.v2.json"),
        StringComparison.OrdinalIgnoreCase);
    public string RecoveryDirectory => IsModular
        ? Path.Combine(DataRootDirectory, "state")
        : Path.Combine(RuntimeDirectory, "recovery");
    public string ActiveStatePath => Path.Combine(DataRootDirectory, "state", "active.json");
    public string RecoveryStatePath => Path.Combine(RecoveryDirectory, "state.json");
    public string Settings2LastKnownGoodPath => Path.Combine(RecoveryDirectory, "settings.v2.lkg.json");
    public string FeatureKillSwitchPath => Path.Combine(RecoveryDirectory, "feature-kill-switch.json");

    public static RuntimePathLayout From(IExecutablePathProvider executablePaths)
    {
        ArgumentNullException.ThrowIfNull(executablePaths);
        var executable = Path.GetFullPath(executablePaths.ExecutablePath);
        var distributionDirectory = Path.GetDirectoryName(executable)
            ?? throw new InvalidOperationException("FACM distribution directory is unavailable.");
        var configuredRoot = NormalizeOptionalRoot(Environment.GetEnvironmentVariable("FACM_ROOT"));
        var configuredDataRoot = NormalizeOptionalRoot(Environment.GetEnvironmentVariable("FACM_DATA_ROOT"));
        if (configuredRoot is not null) distributionDirectory = configuredRoot;
        var dataRoot = configuredDataRoot ?? distributionDirectory;
        var runtime = Path.Combine(dataRoot, "runtime");
        return new RuntimePathLayout(
            distributionDirectory,
            Path.Combine(dataRoot, "settings.ini"),
            Path.Combine(dataRoot, "settings.v2.json"),
            Path.Combine(distributionDirectory, "ui-text.ini"),
            Path.Combine(dataRoot, "logs"),
            runtime,
            Path.Combine(runtime, "cache"),
            Path.Combine(runtime, "pethost"),
            Path.Combine(runtime, "updates"));
    }

    private static string? NormalizeOptionalRoot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Path.GetFullPath(value.Trim()); }
        catch { return null; }
    }
}
