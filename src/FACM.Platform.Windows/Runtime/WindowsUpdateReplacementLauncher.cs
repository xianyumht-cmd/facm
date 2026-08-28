using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FACM.Core.Online;
using FACM.Core.Runtime;

namespace FACM.Platform.Windows.Runtime;

/// <summary>
/// Final Windows edge for a package that has already passed the product receipt checks. The updater
/// helper is an embedded, build-controlled payload. Source must remain inside RuntimePathLayout's
/// update directory and the destination is always the current FACM executable.
/// </summary>
public sealed class WindowsUpdateReplacementLauncher : IUpdateReplacementLauncher
{
    public const string UpdaterResourceName = "FACM.Platform.Windows.Resources.FACM.Updater.exe";
    public const string UpdaterFileName = "FACM.Updater.exe";

    private readonly RuntimePathLayout _layout;
    private readonly IExecutablePathProvider _executablePaths;
    private readonly Func<byte[]> _readUpdaterPayload;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public WindowsUpdateReplacementLauncher(RuntimePathLayout layout, IExecutablePathProvider executablePaths)
        : this(layout, executablePaths, ReadEmbeddedUpdaterPayload, Process.Start)
    {
    }

    internal WindowsUpdateReplacementLauncher(
        RuntimePathLayout layout,
        IExecutablePathProvider executablePaths,
        Func<byte[]> readUpdaterPayload,
        Func<ProcessStartInfo, Process?> startProcess)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _executablePaths = executablePaths ?? throw new ArgumentNullException(nameof(executablePaths));
        _readUpdaterPayload = readUpdaterPayload ?? throw new ArgumentNullException(nameof(readUpdaterPayload));
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
    }

    public Task<bool> StartAsync(
        string validatedPackagePath,
        string expectedSha256,
        string version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSha256(expectedSha256);
        if (UpdateDecisionService.ParseVersion(version) is null)
            throw new InvalidDataException("Validated update version is invalid.");

        var updatesDirectory = Path.GetFullPath(_layout.UpdatesDirectory);
        var source = Path.GetFullPath(validatedPackagePath ?? string.Empty);
        if (!IsUnderDirectory(source, updatesDirectory) || !File.Exists(source))
            throw new InvalidDataException("Validated update package is outside the controlled updates directory.");
        if (!string.Equals(ComputeSha256(source), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update package changed before the Windows replacement boundary.");

        var destination = Path.GetFullPath(_executablePaths.ExecutablePath);
        if (!destination.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Current FACM executable path is invalid for replacement.");

        Directory.CreateDirectory(updatesDirectory);
        var updaterPath = ExtractUpdater(updatesDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var parentPid = Environment.ProcessId;
        var arguments = BuildUpdaterArguments(parentPid, source, destination, expectedSha256);
        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            Arguments = arguments,
            WorkingDirectory = updatesDirectory,
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
            // User cancelled UAC. The current FACM process must remain alive and usable.
            return Task.FromResult(false);
        }
    }

    private string ExtractUpdater(string updatesDirectory)
    {
        var bytes = _readUpdaterPayload();
        if (bytes.Length < 1024 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
            throw new InvalidDataException("Embedded FACM updater payload is invalid.");
        var expectedHash = Convert.ToHexString(SHA256.HashData(bytes));
        var destination = Path.Combine(updatesDirectory, UpdaterFileName);

        if (File.Exists(destination))
        {
            try
            {
                if (string.Equals(ComputeSha256(destination), expectedHash, StringComparison.OrdinalIgnoreCase))
                    return destination;
            }
            catch
            {
                // A damaged cached helper is replaced from the controlled embedded payload below.
            }
        }

        var temporary = destination + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (!string.Equals(ComputeSha256(temporary), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Extracted FACM updater payload failed SHA-256 verification.");
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static byte[] ReadEmbeddedUpdaterPayload()
    {
        using var stream = typeof(WindowsUpdateReplacementLauncher).Assembly.GetManifestResourceStream(UpdaterResourceName)
            ?? throw new InvalidDataException("Embedded FACM updater payload is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    internal static string BuildUpdaterArguments(int parentPid, string source, string destination, string expectedSha256)
    {
        if (parentPid <= 0) throw new ArgumentOutOfRangeException(nameof(parentPid));
        ValidateSha256(expectedSha256);
        return "--parent-pid=" + parentPid.ToString(CultureInfo.InvariantCulture) +
               " --source64=\"" + EncodePath(source) + "\"" +
               " --dest64=\"" + EncodePath(destination) + "\"" +
               " --sha256=" + expectedSha256.ToUpperInvariant();
    }

    private static string EncodePath(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFullPath(value)));

    private static bool IsUnderDirectory(string path, string directory)
    {
        var normalizedDirectory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
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
