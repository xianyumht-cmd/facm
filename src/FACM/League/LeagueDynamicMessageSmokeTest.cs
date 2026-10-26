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
    internal static class LeagueDynamicMessageSmokeTest
    {
        internal static void Validate()
        {
            ValidatePlayerTags();
            LeagueEncounterHistorySmokeTest.Validate();
            ValidateAggregation();
            ValidatePremadeInference();
            ValidateJungleTimeline();
            ValidateJungleFormatter();
            ValidateJunglePrefetch();
            ValidateLoadingAsync().GetAwaiter().GetResult();
            ValidateMissingHistoryAsync().GetAwaiter().GetResult();
            ValidateJungleLoadingAsync().GetAwaiter().GetResult();
            ValidateBatchAsync().GetAwaiter().GetResult();
        }

        private static void ValidatePlayerTags()
        {
            var directory = Path.Combine(Path.GetTempPath(), "ggman-player-tags-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new LeaguePlayerTagStore(directory);
                Require(store.ReadTags("self", "HN1", "HN1").Count == 0,
                    "Empty local player tag storage must not create phantom records.");
                store.SetTag("self", "ally", "HN1", "HN1", "常用队友");
                Require(store.ReadTags("self", "hn1", "hn1")["ally"] == "常用队友" &&
                    store.ReadTags("other", "HN1", "HN1").Count == 0 &&
                    store.ReadTags("self", "HN2", "HN1").Count == 0 &&
                    store.ReadTags("self", "HN1", "HN2").Count == 0,
                    "Player tags must be scoped to own account, region and platform.");
                store.SetTag("self", "ally", "HN1", "HN1", "记住这位");
                var reloaded = new LeaguePlayerTagStore(directory);
                Require(reloaded.ReadTags("self", "HN1", "HN1")["ally"] == "记住这位",
                    "Player tag edits must survive a process restart.");
                store.SetTag("self", "ally", "HN1", "HN1", "");
                Require(reloaded.ReadTags("self", "HN1", "HN1").Count == 0,
                    "Clearing a player tag must remove the scoped record.");
                bool rejected = false;
                try { store.SetTag("self", "self", "HN1", "HN1", "self-tag"); }
                catch (ArgumentException) { rejected = true; }
                Require(rejected, "Self-player tags must be rejected.");
                rejected = false;
                try { store.SetTag("self", "ally", "HN1", "HN1", "invalid\u0001tag"); }
                catch (ArgumentException) { rejected = true; }
                Require(rejected, "Control characters must not enter player tags.");
                store.SetTag("self", "ally", "HN1", "HN1", "unchanged");
                var exchange = Path.Combine(directory, "akari-tags.json");
                Require(store.ExportAkariJson(exchange) == 1,
                    "Player tag export must include exactly the locally stored records.");
                var exported = File.ReadAllText(exchange, Encoding.UTF8);
                Require(exported.Contains("\"type\":\"league-akari-tagged-players\"") &&
                    exported.Contains("\"databaseVersion\":15") && exported.Contains("\"rsoPlatformId\":\"HN1\""),
                    "Player tag export must match Akari 1.5.1's interchange schema.");
                store.SetTag("self", "ally", "HN1", "HN1", "modified");
                Require(store.ImportAkariJson(exchange) == 1 &&
                    store.ReadTags("self", "HN1", "HN1")["ally"] == "unchanged",
                    "Import must merge scoped tags and preserve exported values.");
                store.SetTag("other", "enemy", "HN2", "HN2", "不同账号的标记");
                var browserRecords = reloaded.ReadAllTags();
                Require(browserRecords.Count == 2 && browserRecords.Any(p => p.SelfPuuid == "other" &&
                    p.Puuid == "enemy" && p.Region == "HN2") &&
                    browserRecords.Any(p => p.SelfPuuid == "self" && p.Puuid == "ally" && p.Tag == "unchanged"),
                    "The local tag browser must enumerate every stored account without a live lobby.");
                var expected = browserRecords.Single(p => p.SelfPuuid == "self");
                Require(reloaded.TryEditStoredTag(expected, "重新标记") &&
                    store.ReadTags("self", "HN1", "HN1")["ally"] == "重新标记" &&
                    !reloaded.TryEditStoredTag(expected, "过期覆盖"),
                    "Browser edits must preserve scope and reject stale records.");
                var fresh = reloaded.ReadAllTags().Single(p => p.SelfPuuid == "self");
                Require(reloaded.TryEditStoredTag(fresh, "") &&
                    store.ReadTags("self", "HN1", "HN1").Count == 0 &&
                    reloaded.ReadAllTags().Count == 1,
                    "Browser deletion must remove only the selected scoped tag.");
                var invalidExchange = Path.Combine(directory, "invalid-tags.json");
                File.WriteAllText(invalidExchange, exported.Replace("league-akari-tagged-players", "different-schema"), Encoding.UTF8);
                rejected = false;
                try { store.ImportAkariJson(invalidExchange); }
                catch (InvalidDataException) { rejected = true; }
                Require(rejected && store.ReadTags("other", "HN2", "HN2")["enemy"] == "不同账号的标记",
                    "A foreign player tag file must not mutate valid local tags.");
                var path = Path.Combine(directory, "league-player-tags.json");
                File.WriteAllText(path, "{broken json", Encoding.UTF8);
                rejected = false;
                try { store.SetTag("self", "ally", "HN1", "HN1", "overwritten"); }
                catch (InvalidDataException) { rejected = true; }
                Require(rejected && File.ReadAllText(path, Encoding.UTF8).Contains("broken json"),
                    "Invalid player tag storage must fail without overwriting the damaged file.");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        private static void ValidateAggregation()
        {
            var options = new LeagueDynamicMessageOptions { NameStrategy = "preferName", CsPerMinute = true,
                ChampionDamageShare = true, GoldShare = true, VisionScore = true, KillParticipation = true,
                DamageGoldEfficiency = true };
            Require(LeagueDynamicMessageOptions.Parse(LeagueDynamicMessageOptions.Save(options)).GoldShare &&
                LeagueDynamicMessageOptions.Parse("{\"Target\":\"bad\"}").Target == "all", "Dynamic option validation failed.");
            var service = new LeaguePlayerDataService(new Fake(), new PerformanceBudgetProvider());
            var page = service.ParseMatchPage(Bytes(History()), new LeaguePlayerProfile { PuuId = "self", SummonerId = 1 }, 0, 20);
            Require(page.Matches.Count == 2 && page.Matches[0].StatisticsAvailable &&
                page.Matches[0].ChampionDamageShare == 0.3 && page.Matches[0].GoldShare == 0.5 &&
                page.Matches[0].KillParticipation == 0.8 && page.Matches[0].DamageGoldEfficiency == 0.03,
                "Participant/team metric parsing diverged from the pinned formulas.");
            Require(page.Matches[0].SameTeamPuuids != null &&
                page.Matches[0].SameTeamPuuids.SequenceEqual(new[] { "enemy", "self" }),
                "Historical same-team identities were not derived from participant team IDs.");
            Require(page.Matches[0].MapId == 11 && page.Matches[0].TeamId == 100 &&
                page.Matches[0].ParticipantId == 1 && page.Matches[0].HasSmite,
                "Jungle timeline identity and Rift/Smite metadata were not retained.");
            var snapshot = new LeagueDynamicMessageSnapshot();
            snapshot.Players.Add(new LeagueDynamicMessagePlayer { Friendly = true, History = page,
                Player = new LeagueLivePlayerRow { PuuId = "self", GameName = "Self", ChampionId = 77 }, ChampionName = "Alpha" });
            snapshot.Players.Add(new LeagueDynamicMessagePlayer { Friendly = false,
                Player = new LeagueLivePlayerRow { PuuId = "enemy", GameName = "Enemy", ChampionId = 77 }, ChampionName = "Alpha" });
            var lines = LeagueDynamicMessageFormatter.Build(snapshot, options);
            Require(lines.Count == 2 && lines[0].Contains("KDA 9.00") && lines[0].Contains("50%") &&
                lines[0].Contains("6.0") && lines[0].Contains("40%") && lines[0].Contains("15.0"),
                "Rating lines must aggregate total KDA and mean per-game shares correctly.");
            Require(lines[1].Contains(LeagueEfficiencyText.Get(null, LeagueEfficiencyUiTextKeys.DynamicUnavailable)) &&
                !lines[1].Contains("0%"), "Unavailable history was represented as zero statistics.");
            options.Target = "friendly"; options.ShowCurrentChampion = true;
            Require(LeagueDynamicMessageFormatter.Build(snapshot, options).Single().Contains("KDA 4.00"),
                "Current-champion selection did not filter the sample.");
            options.Target = "all"; options.ShowCurrentChampion = false; options.NameStrategy = "preferChampionName";
            var duplicates = LeagueDynamicMessageFormatter.Build(snapshot, options);
            Require(duplicates[0].StartsWith("Self", StringComparison.Ordinal) && duplicates[1].StartsWith("Enemy", StringComparison.Ordinal),
                "Duplicate champion names must fall back to player identity.");
            Require(LeagueDynamicMessageFormatter.Build(snapshot, options, new HashSet<string>()).Count == 0,
                "Empty player selection unexpectedly generated outgoing lines.");
            snapshot.Players[0].Player.GameName = "/command\nextra";
            options.NameStrategy = "preferName";
            Require(LeagueDynamicMessageFormatter.Build(snapshot, options).All(l => !l.StartsWith("/", StringComparison.Ordinal) &&
                !l.Any(char.IsControl) && l.Length <= LeagueQuickMessagePresets.MaximumMessageLength), "Names injected a command, control or oversized line.");
        }

        private static void ValidatePremadeInference()
        {
            var snapshot = new LeagueDynamicMessageSnapshot();
            var self = new LeagueDynamicMessagePlayer { Friendly = true,
                Player = new LeagueLivePlayerRow { PuuId = "self" }, History = new LeaguePlayerMatchPage() };
            var ally = new LeagueDynamicMessagePlayer { Friendly = true,
                Player = new LeagueLivePlayerRow { PuuId = "ally" }, History = new LeaguePlayerMatchPage() };
            var enemy = new LeagueDynamicMessagePlayer { Friendly = false,
                Player = new LeagueLivePlayerRow { PuuId = "enemy" }, History = new LeaguePlayerMatchPage() };
            snapshot.Players.Add(self); snapshot.Players.Add(ally); snapshot.Players.Add(enemy);
            for (var game = 1; game <= 5; game++)
            {
                self.History.Matches.Add(new LeaguePlayerMatchSummary { GameId = game,
                    SameTeamPuuids = new[] { "self", "ally", "enemy" } });
                ally.History.Matches.Add(new LeaguePlayerMatchSummary { GameId = game,
                    SameTeamPuuids = new[] { "self", "ally" } });
            }
            self.History.Matches.Add(new LeaguePlayerMatchSummary { GameId = 1,
                SameTeamPuuids = new[] { "self", "ally" } });
            var groups = LeaguePremadeInference.Infer(snapshot);
            Require(groups.Count == 1 && groups[0].SequenceEqual(new[] { "ally", "self" }),
                "Premade inference must count distinct same-team games and never mix current opposing sides.");
            self.Player.GameName = "Self"; ally.Player.GameName = "Ally";
            var options = new LeagueDynamicMessageOptions { Target = "friendly", PremadeNameStrategy = "preferName" };
            var lines = LeaguePremadeMessageFormatter.Build(snapshot, options);
            Require(lines.Count == 1 && lines[0].Contains("Ally") && lines[0].Contains("Self") &&
                !lines[0].Contains("Enemy"), "Premade preview must describe only the chosen current side.");
            Require(LeaguePremadeMessageFormatter.Build(snapshot, options,
                new HashSet<string>(StringComparer.Ordinal)).Count == 0,
                "An empty premade selection must not dispatch a message.");
            Require(LeaguePremadeMessageFormatter.Build(snapshot, options,
                new HashSet<string> { "not-a-current-group" }).Count == 0,
                "Arbitrary group IDs must not enter an outgoing message.");
            options.Target = "enemy";
            Require(LeaguePremadeMessageFormatter.Build(snapshot, options).Count == 0,
                "No-evidence opposing groups must not be represented as confirmed negatives.");
            options.Target = "all";
            self.Player.GameName = "/cmd\n" + new string('x', 300);
            Require(LeaguePremadeMessageFormatter.Build(snapshot, options).All(line =>
                line.Length <= LeagueQuickMessagePresets.MaximumMessageLength && !line.Any(char.IsControl) &&
                !line.StartsWith("/", StringComparison.Ordinal)),
                "Premade output must be bounded and reject command/control character injection.");
            var large = new LeagueDynamicMessageSnapshot();
            var ids = Enumerable.Range(0, 20).Select(index => "member" + index).ToArray();
            foreach (var id in ids)
                large.Players.Add(new LeagueDynamicMessagePlayer { Friendly = true,
                    Player = new LeagueLivePlayerRow { PuuId = id, GameName = new string('M', 80) },
                    History = new LeaguePlayerMatchPage() });
            for (var game = 1; game <= 5; game++)
                large.Players[0].History.Matches.Add(new LeaguePlayerMatchSummary { GameId = game,
                    SameTeamPuuids = ids });
            var largeLines = LeaguePremadeMessageFormatter.Build(large, options);
            Require(largeLines.Count == 1 && largeLines[0].Length <= LeagueQuickMessagePresets.MaximumMessageLength &&
                largeLines[0].Contains("["), "Large inferred groups must fit in chat without silently disappearing.");
            var saved = LeagueDynamicMessageOptions.Save(options);
            Require(LeagueDynamicMessageOptions.Parse(saved).PremadeNameStrategy == "preferName" &&
                LeagueDynamicMessageOptions.Parse("{\"Target\":\"all\"}").PremadeNameStrategy == "preferName",
                "Premade name strategy must persist and remain backward-compatible with old settings.");
            Require(LeaguePremadeInference.Infer(snapshot, 6).Count == 0,
                "Duplicate source histories counted one historical game more than once.");
            ally.Friendly = false;
            Require(LeaguePremadeInference.Infer(snapshot).Count == 0,
                "An opposing current-side player was incorrectly inferred as a premade member.");
            ally.Friendly = true;
            foreach (var match in self.History.Matches) match.SameTeamPuuids = new[] { "ally" };
            ally.History.Matches.Clear();
            Require(LeaguePremadeInference.Infer(snapshot).Count == 0,
                "History without a verified source-player identity produced false premade evidence.");
        }

        private static void ValidateJungleTimeline()
        {
            var timeline = "{\"frames\":[" +
                "{\"events\":[{\"type\":\"ELITE_MONSTER_KILL\",\"timestamp\":300000,\"killerId\":1,\"killerTeamId\":100,\"monsterType\":\"DRAGON\"}]}," +
                "{\"participantFrames\":{\"1\":{\"position\":{\"x\":3830,\"y\":7880}}}}," +
                "{\"participantFrames\":{\"1\":{\"position\":{\"x\":3500,\"y\":10000}}}}," +
                "{\"participantFrames\":{\"1\":{\"position\":{\"x\":3600,\"y\":11000},\"level\":3,\"minionsKilled\":2,\"jungleMinionsKilled\":13}}," +
                "\"events\":[{\"type\":\"CHAMPION_KILL\",\"timestamp\":170000,\"killerId\":1,\"position\":{\"x\":3600,\"y\":11000}}]}," +
                "{\"participantFrames\":{\"1\":{\"position\":{\"x\":8000,\"y\":8000}}}," +
                "\"events\":[{\"type\":\"CHAMPION_KILL\",\"timestamp\":210000,\"killerId\":3,\"assistingParticipantIds\":[1],\"position\":{\"x\":8000,\"y\":8000}}]}]}";
            var sample = LeagueJungleTimelineAnalysis.Parse(Bytes(timeline), 7, 64, 1, 100, 11, true);
            Require(sample != null && sample.FirstCamp == "blue" && sample.FirstCampSide == "blue" &&
                sample.TopGanks == 1 && sample.MidGanks == 1 && sample.TopWeight > sample.MidWeight &&
                sample.Level3Gank && sample.Level4Gank && sample.FirstDragonWon == true &&
                sample.Dragons == 1 && sample.SoloDragons == 1 && sample.FirstDragonSeconds == 300d,
                "Pinned Akari jungle timeline calculations diverged on camp, lanes, early ganks or objectives.");
            Require(LeagueJungleTimelineAnalysis.Parse(Bytes(timeline), 8, 64, 1, 100, 12, true) == null &&
                LeagueJungleTimelineAnalysis.Parse(Bytes(timeline), 8, 64, 1, 100, 11, false) == null,
                "Non-Rift or non-jungle matches must not produce jungle analysis.");
            var aggregate = LeagueJungleTimelineAnalysis.Aggregate(new[] { sample, sample });
            Require(aggregate != null && aggregate.Games == 1 && aggregate.Level3GankRate == 1d &&
                aggregate.AverageDragons == 1d && aggregate.FirstDragonRate == 1d &&
                aggregate.FirstCamps["blue/normal/blue"] == 1,
                "Jungle aggregation must deduplicate games and retain source-rate denominators.");
            Require(LeagueJungleTimelineAnalysis.Parse(Bytes("{"), 9, 64, 1, 100, 11, true) == null,
                "Malformed timeline unexpectedly produced a game result.");
        }

        private static void ValidateJungleFormatter()
        {
            var player = new LeagueDynamicMessagePlayer { Friendly = true,
                Player = new LeagueLivePlayerRow { PuuId = "self", GameName = "Self", ChampionId = 77 },
                ChampionName = "Alpha", History = new LeaguePlayerMatchPage(),
                JungleSamples = new[]
                {
                    new LeagueJungleTimelineSample { GameId = 1, ChampionId = 77, TeamId = 100,
                        TopWeight = 6, MidWeight = 2, TotalWeight = 8, FirstCamp = "blue", FirstCampSide = "blue",
                        Level3Gank = true, FirstDragonWon = true, Dragons = 2, Voidgrubs = 3 },
                    new LeagueJungleTimelineSample { GameId = 2, ChampionId = 77, TeamId = 100,
                        TopWeight = 2, BotWeight = 6, TotalWeight = 8, FirstCamp = "red", FirstCampSide = "red",
                        Level4Gank = true, FirstDragonWon = false, Dragons = 1, Heralds = 1 }
                }
            };
            player.History.Matches.Add(new LeaguePlayerMatchSummary { GameId = 1, ChampionId = 77, ChampionName = "Alpha" });
            player.History.Matches.Add(new LeaguePlayerMatchSummary { GameId = 2, ChampionId = 77, ChampionName = "Alpha" });
            var snapshot = new LeagueDynamicMessageSnapshot();
            snapshot.Players.Add(player);
            var options = new LeagueDynamicMessageOptions();
            var selected = new HashSet<string> { "self" };
            var lines = LeagueJungleMessageFormatter.Build(snapshot, options, selected);
            Require(lines.Count > 0 && lines[0].StartsWith("Alpha：打野样本2场", StringComparison.Ordinal) &&
                string.Join(" ", lines).Contains("蓝方常规开野50%") &&
                string.Join(" ", lines).Contains("首龙控制50%") &&
                string.Join(" ", lines).Contains("三级抓人50%"),
                "The current-champion jungle preset omitted a source-derived metric or first-clear distribution.");
            options.JungleShowCurrentChampion = false;
            lines = LeagueJungleMessageFormatter.Build(snapshot, options, selected);
            Require(string.Join(" ", lines).Contains("常用打野Alpha") &&
                !string.Join(" ", lines).Contains("蓝方常规开野"),
                "Global jungle formatting must show main champions instead of current-champion first clears.");
            options.Target = "enemy";
            Require(LeagueJungleMessageFormatter.Build(snapshot, options, selected).Count == 0,
                "Jungle target filtering included an opposing-side player.");
            options.Target = "all";
            player.JungleSamples = null;
            Require(LeagueJungleMessageFormatter.Build(snapshot, options, selected).Count == 0,
                "Unavailable timelines were misrepresented as confirmed zero jungle games.");
            player.JungleSamples = new[] { new LeagueJungleTimelineSample { GameId = 3, ChampionId = 99, TeamId = 200 } };
            options.JungleShowCurrentChampion = true;
            Require(LeagueJungleMessageFormatter.Build(snapshot, options, selected).Single().Contains("暂无打野记录"),
                "Current champion filtering unexpectedly reused another champion's jungle games.");
            var parsed = LeagueDynamicMessageOptions.Parse(LeagueDynamicMessageOptions.Save(options));
            Require(parsed.JungleShowCurrentChampion && parsed.JungleFirstClearDistribution &&
                parsed.JungleNameStrategy == "preferChampionName" &&
                LeagueDynamicMessageOptions.Parse("{\"Target\":\"all\"}").JungleShowCurrentChampion,
                "Jungle preferences did not roundtrip or default like pinned Akari 1.5.1.");
            options.FriendlyShortcut = "Ctrl+F5";
            options.JungleFriendlyShortcut = "Ctrl+F6";
            options.JungleEnemyShortcut = "Ctrl+F7";
            options.JungleAllShortcut = "Ctrl+F8";
            options.PremadeFriendlyShortcut = "Ctrl+F9";
            options.PremadeEnemyShortcut = "Ctrl+F10";
            options.PremadeAllShortcut = "Ctrl+F11";
            var shortcuts = LeagueDynamicMessageOptions.Parse(LeagueDynamicMessageOptions.Save(options));
            Require(shortcuts.JungleAllShortcut == "Ctrl+F8" && shortcuts.PremadeEnemyShortcut == "Ctrl+F10" &&
                shortcuts.FriendlyShortcut == "Ctrl+F5", "Independent preset shortcut groups were not persisted.");
            options.PremadeAllShortcut = options.JungleAllShortcut;
            var rejected = false;
            try { LeagueDynamicMessageOptions.Save(options); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "A repeated binding across preset groups must be rejected.");
            Require(LeagueDynamicMessageOptions.Parse("{\"Target\":\"all\"}").JungleAllShortcut == "" &&
                LeagueDynamicMessageOptions.Parse("{\"Target\":\"all\"}").PremadeFriendlyShortcut == "",
                "Legacy options must leave the new shortcut groups disabled.");
            var targetSnapshot = new LeagueDynamicMessageSnapshot();
            targetSnapshot.Players.Add(new LeagueDynamicMessagePlayer { Friendly = true,
                Player = new LeagueLivePlayerRow { PuuId = "ally" } });
            targetSnapshot.Players.Add(new LeagueDynamicMessagePlayer { Friendly = false,
                Player = new LeagueLivePlayerRow { PuuId = "enemy" } });
            Require(LeagueDynamicMessageService.TargetPlayers(targetSnapshot, "friendly").SetEquals(new[] { "ally" }) &&
                LeagueDynamicMessageService.TargetPlayers(targetSnapshot, "enemy").SetEquals(new[] { "enemy" }) &&
                LeagueDynamicMessageService.TargetPlayers(targetSnapshot, "all").Count == 2 &&
                LeagueDynamicMessageService.TargetPlayers(targetSnapshot, "invalid").Count == 0,
                "Shortcut target selection must respect the current roster and side.");
        }

        private static void ValidateJunglePrefetch()
        {
            var options = new LeagueDynamicMessageOptions();
            Require(!LeagueDynamicMessageService.JunglePrefetchEnabled(options),
                "Default-disabled jungle shortcuts must not schedule timeline background reads.");
            options.JungleEnemyShortcut = "Ctrl+F8";
            Require(LeagueDynamicMessageService.JunglePrefetchEnabled(options),
                "An enabled jungle target shortcut must opt into timeline prefetch.");
            var snapshot = new LeagueDynamicMessageSnapshot();
            snapshot.Players.Add(new LeagueDynamicMessagePlayer { Friendly = true,
                Player = new LeagueLivePlayerRow { PuuId = "allytop", Position = "TOP" }, History = new LeaguePlayerMatchPage() });
            snapshot.Players.Add(new LeagueDynamicMessagePlayer { Friendly = false,
                Player = new LeagueLivePlayerRow { PuuId = "enemyjungle", Role = "JUNGLE" }, History = new LeaguePlayerMatchPage() });
            snapshot.Players.Add(new LeagueDynamicMessagePlayer { Friendly = true,
                Player = new LeagueLivePlayerRow { PuuId = "allyjungle", Position = "jungle" }, History = new LeaguePlayerMatchPage() });
            snapshot.Players.Add(new LeagueDynamicMessagePlayer { Friendly = false,
                Player = new LeagueLivePlayerRow { PuuId = "unavailable", Position = "JUNGLE" } });
            Require(LeagueDynamicMessageService.JunglePrefetchOrder(snapshot).Select(p => p.Player.PuuId)
                .SequenceEqual(new[] { "allyjungle", "enemyjungle", "unavailable", "allytop" }),
                "Known jungle positions must be prioritized while still including missing-history players for recovery.");
            Require(LeagueDynamicMessageService.TargetPlayers(snapshot, "enemy").SetEquals(new[] { "enemyjungle", "unavailable" }),
                "Enemy-only prefetch must not select friendly player identities.");
        }

        private static async Task ValidateLoadingAsync()
        {
            var fake = new Fake();
            var session = Session("first");
            var phase = new LeagueDashboardPhaseState { Connected = true, Phase = "ChampSelect" };
            using (var service = new LeagueDynamicMessageService(fake, new PerformanceBudgetProvider(), () => session, () => phase))
            {
                Require(fake.Paths.Count == 0, "Dynamic messages started background network work.");
                var snapshot = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                Require(snapshot != null && snapshot.Players.Count == 2 && snapshot.Players.All(p => p.History != null) &&
                    snapshot.Players[0].Friendly && !snapshot.Players[1].Friendly && fake.MaxConcurrent == 1,
                    "Both-team loading must use bounded sequential shared LCU reads.");
                snapshot.CreatedUtc = DateTime.UtcNow.AddMinutes(-3);
                var reads = fake.Paths.Count;
                Require(!await service.IsCurrentAsync(snapshot, CancellationToken.None).ConfigureAwait(false) && fake.Paths.Count == reads,
                    "Expired previews must be rejected before additional requests.");
                fake.OnRead = path => { if (path.Contains("/matches?")) session = Session("replacement"); };
                Require(await service.LoadAsync(CancellationToken.None).ConfigureAwait(false) == null,
                    "Account/session replacement retained an old analysis snapshot.");
                fake.OnRead = null; session = Session("first"); fake.RosterReads = 0; fake.ChangeRoster = true;
                Require(await service.LoadAsync(CancellationToken.None).ConfigureAwait(false) == null,
                    "Changed champion roster retained a stale preview.");
                fake.ChangeRoster = false;
                using (var cancellation = new CancellationTokenSource())
                {
                    fake.OnRead = path => cancellation.Cancel();
                    var cancelled = false;
                    try { await service.LoadAsync(cancellation.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { cancelled = true; }
                    Require(cancelled, "Closing/replacing generation did not cancel shared LCU requests.");
                }
                fake.OnRead = null; phase.Phase = "Lobby"; reads = fake.Paths.Count;
                Require(await service.LoadAsync(CancellationToken.None).ConfigureAwait(false) == null && fake.Paths.Count == reads,
                    "Unavailable game analysis must not poll from the Lobby.");
            }
        }

        private static async Task ValidateJungleLoadingAsync()
        {
            var fake = new Fake();
            var session = Session("jungle");
            var phase = new LeagueDashboardPhaseState { Connected = true, Phase = "ChampSelect" };
            using (var service = new LeagueDynamicMessageService(fake, new PerformanceBudgetProvider(), () => session, () => phase))
            {
                var snapshot = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                Require(snapshot != null && snapshot.Players[0].JungleSamples == null &&
                    !fake.Paths.Any(p => p.Contains("game-timelines")),
                    "Jungle timeline requests ran without explicit selection.");
                Require(!await service.LoadJungleAsync(snapshot, "missing", CancellationToken.None).ConfigureAwait(false),
                    "A non-roster jungle identity was accepted.");
                Require(await service.LoadJungleAsync(snapshot, "self", CancellationToken.None).ConfigureAwait(false),
                    "Explicit jungle timeline load rejected current roster and session.");
                var samples = snapshot.Players[0].JungleSamples;
                Require(samples != null && samples.Count == 2 && samples[0].GameId == 1 &&
                    fake.Paths.Count(p => p.Contains("game-timelines")) == 2 && fake.MaxConcurrent == 1,
                    "On-demand jungle timelines must use one bounded authenticated client request at a time.");
                Require(await service.LoadJungleAsync(snapshot, "self", CancellationToken.None).ConfigureAwait(false) &&
                    fake.Paths.Count(p => p.Contains("game-timelines")) == 2,
                    "Already loaded jungle samples were downloaded again.");
                var refreshed = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                Require(refreshed != null && refreshed.Players[0].JungleSamples != null &&
                    refreshed.Players[0].JungleSamples.Count == 2 &&
                    fake.Paths.Count(p => p.Contains("game-timelines")) == 2,
                    "Fresh previews must reuse matching current-account jungle timelines.");
                phase.Phase = "InProgress";
                service.Observe(phase);
                var inGame = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                Require(inGame != null && inGame.Players[0].JungleSamples != null &&
                    inGame.Players[0].JungleSamples.Count == 2 &&
                    await service.LoadJungleAsync(inGame, "self", CancellationToken.None).ConfigureAwait(false) &&
                    fake.Paths.Count(p => p.Contains("game-timelines")) == 2,
                    "ChampSelect to InProgress must preserve reusable player history without repeat timeline GETs.");
                fake.ChangeHistory = true;
                var changedHistory = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                Require(changedHistory != null && changedHistory.Players[0].JungleSamples == null,
                    "Changed match history reused stale cached jungle data.");
                fake.ChangeHistory = false;
                var recached = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                Require(recached != null && await service.LoadJungleAsync(recached, "self", CancellationToken.None).ConfigureAwait(false),
                    "Jungle samples could not be refreshed after a history change.");
                fake.ProfilePuuid = "enemy";
                var switchedAccount = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                Require(switchedAccount != null && switchedAccount.SelfPuuid == "enemy" &&
                    switchedAccount.Players[0].JungleSamples == null,
                    "Signed-in summoner change did not clear the timeline cache.");
                fake.ProfilePuuid = "self";
                var switchedBack = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                Require(switchedBack != null && switchedBack.Players[0].JungleSamples == null,
                    "Returning to a previous account resurrected an old cache scope.");
                phase.Phase = "ChampSelect";
                service.Observe(phase);
                snapshot.CreatedUtc = DateTime.UtcNow.AddMinutes(-3);
                Require(!await service.LoadJungleAsync(snapshot, "self", CancellationToken.None).ConfigureAwait(false) &&
                    fake.Paths.Count(p => p.Contains("game-timelines")) == 4, "Expired jungle snapshots made new timeline requests.");
                snapshot.CreatedUtc = DateTime.UtcNow;
                session = Session("different");
                Require(!await service.LoadJungleAsync(snapshot, "self", CancellationToken.None).ConfigureAwait(false),
                    "Account change reused previous jungle samples.");
                var replacement = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                Require(replacement != null && replacement.Players[0].JungleSamples == null,
                    "LCU session replacement did not invalidate previously loaded jungle samples.");
            }
            var incomplete = new Fake { IncompleteHistory = true };
            session = Session("detail");
            using (var service = new LeagueDynamicMessageService(incomplete, new PerformanceBudgetProvider(), () => session, () => phase))
            {
                var snapshot = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                Require(snapshot != null && snapshot.Players[0].History.Matches[0].ParticipantId == 0,
                    "Fixture did not preserve an unresolved match summary.");
                Require(await service.LoadJungleAsync(snapshot, "self", CancellationToken.None).ConfigureAwait(false) &&
                    snapshot.Players[0].JungleSamples.Count == 1 &&
                    incomplete.Paths.Contains("/lol-match-history/v1/games/1"),
                    "On-demand jungle analysis did not resolve missing participant identity from game detail.");
            }
        }

        private static async Task ValidateMissingHistoryAsync()
        {
            var fake = new Fake();
            var session = Session("history-recovery");
            var phase = new LeagueDashboardPhaseState { Connected = true, Phase = "ChampSelect" };
            using (var service = new LeagueDynamicMessageService(fake, new PerformanceBudgetProvider(), () => session, () => phase))
            {
                var snapshot = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                Require(snapshot != null && snapshot.Players.All(p => p.History != null),
                    "The history-recovery fixture did not initialize its roster.");
                snapshot.Players[0].History = null;
                Require(LeagueDynamicMessageService.TargetNeedsHistory(snapshot, "friendly") &&
                    !LeagueDynamicMessageService.TargetNeedsHistory(snapshot, "enemy") &&
                    LeagueDynamicMessageService.SelectedNeedsHistory(snapshot, new HashSet<string> { "self" }) &&
                    !LeagueDynamicMessageService.SelectedNeedsHistory(snapshot, new HashSet<string> { "enemy" }),
                    "Missing-history send guards must distinguish selected players and target sides.");
                var before = fake.Paths.Count(p => p.Contains("/self/matches?"));
                Require(await service.EnsureHistoriesAsync(snapshot, new HashSet<string> { "self" }, CancellationToken.None)
                    .ConfigureAwait(false) && snapshot.Players[0].History != null &&
                    fake.Paths.Count(p => p.Contains("/self/matches?")) == before + 1 && fake.MaxConcurrent == 1 &&
                    !LeagueDynamicMessageService.TargetNeedsHistory(snapshot, "friendly"),
                    "An explicitly selected player with missing initial history was not recovered with serialized reads.");

                var jungle = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                Require(jungle != null, "Missing-history jungle fixture lost its live roster.");
                jungle.Players[0].History = null;
                jungle.Players[0].JungleSamples = null;
                Require(await service.LoadJungleAsync(jungle, "self", CancellationToken.None).ConfigureAwait(false) &&
                    jungle.Players[0].History != null && jungle.Players[0].JungleSamples != null &&
                    jungle.Players[0].JungleSamples.Count == 2,
                    "Jungle loading must refill missing history before timeline analysis.");

                var stale = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                stale.Players[0].History = null;
                stale.CreatedUtc = DateTime.UtcNow.AddMinutes(-3);
                before = fake.Paths.Count;
                Require(!await service.EnsureHistoriesAsync(stale, new HashSet<string> { "self" },
                    CancellationToken.None).ConfigureAwait(false) && fake.Paths.Count == before,
                    "An expired roster was allowed to recover histories or issue network requests.");
                stale.CreatedUtc = DateTime.UtcNow;
                session = Session("another-account-session");
                before = fake.Paths.Count;
                Require(!await service.LoadJungleAsync(stale, "self", CancellationToken.None).ConfigureAwait(false) &&
                    fake.Paths.Count == before, "A replaced client session recovered stale jungle history.");
            }
        }

        private static async Task ValidateBatchAsync()
        {
            var fake = new Fake();
            var phase = new LeagueDashboardPhaseState { Connected = true, Phase = "ChampSelect" };
            var session = Session("batch");
            using (var service = new LeagueDynamicMessageService(fake, new PerformanceBudgetProvider(), () => session, () => phase))
            {
                var snapshot = await service.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                service.Select(snapshot, new[] { "enemy", "not-in-roster" });
                Require(service.Selection(snapshot).SetEquals(new[] { "enemy" }), "Player selection escaped the active roster.");
                var calls = 0;
                Require(await service.SendAsync(snapshot, new[] { "first", "second" }, 0, (line, token) =>
                { calls++; return Task.FromResult(true); }, CancellationToken.None).ConfigureAwait(false) && calls == 2,
                    "Bounded dynamic batch did not reuse the provided line sender.");
                calls = 0;
                Require(!await service.SendAsync(snapshot, new[] { "first", "second" }, 0, (line, token) =>
                { calls++; phase.Phase = "Lobby"; return Task.FromResult(true); }, CancellationToken.None).ConfigureAwait(false) && calls == 1,
                    "Phase departure continued the dynamic batch.");
                phase.Phase = "ChampSelect"; calls = 0;
                Require(!await service.SendAsync(snapshot, new[] { "first", "second" }, 0, (line, token) =>
                { calls++; service.Cancel(); return Task.FromResult(true); }, CancellationToken.None).ConfigureAwait(false) && calls == 1,
                    "Explicit cancellation continued the dynamic batch.");
                calls = 0; fake.ChangeRoster = true;
                Require(!await service.SendAsync(snapshot, new[] { "stale" }, 0, (line, token) =>
                { calls++; return Task.FromResult(true); }, CancellationToken.None).ConfigureAwait(false) && calls == 0,
                    "Stale roster bypassed the final batch preflight.");
                service.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "InProgress" });
                Require(service.Selection(snapshot) == null, "Player identities survived their phase selection scope.");
            }
            var options = new LeagueDynamicMessageOptions { EnemyShortcut = "Ctrl+F6", Target = "enemy", VisionScore = true };
            var settings = new FACM.Services.AppSettings { LeagueDynamicMessageOptions = LeagueDynamicMessageOptions.Save(options) };
            var parsed = FACM.Services.AppSettings.ParseLines(settings.BuildLinesForSmokeTest());
            Require(LeagueDynamicMessageOptions.Parse(parsed.LeagueDynamicMessageOptions).EnemyShortcut == "Ctrl+F6",
                "Dynamic shortcuts failed INI persistence.");
            var restored = new FACM.Services.AppSettings();
            FACM.Services.CloudSettingsSnapshot.Capture(settings).ApplyTo(restored);
            var restoredOptions = LeagueDynamicMessageOptions.Parse(restored.LeagueDynamicMessageOptions);
            Require(restoredOptions.Target == "enemy" && restoredOptions.VisionScore && restoredOptions.EnemyShortcut == "Ctrl+F6",
                "Dynamic options failed cloud persistence.");
            var rejected = false;
            try { LeagueDynamicMessageOptions.Save(new LeagueDynamicMessageOptions { EnemyShortcut = "F6", AllShortcut = "F6" }); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "Duplicate dynamic shortcuts must fail before registration.");
        }

        private static LeagueClientSession Session(string key) { return new LeagueClientSession("fixture", 1, 12345, key, "https", "smoke"); }
        private static byte[] Bytes(string text) { return Encoding.UTF8.GetBytes(text); }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        private static string History()
        {
            return "{\"games\":{\"games\":[" + Match(1, 3, 5, 2, 300, 120, 10, true) + "," + Match(2, 7, 3, 0, 500, 120, 20, false) + "]}}";
        }

        private static string Match(int id, int kills, int assists, int deaths, int damage, int cs, int vision, bool win)
        {
            return "{\"gameId\":" + id + ",\"mapId\":11,\"gameDuration\":1200,\"participantIdentities\":[" +
                "{\"participantId\":1,\"player\":{\"puuid\":\"self\",\"summonerId\":1}}," +
                "{\"participantId\":2,\"player\":{\"puuid\":\"enemy\",\"summonerId\":2}}],\"participants\":[" +
                "{\"participantId\":1,\"teamId\":100,\"spell1Id\":11,\"championId\":" + (id == 1 ? 77 : 88) + ",\"stats\":{\"kills\":" + kills +
                ",\"assists\":" + assists + ",\"deaths\":" + deaths + ",\"win\":" + (win ? "true" : "false") +
                ",\"totalDamageDealtToChampions\":" + damage + ",\"totalDamageTaken\":200,\"goldEarned\":10000,\"totalMinionsKilled\":" + cs +
                ",\"neutralMinionsKilled\":0,\"visionScore\":" + vision + ",\"teamPosition\":\"TOP\"}}," +
                "{\"participantId\":2,\"teamId\":100,\"championId\":99,\"stats\":{\"kills\":" + (10 - kills) +
                ",\"assists\":1,\"deaths\":1,\"win\":true,\"totalDamageDealtToChampions\":" + (1000 - damage) +
                ",\"totalDamageTaken\":800,\"goldEarned\":10000}}]}";
        }

        private sealed class Fake : ILeagueClientApi
        {
            internal readonly List<string> Paths = new List<string>();
            internal int RosterReads, MaxConcurrent;
            private int _concurrent;
            internal bool ChangeRoster;
            internal bool ChangeHistory;
            internal string ProfilePuuid = "self";
            internal bool IncompleteHistory;
            internal Action<string> OnRead;
            public async Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); Paths.Add(path);
                MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref _concurrent));
                try
                {
                    await Task.Delay(1, token).ConfigureAwait(false);
                    OnRead?.Invoke(path); token.ThrowIfCancellationRequested();
                    if (path == LeagueDashboardDetailsService.SummonerPath)
                        return Bytes("{\"puuid\":\"" + ProfilePuuid + "\",\"summonerId\":1}");
                    if (path == LeagueLiveDataService.ChampSelectSessionPath)
                    {
                        RosterReads++;
                        return Bytes("{\"localPlayerCellId\":0,\"myTeam\":[{\"cellId\":0,\"puuid\":\"self\",\"summonerId\":1,\"gameName\":\"Self\",\"championId\":77}]," +
                            "\"theirTeam\":[{\"cellId\":5,\"puuid\":\"enemy\",\"summonerId\":2,\"gameName\":\"Enemy\",\"championId\":" +
                            (ChangeRoster && RosterReads > 1 ? 88 : 99) + "}]}");
                    }
                    if (path == LeagueLiveDataService.GameflowSessionPath)
                        return Bytes("{\"phase\":\"InProgress\",\"gameData\":{\"gameId\":100,\"teamOne\":[" +
                            "{\"puuid\":\"self\",\"summonerId\":1,\"championId\":77}],\"teamTwo\":[" +
                            "{\"puuid\":\"enemy\",\"summonerId\":2,\"championId\":99}]}}");
                    if (path.Contains("/matches?")) return Bytes(IncompleteHistory ?
                        "{\"games\":{\"games\":[{\"gameId\":1,\"mapId\":11}]}}" : ChangeHistory ?
                        "{\"games\":{\"games\":[" + Match(3, 1, 4, 0, 200, 130, 15, false) + "]}}" : History());
                    if (path == "/lol-match-history/v1/games/1") return Bytes(Match(1, 3, 5, 2, 300, 120, 10, true));
                    if (path.StartsWith("/lol-match-history/v1/game-timelines/", StringComparison.Ordinal))
                        return Bytes("{\"frames\":[{\"events\":[]},{\"participantFrames\":{\"1\":{\"position\":{\"x\":3830,\"y\":7880}}}}]}");
                    if (path == LeaguePlayerDataService.ChampionSummaryPath) return Bytes("[]");
                    throw new InvalidOperationException("Unexpected dynamic analysis route.");
                }
                finally { Interlocked.Decrement(ref _concurrent); }
            }
        }
    }
}
