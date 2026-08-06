using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using FACM.App.Models;

namespace FACM.App.Services;

public sealed partial class PayloadService
{
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".exe", ".bat", ".cmd" };

    private readonly Assembly _assembly = Assembly.GetExecutingAssembly();

    public string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FACM");

    public async Task<PayloadManifest> LoadManifestAsync(CancellationToken cancellationToken = default)
    {
        string resourceName = _assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".Payloads.payloads.manifest.json", StringComparison.OrdinalIgnoreCase));

        await using Stream stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("无法读取内置工具清单。");

        PayloadManifest manifest = await JsonSerializer.DeserializeAsync<PayloadManifest>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken) ?? new PayloadManifest();

        foreach (PayloadDefinition payload in manifest.Payloads)
        {
            ValidateDefinition(payload);
        }

        return manifest;
    }

    public async Task<PayloadRunResult> ExtractAndRunAsync(
        PayloadDefinition payload,
        CancellationToken cancellationToken = default)
    {
        ValidateDefinition(payload);
        string executablePath = await ExtractAsync(payload, cancellationToken);
        ProcessStartInfo startInfo = BuildStartInfo(payload, executablePath);

        Process? process = Process.Start(startInfo);
        if (process is null)
        {
            return new PayloadRunResult(false, executablePath, "Windows 未能启动该工具。");
        }

        return new PayloadRunResult(true, executablePath, $"已启动，进程 ID：{process.Id}");
    }

    public async Task<string> ExtractAsync(
        PayloadDefinition payload,
        CancellationToken cancellationToken = default)
    {
        ValidateDefinition(payload);

        string hashFolder = payload.Sha256[..12].ToLowerInvariant();
        string targetDirectory = Path.Combine(AppDataRoot, "Runtime", payload.Id, hashFolder);
        string targetPath = Path.Combine(targetDirectory, payload.FileName);
        Directory.CreateDirectory(targetDirectory);

        if (File.Exists(targetPath))
        {
            string existingHash = await ComputeSha256Async(targetPath, cancellationToken);
            if (existingHash.Equals(payload.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return targetPath;
            }

            File.Delete(targetPath);
        }

        string resourceSuffix = ".Payloads." + payload.FileName;
        string resourceName = _assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"未找到内置文件：{payload.FileName}");

        string temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using Stream input = _assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"无法读取内置文件：{payload.FileName}");
            await using FileStream output = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough);

            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);

            string extractedHash = await ComputeSha256Async(temporaryPath, cancellationToken);
            if (!extractedHash.Equals(payload.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException($"完整性校验失败：{payload.DisplayName}");
            }

            File.Move(temporaryPath, targetPath, true);
            return targetPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static ProcessStartInfo BuildStartInfo(PayloadDefinition payload, string path)
    {
        string extension = Path.GetExtension(path);
        ProcessStartInfo startInfo;

        if (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            string commandInterpreter = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            string fixedArguments = string.IsNullOrWhiteSpace(payload.Arguments)
                ? string.Empty
                : " " + payload.Arguments.Trim();

            startInfo = new ProcessStartInfo
            {
                FileName = commandInterpreter,
                Arguments = $"/d /s /c \"\"{path}\"{fixedArguments}\"",
                WorkingDirectory = Path.GetDirectoryName(path)!,
                UseShellExecute = true
            };
        }
        else
        {
            startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = payload.Arguments.Trim(),
                WorkingDirectory = Path.GetDirectoryName(path)!,
                UseShellExecute = true
            };
        }

        if (payload.RequiresElevation)
        {
            startInfo.Verb = "runas";
        }

        return startInfo;
    }

    private static void ValidateDefinition(PayloadDefinition payload)
    {
        if (!SafeIdRegex().IsMatch(payload.Id))
        {
            throw new InvalidDataException($"工具 ID 不合法：{payload.Id}");
        }

        if (string.IsNullOrWhiteSpace(payload.DisplayName))
        {
            throw new InvalidDataException($"工具 {payload.Id} 缺少显示名称。");
        }

        if (!payload.FileName.Equals(Path.GetFileName(payload.FileName), StringComparison.Ordinal) ||
            !AllowedExtensions.Contains(Path.GetExtension(payload.FileName)))
        {
            throw new InvalidDataException($"工具文件名不合法：{payload.FileName}");
        }

        if (payload.Sha256.Length != 64 || !payload.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException($"工具 {payload.DisplayName} 缺少有效 SHA-256。");
        }

        if (payload.Arguments.Contains('\r') || payload.Arguments.Contains('\n'))
        {
            throw new InvalidDataException($"工具 {payload.DisplayName} 的参数不合法。");
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdRegex();
}
