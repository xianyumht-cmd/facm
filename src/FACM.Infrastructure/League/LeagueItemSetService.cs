using System.Globalization;
using System.Text;
using System.Text.Json;
using FACM.Core.League;

namespace FACM.Infrastructure.League;

internal interface ILeagueItemSetFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void CreateDirectory(string path);
    void WriteAllText(string path, string content);
    string ReadAllText(string path);
    string[] GetFiles(string directory, string pattern);
    void MoveFile(string source, string destination);
    void ReplaceFile(string source, string destination, string backup);
    void DeleteFile(string path);
}

internal sealed class LeagueItemSetPhysicalFileSystem : ILeagueItemSetFileSystem
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void WriteAllText(string path, string content) => File.WriteAllText(path, content ?? string.Empty, Utf8NoBom);
    public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);
    public string[] GetFiles(string directory, string pattern) => Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
    public void MoveFile(string source, string destination) => File.Move(source, destination);
    public void ReplaceFile(string source, string destination, string backup) => File.Replace(source, destination, backup, ignoreMetadataErrors: true);
    public void DeleteFile(string path) => File.Delete(path);
}

/// <summary>
/// FACM 4.0 item-set service. Preparation is read-only. Apply rechecks the shared Workbench live
/// snapshot before the first write, constrains output to League's Recommended directory, performs an
/// atomic owned-file commit, and only cleans files with the FACM 4.0 prefix.
/// </summary>
public sealed class LeagueItemSetService : ILeagueItemSetService, IDisposable
{
    internal const string InstallDirPath = "/data-store/v1/install-dir";
    internal const string FilePrefix = "facm4-";

    private readonly ILeagueWorkbenchDataSource _workbench;
    private readonly ILeagueReadGateway _lcu;
    private readonly IOpggBuildSource _opgg;
    private readonly bool _ownsOpgg;
    private readonly ILeagueItemSetFileSystem _files;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _disposed;

    public LeagueItemSetService(
        ILeagueWorkbenchDataSource workbench,
        ILeagueReadGateway lcu)
        : this(workbench, lcu, new OpggBuildHttpSource(), new LeagueItemSetPhysicalFileSystem(), ownsOpgg: true)
    {
    }

    internal LeagueItemSetService(
        ILeagueWorkbenchDataSource workbench,
        ILeagueReadGateway lcu,
        IOpggBuildSource opgg,
        ILeagueItemSetFileSystem files,
        bool ownsOpgg = false)
    {
        _workbench = workbench ?? throw new ArgumentNullException(nameof(workbench));
        _lcu = lcu ?? throw new ArgumentNullException(nameof(lcu));
        _opgg = opgg ?? throw new ArgumentNullException(nameof(opgg));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _ownsOpgg = ownsOpgg;
    }

    public async Task<LeagueItemSetPlan?> PrepareAsync(
        LeagueBuildAdvisorSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsUsableChampSelectSnapshot(snapshot)) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            var path = LeagueBuildAdvisorService.BuildPath(
                snapshot.ChampionId,
                snapshot.Mode,
                snapshot.Position,
                snapshot.Version);
            var bytes = await _opgg.TryGetBytesAsync(path, timeout.Token).ConfigureAwait(false);
            var blocks = ParseBlocks(bytes);
            if (blocks.Count == 0) return null;

            var provisional = new LeagueItemSetPlan(
                snapshot.ChampionId,
                snapshot.ChampionName,
                snapshot.QueueId,
                snapshot.Mode,
                snapshot.Position,
                snapshot.Version,
                string.Empty,
                string.Empty,
                blocks);
            var uid = BuildUid(provisional);
            var title = BuildTitle(provisional);
            return provisional with { Uid = uid, Title = title };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<LeagueItemSetApplyResult> ApplyAsync(
        LeagueItemSetPlan plan,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(plan);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!plan.HasItems)
                return Failed("empty-plan");

            var live = await _workbench.LoadLiveAsync(cancellationToken).ConfigureAwait(false);
            if (live.State == LeagueWorkbenchDataState.Unavailable ||
                !string.Equals(live.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
            {
                return Blocked("champ-select-required");
            }

            var currentChampion = ResolveLocalChampionId(live);
            if (plan.ChampionId <= 0 || currentChampion != plan.ChampionId)
                return Blocked("champion-changed");
            if (plan.QueueId > 0 && live.Queue?.QueueId is > 0 && plan.QueueId != live.Queue.QueueId)
                return Blocked("queue-changed");

            cancellationToken.ThrowIfCancellationRequested();
            var installBytes = await _lcu.TryGetBytesAsync(InstallDirPath, cancellationToken).ConfigureAwait(false);
            var installDir = ParseInstallDirectory(installBytes);
            if (!TryResolveTargetDirectory(installDir, _files, out var targetDirectory, out _))
                return Failed("install-layout-unavailable");

            var fileName = BuildSafeFileName(plan.Uid);
            if (fileName is null) return Failed("invalid-owned-file-name");

            var json = BuildItemSetJson(plan);
            if (!VerifyItemSetJson(json, plan)) return Failed("generated-json-invalid");

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!CommitOwnedFile(targetDirectory, fileName, json, plan, cancellationToken))
                    return Failed("write-or-verify-failed", targetDirectory, fileName);

                var removed = CleanupOldOwnedFiles(targetDirectory, fileName, out var cleanupWarning);
                return new LeagueItemSetApplyResult(
                    LeagueItemSetApplyState.Success,
                    "success",
                    targetDirectory,
                    fileName,
                    removed,
                    cleanupWarning);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return Failed("filesystem-write-failed", targetDirectory, fileName);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    internal static IReadOnlyList<LeagueItemSetBlock> ParseBlocks(byte[]? bytes)
    {
        using var document = ParseDocument(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object ||
            !TryGetObject(document.RootElement, "data", out var data))
            return Array.Empty<LeagueItemSetBlock>();

        var blocks = new List<LeagueItemSetBlock>();
        AddIndividualBlocks(blocks, data, "starter_items", "出门装", maxRows: 3);
        AddCombinedBlock(blocks, data, "boots", "鞋子");
        AddCombinedBlock(blocks, data, "prism_items", "特殊装备");
        AddIndividualBlocks(blocks, data, "core_items", "核心装备", maxRows: 4);
        AddCombinedBlock(blocks, data, "last_items", "后期装备");
        return blocks;
    }

    internal static int RestoreRecipe(int itemId) => itemId switch
    {
        3042 => 3004,
        223042 => 223004,
        323042 => 323004,
        3040 => 3003,
        223040 => 223003,
        323040 => 323003,
        3121 => 3119,
        223121 => 223119,
        323121 => 323119,
        2530 => 2526,
        222530 => 222526,
        322530 => 322526,
        _ => itemId
    };

    internal static bool TryResolveTargetDirectory(
        string? installDir,
        ILeagueItemSetFileSystem files,
        out string targetDirectory,
        out string layout)
    {
        targetDirectory = string.Empty;
        layout = string.Empty;
        if (files is null || string.IsNullOrWhiteSpace(installDir)) return false;

        string fullInstall;
        try
        {
            if (!Path.IsPathRooted(installDir)) return false;
            fullInstall = Path.GetFullPath(installDir.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return false;
        }
        if (!files.DirectoryExists(fullInstall)) return false;

        var leaf = Path.GetFileName(fullInstall);
        if (string.Equals(leaf, "LeagueClient", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(fullInstall);
            if (parent is null) return false;
            var gameRoot = Path.GetFullPath(Path.Combine(parent.FullName, "Game"));
            if (!files.DirectoryExists(gameRoot)) return false;
            var candidate = Path.GetFullPath(Path.Combine(gameRoot, "Config", "Global", "Recommended"));
            if (!IsUnder(candidate, gameRoot)) return false;
            targetDirectory = candidate;
            layout = "tencent-sibling-game";
            return true;
        }

        var standard = Path.GetFullPath(Path.Combine(fullInstall, "Config", "Global", "Recommended"));
        if (!IsUnder(standard, fullInstall)) return false;
        targetDirectory = standard;
        layout = "standard-install";
        return true;
    }

    internal static string BuildItemSetJson(LeagueItemSetPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var blocks = plan.Blocks
            .Where(block => block is not null && block.ItemIds.Count > 0)
            .Select(block => new
            {
                type = block.Title ?? string.Empty,
                items = block.ItemIds.Select(id => new
                {
                    id = RestoreRecipe(id).ToString(CultureInfo.InvariantCulture),
                    count = 1
                }).ToArray()
            }).ToArray();

        return JsonSerializer.Serialize(new
        {
            uid = plan.Uid,
            title = plan.Title,
            sortrank = 0,
            type = "global",
            map = "any",
            mode = "any",
            blocks,
            associatedChampions = Array.Empty<object>(),
            associatedMaps = Array.Empty<object>(),
            preferredItemSlots = Array.Empty<object>()
        });
    }

    internal static bool VerifyItemSetJson(string? json, LeagueItemSetPlan plan)
    {
        if (string.IsNullOrWhiteSpace(json) || plan is null) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!string.Equals(ReadString(root, "uid"), plan.Uid, StringComparison.Ordinal)) return false;
            if (!string.Equals(ReadString(root, "title"), plan.Title, StringComparison.Ordinal)) return false;
            if (!root.TryGetProperty("blocks", out var blocks) || blocks.ValueKind != JsonValueKind.Array) return false;

            var expectedBlocks = plan.Blocks.Count(block => block is not null && block.ItemIds.Count > 0);
            var actualBlocks = 0;
            var actualItems = 0;
            foreach (var block in blocks.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) return false;
                actualBlocks++;
                if (!block.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return false;
                actualItems += items.GetArrayLength();
            }
            return actualBlocks == expectedBlocks && actualItems == plan.ItemCount;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool CommitOwnedFile(
        string targetDirectory,
        string fileName,
        string json,
        LeagueItemSetPlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_files.DirectoryExists(targetDirectory)) _files.CreateDirectory(targetDirectory);

        var destination = Path.Combine(targetDirectory, fileName);
        var token = Guid.NewGuid().ToString("N");
        var temp = Path.Combine(targetDirectory, ".facm4-" + token + ".tmp");
        var backup = Path.Combine(targetDirectory, ".facm4-" + token + ".bak");
        var hadDestination = _files.FileExists(destination);
        var replaced = false;
        try
        {
            _files.WriteAllText(temp, json);
            if (!VerifyItemSetJson(_files.ReadAllText(temp), plan)) return false;
            cancellationToken.ThrowIfCancellationRequested();

            if (hadDestination)
            {
                _files.ReplaceFile(temp, destination, backup);
                replaced = true;
            }
            else
            {
                _files.MoveFile(temp, destination);
            }

            if (!VerifyItemSetJson(_files.ReadAllText(destination), plan))
            {
                if (hadDestination && _files.FileExists(backup))
                {
                    try
                    {
                        if (_files.FileExists(destination)) _files.DeleteFile(destination);
                        _files.MoveFile(backup, destination);
                    }
                    catch
                    {
                        // Preserve the backup when rollback cannot complete.
                    }
                }
                else if (_files.FileExists(destination))
                {
                    try { _files.DeleteFile(destination); } catch { }
                }
                return false;
            }

            if (replaced && _files.FileExists(backup)) _files.DeleteFile(backup);
            return true;
        }
        finally
        {
            if (_files.FileExists(temp))
            {
                try { _files.DeleteFile(temp); } catch { }
            }
        }
    }

    private int CleanupOldOwnedFiles(string targetDirectory, string keepFileName, out bool warning)
    {
        warning = false;
        var removed = 0;
        string[] candidates;
        try
        {
            candidates = _files.GetFiles(targetDirectory, FilePrefix + "*.json") ?? Array.Empty<string>();
        }
        catch
        {
            warning = true;
            return 0;
        }

        foreach (var path in candidates)
        {
            var fileName = Path.GetFileName(path);
            if (!IsOwnedFileName(fileName) || string.Equals(fileName, keepFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                _files.DeleteFile(path);
                removed++;
            }
            catch
            {
                warning = true;
            }
        }
        return removed;
    }

    private static void AddIndividualBlocks(
        ICollection<LeagueItemSetBlock> blocks,
        JsonElement data,
        string property,
        string title,
        int maxRows)
    {
        if (!data.TryGetProperty(property, out var rows) || rows.ValueKind != JsonValueKind.Array) return;
        var index = 0;
        foreach (var row in rows.EnumerateArray().Take(maxRows))
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            var ids = ReadIds(row);
            if (ids.Count == 0) continue;
            index++;
            blocks.Add(new LeagueItemSetBlock(FormatPickRate(title + " " + index, row), ids));
        }
    }

    private static void AddCombinedBlock(
        ICollection<LeagueItemSetBlock> blocks,
        JsonElement data,
        string property,
        string title)
    {
        if (!data.TryGetProperty(property, out var rows) || rows.ValueKind != JsonValueKind.Array) return;
        var ids = new List<int>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            ids.AddRange(ReadIds(row));
        }
        if (ids.Count > 0) blocks.Add(new LeagueItemSetBlock(title, ids));
    }

    private static IReadOnlyList<int> ReadIds(JsonElement row)
    {
        var ids = new List<int>();
        if (!row.TryGetProperty("ids", out var array) || array.ValueKind != JsonValueKind.Array) return ids;
        foreach (var value in array.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0)
                ids.Add(number);
            else if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) && number > 0)
                ids.Add(number);
        }
        return ids;
    }

    private static string FormatPickRate(string title, JsonElement row)
    {
        if (!row.TryGetProperty("pick_rate", out var value)) return title;
        double rate;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out rate))
            return title + " · " + NormalizeRate(rate).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out rate))
            return title + " · " + NormalizeRate(rate).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        return title;
    }

    private static double NormalizeRate(double rate) => rate <= 1d ? rate * 100d : rate;

    private static string BuildUid(LeagueItemSetPlan plan) =>
        FilePrefix +
        plan.ChampionId.ToString(CultureInfo.InvariantCulture) + "-" +
        SafeSegment(plan.Mode) + "-global-all-" +
        SafeSegment(plan.Position) + "-" +
        SafeSegment(plan.Version);

    private static string BuildTitle(LeagueItemSetPlan plan)
    {
        var champion = string.IsNullOrWhiteSpace(plan.ChampionName)
            ? "#" + plan.ChampionId.ToString(CultureInfo.InvariantCulture)
            : plan.ChampionName.Trim();
        var title = "[FACM 4] " + champion;
        if (!string.IsNullOrWhiteSpace(plan.Mode)) title += " - " + plan.Mode.Trim();
        if (!string.IsNullOrWhiteSpace(plan.Position) && !string.Equals(plan.Position, "none", StringComparison.OrdinalIgnoreCase))
            title += " - " + plan.Position.Trim();
        return title.Length <= 100 ? title : title[..100];
    }

    private static string? BuildSafeFileName(string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid) || !uid.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)) return null;
        if (uid.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        if (uid.Contains('/') || uid.Contains('\\') || uid.Contains("..", StringComparison.Ordinal)) return null;
        return uid + ".json";
    }

    private static bool IsOwnedFileName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) &&
        fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) &&
        fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
        !fileName.Contains('/') &&
        !fileName.Contains('\\') &&
        !fileName.Contains("..", StringComparison.Ordinal);

    private static string SafeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "_";
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.') builder.Append(ch);
            else builder.Append('_');
        }
        return builder.Length == 0 ? "_" : builder.ToString();
    }

    private static string ParseInstallDirectory(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return string.Empty;
        var text = Encoding.UTF8.GetString(bytes).Trim();
        if (text.Length == 0) return string.Empty;
        try
        {
            var decoded = JsonSerializer.Deserialize<string>(text);
            if (!string.IsNullOrWhiteSpace(decoded)) return decoded.Trim();
        }
        catch (JsonException)
        {
        }
        return text.Trim().Trim('"');
    }

    private static int ResolveLocalChampionId(LeagueWorkbenchLiveSnapshot live)
    {
        var local = live.Players.FirstOrDefault(player => player.IsLocalPlayer);
        if (local is not null && local.ChampionId > 0) return local.ChampionId;
        if (local is not null && local.ChampionPickIntent > 0) return local.ChampionPickIntent;
        return live.LocalActionChampionId > 0 ? live.LocalActionChampionId : 0;
    }

    private static bool IsUsableChampSelectSnapshot(LeagueBuildAdvisorSnapshot? snapshot) =>
        snapshot is not null &&
        snapshot.State == LeagueBuildAdvisorState.Ready &&
        string.Equals(snapshot.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) &&
        snapshot.ChampionId > 0 &&
        snapshot.Recommendation is not null &&
        !string.IsNullOrWhiteSpace(snapshot.Mode) &&
        !string.IsNullOrWhiteSpace(snapshot.Position) &&
        !string.IsNullOrWhiteSpace(snapshot.Version);

    private static bool IsUnder(string child, string parent)
    {
        var normalizedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedChild = Path.GetFullPath(child);
        return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonDocument? ParseDocument(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try { return JsonDocument.Parse(bytes); }
        catch (JsonException) { return null; }
    }

    private static bool TryGetObject(JsonElement source, string property, out JsonElement value)
    {
        if (source.ValueKind == JsonValueKind.Object &&
            source.TryGetProperty(property, out value) &&
            value.ValueKind == JsonValueKind.Object)
            return true;
        value = default;
        return false;
    }

    private static string ReadString(JsonElement source, string property)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(property, out var value)) return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    }

    private static LeagueItemSetApplyResult Blocked(string detail) =>
        new(LeagueItemSetApplyState.Blocked, detail, string.Empty, string.Empty, 0, false);

    private static LeagueItemSetApplyResult Failed(
        string detail,
        string targetDirectory = "",
        string fileName = "") =>
        new(LeagueItemSetApplyState.Failed, detail, targetDirectory, fileName, 0, false);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writeGate.Dispose();
        if (_ownsOpgg && _opgg is IDisposable disposable) disposable.Dispose();
    }
}
