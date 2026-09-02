using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using FACM.Core.Online;
using FACM.Core.Runtime;

namespace FACM.Platform.Windows.Runtime;

/// <summary>
/// Modular FACM updates must be applied by the native bootstrapper. The WinUI Core executable is
/// intentionally unsigned and is only one component of the active composition, so replacing it
/// directly would both fail identity verification and leave the component state inconsistent.
/// </summary>
public sealed class WindowsBootstrapperUpdateLauncher :
    IUpdateReplacementLauncher,
    IManifestAwareUpdateReplacementLauncher
{
    private readonly RuntimePathLayout _layout;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public WindowsBootstrapperUpdateLauncher(RuntimePathLayout layout)
        : this(layout, Process.Start)
    {
    }

    internal WindowsBootstrapperUpdateLauncher(
        RuntimePathLayout layout,
        Func<ProcessStartInfo, Process?> startProcess)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
    }

    public Task<bool> StartAsync(
        string validatedPackagePath,
        string expectedSha256,
        string version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A modular release needs the signed application manifest URL. Never fall back to the
        // legacy direct-file replacement path when that metadata is missing.
        return Task.FromResult(false);
    }

    public Task<bool> StartAsync(
        string validatedPackagePath,
        string expectedSha256,
        string version,
        string bootstrapManifestUrl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSha256(expectedSha256);
        var parsedVersion = UpdateDecisionService.ParseVersion(version)
            ?? throw new InvalidDataException("Validated update version is invalid.");

        var updatesDirectory = Path.GetFullPath(_layout.UpdatesDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var source = Path.GetFullPath(validatedPackagePath ?? string.Empty);
        if (!source.StartsWith(updatesDirectory, StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
            throw new InvalidDataException("Validated modular package is outside the controlled updates directory.");
        if (!string.Equals(ComputeSha256(source), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Validated modular package changed before bootstrapper launch.");

        if (!Uri.TryCreate(bootstrapManifestUrl, UriKind.Absolute, out var manifestUri) ||
            !UpdateManifestPolicy.IsApprovedReleaseManifestUrl(manifestUri, parsedVersion))
            throw new InvalidDataException("Modular update manifest URL is not an approved FACM release manifest.");

        var bootstrapper = Path.Combine(_layout.DistributionDirectory, "FACM.exe");
        if (!File.Exists(bootstrapper))
            throw new FileNotFoundException("FACM native bootstrapper is missing.", bootstrapper);

        var startInfo = new ProcessStartInfo
        {
            FileName = bootstrapper,
            Arguments = BuildBootstrapperArguments(manifestUri.AbsoluteUri),
            WorkingDirectory = Path.GetDirectoryName(bootstrapper) ?? _layout.DistributionDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = _startProcess(startInfo);
            return Task.FromResult(process is not null);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // UAC cancellation must leave the current FACM Core alive and usable.
            return Task.FromResult(false);
        }
    }

    internal static string BuildBootstrapperArguments(string manifestUrl)
    {
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Bootstrapper manifest URL must be absolute HTTPS.");
        return "--update --manifest-url=\"" + uri.AbsoluteUri.Replace("\"", "%22", StringComparison.Ordinal) + "\"";
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void ValidateSha256(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Update SHA-256 identity is invalid.");
    }
}
