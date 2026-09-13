using System;
using System.Collections.Generic;
using FACM.Mayhem;

namespace FACM.League
{
    internal enum LeagueRuntimeCompanionApplyTarget
    {
        Runes,
        SummonerSpells
    }

    internal sealed class LeagueRuntimeCompanionApplyPreparation
    {
        public LeagueRuntimeCompanionApplyTarget Target { get; set; }
        public LeagueBuildApplyPlan Plan { get; set; }
        public LeagueBuildAdvisorSnapshot SourceSnapshot { get; set; }

        public bool IsUsable
        {
            get
            {
                if (Plan == null || SourceSnapshot == null) return false;
                return Target == LeagueRuntimeCompanionApplyTarget.Runes ? Plan.HasRunes : Plan.HasSpells;
            }
        }
    }

    internal sealed class LeagueRuntimeCompanionRecentChampion
    {
        public string Status { get; set; }
        public int ChampionId { get; set; }
        public int SampleMatches { get; set; }
        public int ResolvedMatches { get; set; }
        public int ChampionGames { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public double AverageKills { get; set; }
        public double AverageDeaths { get; set; }
        public double AverageAssists { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public bool HasStats
        {
            get
            {
                return ChampionGames > 0 && string.Equals(Status, "ready", StringComparison.OrdinalIgnoreCase);
            }
        }

        public LeagueRuntimeCompanionRecentChampion Clone()
        {
            return new LeagueRuntimeCompanionRecentChampion
            {
                Status = Status,
                ChampionId = ChampionId,
                SampleMatches = SampleMatches,
                ResolvedMatches = ResolvedMatches,
                ChampionGames = ChampionGames,
                Wins = Wins,
                Losses = Losses,
                AverageKills = AverageKills,
                AverageDeaths = AverageDeaths,
                AverageAssists = AverageAssists,
                UpdatedAtUtc = UpdatedAtUtc
            };
        }
    }

    /// <summary>
    /// Pure local-player projection used by Runtime Companion. It never looks up another player's
    /// history and ignores unresolved participant rows for champion performance statistics.
    /// </summary>
    internal static class LeagueRuntimeCompanionRecentChampionProjection
    {
        public static LeagueRuntimeCompanionRecentChampion Project(LeaguePlayerMatchPage page, int championId)
        {
            if (championId <= 0) return null;
            if (page == null)
            {
                return new LeagueRuntimeCompanionRecentChampion
                {
                    Status = "unavailable",
                    ChampionId = championId,
                    UpdatedAtUtc = DateTime.UtcNow
                };
            }

            var result = new LeagueRuntimeCompanionRecentChampion
            {
                Status = "empty",
                ChampionId = championId,
                SampleMatches = page.Matches == null ? 0 : page.Matches.Count,
                UpdatedAtUtc = DateTime.UtcNow
            };
            if (page.Matches == null || page.Matches.Count == 0) return result;

            var kills = 0;
            var deaths = 0;
            var assists = 0;
            foreach (var match in page.Matches)
            {
                if (match == null || !match.ParticipantResolved) continue;
                result.ResolvedMatches++;
                if (match.ChampionId != championId) continue;
                result.ChampionGames++;
                if (match.Win) result.Wins++;
                kills += match.Kills;
                deaths += match.Deaths;
                assists += match.Assists;
            }

            if (result.ChampionGames <= 0) return result;
            result.Losses = Math.Max(0, result.ChampionGames - result.Wins);
            result.AverageKills = (double)kills / result.ChampionGames;
            result.AverageDeaths = (double)deaths / result.ChampionGames;
            result.AverageAssists = (double)assists / result.ChampionGames;
            result.Status = "ready";
            return result;
        }
    }

    /// <summary>
    /// Presentation snapshot for the Runtime Companion. The UI receives projected state instead of
    /// owning League/OP.GG/Mayhem request orchestration. Mutable collections are copied before
    /// publication so a later refresh cannot rewrite an already-rendered snapshot.
    /// </summary>
    internal sealed class LeagueRuntimeCompanionSnapshot
    {
        public bool SessionAvailable { get; set; }
        public bool BenchEnabled { get; set; }
        public int LocalChampionId { get; set; }
        public LeagueBenchSwapRoute SwapRoute { get; set; }
        public IReadOnlyList<int> BenchChampionIds { get; set; } = Array.Empty<int>();
        public string TimerPhase { get; set; }
        public int TimerMillisecondsLeft { get; set; }
        public IReadOnlyList<int> AllyBans { get; set; } = Array.Empty<int>();
        public IReadOnlyList<int> EnemyBans { get; set; } = Array.Empty<int>();
        public IReadOnlyList<LeagueLivePlayerRow> Players { get; set; } = Array.Empty<LeagueLivePlayerRow>();
        public LeagueRuntimeCompanionRecentChampion RecentChampion { get; set; }

        public bool BuildLoading { get; set; }
        public string BuildError { get; set; }
        public LeagueBuildAdvisorSnapshot Build { get; set; }

        public bool GuideLoading { get; set; }
        public int GuideChampionId { get; set; }
        public MayhemChampionResult Guide { get; set; }
        public string GuideError { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public bool HasBuild
        {
            get
            {
                return Build != null &&
                       Build.Connected &&
                       Build.Activity == Performance.LeagueActivityLevel.ChampSelect &&
                       Build.ChampionId > 0 &&
                       Build.Recommendation != null &&
                       string.Equals(Build.Status, "ready", StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool HasGuide
        {
            get { return Guide != null && string.IsNullOrWhiteSpace(GuideError); }
        }

        public bool HasVisibleChampSelectContext
        {
            get
            {
                if (SessionAvailable) return true;
                return Build != null && Build.Connected && Build.Activity == Performance.LeagueActivityLevel.ChampSelect;
            }
        }

        public LeagueRuntimeCompanionSnapshot Clone()
        {
            return new LeagueRuntimeCompanionSnapshot
            {
                SessionAvailable = SessionAvailable,
                BenchEnabled = BenchEnabled,
                LocalChampionId = LocalChampionId,
                SwapRoute = SwapRoute,
                BenchChampionIds = BenchChampionIds == null
                    ? Array.Empty<int>()
                    : new List<int>(BenchChampionIds).AsReadOnly(),
                TimerPhase = TimerPhase,
                TimerMillisecondsLeft = TimerMillisecondsLeft,
                AllyBans = AllyBans == null ? Array.Empty<int>() : new List<int>(AllyBans).AsReadOnly(),
                EnemyBans = EnemyBans == null ? Array.Empty<int>() : new List<int>(EnemyBans).AsReadOnly(),
                Players = ClonePlayers(Players),
                RecentChampion = RecentChampion == null ? null : RecentChampion.Clone(),
                BuildLoading = BuildLoading,
                BuildError = BuildError,
                Build = CloneBuild(Build),
                GuideLoading = GuideLoading,
                GuideChampionId = GuideChampionId,
                Guide = Guide,
                GuideError = GuideError,
                UpdatedAtUtc = UpdatedAtUtc
            };
        }

        private static IReadOnlyList<LeagueLivePlayerRow> ClonePlayers(IReadOnlyList<LeagueLivePlayerRow> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<LeagueLivePlayerRow>();
            var rows = new List<LeagueLivePlayerRow>(source.Count);
            foreach (var row in source)
            {
                if (row == null) continue;
                rows.Add(new LeagueLivePlayerRow
                {
                    Side = row.Side,
                    CellId = row.CellId,
                    IsLocalPlayer = row.IsLocalPlayer,
                    GameName = row.GameName,
                    TagLine = row.TagLine,
                    DisplayName = row.DisplayName,
                    PuuId = row.PuuId,
                    SummonerId = row.SummonerId,
                    Position = row.Position,
                    Role = row.Role,
                    ChampionId = row.ChampionId,
                    ChampionPickIntent = row.ChampionPickIntent,
                    Spell1Id = row.Spell1Id,
                    Spell2Id = row.Spell2Id
                });
            }
            return rows.AsReadOnly();
        }

        internal static LeagueBuildAdvisorSnapshot CloneBuild(LeagueBuildAdvisorSnapshot source)
        {
            if (source == null) return null;
            return new LeagueBuildAdvisorSnapshot
            {
                Connected = source.Connected,
                Phase = source.Phase,
                Activity = source.Activity,
                BudgetName = source.BudgetName,
                QueueId = source.QueueId,
                ChampionId = source.ChampionId,
                ChampionName = source.ChampionName,
                Mode = source.Mode,
                Position = source.Position,
                Source = source.Source,
                Version = source.Version,
                Status = source.Status,
                FromCache = source.FromCache,
                UpdatedAtUtc = source.UpdatedAtUtc,
                Recommendation = CloneRecommendation(source.Recommendation)
            };
        }

        private static LeagueBuildRecommendation CloneRecommendation(LeagueBuildRecommendation source)
        {
            if (source == null) return null;
            var clone = new LeagueBuildRecommendation
            {
                Tier = source.Tier,
                Rank = source.Rank,
                WinRate = source.WinRate,
                PickRate = source.PickRate,
                BanRate = source.BanRate
            };
            foreach (var row in source.Rows)
            {
                if (row == null) continue;
                clone.Rows.Add(new LeagueBuildAdvisorRow
                {
                    Category = row.Category,
                    Recommendation = row.Recommendation,
                    Evidence = row.Evidence,
                    IconReferences = row.IconReferences == null
                        ? Array.Empty<string>()
                        : new List<string>(row.IconReferences).AsReadOnly()
                });
            }
            return clone;
        }
    }

    internal sealed class LeagueRuntimeCompanionUpdateEventArgs : EventArgs
    {
        public LeagueRuntimeCompanionUpdateEventArgs(LeagueRuntimeCompanionSnapshot snapshot)
        {
            Snapshot = snapshot == null ? null : snapshot.Clone();
        }

        public LeagueRuntimeCompanionSnapshot Snapshot { get; private set; }
    }
}
