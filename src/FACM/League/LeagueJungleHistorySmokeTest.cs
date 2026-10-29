using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueJungleHistorySmokeTest
    {
        internal static void Validate()
        {
            var first = Match(1001, 1, 64, LeaguePlayerHistorySource.Lcu, null);
            var second = Match(1002, 1, 64, LeaguePlayerHistorySource.Sgp, "TENCENT_HN10");
            var third = Match(1003, 2, 11, LeaguePlayerHistorySource.Lcu, null);
            var candidates = LeagueJungleHistoryAnalysis.Candidates(new[] {
                first, second, third, first,
                Match(1004, 1, 64, LeaguePlayerHistorySource.Sgp, null),
                Match(1005, 1, 64, LeaguePlayerHistorySource.Lcu, "TENCENT_HN10"),
                Match(1006, 1, 64, LeaguePlayerHistorySource.Lcu, null, "other"),
                Match(1007, 1, 64, LeaguePlayerHistorySource.Lcu, null, null, false)
            }, "target");
            Require(candidates.Count == 3 && candidates.Select(m => m.GameId)
                .SequenceEqual(new long[] { 1001, 1002, 1003 }),
                "Only unique current-target Rift jungle matches with original source/server provenance may request timelines.");
            var selectedAttempts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                LeaguePlayerDataService.HistoryDetailKey(first),
                LeaguePlayerDataService.HistoryDetailKey(third) };
            var overallCoverage = LeagueJungleHistoryAnalysis.Coverage(candidates, "target", selectedAttempts, 0);
            var heroCoverage = LeagueJungleHistoryAnalysis.Coverage(candidates, "target", selectedAttempts, 64);
            Require(overallCoverage.Item1 == 3 && overallCoverage.Item2 == 2 &&
                heroCoverage.Item1 == 2 && heroCoverage.Item2 == 1 &&
                LeagueJungleHistoryAnalysis.Coverage(candidates, "target", selectedAttempts, 11).Item1 == 1,
                "A hero with no successful timeline must remain selectable with its own eligible/attempted coverage.");
            var loaded = Enumerable.Range(0, 20).Select(i => Match(3000 + i, 1, 64,
                LeaguePlayerHistorySource.Lcu, null)).ToList();
            var attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                LeaguePlayerDataService.HistoryDetailKey(loaded[0]),
                LeaguePlayerDataService.HistoryDetailKey(loaded[1]) };
            var firstBatch = LeagueJungleHistoryAnalysis.PendingBatch(loaded, "target", attempted, 0);
            Require(firstBatch.Count == LeagueJungleHistoryAnalysis.BatchSize &&
                firstBatch.First().GameId == 3002 && firstBatch.Last().GameId == 3009 &&
                attempted.Count == 2 &&
                LeagueJungleHistoryAnalysis.PendingBatch(loaded, "target", attempted, 39).Count == 1 &&
                LeagueJungleHistoryAnalysis.PendingBatch(loaded, "target", attempted, 40).Count == 0,
                "Pending batches must skip attempted keys, cap each explicit read at eight and stop after forty successes.");
            var a = new LeagueJungleAnalysis { PositionSamples = 2, AvailableSamples = 14,
                Level3Gank = true, Level4Gank = null, StartBlue = true, StartCamp = "blue",
                ObjectivesAvailable = true, GotFirstDragon = null, Dragons = 2, SoloDragons = 1,
                Voidgrubs = 3, FirstDragonTime = 600000, FirstVoidgrubTime = 360000 };
            a.ZoneWeights[0] = 5; a.ZoneWeights[1] = 1;
            a.Ganks[0] = 2;
            a.MinutePositions.Add(new PointF(1000, 12000));
            a.MinuteLanes.Add(2);
            a.GankPositions.Add(new PointF(2000, 11000));
            a.GankLanes.Add(1);
            a.Level3KillPositions.Add(new PointF(3000, 9000));
            a.OmittedGankMapPoints = 1;
            a.OmittedEarlyMapPoints = 1;
            var b = new LeagueJungleAnalysis { PositionSamples = 1, AvailableSamples = 14,
                Level3Gank = false, Level4Gank = true, StartBlue = false, StartCamp = "raptors",
                ObjectivesAvailable = true, GotFirstDragon = false,
                Dragons = 0, FirstVoidgrubTime = 480000 };
            b.ZoneWeights[1] = 4; b.Ganks[1] = 1;
            b.MinutePositions.Add(new PointF(5000, 6000));
            b.MinuteLanes.Add(1);
            b.GankPositions.Add(new PointF(6000, 6000));
            b.GankLanes.Add(1);
            b.Level4KillPositions.Add(new PointF(7000, 6000));
            var unavailable = new LeagueJungleAnalysis { ObjectivesAvailable = false, Level3Gank = null,
                Level4Gank = null, MissingKillPositions = 2 };
            var samples = new[] {
                new LeagueJungleHistorySample { Match = first, Analysis = a },
                new LeagueJungleHistorySample { Match = second, Analysis = b },
                new LeagueJungleHistorySample { Match = first, Analysis = unavailable },
                new LeagueJungleHistorySample { Match = third, Analysis = unavailable }
            };
            var aggregate = LeagueJungleHistoryAggregate.Calculate(samples);
            Require(aggregate != null && aggregate.Games == 3 && aggregate.PositionGames == 2 &&
                Math.Abs(aggregate.ZoneRate(0).Value - .5d) < .0001 &&
                aggregate.Ganks[0] == 2 && aggregate.Ganks[1] == 1 &&
                aggregate.Level3Known == 2 && aggregate.Level3Detected == 1 &&
                aggregate.Level4Known == 1 && aggregate.Level4Detected == 1 &&
                aggregate.MissingKillPositions == 2,
                "Jungle rates must weight source-confirmed positions and separate early signal denominators from unknowns.");
            Require(aggregate.MinutePositions.Count == 2 && aggregate.GankPositions.Count == 2 &&
                aggregate.OmittedGankMapPoints == 1 && aggregate.OmittedEarlyMapPoints == 1 &&
                aggregate.Level3KillPositions.Count == 1 && aggregate.Level4KillPositions.Count == 1 &&
                aggregate.Level3KillPositions[0].Y == 9000 && aggregate.Level4KillPositions[0].X == 7000 &&
                aggregate.MinutePositions[0].X == 1000 && aggregate.GankPositions[1].Y == 6000 &&
                aggregate.MinuteLanes.SequenceEqual(new[] { 2, 1 }) &&
                aggregate.GankLanes.SequenceEqual(new[] { 1, 1 }),
                "A distinct-game aggregate must preserve bounded minute and gank coordinates without replaying raw timeline frames.");
            Require(aggregate.ObjectiveGames == 2 && aggregate.FirstDragonKnown == 1 &&
                aggregate.FirstDragonWon == 0 && aggregate.Dragons == 2 && aggregate.SoloDragons == 1 &&
                Math.Abs(aggregate.FirstDragonSeconds.Value - 600d) < .0001 &&
                Math.Abs(aggregate.FirstVoidgrubSeconds.Value - 420d) < .0001 &&
                aggregate.FirstCampGames == 2 && aggregate.Camps["blue/normal/blue"] == 1 &&
                aggregate.Camps["blue/invade/raptors"] == 1,
                "Objective averages and camp-side grouping must exclude unknown teams/times and duplicated games.");
            var hero = LeagueJungleHistoryAggregate.Calculate(samples.Where(s => s.Match.ChampionId == 64));
            Require(hero.Games == 2 && hero.ObjectiveGames == 2 && hero.FirstDragonKnown == 1 &&
                hero.FirstDragonWon == 0 && hero.Level3Known == 2 && hero.Level4Known == 1 &&
                hero.MinutePositions.Count == 2 && hero.GankPositions.Count == 2 &&
                hero.Level3KillPositions.Count == 1 && hero.Level4KillPositions.Count == 1,
                "Per-champion jungle totals must use only that hero's successfully loaded timelines.");
            var red = Match(1008, 1, 64, LeaguePlayerHistorySource.Lcu, null);
            red.TeamId = 200;
            var redAnalysis = new LeagueJungleAnalysis { Level3Gank = true, Level4Gank = false,
                ObjectivesAvailable = true, StartCamp = "raptors", StartBlue = false };
            redAnalysis.MinutePositions.Add(new PointF(12000, 2000));
            redAnalysis.GankPositions.Add(new PointF(13000, 2500));
            var withRed = samples.Concat(new[] {
                new LeagueJungleHistorySample { Match = red, Analysis = redAnalysis } }).ToArray();
            var sides = LeagueJungleHistoryAggregate.Calculate(withRed);
            Require(sides.BlueGames == 3 && sides.RedGames == 1 &&
                sides.BlueLevel3Known == 2 && sides.BlueLevel3Detected == 1 &&
                sides.BlueLevel4Known == 1 && sides.BlueLevel4Detected == 1 &&
                sides.RedLevel3Known == 1 && sides.RedLevel3Detected == 1 &&
                sides.RedLevel4Known == 1 && sides.RedLevel4Detected == 0 &&
                sides.Camps["red/normal/raptors"] == 1,
                "Pinned by-team early-gank rates and first-camp sides require separate blue/red coverage.");
            var blueMap = LeagueJungleHistoryAggregate.Calculate(
                LeagueJungleHistoryAnalysis.MapSamples(withRed, 64, 100));
            var redMap = LeagueJungleHistoryAggregate.Calculate(
                LeagueJungleHistoryAnalysis.MapSamples(withRed, 64, 200));
            var allMap = LeagueJungleHistoryAggregate.Calculate(
                LeagueJungleHistoryAnalysis.MapSamples(withRed, 64, 0));
            Require(blueMap.Games == 2 && blueMap.MinutePositions.Count == 2 &&
                blueMap.GankPositions.Count == 2 && redMap.Games == 1 &&
                redMap.MinutePositions.Single().X == 12000 &&
                redMap.GankPositions.Single().X == 13000 &&
                allMap.Games == 3 && allMap.MinutePositions.Count == 3 &&
                redMap.MinuteLanes.SequenceEqual(new[] { -1 }) &&
                redMap.GankLanes.SequenceEqual(new[] { -1 }) &&
                LeagueJungleHistoryAnalysis.MapSamples(withRed, 11, 200).Count() == 0 &&
                !LeagueJungleHistoryAnalysis.MapSamples(withRed, 64, 300).Any(),
                "Side-filtered spatial samples must not mix blue/red teams, champions or unknown sides.");
            var blueHeat = LeagueEventMap.BuildHeatmap(blueMap.MinutePositions, blueMap.GankPositions, 11);
            var redHeat = LeagueEventMap.BuildHeatmap(redMap.MinutePositions, redMap.GankPositions, 11);
            Require(blueHeat.Length > 0 && redHeat.Length > 0 &&
                !blueHeat.Any(c => c.Center.X > .75f) &&
                redHeat.Any(c => c.Center.X > .75f),
                "Side selection must isolate both scatter and weighted heatmap inputs.");
            var ui = UiTextCatalog.Load();
            Require(ui.Get(UiTextKeys.LeagueJungleMapAllSides) !=
                ui.Get(UiTextKeys.LeagueJungleMapSideFilter),
                "Map side selector must expose distinct localized label and all-sides choice.");
            var text = LeagueJungleHistoryAnalysis.Report(ui, samples, 7, 4, 64);
            Require(text.Contains("2/7") && text.Contains("0/1") && text.Contains("1/2") &&
                LeagueJungleHistoryAnalysis.Report(ui, new LeagueJungleHistorySample[0], 1, 0)
                    .Contains(ui.Get(UiTextKeys.LeagueJungleHistoryUnavailable)),
                "Native report must expose attempted/eligible coverage and never present a missing timeline as a loss.");
        }

        private static LeaguePlayerMatchSummary Match(long id, int participant, int champion,
            LeaguePlayerHistorySource source, string region, string puuid = "target", bool jungler = true)
        {
            return new LeaguePlayerMatchSummary { GameId = id, ParticipantId = participant,
                ChampionId = champion, MapId = 11, TeamId = 100, Position = jungler ? "JUNGLE" : "TOP",
                PuuId = puuid, GameType = "MATCHED_GAME", ParticipantResolved = true,
                StatisticsAvailable = true, WinResult = "win", DataSource = source, SgpServerId = region };
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
