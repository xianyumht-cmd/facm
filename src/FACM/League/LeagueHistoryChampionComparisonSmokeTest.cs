using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueHistoryChampionComparisonSmokeTest
    {
        internal static void Validate()
        {
            var first = Match(1, 22, 60, 60, 60);
            first.Kills = 2; first.Deaths = 0; first.Assists = 0;
            first.VisionScore = 0; first.SoloKills = 0;
            first.TotalPings = 4; first.EnemyMissingPings = 2;
            var second = Match(2, 22, 600, 0, 120);
            second.Kills = 0; second.Deaths = 10; second.Assists = 0;
            second.VisionScore = null; second.SoloKills = null;
            var other = Match(3, 145, 0, 0, 60);
            var matches = new[] { first, second, other, first };
            var values = LeagueHistoryChampionComparison.Calculate(matches, 22);
            Func<string, LeagueHistoryComparisonValue> get = key => values.Single(v => v.Key == key);
            Require(values.Count == 29 && get(UiTextKeys.LeaguePlayerCompareKda).Value == .2d &&
                get(UiTextKeys.LeaguePlayerCompareKda).Total == 2,
                "Hero comparisons must deduplicate games and use aggregate KDA, not mean game KDA.");
            Require(get(UiTextKeys.LeaguePlayerCompareKdaVariation).Value == 1d &&
                get(UiTextKeys.LeaguePlayerCompareKillDamageEfficiency).Value == 1.5d &&
                get(UiTextKeys.LeaguePlayerCompareKillDamageEfficiency).Known == 2,
                "Pinned KDA population variation and per-game kill/damage efficiency must not pool unlike denominators.");
            Require(get(UiTextKeys.LeaguePlayerCompareDamageMinute).Value == 180d &&
                get(UiTextKeys.LeaguePlayerCompareDamageShare).Value == .75d,
                "Per-minute and ratio comparisons must average individual-game values.");
            Require(get(UiTextKeys.LeaguePlayerCompareVision).Value == 0d &&
                get(UiTextKeys.LeaguePlayerCompareVision).Known == 1 &&
                !get(UiTextKeys.LeaguePlayerCompareSoloKills).Value.HasValue &&
                get(UiTextKeys.LeaguePlayerCompareSoloKills).Known == 1,
                "Known zero must remain zero; source all-non-null solo kills must not use a partial mean.");
            Require(!get(UiTextKeys.LeaguePlayerComparePings).Value.HasValue &&
                get(UiTextKeys.LeaguePlayerComparePings).Known == 1 &&
                !get(UiTextKeys.LeaguePlayerCompareMissingPings).Value.HasValue,
                "One missing SGP ping source must leave both whole-sample averages unavailable.");
            second.TotalPings = 8; second.EnemyMissingPings = 0;
            values = LeagueHistoryChampionComparison.Calculate(matches, 22);
            Require(get(UiTextKeys.LeaguePlayerComparePings).Value == 6d &&
                get(UiTextKeys.LeaguePlayerCompareMissingPings).Value == 1d &&
                get(UiTextKeys.LeaguePlayerCompareMissingPings).Known == 2,
                "Two verified ping samples must permit zero missing-pings and average independently.");
            var independent = LeagueHistoryChampionComparison.Calculate(matches, 145);
            Require(independent.Single(v => v.Key == UiTextKeys.LeaguePlayerCompareDamageShare).Value == 0 &&
                !independent.Single(v => v.Key == UiTextKeys.LeaguePlayerCompareKdaVariation).Value.HasValue &&
                independent.All(v => v.Total == 1),
                "Another hero must retain independent sample coverage and verified zero denominators.");
            first.MetricBaselines = null;
            Require(!LeagueHistoryChampionComparison.Cs(first).HasValue,
                "A default zero CS without both source components cannot become measured evidence.");
            first.GameMode = "CHERRY"; first.PlayerSubteamId = 1;
            first.ParticipantRoster[1].PlayerSubteamId = 2;
            Require(LeagueHistoryChampionComparison.Relative(first, m => m.DamageDealtToChampions,
                    true, false) == 1d,
                "Cherry comparisons must exclude another subteam sharing ordinary teamId.");
            Require(LeagueHistoryChampionComparison.KillDamageEfficiency(first) == 1d,
                "A Cherry solo-subteam kill efficiency must not include ordinary-team opponent damage.");
            first.ParticipantRoster[1].PlayerSubteamId = null;
            Require(!LeagueHistoryChampionComparison.Relative(first, m => m.DamageDealtToChampions,
                    true, false).HasValue,
                "Unassigned Cherry participants must leave team comparison evidence unknown.");
            Require(!LeagueHistoryChampionComparison.KillDamageEfficiency(first).HasValue,
                "Missing Cherry subteam evidence must suppress kill/damage efficiency too.");
            var zero = Match(4, 1, 0, 0, 60);
            Require(LeagueHistoryChampionComparison.KillDamageEfficiency(zero) == 1d,
                "The pinned noZero fallback yields one when verified team kills or damage totals are zero.");
            zero.Kills = 1; zero.ParticipantRoster[1].DamageDealtToChampions = 100;
            Require(!LeagueHistoryChampionComparison.KillDamageEfficiency(zero).HasValue,
                "A positive team total with zero personal damage must remain unavailable, not infinite.");
            zero.ParticipantRoster[1].StatisticsAvailable = false;
            Require(!LeagueHistoryChampionComparison.KillDamageEfficiency(zero).HasValue,
                "Missing teammate statistics must not fabricate team damage or kills.");
            Require(LeagueHistoryChampionComparison.Calculate(matches, 999).All(v =>
                !v.Value.HasValue && v.Known == 0 && v.Total == 0),
                "Absent champions must display unavailable values, never another hero's metrics.");
            var positions = LeagueHistoryPositionChart.Project(new[] {
                new LeagueHistoryPositionStat { Position = "JUNGLE", Games = 2 },
                new LeagueHistoryPositionStat { Position = "TOP", Games = 2 },
                new LeagueHistoryPositionStat { Position = "BOTTOM", Games = 1 },
                new LeagueHistoryPositionStat { Position = "UNKNOWN", Games = 10 },
                new LeagueHistoryPositionStat { Position = "MIDDLE", Games = 0 },
                new LeagueHistoryPositionStat { Position = "TOP", Games = 1 }
            });
            Require(positions.Count == 3 && positions[0].Position == "TOP" &&
                positions[1].Position == "JUNGLE" && positions[2].Position == "BOTTOM" &&
                LeagueHistoryPositionChart.Project(null).Count == 0,
                "Position distribution must use five recognized roles, exclude unknown/zero and deduplicate.");
            using (var chart = new LeagueHistoryPositionChart(UiTextCatalog.Load()))
            using (var wide = new Bitmap(500, 310))
            using (var narrow = new Bitmap(310, 390))
            {
                chart.SetPositions(positions);
                chart.Size = wide.Size;
                chart.DrawToBitmap(wide, new Rectangle(Point.Empty, wide.Size));
                Require(chart.AccessibleDescription.Contains("40%"),
                    "Position percentages must use only the verified-role game count.");
                chart.Size = narrow.Size;
                chart.DrawToBitmap(narrow, new Rectangle(Point.Empty, narrow.Size));
                chart.SetPositions(null);
                Require(string.IsNullOrEmpty(chart.AccessibleDescription),
                    "Empty/changed position data must clear the prior chart's accessible summary.");
            }
            using (var view = new LeagueHistoryChampionComparisonView(UiTextCatalog.Load()))
            using (var bitmap = new Bitmap(520, 330))
            {
                view.Size = bitmap.Size;
                view.RefreshData(matches);
                view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                Require(view.Controls.OfType<ListView>().Single().Items.Count == 29,
                    "Native comparison must expose every available metric row.");
                view.RefreshData(new LeaguePlayerMatchSummary[0]);
                Require(view.Controls.OfType<ListView>().Single().Items[0].SubItems[1].Text.Contains("(0/0)"),
                    "An emptied analysis snapshot must clear previous comparison values.");
            }
        }

        private static LeaguePlayerMatchSummary Match(long id, int hero, double damage, double peerDamage, int seconds)
        {
            var match = new LeaguePlayerMatchSummary { GameId = id, ChampionId = hero,
                ParticipantId = 1, TeamId = 100, GameMode = "CLASSIC", GameType = "MATCHED_GAME",
                ParticipantResolved = true, StatisticsAvailable = true, WinResult = "win",
                GameDurationSeconds = seconds, DamageDealtToChampions = damage, DamageTaken = 0,
                GoldEarned = 0, TotalHeal = 0, VisionScore = 0, CreepScore = 10,
                MetricBaselines = new Dictionary<string, double[]> { { "cs", new[] { 20d, 10d, 20d, 10d } } } };
            var peer = new LeaguePlayerMatchSummary { ParticipantId = 2, TeamId = 100,
                StatisticsAvailable = true, DamageDealtToChampions = peerDamage, DamageTaken = 0,
                GoldEarned = 0, TotalHeal = 0, VisionScore = 0, CreepScore = 10,
                MetricBaselines = new Dictionary<string, double[]> { { "cs", new[] { 20d, 10d, 20d, 10d } } } };
            match.ParticipantRoster = new[] { match, peer };
            return match;
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
