using System;
using System.Collections.Generic;
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
            ValidateAggregation();
            ValidatePremadeInference();
            ValidateLoadingAsync().GetAwaiter().GetResult();
            ValidateBatchAsync().GetAwaiter().GetResult();
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
            return "{\"gameId\":" + id + ",\"gameDuration\":1200,\"participantIdentities\":[" +
                "{\"participantId\":1,\"player\":{\"puuid\":\"self\",\"summonerId\":1}}," +
                "{\"participantId\":2,\"player\":{\"puuid\":\"enemy\",\"summonerId\":2}}],\"participants\":[" +
                "{\"participantId\":1,\"teamId\":100,\"championId\":" + (id == 1 ? 77 : 88) + ",\"stats\":{\"kills\":" + kills +
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
            internal Action<string> OnRead;
            public async Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); Paths.Add(path);
                MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref _concurrent));
                try
                {
                    await Task.Delay(1, token).ConfigureAwait(false);
                    OnRead?.Invoke(path); token.ThrowIfCancellationRequested();
                    if (path == LeagueDashboardDetailsService.SummonerPath) return Bytes("{\"puuid\":\"self\",\"summonerId\":1}");
                    if (path == LeagueLiveDataService.ChampSelectSessionPath)
                    {
                        RosterReads++;
                        return Bytes("{\"localPlayerCellId\":0,\"myTeam\":[{\"cellId\":0,\"puuid\":\"self\",\"summonerId\":1,\"gameName\":\"Self\",\"championId\":77}]," +
                            "\"theirTeam\":[{\"cellId\":5,\"puuid\":\"enemy\",\"summonerId\":2,\"gameName\":\"Enemy\",\"championId\":" +
                            (ChangeRoster && RosterReads > 1 ? 88 : 99) + "}]}");
                    }
                    if (path.Contains("/matches?")) return Bytes(History());
                    if (path == LeaguePlayerDataService.ChampionSummaryPath) return Bytes("[]");
                    throw new InvalidOperationException("Unexpected dynamic analysis route.");
                }
                finally { Interlocked.Decrement(ref _concurrent); }
            }
        }
    }
}
