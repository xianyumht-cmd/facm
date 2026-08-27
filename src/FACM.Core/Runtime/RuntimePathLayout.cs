namespace FACM.Core.Runtime;

public sealed record RuntimePathLayout(
    string DistributionDirectory,
    string SettingsPath,
    string UiTextPath,
    string LogsDirectory,
    string RuntimeDirectory,
    string CacheDirectory,
    string PetHostDataDirectory,
    string UpdatesDirectory)
{
    public static RuntimePathLayout From(IExecutablePathProvider executablePaths)
    {
        ArgumentNullException.ThrowIfNull(executablePaths);
        var executable = Path.GetFullPath(executablePaths.ExecutablePath);
        var distributionDirectory = Path.GetDirectoryName(executable)
            ?? throw new InvalidOperationException("FACM distribution directory is unavailable.");
        var runtime = Path.Combine(distributionDirectory, "runtime");
        return new RuntimePathLayout(
            distributionDirectory,
            Path.Combine(distributionDirectory, "settings.ini"),
            Path.Combine(distributionDirectory, "ui-text.ini"),
            Path.Combine(distributionDirectory, "logs"),
            runtime,
            Path.Combine(runtime, "cache"),
            Path.Combine(runtime, "pethost"),
            Path.Combine(runtime, "updates"));
    }
}
