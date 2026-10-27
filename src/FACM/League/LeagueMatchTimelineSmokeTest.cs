using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;

namespace FACM.League
{
    internal static class LeagueMatchTimelineSmokeTest
    {
        private const string Timeline = "{\"gameId\":701,\"frames\":[{\"timestamp\":0,\"participantFrames\":{\"1\":{\"participantId\":1,\"level\":1,\"totalGold\":500,\"currentGold\":100,\"xp\":0,\"minionsKilled\":0,\"jungleMinionsKilled\":0,\"damageStats\":{\"totalDamageDone\":0}}},\"events\":[{\"type\":\"ITEM_PURCHASED\",\"timestamp\":0,\"participantId\":1,\"itemId\":1055},{\"type\":\"ITEM_UNDO\",\"timestamp\":0,\"participantId\":1,\"beforeId\":1055,\"afterId\":0}]},{\"timestamp\":60000,\"participantFrames\":{\"1\":{\"participantId\":1,\"level\":2,\"totalGold\":700,\"xp\":300,\"minionsKilled\":3,\"jungleMinionsKilled\":1}},\"events\":[{\"type\":\"CHAMPION_KILL\",\"timestamp\":55000,\"killerId\":2,\"victimId\":3,\"assistingParticipantIds\":[1],\"position\":{\"x\":500,\"y\":600},\"victimDamageReceived\":[{\"physicalDamage\":100}]}]}]}";

        internal static void Validate()
        {
            var parsed = LeagueMatchTimelineParser.Parse(Bytes(Timeline), 701, false);
            Require(parsed != null && parsed.GameId == 701 && parsed.Frames.Count == 2 && parsed.Events.Count == 3 &&
                parsed.Events[0].Type == "ITEM_PURCHASED" && parsed.Events[1].Type == "ITEM_UNDO" &&
                parsed.Events[2].Involves(1) && parsed.Events[2].Involves(2) && parsed.Events[2].Involves(3) &&
                !parsed.Events[2].Involves(4) && parsed.Events[2].Data.ContainsKey("victimDamageReceived") &&
                parsed.Frames[0].Participants[1].ContainsKey("damageStats") &&
                LeagueMatchTimelineParser.Number(parsed.Frames[1].Participants[1], "currentGold") == null &&
                LeagueMatchTimelineParser.Time(125000) == "2:05",
                "Timeline projection must preserve equal-timestamp inventory order, assist filters, detailed SGP fields and absent values.");
            Require(LeagueMatchTimelineParser.Parse(Bytes("{\"json\":" + Timeline + "}"), 701, true) != null &&
                LeagueMatchTimelineParser.Parse(Bytes("{\"json\":" + Timeline + "}"), 702, true) == null &&
                LeagueMatchTimelineParser.Parse(Bytes(Timeline.Replace("\"participantId\":1,\"level\":1", "\"participantId\":2,\"level\":1")), 701, false) == null &&
                LeagueMatchTimelineParser.Parse(Bytes(Timeline.Replace("\"timestamp\":60000", "\"timestamp\":-1")), 701, false) == null &&
                LeagueMatchTimelineParser.Parse(Bytes("{\"frames\":{}}"), 701, false) == null,
                "Wrong game envelopes, mismatched frame identities, invalid chronology and malformed frame arrays must fail closed.");

            Require(LeagueTimelineMetrics.Series(parsed, new[] { 1 }, 0, false).SequenceEqual(new double?[] { 500, 700 }) &&
                LeagueTimelineMetrics.Series(parsed, new[] { 1 }, 1, false).SequenceEqual(new double?[] { 0, 4 }) &&
                LeagueTimelineMetrics.Series(parsed, new[] { 1, 2 }, 2, true).SequenceEqual(new double?[] { 0, 300 }) &&
                LeagueTimelineMetrics.Series(parsed, new[] { 1 }, 3, false).All(value => !value.HasValue),
                "Timeline chart series must use gold, full CS and XP source values while preserving missing SGP damage samples.");
            var damage = new Dictionary<string, object> { { "damageStats", new Dictionary<string, object>
                { { "totalDamageDoneToChampions", 1234 }, { "totalDamageTaken", 4567 } } } };
            Require(LeagueTimelineMetrics.Value(damage, 3) == 1234 && LeagueTimelineMetrics.Value(damage, 4) == 4567 &&
                LeagueTimelineMetrics.Value(damage, 0) == null,
                "Damage comparison must read the exact SGP nested keys without inventing other missing metrics.");
            parsed.Frames[0].Participants[2] = new Dictionary<string, object> { { "totalGold", 1000 } };
            Require(LeagueTimelineMetrics.Series(parsed, new[] { 1, 2 }, 0, true)
                .SequenceEqual(new double?[] { 750, 700 }),
                "A team comparison must average only present verified player samples, never sum them or replace missing players with zero.");
            using (var chart = new LeagueTimelineChart(FACM.Services.UiTextCatalog.Load()))
            using (var bitmap = new System.Drawing.Bitmap(520, 210))
            {
                chart.Size = bitmap.Size;
                chart.SetData(new long[] { 0, 60000 }, new List<LeagueTimelineSeries>
                {
                    new LeagueTimelineSeries { Name = "fixture", Values = new double?[] { 500, 700 }, IsAverage = true }
                });
                chart.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
                chart.SetData(new long[0], new List<LeagueTimelineSeries>());
                chart.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
            }

            ValidateBuildProgression();
            var frameStats = new Dictionary<string, object> { { "health", 512.5 }, { "armor", -12.25 },
                { "attackSpeed", "NaN" }, { "magicPen", double.PositiveInfinity }, { "power", 0 } };
            Require(LeagueTimelineFrameStats.Format(frameStats, "health") == "512.5" &&
                LeagueTimelineFrameStats.Format(frameStats, "armor") == "-12.25" &&
                LeagueTimelineFrameStats.Format(frameStats, "power") == "0" &&
                LeagueTimelineFrameStats.Format(frameStats, "attackSpeed") == "—" &&
                LeagueTimelineFrameStats.Format(frameStats, "magicPen") == "—" &&
                LeagueTimelineFrameStats.Format(frameStats, "unknown") == "—",
                "Detailed frame values must retain signed decimals and zero while rejecting missing/nonfinite values.");
            parsed.Frames[0].Participants[1]["championStats"] = frameStats;
            using (var statsPanel = new LeagueTimelineFrameStats(FACM.Services.UiTextCatalog.Load()))
            using (var bitmap = new System.Drawing.Bitmap(520, 210))
            {
                statsPanel.Size = bitmap.Size;
                statsPanel.SetData(parsed, 1);
                statsPanel.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
                statsPanel.SetData(null, 1);
                statsPanel.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
            }

            var session = new LeagueClientSession("LeagueClientUx", 1, 12345, "fixture", "https", "fixture", "HN10", "TENCENT");
            var local = new TimelineApi();
            var regional = new RegionalApi();
            var service = new LeaguePlayerDataService(local, new PerformanceBudgetProvider(), null, regional, () => session);
            var selected = new LeaguePlayerMatchSummary { GameId = 701, DataSource = LeaguePlayerHistorySource.Lcu };
            var result = service.LoadMatchTimelineAsync("self", selected, CancellationToken.None, session).GetAwaiter().GetResult();
            Require(result != null && local.TimelineReads == 1 && local.ProfileReads == 2 && regional.Reads == 0 &&
                local.LastPath == "/lol-match-history/v1/game-timelines/701" && service.TryGetCachedPage() == null,
                "Local timelines require one selected-game route between owner fences, without a history-cache write.");
            selected.DataSource = LeaguePlayerHistorySource.Sgp;
            selected.SgpServerId = "TENCENT_HN1";
            result = service.LoadMatchTimelineAsync("self", selected, CancellationToken.None, session).GetAwaiter().GetResult();
            Require(result != null && regional.Reads == 1 && regional.Server == "TENCENT_HN1" &&
                local.TimelineReads == 1 && local.ProfileReads == 4,
                "A regional timeline must use the same selected server's DETAILS route and never query local timelines.");
            local.ChangeAfterTimeline = true;
            selected.DataSource = LeaguePlayerHistorySource.Lcu;
            Require(service.LoadMatchTimelineAsync("self", selected, CancellationToken.None, session).GetAwaiter().GetResult() == null,
                "A timeline response cannot survive an account change during its selected-game read.");
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                var cancelled = false;
                try { service.LoadMatchTimelineAsync("self", selected, cancel.Token, session).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { cancelled = true; }
                Require(cancelled, "Closing a timeline owner must cancel the outstanding read instead of publishing stale data.");
            }
        }

        private static void ValidateBuildProgression()
        {
            var source = new LeagueMatchTimeline();
            source.Events.Add(BuildEvent("ITEM_PURCHASED", 1, 1000, "itemId", 6032));
            source.Events.Add(BuildEvent("ITEM_PURCHASED", 1, 31000, "itemId", 1055));
            source.Events.Add(BuildEvent("ITEM_PURCHASED", 1, 61001, "itemId", 220000));
            source.Events.Add(BuildEvent("ITEM_PURCHASED", 2, 70000, "itemId", 1055));
            source.Events.Add(BuildEvent("SKILL_LEVEL_UP", 1, 71000, "skillSlot", 1));
            var evolution = BuildEvent("SKILL_LEVEL_UP", 1, 72000, "skillSlot", 4);
            evolution.Data["levelUpType"] = "EVOLVE";
            source.Events.Add(evolution);
            source.Events.Add(BuildEvent("SKILL_LEVEL_UP", 1, 73000, "skillSlot", 2));
            source.Events.Add(BuildEvent("SKILL_LEVEL_UP", 2, 74000, "skillSlot", 3));
            var builds = LeagueTimelineBuilds.Project(source);
            Require(builds.Steps.Count == 8 && builds.Steps.Take(4).Select(step => step.PurchaseGroup)
                .SequenceEqual(new[] { 1, 1, 2, 1 }) && builds.Anvils[1] == 2 && !builds.Anvils.ContainsKey(2) &&
                builds.Steps.Skip(4).Select(step => step.SkillLevel).SequenceEqual(new int?[] { 1, null, 2, 1 }) &&
                builds.Steps[5].IsEvolution,
                "Build progression must preserve per-player 30-second purchase grouping, both anvil IDs and EVOLVE-excluded skill levels.");
        }
        private static LeagueMatchTimelineEvent BuildEvent(string type, int id, long timestamp, string key, int value)
        {
            return new LeagueMatchTimelineEvent { Type = type, ParticipantId = id, Timestamp = timestamp,
                Assists = new int[0], Data = new Dictionary<string, object> { { key, value } } };
        }

        private static byte[] Bytes(string text) { return Encoding.UTF8.GetBytes(text); }
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
        private sealed class TimelineApi : ILeagueClientApi
        {
            internal int ProfileReads, TimelineReads;
            internal string LastPath;
            internal bool ChangeAfterTimeline;
            private bool _changed;
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (path == LeagueDashboardDetailsService.SummonerPath)
                {
                    ProfileReads++;
                    return Task.FromResult(Bytes(_changed ? "{\"puuid\":\"other\"}" : "{\"puuid\":\"self\"}"));
                }
                TimelineReads++;
                LastPath = path;
                if (ChangeAfterTimeline) _changed = true;
                return Task.FromResult(Bytes(Timeline));
            }
        }
        private sealed class RegionalApi : ILeagueSgpMatchHistoryApi
        {
            internal int Reads;
            internal string Server;
            public Task<byte[]> TryGetGameDetailsAsync(long gameId, CancellationToken token, string serverId = null)
            {
                Reads++; Server = serverId;
                return Task.FromResult(Bytes("{\"json\":" + Timeline + "}"));
            }
            public Task<byte[]> TryGetGameSummaryAsync(long id, CancellationToken token, string serverId = null)
            { throw new InvalidOperationException("A timeline must not request game summaries."); }
            public Task<byte[]> TryGetSummaryAsync(string puuid, int start, int count, CancellationToken token, string serverId = null)
            { throw new InvalidOperationException("A timeline must not request history pages."); }
            public Task<byte[]> TryGetSummonerAsync(string puuid, CancellationToken token, string serverId = null)
            { throw new InvalidOperationException("A timeline must not request another target profile."); }
        }
    }
}
