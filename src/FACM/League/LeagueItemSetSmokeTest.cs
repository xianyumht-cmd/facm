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
        private static readonly string TencentInstall = @"C:\WeGameApps\英雄联盟\LeagueClient";
        private static readonly string TencentGame = @"C:\WeGameApps\英雄联盟\Game";
        private static readonly string TencentRecommended = Path.GetFullPath(Path.Combine(TencentGame, "Config", "Global", "Recommended"));
        private static readonly string StandardInstall = @"C:\Riot Games\League of Legends";
        private static readonly string StandardRecommended = Path.GetFullPath(Path.Combine(StandardInstall, "Config", "Global", "Recommended"));

        public static void Validate()
        {
            ValidatePreparationAndParsing();
            ValidateRecipeRestore();
            ValidateInstallLayouts();
            ValidateVerifiedCommitAndOwnership();
            ValidateContextDrift();
            ValidateFailedCommitKeepsOldFiles();
            ValidateCancellationBeforeCommit();
            Require(LeagueItemSetUiBridge.HasTrayAccessForSmokeTest(), "Gate 3 tray bridge cannot access the FACM tray contract.");
        }

        private static void ValidatePreparationAndParsing()
        {
            var lcu = new FakeLeagueApi();
            var opgg = new FakeOpggApi();
            var files = TencentFiles();
            using (var service = NewService(lcu, opgg, files))
            {
                var plan = service.PrepareAsync(Snapshot(), CancellationToken.None).GetAwaiter().GetResult();
                Require(plan != null && plan.HasItems, "Gate 3 did not parse current OP.GG item-set data.");
                Require(plan.Blocks.Count == 7 && plan.ItemCount == 15, "Gate 3 item groups/count changed unexpectedly.");
                Require(plan.Uid.StartsWith(LeagueItemSetService.FilePrefix, StringComparison.Ordinal), "Gate 3 UID escaped FACM ownership.");
                Require(opgg.Paths.Count == 1 && opgg.Paths[0].Contains("/ranked/157/mid"), "Gate 3 did not reuse accepted advisor context.");
                Require(files.MutationCount == 0, "Gate 3 Prepare touched the filesystem.");
            }
        }

        private static void ValidateRecipeRestore()
        {
            Require(LeagueItemSetService.RestoreRecipe(3042) == 3004, "Muramana recipe restore missing.");
            Require(LeagueItemSetService.RestoreRecipe(223040) == 223003, "Arena Seraph recipe restore missing.");
            Require(LeagueItemSetService.RestoreRecipe(323121) == 323119, "Prefixed Fimbulwinter recipe restore missing.");
            Require(LeagueItemSetService.RestoreRecipe(2530) == 2526, "Upgraded item recipe restore missing.");
            Require(LeagueItemSetService.RestoreRecipe(3031) == 3031, "Unmapped item was modified.");
        }

        private static void ValidateInstallLayouts()
        {
            string target;
            string layout;
            var tencent = TencentFiles();
            Require(LeagueItemSetService.TryResolveTargetDirectory(TencentInstall, tencent, out target, out layout), "Tencent layout not recognized.");
            Require(string.Equals(target, TencentRecommended, StringComparison.OrdinalIgnoreCase), "Tencent target is not Game/Config/Global/Recommended.");
            Require(layout == "tencent-sibling-game", "Tencent layout marker changed.");

            var standard = new FakeFileSystem();
            standard.AddDirectory(StandardInstall);
            Require(LeagueItemSetService.TryResolveTargetDirectory(StandardInstall, standard, out target, out layout), "Standard Riot layout not recognized.");
            Require(string.Equals(target, StandardRecommended, StringComparison.OrdinalIgnoreCase), "Standard target path changed.");

            var brokenTencent = new FakeFileSystem();
            brokenTencent.AddDirectory(TencentInstall);
            Require(!LeagueItemSetService.TryResolveTargetDirectory(TencentInstall, brokenTencent, out target, out layout), "Tencent layout guessed without sibling Game.");
            Require(!LeagueItemSetService.TryResolveTargetDirectory("relative\\LeagueClient", tencent, out target, out layout), "Relative install-dir was accepted.");
            Require(!LeagueItemSetService.TryResolveTargetDirectory(@"C:\DoesNotExist\LeagueClient", tencent, out target, out layout), "Missing install-dir was accepted.");
        }

        private static void ValidateVerifiedCommitAndOwnership()
        {
            var lcu = new FakeLeagueApi { InstallDirectory = TencentInstall };
            var files = TencentFiles();
            files.AddDirectory(TencentRecommended);
            var user = Path.Combine(TencentRecommended, "user.json");
            var thirdParty = Path.Combine(TencentRecommended, "third-party.json");
            var oldFacm = Path.Combine(TencentRecommended, "facm1-old.json");
            files.AddFile(user, "user-owned");
            files.AddFile(thirdParty, "third-party-owned");
            files.AddFile(oldFacm, "old-facm");

            using (var service = NewService(lcu, new FakeOpggApi(), files))
            {
                var plan = service.PrepareAsync(Snapshot(), CancellationToken.None).GetAwaiter().GetResult();
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Succeeded, "Verified Gate 3 write did not succeed.");
                Require(result.RemovedOldFiles == 1, "Gate 3 did not clean exactly one superseded FACM file.");
                Require(files.ReadAllText(user) == "user-owned" && files.ReadAllText(thirdParty) == "third-party-owned", "Gate 3 touched a non-FACM Recommended file.");
                Require(!files.FileExists(oldFacm), "Superseded FACM file was not cleaned after success.");

                var destination = Path.Combine(result.TargetDirectory, result.FileName);
                var json = files.ReadAllText(destination);
                Require(files.FileExists(destination) && service.VerifyItemSetJson(json, plan), "Committed item set failed read-back verification.");
                Require(json.Contains("\"id\":\"3004\""), "Recipe-restored item id is absent from output JSON.");
                Require(json.Contains("\"type\":\"global\""), "League global item-set shape is absent.");
                Require(files.DeletedPaths.All(IsOwnedDelete), "Gate 3 deleted a path outside FACM ownership.");
            }
        }

        private static void ValidateContextDrift()
        {
            var lcu = new FakeLeagueApi { InstallDirectory = TencentInstall };
            var files = TencentFiles();
            using (var service = NewService(lcu, new FakeOpggApi(), files))
            {
                var plan = service.PrepareAsync(Snapshot(), CancellationToken.None).GetAwaiter().GetResult();
                lcu.Phase = "InProgress";
                var before = files.MutationCount;
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Status == "blocked" && result.BlockReason == "champ-select-required", "Gate 3 did not block after leaving Champ Select.");
                Require(files.MutationCount == before, "Gate 3 wrote after phase drift.");
            }

            lcu = new FakeLeagueApi { InstallDirectory = TencentInstall };
            files = TencentFiles();
            using (var service = NewService(lcu, new FakeOpggApi(), files))
            {
                var plan = service.PrepareAsync(Snapshot(), CancellationToken.None).GetAwaiter().GetResult();
                lcu.ChampionId = 145;
                var before = files.MutationCount;
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Status == "blocked" && result.BlockReason == "champion-changed", "Gate 3 did not block stale champion data.");
                Require(files.MutationCount == before, "Gate 3 wrote after champion drift.");
            }
        }

        private static void ValidateFailedCommitKeepsOldFiles()
        {
            var lcu = new FakeLeagueApi { InstallDirectory = TencentInstall };
            var files = TencentFiles();
            files.AddDirectory(TencentRecommended);
            var oldFacm = Path.Combine(TencentRecommended, "facm1-old.json");
            var user = Path.Combine(TencentRecommended, "user.json");
            files.AddFile(oldFacm, "old-facm");
            files.AddFile(user, "user-owned");
            files.FailNextMove = true;

            using (var service = NewService(lcu, new FakeOpggApi(), files))
            {
                var plan = service.PrepareAsync(Snapshot(), CancellationToken.None).GetAwaiter().GetResult();
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
                Require(!result.Succeeded, "Gate 3 falsely reported success after forced commit failure.");
                Require(files.ReadAllText(oldFacm) == "old-facm", "Failed Gate 3 commit removed previous FACM item set.");
                Require(files.ReadAllText(user) == "user-owned", "Failed Gate 3 commit touched user file.");
                Require(!files.DeletedPaths.Any(path => string.Equals(path, oldFacm, StringComparison.OrdinalIgnoreCase)), "Cleanup ran after failed new commit.");
            }
        }

        private static void ValidateCancellationBeforeCommit()
        {
            var lcu = new FakeLeagueApi { InstallDirectory = TencentInstall };
            var files = TencentFiles();
            using (var service = NewService(lcu, new FakeOpggApi(), files))
            using (var cancellation = new CancellationTokenSource())
            {
                var plan = service.PrepareAsync(Snapshot(), CancellationToken.None).GetAwaiter().GetResult();
                cancellation.Cancel();
                var before = files.MutationCount;
                try
                {
                    service.ApplyAsync(plan, cancellation.Token).GetAwaiter().GetResult();
                    throw new InvalidOperationException("Gate 3 ignored caller cancellation.");
                }
                catch (OperationCanceledException) { }
                Require(files.MutationCount == before, "Cancelled Gate 3 operation mutated filesystem.");
            }
        }

        private static LeagueItemSetService NewService(FakeLeagueApi lcu, FakeOpggApi opgg, FakeFileSystem files)
        {
            return new LeagueItemSetService(lcu, new PerformanceBudgetProvider(), opgg, files);
        }

        private static LeagueBuildAdvisorSnapshot Snapshot()
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

        private static FakeFileSystem TencentFiles()
        {
            var files = new FakeFileSystem();
            files.AddDirectory(TencentInstall);
            files.AddDirectory(TencentGame);
            return files;
        }

        private static bool IsOwnedDelete(string path)
        {
            var name = Path.GetFileName(path) ?? string.Empty;
            return name.StartsWith("facm1-", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(".facm1-", StringComparison.OrdinalIgnoreCase);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeOpggApi : IOpggBuildApi
        {
            public readonly List<string> Paths = new List<string>();

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Paths.Add(path);
                return Task.FromResult(Bytes("{\"data\":{" +
                    "\"starter_items\":[{\"ids\":[1055,2003],\"pick_rate\":0.61},{\"ids\":[1054,2003],\"pick_rate\":0.18}]," +
                    "\"boots\":[{\"ids\":[3006]},{\"ids\":[3111]}]," +
                    "\"prism_items\":[{\"ids\":[2501]}]," +
                    "\"core_items\":[{\"ids\":[3042,3031],\"pick_rate\":0.37},{\"ids\":[3153,6673],\"pick_rate\":0.21}]," +
                    "\"last_items\":[{\"ids\":[3040,3121,2530,3026]}]}}"));
            }
        }

        private sealed class FakeLeagueApi : ILeagueClientApi
        {
            public string Phase = "ChampSelect";
            public int ChampionId = 157;
            public int QueueId = 420;
            public string InstallDirectory = TencentInstall;

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (path == LeagueDashboardPhaseService.PhasePath)
                    return Task.FromResult(Bytes("\"" + Phase + "\""));
                if (path == LeagueLiveDataService.ChampSelectSessionPath)
                    return Task.FromResult(Bytes("{\"gameId\":123,\"queueId\":" + QueueId + ",\"localPlayerCellId\":1," +
                        "\"myTeam\":[{\"cellId\":1,\"puuid\":\"local-puuid\",\"assignedPosition\":\"MIDDLE\",\"championId\":" + ChampionId + ",\"championPickIntent\":" + ChampionId + "}]," +
                        "\"theirTeam\":[],\"actions\":[]}"));
                if (path == LeagueLiveDataService.GameflowSessionPath)
                    return Task.FromResult(Bytes("{\"phase\":\"InProgress\",\"map\":{\"id\":11,\"gameMode\":\"CLASSIC\"}," +
                        "\"gameData\":{\"gameId\":123,\"queue\":{\"id\":" + QueueId + ",\"gameMode\":\"CLASSIC\"}," +
                        "\"teamOne\":[{\"puuid\":\"local-puuid\",\"championId\":" + ChampionId + "}],\"teamTwo\":[]}}"));
                if (path == LeagueItemSetService.InstallDirPath)
                    return Task.FromResult(Bytes("\"" + (InstallDirectory ?? string.Empty).Replace("\\", "\\\\") + "\""));
                return Task.FromResult<byte[]>(null);
            }
        }

        private sealed class FakeFileSystem : ILeagueItemSetFileSystem
        {
            private readonly HashSet<string> _directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public int MutationCount { get; private set; }
            public bool FailNextMove { get; set; }
            public readonly List<string> DeletedPaths = new List<string>();

            public void AddDirectory(string path) { _directories.Add(Normalize(path)); }
            public void AddFile(string path, string content)
            {
                var normalized = Normalize(path);
                var parent = Path.GetDirectoryName(normalized);
                if (!string.IsNullOrWhiteSpace(parent)) AddDirectory(parent);
                _files[normalized] = content ?? string.Empty;
            }
            public bool DirectoryExists(string path) { return _directories.Contains(Normalize(path)); }
            public bool FileExists(string path) { return _files.ContainsKey(Normalize(path)); }
            public void CreateDirectory(string path) { MutationCount++; AddDirectory(path); }
            public void WriteAllText(string path, string content) { MutationCount++; AddFile(path, content); }
            public string ReadAllText(string path)
            {
                string value;
                return _files.TryGetValue(Normalize(path), out value) ? value : null;
            }
            public string[] GetFiles(string directory, string pattern)
            {
                var prefix = pattern != null && pattern.EndsWith("*.json", StringComparison.OrdinalIgnoreCase)
                    ? pattern.Substring(0, pattern.Length - "*.json".Length)
                    : string.Empty;
                return _files.Keys.Where(path =>
                    string.Equals(Normalize(Path.GetDirectoryName(path)), Normalize(directory), StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(path).EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToArray();
            }
            public void MoveFile(string source, string destination)
            {
                MutationCount++;
                if (FailNextMove) { FailNextMove = false; throw new IOException("forced move failure"); }
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
                var bak = Normalize(backup);
                string sourceValue;
                string oldValue;
                if (!_files.TryGetValue(from, out sourceValue)) throw new FileNotFoundException("source not found", from);
                if (!_files.TryGetValue(to, out oldValue)) throw new FileNotFoundException("destination not found", to);
                _files[bak] = oldValue;
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
