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
