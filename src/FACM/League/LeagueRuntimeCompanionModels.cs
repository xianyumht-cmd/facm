using System;
using System.Collections.Generic;
using FACM.Mayhem;

namespace FACM.League
{
    /// <summary>
    /// Immutable-enough presentation snapshot for the Runtime Companion. The UI receives projected
    /// state instead of owning League/Mayhem request orchestration. Collections are copied by the
    /// controller before publication so later refreshes cannot mutate an already rendered snapshot.
    /// </summary>
    internal sealed class LeagueRuntimeCompanionSnapshot
    {
        public bool SessionAvailable { get; set; }
        public bool BenchEnabled { get; set; }
        public int LocalChampionId { get; set; }
        public LeagueBenchSwapRoute SwapRoute { get; set; }
        public IReadOnlyList<int> BenchChampionIds { get; set; } = Array.Empty<int>();
        public bool GuideLoading { get; set; }
        public int GuideChampionId { get; set; }
        public MayhemChampionResult Guide { get; set; }
        public string GuideError { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public bool HasGuide
        {
            get { return Guide != null && string.IsNullOrWhiteSpace(GuideError); }
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
                GuideLoading = GuideLoading,
                GuideChampionId = GuideChampionId,
                Guide = Guide,
                GuideError = GuideError,
                UpdatedAtUtc = UpdatedAtUtc
            };
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
