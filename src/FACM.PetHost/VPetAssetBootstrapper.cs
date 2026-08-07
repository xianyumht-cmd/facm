using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FACM.PetHost;

internal static class PetHostPaths
{
    internal const string UpstreamCommit = "ac77ba144ed39f61624d93542c008b38be4d85aa";
    internal const string UpstreamShortCommit = "ac77ba14";

    internal static string RootDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FACM", "PetHost");
    internal static string AssetsDirectory => Path.Combine(RootDirectory, "Assets");
    internal static string AssetVersionDirectory => Path.Combine(AssetsDirectory, "vpet-" + UpstreamShortCommit);
    internal static string AssetStagingDirectory => Path.Combine(AssetsDirectory, ".vpet-" + UpstreamShortCommit + ".partial");
    internal static string PetDirectory => Path.Combine(AssetVersionDirectory, "pet");
    internal static string VupDirectory => Path.Combine(PetDirectory, "vup");
    internal static string PetConfigPath => Path.Combine(PetDirectory, "vup.lps");
    internal static string CacheDirectory => Path.Combine(RootDirectory, "Cache");
    internal static string CompletionMarker => Path.Combine(AssetVersionDirectory, ".facm-complete.json");
}

internal sealed class VPetAssetBootstrapper
{
    private const string Repository = "LorisYounger/VPet";
    private const string RepositoryPetPrefix = "VPet-Simulator.Windows/mod/0000_core/pet/";
    private const int DownloadConcurrency = 20;

    private static readonly string[] RequiredDirectoryPrefixes =
    {
        "vup/Default/", "vup/IDEL/", "vup/MOVE/", "vup/Raise/", "vup/StartUP/", "vup/Touch_Body/", "vup/Touch_Head/"
    };

    private static readonly HttpClient Http = CreateHttpClient();

    public async Task<string> EnsureAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(PetHostPaths.RootDirectory);
        Directory.CreateDirectory(PetHostPaths.AssetsDirectory);
        Directory.CreateDirectory(PetHostPaths.CacheDirectory);

        if (IsComplete())
        {
            progress?.Report("高精度动作资源已缓存");
            return PetHostPaths.PetDirectory;
        }

        progress?.Report("正在核对 VPet 官方动作清单…");
        var entries = await GetRequiredEntriesAsync(cancellationToken).ConfigureAwait(false);
        if (!entries.Any(x => x.Path.Equals(RepositoryPetPrefix + "vup.lps", StringComparison.Ordinal)))
            throw new InvalidOperationException("VPet 上游资源清单缺少 vup.lps。固定版本可能已失效。");
        if (entries.Count < 20)
            throw new InvalidOperationException("VPet 最小动作集异常偏少，已拒绝继续启动以避免残缺桌宠。");

        var totalBytes = entries.Sum(x => Math.Max(0L, x.Size));
        if (totalBytes <= 0 || totalBytes > 700L * 1024L * 1024L)
            throw new InvalidOperationException("VPet 最小动作集大小异常，已拒绝自动下载。");

        RecoverInterruptedStage();
        var stageDirectory = PetHostPaths.AssetStagingDirectory;
        var stagePetDirectory = Path.Combine(stageDirectory, "pet");
        Directory.CreateDirectory(stagePetDirectory);

        var completed = 0;
        foreach (var entry in entries)
        {
            var relative = entry.Path.Substring(RepositoryPetPrefix.Length);
            var destination = SafeLocalPath(stagePetDirectory, relative);
            if (IsDownloadedFileComplete(destination, entry.Size)) completed++;
        }

        if (completed > 0)
            progress?.Report($"继续上次进度 {completed}/{entries.Count} · 共约 {totalBytes / 1024d / 1024d:0.0} MB");
        else
            progress?.Report($"官方动作集 {entries.Count} 个文件 · 约 {totalBytes / 1024d / 1024d:0.0} MB");

        using var gate = new SemaphoreSlim(DownloadConcurrency, DownloadConcurrency);
        var tasks = entries.Select(async entry =>
        {
            var relative = entry.Path.Substring(RepositoryPetPrefix.Length);
            var destination = SafeLocalPath(stagePetDirectory, relative);
            if (IsDownloadedFileComplete(destination, entry.Size)) return;

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsDownloadedFileComplete(destination, entry.Size)) return;
                var parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                await DownloadPinnedRawAsync(entry.Path, destination, entry.Size, cancellationToken).ConfigureAwait(false);
                var now = Interlocked.Increment(ref completed);
                if (now == entries.Count || now % 12 == 0) progress?.Report($"正在缓存高精度动作 {now}/{entries.Count}");
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            var relative = entry.Path.Substring(RepositoryPetPrefix.Length);
            var destination = SafeLocalPath(stagePetDirectory, relative);
            if (!IsDownloadedFileComplete(destination, entry.Size))
                throw new InvalidOperationException("VPet 动作资源未完整下载：" + relative);
        }

        var notice = new StringBuilder()
            .AppendLine("FACM.PetHost VPet animation cache")
            .AppendLine("Source: https://github.com/LorisYounger/VPet")
            .AppendLine("Animation copyright: VUP-Simulator team")
            .AppendLine("Usage: upstream non-commercial animation authorization terms")
            .AppendLine("Pinned commit: " + PetHostPaths.UpstreamCommit)
            .AppendLine("FACM does not sell or relicense these animation assets.")
            .ToString();
        await File.WriteAllTextAsync(Path.Combine(stageDirectory, "VPET-ASSET-NOTICE.txt"), notice, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        var marker = JsonSerializer.Serialize(new
        {
            source = "https://github.com/LorisYounger/VPet",
            commit = PetHostPaths.UpstreamCommit,
            files = entries.Count,
            bytes = totalBytes,
            cached_at_utc = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(stageDirectory, ".facm-complete.json"), marker, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        if (Directory.Exists(PetHostPaths.AssetVersionDirectory)) Directory.Delete(PetHostPaths.AssetVersionDirectory, true);
        Directory.Move(stageDirectory, PetHostPaths.AssetVersionDirectory);
        progress?.Report("VPet 高精度动作资源准备完成");
        return PetHostPaths.PetDirectory;
    }

    internal static bool IsComplete()
    {
        try
        {
            if (!File.Exists(PetHostPaths.CompletionMarker) || !File.Exists(PetHostPaths.PetConfigPath)) return false;
            if (!Directory.Exists(PetHostPaths.VupDirectory)) return false;
            var marker = File.ReadAllText(PetHostPaths.CompletionMarker);
            return marker.Contains(PetHostPaths.UpstreamCommit, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void RecoverInterruptedStage()
    {
        if (Directory.Exists(PetHostPaths.AssetStagingDirectory)) return;
        try
        {
            var candidates = Directory.GetDirectories(PetHostPaths.AssetsDirectory, ".vpet-" + PetHostPaths.UpstreamShortCommit + ".partial-*")
                .Select(path => new DirectoryInfo(path)).OrderByDescending(info => info.LastWriteTimeUtc).ToArray();
            if (candidates.Length == 0) return;
            Directory.Move(candidates[0].FullName, PetHostPaths.AssetStagingDirectory);
            for (var index = 1; index < candidates.Length; index++)
            {
                try { candidates[index].Delete(true); } catch { }
            }
        }
        catch { }
    }

    private static bool IsDownloadedFileComplete(string destination, long expectedSize)
    {
        try
        {
            if (!File.Exists(destination)) return false;
            var size = new FileInfo(destination).Length;
            return expectedSize > 0 ? size == expectedSize : size > 0;
        }
        catch { return false; }
    }

    private static async Task<List<GitTreeEntry>> GetRequiredEntriesAsync(CancellationToken cancellationToken)
    {
        var commitUri = new Uri($"https://api.github.com/repos/{Repository}/git/commits/{PetHostPaths.UpstreamCommit}");
        using var commitResponse = await Http.GetAsync(commitUri, cancellationToken).ConfigureAwait(false);
        commitResponse.EnsureSuccessStatusCode();
        await using var commitStream = await commitResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var commitJson = await JsonDocument.ParseAsync(commitStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var treeSha = commitJson.RootElement.GetProperty("tree").GetProperty("sha").GetString();
        if (string.IsNullOrWhiteSpace(treeSha)) throw new InvalidOperationException("无法解析 VPet 固定提交的 tree SHA。");

        var treeUri = new Uri($"https://api.github.com/repos/{Repository}/git/trees/{treeSha}?recursive=1");
        using var treeResponse = await Http.GetAsync(treeUri, cancellationToken).ConfigureAwait(false);
        treeResponse.EnsureSuccessStatusCode();
        await using var treeStream = await treeResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var treeJson = await JsonDocument.ParseAsync(treeStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (treeJson.RootElement.TryGetProperty("truncated", out var truncated) && truncated.GetBoolean())
            throw new InvalidOperationException("VPet Git tree 返回被截断，无法安全构建最小动作集。");

        var result = new List<GitTreeEntry>();
        foreach (var item in treeJson.RootElement.GetProperty("tree").EnumerateArray())
        {
            if (!string.Equals(item.GetProperty("type").GetString(), "blob", StringComparison.Ordinal)) continue;
            var path = item.GetProperty("path").GetString();
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(RepositoryPetPrefix, StringComparison.Ordinal)) continue;
            var relative = path.Substring(RepositoryPetPrefix.Length);
            if (!relative.Equals("vup.lps", StringComparison.Ordinal) && !RequiredDirectoryPrefixes.Any(prefix => relative.StartsWith(prefix, StringComparison.Ordinal))) continue;
            long size = 0;
            if (item.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize)) size = parsedSize;
            result.Add(new GitTreeEntry(path, size));
        }
        return result.OrderBy(x => x.Path, StringComparer.Ordinal).ToList();
    }

    private static async Task DownloadPinnedRawAsync(string repositoryPath, string destination, long expectedSize, CancellationToken cancellationToken)
    {
        var encodedPath = string.Join("/", repositoryPath.Split('/').Select(Uri.EscapeDataString));
        var uri = new Uri($"https://raw.githubusercontent.com/LorisYounger/VPet/{PetHostPaths.UpstreamCommit}/{encodedPath}");
        var temporary = destination + ".download";
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            if (expectedSize > 0 && new FileInfo(temporary).Length != expectedSize) throw new IOException("下载文件大小不匹配：" + repositoryPath);
            File.Move(temporary, destination, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static string SafeLocalPath(string root, string relative)
    {
        var normalized = relative.Replace('/', Path.DirectorySeparatorChar);
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, normalized));
        if (!candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("检测到非法 VPet 资源路径：" + relative);
        return candidate;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM-PetHost/3.1");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private sealed record GitTreeEntry(string Path, long Size);
}
