using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;

namespace FACM.League
{
    internal static class LeagueItemSetSmokeTest
    {
        public static void Validate()
        {
            ValidatePreparationIsReadOnlyAndParsesCurrentShape();
            ValidateRecipeRestore();
            ValidateInstallLayoutsFailClosed();
            ValidateHappyPathOwnershipAndVerification();
            ValidateContextDriftBlocksDiskWrites();
            ValidateWriteFailurePreservesOldFiles();
            ValidateCancellationBeforeCommit();
            Require(LeagueItemSetUiBridge.HasTrayAccessForSmokeTest(), "Gate 3 tray bridge cannot access the FACM tray contract.");
        }

        private static void ValidatePreparationIsReadOnlyAndParsesCurrentShape()
        {
            var lcu = new FakeLeagueApi();
            var opgg = new FakeOpggApi();
            var files = CreateTencentFileSystem();
            using (var service = new LeagueItemSetService(lcu, new PerformanceBudgetProvider(), opgg, files))
            {
                var plan = service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                Require(plan != null && plan.HasItems, "Gate 3 did not parse the current OP.GG item-set shape.");
                Require(plan.Blocks.Count == 7, "Gate 3 did not retain starter/boots/prism/core/last groups.");
                Require(plan.ItemCount == 15, "Gate 3 item count changed unexpectedly.");
                Require(plan.Uid.StartsWith(LeagueItemSetService.FilePrefix, StringComparison.Ordinal),
                    "Gate 3 generated an item-set UID outside the FACM-owned namespace.");
                Require(opgg.Paths.Count == 1 && opgg.Paths[0].Contains("/ranked/157/mid"),
                    "Gate 3 did not reuse the accepted Build Advisor champion/mode/position context.");
                Require(files.MutationCount == 0, "Preparing Gate 3 touched the filesystem before user confirmation.");
            }
        }

        private static void ValidateRecipeRestore()
        {
            Require(LeagueItemSetService.RestoreRecipe(3042) == 3004, "Gate 3 lost Muramana recipe restoration.");
            Require(LeagueItemSetService.RestoreRecipe(223040) == 223003, "Gate 3 lost Arena Seraph recipe restoration.");
            Require(LeagueItemSetService.RestoreRecipe(323121) == 323119, "Gate 3 lost prefixed Fimbulwinter recipe restoration.");
            Require(LeagueItemSetService.RestoreRecipe(2530) == 2526, "Gate 3 lost upgraded item recipe restoration.");
            Require(LeagueItemSetService.RestoreRecipe(3031) == 3031, "Gate 3 changed an item without a recipe restoration rule.");
        }

        private static void ValidateInstallLayoutsFailClosed()
        {
            var tencent = CreateTencentFileSystem();
            string target;
            string layout;
            Require(LeagueItemSetService.TryResolveTargetDirectory(
                    TencentInstall, tencent, out target, out layout),
                "Gate 3 did not recognize the verified Tencent LeagueClient + sibling Game layout.");
            Require(string.Equals(target, TencentRecommended, StringComparison.OrdinalIgnoreCase),
                "Gate 3 Tencent item-set path is not Game/Config/Global/Recommended.");
            Require(layout == "tencent-sibling-game", "Gate 3 did not mark the Tencent sibling Game layout.");

            var standard = new FakeFileSystem();
            standard.AddDirectory(StandardInstall);
            Require(LeagueItemSetService.TryResolveTargetDirectory(
                    StandardInstall, standard, out target, out layout),
                "Gate 3 did not recognize a standard Riot install layout.");
            Require(string.Equals(target, StandardRecommended, StringComparison.OrdinalIgnoreCase),
                "Gate 3 standard item-set path is incorrect.");
            Require(layout == "standard-install", "Gate 3 did not mark the standard install layout.");

            var missingGame = new FakeFileSystem();
            missingGame.AddDirectory(TencentInstall);
            Require(!LeagueItemSetService.TryResolveTargetDirectory(
                    TencentInstall, missingGame, out target, out layout),
                "Gate 3 guessed a Tencent Recommended path without a confirmed sibling Game directory.");
            Require(!LeagueItemSetService.TryResolveTargetDirectory(
                    "relative\\LeagueClient", tencent, out target, out layout),
                "Gate 3 accepted a relative install-dir path.");
            Require(!LeagueItemSetService.TryResolveTargetDirectory(
                    @"C:\\DoesNotExist\\LeagueClient", tencent, out target, out layout),
                "Gate 3 accepted a missing install directory.");
        }

        private static void ValidateHappyPathOwnershipAndVerification()
        {
            var lcu = new FakeLeagueApi { InstallDirectory = TencentInstall };
            var files = CreateTencentFileSystem();
            files.AddDirectory(TencentRecommended);
            var userFile = Path.Combine(TencentRecommended, "user.json");
            var thirdParty = Path.Combine(TencentRecommended, "third-party.json");
            var oldFacm = Path.Combine(TencentRecommended, LeagueItemSetService.FilePrefix + "old.json");
            files.AddFile(userFile, "user-owned");
            files.AddFile(thirdParty, "third-party-owned");
            files.AddFile(oldFacm, "old-facm");

            using (var service = new LeagueItemSetService(lcu, new PerformanceBudgetProvider(), new FakeOpggApi(), files))
            {
                var plan = service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();

                Require(result.Succeeded, "Gate 3 happy path did not report a verified write.");
                Require(result.RemovedOldFiles == 1, "Gate 3 did not clean exactly the previous FACM-owned JSON.");
                Require(files.FileExists(userFile) && files.ReadAllText(userFile) == "user-owned",
                    "Gate 3 modified or removed a user-owned Recommended JSON.");
                Require(files.FileExists(thirdParty) && files.ReadAllText(thirdParty) == "third-party-owned",
                    "Gate 3 modified or removed a third-party Recommended JSON.");
                Require(!files.FileExists(oldFacm), "Gate 3 left the superseded FACM-owned JSON after verified success.");

                var destination = Path.Combine(result.TargetDirectory, result.FileName);
                Require(files.FileExists(destination), "Gate 3 did not commit its owned item-set file.");
                var written = files.ReadAllText(destination);
                Require(service.VerifyItemSetJson(written, plan), "Gate 3 read-back verification rejected its committed item set.");
                Require(written.Contains("\"id\":\"3004\""), "Gate 3 did not restore the Muramana recipe in the League JSON.");
                Require(written.Contains("\"type\":\"global\""), "Gate 3 output is missing the League global item-set shape.");
                Require(files.DeletedPaths.All(path =>
                        Path.GetFileName(path).StartsWith(LeagueItemSetService.FilePrefix, StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(path).StartsWith(".facm1-", StringComparison.OrdinalIgnoreCase)),
                    "Gate 3 deleted a file outside its owned prefix.");
            }
        }

        private static void ValidateContextDriftBlocksDiskWrites()
        {
            var lcu = new FakeLeagueApi { InstallDirectory = TencentInstall };
            var files = CreateTencentFileSystem();
            using (var service = new LeagueItemSetService(lcu, new PerformanceBudgetProvider(), new FakeOpggApi(), files))
            {
                var plan = service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                lcu.Phase = "InProgress";
                var before = files.MutationCount;
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Status == "blocked" && result.BlockReason == "champ-select-required",
                    "Gate 3 did not block after leaving Champ Select.");
                Require(files.MutationCount == before, "Gate 3 wrote to disk after phase drift to In Game.");
            }

            lcu = new FakeLeagueApi { InstallDirectory = TencentInstall };
            files = CreateTencentFileSystem();
            using (var service = new LeagueItemSetService(lcu, new PerformanceBudgetProvider(), new FakeOpggApi(), files))
            {
                var plan = service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                lcu.ChampionId = 145;
                var before = files.MutationCount;
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Status == "blocked" && result.BlockReason == "champion-changed",
                    "Gate 3 did not block a stale champion item set.");
                Require(files.MutationCount == before, "Gate 3 wrote a stale champion item set.");
            }
        }

        private static void ValidateWriteFailurePreservesOldFiles()
        {
            var lcu = new FakeLeagueApi { InstallDirectory = TencentInstall };
            var files = CreateTencentFileSystem();
            files.AddDirectory(TencentRecommended);
            var oldFacm = Path.Combine(TencentRecommended, LeagueItemSetService.FilePrefix + "old.json");
            var userFile = Path.Combine(TencentRecommended, "user.json");
            files.AddFile(oldFacm, "old-facm");
            files.AddFile(userFile, "user-owned");
            files.FailNextMove = true;

            using (var service = new LeagueItemSetService(lcu, new PerformanceBudgetProvider(), new FakeOpggApi(), files))
            {
                var plan = service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
                Require(!result.Succeeded, "Gate 3 falsely reported success after the atomic commit failed.");
                Require(files.FileExists(oldFacm) && files.ReadAllText(oldFacm) == "old-facm",
                    "Gate 3 removed the previous FACM item set after a failed new commit.");
                Require(files.FileExists(userFile) && files.ReadAllText(userFile) == "user-owned",
                    "Gate 3 touched a user file while handling a failed commit.");
                Require(files.DeletedPaths.All(path => !string.Equals(path, oldFacm, StringComparison.OrdinalIgnoreCase)),
                    "Gate 3 cleanup ran even though the new item set never committed.");
            }
        }

        private static void ValidateCancellationBeforeCommit()
        {
            var lcu = new FakeLeagueApi { InstallDirectory = TencentInstall };
            var files = CreateTencentFileSystem();
            using (var service = new LeagueItemSetService(lcu, new PerformanceBudgetProvider(), new FakeOpggApi(), files))
            using (var cancellation = new CancellationTokenSource())
            {
                var plan = service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                cancellation.Cancel();
                var before = files.MutationCount;
                try
                {
                    service.ApplyAsync(plan, cancellation.Token).GetAwaiter().GetResult();
                    throw new InvalidOperationException("Gate 3 ignored caller cancellation before disk commit.");
                }
                catch (OperationCanceledException)
                {
                    // Expected.
                }
                Require(files.MutationCount == before, "Cancelled Gate 3 operation wrote to the filesystem.");
            }
        }

        private static LeagueBuildAdvisorSnapshot CreateSnapshot()
        {
            var recommendation = new LeagueBuildRecommendation();
            recommendation.Rows.Add(new LeagueBuildAdvisorRow { Category = "starter-items", Recommendation = "多兰之刃 · 生命药水" });
            recommendation.Rows.Add(new LeagueBuildAdvisorRow { Category = "boots", Recommendation = "狂战士胫甲" });
            recommendation.Rows.Add(new LeagueBuildAdvisorRow { Category = "core-items", Recommendation = "破败王者之刃 · 无尽之刃" });
            return new LeagueBuildAdvisorSnapshot
            {
                Connected = true,
                Phase = "ChampSelect",
                Activity = LeagueActivityLevel.ChampSelect,
                BudgetName = "champ-select",
                QueueId = 420,
                ChampionId = 157,
                ChampionName = "疾风剑豪",
                Mode = "ranked",
                Position = "mid",
                Source = "OP.GG Global",
                Version = "16.16",
                Status = "ready",
                Recommendation = recommendation
            };
        }

        private static FakeFileSystem CreateTencentFileSystem()
        {
            var files = new FakeFileSystem();
            files.AddDirectory(TencentInstall);
            files.AddDirectory(TencentGame);
            return files;
        }

        private static readonly string TencentInstall = @"C:\WeGameApps\英雄联盟\LeagueClient";
        private static readonly string TencentGame = @"C:\WeGameApps\英雄联盟\Game";
        private static readonly string TencentRecommended = Path.GetFullPath(Path.Combine(TencentGame, "Config", "Global", "Recommended"));
        private static readonly string StandardInstall = @"C:\Riot Games\League of Legends";
        private static readonly string StandardRecommended = Path.GetFullPath(Path.Combine(StandardInstall, "Config", "Global", "Recommended"));

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeOpggApi : IOpggBuildApi
        {
            public List<string> Paths { get; } = new List<string>();

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Paths.Add(path);
                return Bytes("{\"data\":{" +
                    "\"starter_items\":[{\"ids\":[1055,2003],\"pick_rate\":0.61},{\"ids\":[1054,2003],\"pick_rate\":0.18}]," +
                    "\"boots\":[{\"ids\":[3006]},{\"ids\":[3111]}]," +
                    "\"prism_items\":[{\"ids\":[2501]}]," +
                    "\"core_items\":[{\"ids\":[3042,3031],\"pick_rate\":0.37},{\"ids\":[3153,6673],\"pick_rate\":0.21}]," +
                    "\"last_items\":[{\"ids\":[3040,3121,2530,3026]}]}}");
            }
        }

        private sealed class FakeLeagueApi : ILeagueClientApi
        {
            public string Phase { get; set; } = "ChampSelect";
            public int ChampionId { get; set; } = 157;
            public int QueueId { get; set; } = 420;
            public string InstallDirectory { get; set; } = TencentInstall;
            public List<string> ReadPaths { get; } = new List<string>();

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadPaths.Add(path);
                if (path == LeagueDashboardPhaseService.PhasePath)
                    return Bytes("\"" + Phase + "\"");
                if (path == LeagueLiveDataService.ChampSelectSessionPath)
                {
                    return Bytes("{\"gameId\":123,\"queueId\":" + QueueId + ",\"localPlayerCellId\":1," +
                                 "\"myTeam\":[{\"cellId\":1,\"puuid\":\"local-puuid\",\"assignedPosition\":\"MIDDLE\"," +
                                 "\"championId\":" + ChampionId + ",\"championPickIntent\":" + ChampionId + "}]," +
                                 "\"theirTeam\":[],\"actions\":[]}");
                }
                if (path == LeagueLiveDataService.GameflowSessionPath)
                    return Bytes("{\"phase\":\"InProgress\",\"map\":{\"id\":11,\"gameMode\":\"CLASSIC\"}," +
                                 "\"gameData\":{\"gameId\":123,\"queue\":{\"id\":" + QueueId + ",\"gameMode\":\"CLASSIC\"}," +
                                 "\"teamOne\":[{\"puuid\":\"local-puuid\",\"championId\":" + ChampionId + "}],\"teamTwo\":[]}}");
                if (path == LeagueItemSetService.InstallDirPath)
                    return Bytes("\"" + (InstallDirectory ?? string.Empty).Replace("\\", "\\\\") + "\"");
                return Task.FromResult<byte[]>(null);
            }
        }

        private sealed class FakeFileSystem : ILeagueItemSetFileSystem
        {
            private readonly HashSet<string> _directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public int MutationCount { get; private set; }
            public bool FailNextMove { get; set; }
            public List<string> DeletedPaths { get; } = new List<string>();

            public void AddDirectory(string path)
            {
                _directories.Add(Normalize(path));
            }

            public void AddFile(string path, string content)
            {
                var normalized = Normalize(path);
                var directory = Path.GetDirectoryName(normalized);
                if (!string.IsNullOrWhiteSpace(directory)) AddDirectory(directory);
                _files[normalized] = content ?? string.Empty;
            }

            public bool DirectoryExists(string path)
            {
                return _directories.Contains(Normalize(path));
            }

            public bool FileExists(string path)
            {
                return _files.ContainsKey(Normalize(path));
            }

            public void CreateDirectory(string path)
            {
                MutationCount++;
                AddDirectory(path);
            }

            public void WriteAllText(string path, string content)
            {
                MutationCount++;
                AddFile(path, content);
            }

            public string ReadAllText(string path)
            {
                string value;
                return _files.TryGetValue(Normalize(path), out value) ? value : null;
            }

            public string[] GetFiles(string directory, string pattern)
            {
                var normalizedDirectory = Normalize(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var prefix = pattern != null && pattern.EndsWith("*.json", StringComparison.OrdinalIgnoreCase)
                    ? pattern.Substring(0, pattern.Length - "*.json".Length)
                    : string.Empty;
                return _files.Keys.Where(path =>
                {
                    var parent = Path.GetDirectoryName(path);
                    var name = Path.GetFileName(path);
                    return string.Equals(Normalize(parent), Normalize(directory), StringComparison.OrdinalIgnoreCase) &&
                           name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                           name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                }).ToArray();
            }

            public void MoveFile(string source, string destination)
            {
                MutationCount++;
                if (FailNextMove)
                {
                    FailNextMove = false;
                    throw new IOException("forced move failure");
                }
                var from = Normalize(source);
                var to = Normalize(destination);
                string value;
                if (!_files.TryGetValue(from, out value)) throw new FileNotFoundException("source not found", from);
                if (_files.ContainsKey(to)) throw new IOException("destination exists");
                _files.Remove(from);
                _files[to] = value;
            }

            public void ReplaceFile(string source, string destination, string backup)
            {
                MutationCount++;
                var from = Normalize(source);
                var to = Normalize(destination);
                var backupPath = Normalize(backup);
                string sourceValue;
                string oldValue;
                if (!_files.TryGetValue(from, out sourceValue)) throw new FileNotFoundException("source not found", from);
                if (!_files.TryGetValue(to, out oldValue)) throw new FileNotFoundException("destination not found", to);
                _files[backupPath] = oldValue;
                _files[to] = sourceValue;
                _files.Remove(from);
            }

            public void DeleteFile(string path)
            {
                MutationCount++;
                var normalized = Normalize(path);
                DeletedPaths.Add(normalized);
                _files.Remove(normalized);
            }

            private static string Normalize(string path)
            {
                return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
            }
        }

        private static byte[] Bytes(string value)
        {
            return Encoding.UTF8.GetBytes(value ?? string.Empty);
        }
    }
}
