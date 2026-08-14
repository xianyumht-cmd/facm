using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
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

        public bool DirectoryExists(string path) { return Directory.Exists(path); }
        public bool FileExists(string path) { return File.Exists(path); }
        public void CreateDirectory(string path) { Directory.CreateDirectory(path); }
        public void WriteAllText(string path, string content) { File.WriteAllText(path, content ?? string.Empty, Utf8NoBom); }
        public string ReadAllText(string path) { return File.ReadAllText(path, Encoding.UTF8); }
        public string[] GetFiles(string directory, string pattern) { return Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly); }
        public void MoveFile(string source, string destination) { File.Move(source, destination); }
        public void ReplaceFile(string source, string destination, string backup) { File.Replace(source, destination, backup, true); }
        public void DeleteFile(string path) { File.Delete(path); }
    }

    internal sealed class LeagueItemSetService : IDisposable
    {
        internal const string InstallDirPath = "/data-store/v1/install-dir";
        internal const string FilePrefix = "facm1-";

        private readonly ILeagueClientApi _client;
        private readonly LeagueLiveDataService _live;
        private readonly IOpggBuildApi _opgg;
        private readonly bool _ownsOpgg;
        private readonly ILeagueItemSetFileSystem _files;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        private readonly SemaphoreSlim _writeGate = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public LeagueItemSetService(ILeagueClientApi client, PerformanceBudgetProvider budgets)
            : this(client, budgets, new OpggBuildApiClient(), new LeagueItemSetPhysicalFileSystem(), true)
        {
        }

        internal LeagueItemSetService(
            ILeagueClientApi client,
            PerformanceBudgetProvider budgets,
            IOpggBuildApi opgg,
            ILeagueItemSetFileSystem files,
            bool ownsOpgg = false)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            if (budgets == null) throw new ArgumentNullException(nameof(budgets));
            _live = new LeagueLiveDataService(client, budgets);
            _opgg = opgg ?? throw new ArgumentNullException(nameof(opgg));
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _ownsOpgg = ownsOpgg;
        }

        public async Task<LeagueItemSetPlan> PrepareAsync(
            LeagueBuildAdvisorSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!IsUsableChampSelectSnapshot(snapshot)) return null;

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    var path = LeagueBuildAdvisorDataService.BuildPath(
                        snapshot.ChampionId,
                        snapshot.Mode,
                        snapshot.Position,
                        snapshot.Version);
                    var bytes = await _opgg.TryGetBytesAsync(path, timeout.Token).ConfigureAwait(false);
                    var plan = ParsePlan(bytes);
                    if (plan == null || !plan.HasItems) return null;

                    plan.ChampionId = snapshot.ChampionId;
                    plan.ChampionName = snapshot.ChampionName;
                    plan.QueueId = snapshot.QueueId;
                    plan.Mode = snapshot.Mode;
                    plan.Position = snapshot.Position;
                    plan.Version = snapshot.Version;
                    plan.Uid = BuildUid(plan);
                    plan.Title = BuildTitle(plan);
                    return plan;
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    return null;
                }
            }
        }

        public async Task<LeagueItemSetWriteResult> ApplyAsync(
            LeagueItemSetPlan plan,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = new LeagueItemSetWriteResult { Status = "failed" };
                var live = await _live.RefreshAsync(cancellationToken).ConfigureAwait(false);
                var local = live == null ? null : live.Players.FirstOrDefault(row => row.IsLocalPlayer);
                var currentChampion = LeagueBuildAdvisorDataService.ResolveChampionId(live, local);
                if (live == null || !live.Connected || live.Activity != LeagueActivityLevel.ChampSelect)
                {
                    result.Status = "blocked";
                    result.BlockReason = "champ-select-required";
                    return result;
                }
                if (plan.ChampionId <= 0 || currentChampion != plan.ChampionId)
                {
                    result.Status = "blocked";
                    result.BlockReason = "champion-changed";
                    return result;
                }
                if (plan.QueueId > 0 && live.QueueId > 0 && plan.QueueId != live.QueueId)
                {
                    result.Status = "blocked";
                    result.BlockReason = "queue-changed";
                    return result;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var installBytes = await _client.TryGetBytesAsync(InstallDirPath, cancellationToken).ConfigureAwait(false);
                var installDir = ParseInstallDirectory(installBytes);
                string targetDirectory;
                string layout;
                if (!TryResolveTargetDirectory(installDir, _files, out targetDirectory, out layout))
                {
                    result.Status = "failed";
                    result.Error = "install-layout-unavailable";
                    return result;
                }

                result.TargetDirectory = targetDirectory;
                var fileName = BuildSafeFileName(plan.Uid);
                if (fileName == null)
                {
                    result.Status = "failed";
                    result.Error = "invalid-owned-file-name";
                    return result;
                }
                result.FileName = fileName;

                var json = BuildItemSetJson(plan);
                if (!VerifyItemSetJson(json, plan))
                {
                    result.Status = "failed";
                    result.Error = "generated-json-invalid";
                    return result;
                }

                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var write = CommitOwnedFile(targetDirectory, fileName, json, plan, cancellationToken);
                    if (!write)
                    {
                        result.Status = "failed";
                        result.Error = "write-or-verify-failed";
                        return result;
                    }

                    bool cleanupWarning;
                    result.RemovedOldFiles = CleanupOldOwnedFiles(targetDirectory, fileName, out cleanupWarning);
                    result.CleanupWarning = cleanupWarning;
                    result.Status = "success";
                    AppLog.Info(
                        "League item set written; layout=" + layout +
                        "; directory=" + targetDirectory +
                        "; file=" + fileName +
                        "; cleanup=" + result.RemovedOldFiles);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    AppLog.Info("League item set write failed; directory=" + targetDirectory + "; error=" + exception.Message);
                    result.Status = "failed";
                    result.Error = "filesystem-write-failed";
                    return result;
                }
            }
            finally
            {
                _writeGate.Release();
            }
        }

        internal LeagueItemSetPlan ParsePlan(byte[] bytes)
        {
            var root = ParseObject(bytes);
            var data = ReadDictionary(root, "data");
            if (data == null) return null;

            var plan = new LeagueItemSetPlan();
            var starterIndex = 0;
            foreach (var row in EnumerateDictionaries(ReadValue(data, "starter_items")).Take(3))
            {
                starterIndex++;
                AddBlock(plan, FormatPickRate("出门装 " + starterIndex, row), ReadIntArray(ReadValue(row, "ids")));
            }

            var boots = new List<int>();
            foreach (var row in EnumerateDictionaries(ReadValue(data, "boots")))
                boots.AddRange(ReadIntArray(ReadValue(row, "ids")));
            AddBlock(plan, "鞋子", boots);

            var prism = new List<int>();
            foreach (var row in EnumerateDictionaries(ReadValue(data, "prism_items")))
                prism.AddRange(ReadIntArray(ReadValue(row, "ids")));
            AddBlock(plan, "特殊装备", prism);

            var coreIndex = 0;
            foreach (var row in EnumerateDictionaries(ReadValue(data, "core_items")).Take(4))
            {
                coreIndex++;
                AddBlock(plan, FormatPickRate("核心装备 " + coreIndex, row), ReadIntArray(ReadValue(row, "ids")));
            }

            var last = new List<int>();
            foreach (var row in EnumerateDictionaries(ReadValue(data, "last_items")))
                last.AddRange(ReadIntArray(ReadValue(row, "ids")));
            AddBlock(plan, "后期装备", last);
            return plan;
        }

        internal static int RestoreRecipe(int itemId)
        {
            switch (itemId)
            {
                case 3042: return 3004;
                case 223042: return 223004;
                case 323042: return 323004;
                case 3040: return 3003;
                case 223040: return 223003;
                case 323040: return 323003;
                case 3121: return 3119;
                case 223121: return 223119;
                case 323121: return 323119;
                case 2530: return 2526;
                case 222530: return 222526;
                case 322530: return 322526;
                default: return itemId;
            }
        }

        internal static bool TryResolveTargetDirectory(
            string installDir,
            ILeagueItemSetFileSystem files,
            out string targetDirectory,
            out string layout)
        {
            targetDirectory = null;
            layout = null;
            if (files == null || string.IsNullOrWhiteSpace(installDir)) return false;
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
                if (parent == null) return false;
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

        internal string BuildItemSetJson(LeagueItemSetPlan plan)
        {
            if (plan == null) return null;
            var blocks = new List<object>();
            foreach (var block in plan.Blocks.Where(item => item != null && item.Items.Count > 0))
            {
                var items = new List<object>();
                foreach (var id in block.Items)
                {
                    items.Add(new Dictionary<string, object>
                    {
                        { "id", RestoreRecipe(id).ToString(CultureInfo.InvariantCulture) },
                        { "count", 1 }
                    });
                }
                blocks.Add(new Dictionary<string, object>
                {
                    { "type", block.Title ?? string.Empty },
                    { "items", items.ToArray() }
                });
            }

            return _json.Serialize(new Dictionary<string, object>
            {
                { "uid", plan.Uid },
                { "title", plan.Title },
                { "sortrank", 0 },
                { "type", "global" },
                { "map", "any" },
                { "mode", "any" },
                { "blocks", blocks.ToArray() },
                { "associatedChampions", new object[0] },
                { "associatedMaps", new object[0] },
                { "preferredItemSlots", new object[0] }
            });
        }

        internal bool VerifyItemSetJson(string json, LeagueItemSetPlan plan)
        {
            if (string.IsNullOrWhiteSpace(json) || plan == null) return false;
            Dictionary<string, object> root;
            try { root = _json.DeserializeObject(json) as Dictionary<string, object>; }
            catch { return false; }
            if (root == null) return false;
            if (!string.Equals(ReadString(root, "uid"), plan.Uid, StringComparison.Ordinal)) return false;
            if (!string.Equals(ReadString(root, "title"), plan.Title, StringComparison.Ordinal)) return false;
            var blocks = EnumerateDictionaries(ReadValue(root, "blocks")).ToList();
            if (blocks.Count != plan.Blocks.Count(block => block != null && block.Items.Count > 0)) return false;
            var actualItemCount = 0;
            foreach (var block in blocks)
                actualItemCount += EnumerateDictionaries(ReadValue(block, "items")).Count();
            return actualItemCount == plan.ItemCount;
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
            var temp = Path.Combine(targetDirectory, ".facm1-" + token + ".tmp");
            var backup = Path.Combine(targetDirectory, ".facm1-" + token + ".bak");
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
                        }
                    }
                    else if (_files.FileExists(destination))
                    {
                        try { _files.DeleteFile(destination); }
                        catch { }
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
                    try { _files.DeleteFile(temp); }
                    catch { }
                }
            }
        }

        private int CleanupOldOwnedFiles(
            string targetDirectory,
            string keepFileName,
            out bool warning)
        {
            warning = false;
            var removed = 0;
            string[] candidates;
            try { candidates = _files.GetFiles(targetDirectory, FilePrefix + "*.json") ?? new string[0]; }
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

        private static void AddBlock(LeagueItemSetPlan plan, string title, IEnumerable<int> ids)
        {
            if (plan == null || ids == null) return;
            var values = ids.Where(id => id > 0).ToList();
            if (values.Count == 0) return;
            var block = new LeagueItemSetBlock { Title = string.IsNullOrWhiteSpace(title) ? "推荐装备" : title };
            block.Items.AddRange(values);
            plan.Blocks.Add(block);
        }

        private static string FormatPickRate(string title, Dictionary<string, object> row)
        {
            var rate = ReadDoubleNullable(row, "pick_rate");
            if (!rate.HasValue) return title;
            return title + " · " + (rate.Value * 100.0).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        }

        private static string BuildUid(LeagueItemSetPlan plan)
        {
            return FilePrefix +
                   plan.ChampionId.ToString(CultureInfo.InvariantCulture) + "-" +
                   SafeSegment(plan.Mode) + "-global-all-" +
                   SafeSegment(plan.Position) + "-" +
                   SafeSegment(plan.Version);
        }

        private static string BuildTitle(LeagueItemSetPlan plan)
        {
            var champion = string.IsNullOrWhiteSpace(plan.ChampionName)
                ? "#" + plan.ChampionId.ToString(CultureInfo.InvariantCulture)
                : plan.ChampionName.Trim();
            var title = "[FACM] " + champion;
            if (!string.IsNullOrWhiteSpace(plan.Mode)) title += " - " + plan.Mode.Trim();
            if (!string.IsNullOrWhiteSpace(plan.Position) && !string.Equals(plan.Position, "none", StringComparison.OrdinalIgnoreCase))
                title += " - " + plan.Position.Trim();
            return title.Length <= 100 ? title : title.Substring(0, 100);
        }

        private static string BuildSafeFileName(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid) || !uid.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)) return null;
            if (uid.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            if (uid.IndexOf('/') >= 0 || uid.IndexOf('\\') >= 0 || uid.Contains("..")) return null;
            return uid + ".json";
        }

        private static bool IsOwnedFileName(string fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName) &&
                   fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) &&
                   fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                   fileName.IndexOf('/') < 0 && fileName.IndexOf('\\') < 0 && !fileName.Contains("..");
        }

        private static string SafeSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "_";
            var input = value.Trim();
            var output = new StringBuilder(input.Length);
            foreach (var ch in input)
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') ||
                    (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' || ch == '.')
                    output.Append(ch);
                else
                    output.Append('_');
            }
            return output.Length == 0 ? "_" : output.ToString();
        }

        private static string ParseInstallDirectory(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var text = Encoding.UTF8.GetString(bytes).Trim();
            if (text.Length == 0) return null;
            try
            {
                var serializer = new JavaScriptSerializer();
                var decoded = serializer.DeserializeObject(text) as string;
                if (!string.IsNullOrWhiteSpace(decoded)) return decoded.Trim();
            }
            catch { }
            return text.Trim().Trim('"');
        }

        private static bool IsUnder(string child, string parent)
        {
            if (string.IsNullOrWhiteSpace(child) || string.IsNullOrWhiteSpace(parent)) return false;
            var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var normalizedChild = Path.GetFullPath(child);
            return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUsableChampSelectSnapshot(LeagueBuildAdvisorSnapshot snapshot)
        {
            return snapshot != null && snapshot.Connected &&
                   snapshot.Activity == LeagueActivityLevel.ChampSelect &&
                   snapshot.ChampionId > 0 && snapshot.Recommendation != null &&
                   string.Equals(snapshot.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(snapshot.Mode) &&
                   !string.IsNullOrWhiteSpace(snapshot.Position) &&
                   !string.IsNullOrWhiteSpace(snapshot.Version);
        }

        private Dictionary<string, object> ParseObject(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try { return _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch { return null; }
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> source, string key)
        {
            return ReadValue(source, key) as Dictionary<string, object>;
        }

        private static object ReadValue(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static double? ReadDoubleNullable(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            if (value == null) return null;
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private static List<int> ReadIntArray(object value)
        {
            var output = new List<int>();
            foreach (var item in EnumerateValues(value))
            {
                int parsed;
                try { parsed = Convert.ToInt32(item, CultureInfo.InvariantCulture); }
                catch { continue; }
                if (parsed > 0) output.Add(parsed);
            }
            return output;
        }

        private static IEnumerable<Dictionary<string, object>> EnumerateDictionaries(object value)
        {
            foreach (var item in EnumerateValues(value))
            {
                var row = item as Dictionary<string, object>;
                if (row != null) yield return row;
            }
        }

        private static IEnumerable<object> EnumerateValues(object value)
        {
            if (value == null || value is string) yield break;
            var enumerable = value as IEnumerable;
            if (enumerable == null) yield break;
            foreach (var item in enumerable) yield return item;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LeagueItemSetService));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _writeGate.Dispose();
            if (_ownsOpgg)
            {
                var disposable = _opgg as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
        }
    }
}
