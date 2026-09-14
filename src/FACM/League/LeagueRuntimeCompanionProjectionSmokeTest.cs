using System;
using System.Collections.Generic;
using FACM.Performance;

namespace FACM.League
{
    internal static class LeagueRuntimeCompanionProjectionSmokeTest
    {
        public static void Validate()
        {
            ValidateExactPositionCounterEvidence();
            ValidateRevealedCounterWithoutPositionKeepsSourceEvidenceShape();
            ValidateNetworkFreeSpellPresentation();
        }

        private static void ValidateExactPositionCounterEvidence()
        {
            var recommendation = new LeagueBuildRecommendation();
            recommendation.Rows.Add(new LeagueBuildAdvisorRow
            {
                Category = "counters",
                Recommendation = "A · B · C",
                Evidence = "source",
                IconReferences = new[] { "/champions/11.png", "/champions/22.png", "/champions/33.png" },
                CounterStats = new[]
                {
                    new LeagueBuildCounterStat { ChampionId = 11, Games = 100, Wins = 50, HasWins = true },
                    new LeagueBuildCounterStat { ChampionId = 22, Games = 200, Wins = 120, HasWins = true },
                    new LeagueBuildCounterStat { ChampionId = 33, Games = 300, Wins = 165, HasWins = true }
                }
            });
            var build = new LeagueBuildAdvisorSnapshot
            {
                Connected = true,
                Activity = LeagueActivityLevel.ChampSelect,
                ChampionId = 1,
                Status = "ready",
                Recommendation = recommendation
            };
            var players = new List<LeagueLivePlayerRow>
            {
                new LeagueLivePlayerRow { Side = "ally", IsLocalPlayer = true, Position = "TOP", ChampionId = 1 },
                new LeagueLivePlayerRow { Side = "enemy", Position = "JUNGLE", ChampionId = 11 },
                new LeagueLivePlayerRow { Side = "enemy", Position = "TOP", ChampionId = 22 }
            };

            LeagueRuntimeCompanionSnapshot.PrioritizeRevealedEnemyCountersForSmokeTest(build, players);

            var row = recommendation.Rows[0];
            Require(row.Recommendation.StartsWith("B", StringComparison.Ordinal),
                "Exact-position revealed counter did not receive first priority.");
            Require(row.CounterStats.Count == 3 && row.CounterStats[0].ChampionId == 22,
                "Counter statistics lost alignment after exact-position ordering.");
            Require(row.IconReferences.Count == 3 && row.IconReferences[0].IndexOf("22.png", StringComparison.Ordinal) >= 0,
                "Counter icon references lost alignment after exact-position ordering.");
            Require(row.Evidence.StartsWith("TOP · ", StringComparison.Ordinal),
                "Exact-position counter evidence does not expose the verified position.");
            Require(row.Evidence.IndexOf("win 60.0%", StringComparison.Ordinal) >= 0 &&
                    row.Evidence.IndexOf("200 games", StringComparison.Ordinal) >= 0,
                "Exact-position counter evidence lost source win/sample evidence.");
        }

        private static void ValidateRevealedCounterWithoutPositionKeepsSourceEvidenceShape()
        {
            var recommendation = new LeagueBuildRecommendation();
            recommendation.Rows.Add(new LeagueBuildAdvisorRow
            {
                Category = "counters",
                Recommendation = "A · B",
                Evidence = "source",
                CounterStats = new[]
                {
                    new LeagueBuildCounterStat { ChampionId = 11, Games = 50, Wins = 20, HasWins = true },
                    new LeagueBuildCounterStat { ChampionId = 22, Games = 80, Wins = 44, HasWins = true }
                }
            });
            var build = new LeagueBuildAdvisorSnapshot { Recommendation = recommendation };
            var players = new List<LeagueLivePlayerRow>
            {
                new LeagueLivePlayerRow { Side = "ally", IsLocalPlayer = true, Position = null },
                new LeagueLivePlayerRow { Side = "enemy", Position = "TOP", ChampionId = 22 }
            };

            LeagueRuntimeCompanionSnapshot.PrioritizeRevealedEnemyCountersForSmokeTest(build, players);

            var row = recommendation.Rows[0];
            Require(row.Recommendation.StartsWith("B", StringComparison.Ordinal),
                "Revealed source counter did not receive priority when position evidence was absent.");
            Require(!row.Evidence.StartsWith("TOP · ", StringComparison.Ordinal),
                "Counter evidence invented exact-position context without local position evidence.");
            Require(row.Evidence.IndexOf("win 55.0%", StringComparison.Ordinal) >= 0 &&
                    row.Evidence.IndexOf("80 games", StringComparison.Ordinal) >= 0,
                "Revealed counter evidence lost source statistics.");
        }

        private static void ValidateNetworkFreeSpellPresentation()
        {
            Require(string.Equals(
                    LeagueRuntimeCompanionSpellPresentation.Format(4, 14),
                    "Flash/Ignite",
                    StringComparison.Ordinal),
                "Known summoner-spell IDs did not resolve to compact fallback labels.");
            Require(string.Equals(
                    LeagueRuntimeCompanionSpellPresentation.Format(4, 9999),
                    "Flash/S9999",
                    StringComparison.Ordinal),
                "Unknown summoner-spell IDs were guessed instead of remaining explicit.");
            Require(string.Equals(
                    LeagueRuntimeCompanionSpellPresentation.Format(0, 4),
                    "Flash",
                    StringComparison.Ordinal),
                "Missing summoner-spell slots should be omitted from compact presentation.");

            var sourcePlayer = new LeagueLivePlayerRow
            {
                Side = "ally",
                CellId = 1,
                IsLocalPlayer = true,
                GameName = "Me",
                TagLine = "CN1",
                ChampionId = 58,
                Spell1Id = 4,
                Spell2Id = 14
            };
            var source = new LeagueRuntimeCompanionSnapshot
            {
                SessionAvailable = true,
                LocalChampionId = 58,
                Players = new[] { sourcePlayer }
            };
            var clone = source.Clone();

            Require(string.Equals(sourcePlayer.AccountName, "Me#CN1", StringComparison.Ordinal),
                "Runtime spell presentation leaked into the shared LeagueLive source row.");
            Require(clone.Players.Count == 1 &&
                    string.Equals(clone.Players[0].PresentationSuffix, "Flash/Ignite", StringComparison.Ordinal),
                "Runtime Companion clone did not project exposed spell IDs into a presentation suffix.");
            Require(string.Equals(clone.Players[0].AccountName, "Me#CN1 · Flash/Ignite", StringComparison.Ordinal),
                "Draft tooltip account presentation did not include the compact spell suffix.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}