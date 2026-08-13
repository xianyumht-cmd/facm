using System;

namespace FACM.Performance
{
    internal enum LeagueActivityLevel
    {
        None = 0,
        Client = 1,
        Queueing = 2,
        ChampSelect = 3,
        InGame = 4
    }

    internal sealed class PerformanceContext
    {
        public PerformanceContext(LeagueActivityLevel leagueActivity, bool uiVisible)
        {
            LeagueActivity = leagueActivity;
            UiVisible = uiVisible;
        }

        public LeagueActivityLevel LeagueActivity { get; private set; }
        public bool UiVisible { get; private set; }
    }

    internal sealed class PerformanceBudget
    {
        public PerformanceBudget(
            string name,
            int networkConcurrency,
            int imageDecodeConcurrency,
            int diskIoConcurrency,
            int backgroundCpuConcurrency,
            int matchHistoryPrefetchCount,
            TimeSpan nonCriticalPollInterval,
            bool allowBackgroundPrefetch,
            bool allowMaintenanceWork,
            bool allowVisualEnhancements)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Budget name is required.", nameof(name));
            if (networkConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(networkConcurrency));
            if (imageDecodeConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(imageDecodeConcurrency));
            if (diskIoConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(diskIoConcurrency));
            if (backgroundCpuConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(backgroundCpuConcurrency));
            if (matchHistoryPrefetchCount < 0) throw new ArgumentOutOfRangeException(nameof(matchHistoryPrefetchCount));
            if (nonCriticalPollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(nonCriticalPollInterval));

            Name = name;
            NetworkConcurrency = networkConcurrency;
            ImageDecodeConcurrency = imageDecodeConcurrency;
            DiskIoConcurrency = diskIoConcurrency;
            BackgroundCpuConcurrency = backgroundCpuConcurrency;
            MatchHistoryPrefetchCount = matchHistoryPrefetchCount;
            NonCriticalPollInterval = nonCriticalPollInterval;
            AllowBackgroundPrefetch = allowBackgroundPrefetch;
            AllowMaintenanceWork = allowMaintenanceWork;
            AllowVisualEnhancements = allowVisualEnhancements;
        }

        public string Name { get; private set; }
        public int NetworkConcurrency { get; private set; }
        public int ImageDecodeConcurrency { get; private set; }
        public int DiskIoConcurrency { get; private set; }
        public int BackgroundCpuConcurrency { get; private set; }
        public int MatchHistoryPrefetchCount { get; private set; }
        public TimeSpan NonCriticalPollInterval { get; private set; }
        public bool AllowBackgroundPrefetch { get; private set; }
        public bool AllowMaintenanceWork { get; private set; }
        public bool AllowVisualEnhancements { get; private set; }
    }

    internal static class PerformancePolicy
    {
        private static readonly PerformanceBudget Desktop = new PerformanceBudget(
            "desktop",
            4,
            2,
            2,
            2,
            20,
            TimeSpan.FromSeconds(15),
            true,
            true,
            true);

        private static readonly PerformanceBudget Client = new PerformanceBudget(
            "league-client",
            3,
            2,
            2,
            2,
            12,
            TimeSpan.FromSeconds(20),
            true,
            true,
            true);

        private static readonly PerformanceBudget Queueing = new PerformanceBudget(
            "queueing",
            2,
            1,
            1,
            1,
            4,
            TimeSpan.FromSeconds(30),
            false,
            false,
            false);

        private static readonly PerformanceBudget ChampSelect = new PerformanceBudget(
            "champ-select",
            2,
            1,
            1,
            1,
            0,
            TimeSpan.FromSeconds(45),
            false,
            false,
            false);

        private static readonly PerformanceBudget InGame = new PerformanceBudget(
            "in-game",
            1,
            1,
            1,
            1,
            0,
            TimeSpan.FromSeconds(60),
            false,
            false,
            false);

        private static readonly PerformanceBudget Background = new PerformanceBudget(
            "background",
            1,
            1,
            1,
            1,
            0,
            TimeSpan.FromSeconds(60),
            false,
            false,
            false);

        public static PerformanceBudget Resolve(PerformanceContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Game-sensitive states always win over window visibility. A minimized FACM must never
            // interpret "not visible" as permission to do more work while League is in a match.
            if (context.LeagueActivity == LeagueActivityLevel.InGame) return InGame;
            if (context.LeagueActivity == LeagueActivityLevel.ChampSelect) return ChampSelect;
            if (!context.UiVisible) return Background;
            if (context.LeagueActivity == LeagueActivityLevel.Queueing) return Queueing;
            if (context.LeagueActivity == LeagueActivityLevel.Client) return Client;
            return Desktop;
        }

        public static bool IsNoMoreAggressiveThan(PerformanceBudget candidate, PerformanceBudget ceiling)
        {
            if (candidate == null || ceiling == null) return false;
            return candidate.NetworkConcurrency <= ceiling.NetworkConcurrency &&
                   candidate.ImageDecodeConcurrency <= ceiling.ImageDecodeConcurrency &&
                   candidate.DiskIoConcurrency <= ceiling.DiskIoConcurrency &&
                   candidate.BackgroundCpuConcurrency <= ceiling.BackgroundCpuConcurrency &&
                   candidate.MatchHistoryPrefetchCount <= ceiling.MatchHistoryPrefetchCount &&
                   candidate.NonCriticalPollInterval >= ceiling.NonCriticalPollInterval &&
                   (!candidate.AllowBackgroundPrefetch || ceiling.AllowBackgroundPrefetch) &&
                   (!candidate.AllowMaintenanceWork || ceiling.AllowMaintenanceWork) &&
                   (!candidate.AllowVisualEnhancements || ceiling.AllowVisualEnhancements);
        }
    }

    internal sealed class PerformanceBudgetProvider
    {
        private readonly object _sync = new object();
        private LeagueActivityLevel _leagueActivity;
        private bool _uiVisible = true;
        private PerformanceBudget _current = PerformancePolicy.Resolve(new PerformanceContext(LeagueActivityLevel.None, true));

        public event Action<PerformanceBudget> BudgetChanged;

        public PerformanceBudget Current
        {
            get
            {
                lock (_sync) return _current;
            }
        }

        public void UpdateLeagueActivity(LeagueActivityLevel activity)
        {
            Update(activity, null);
        }

        public void UpdateUiVisibility(bool visible)
        {
            Update(null, visible);
        }

        private void Update(LeagueActivityLevel? activity, bool? visible)
        {
            PerformanceBudget changed = null;
            lock (_sync)
            {
                if (activity.HasValue) _leagueActivity = activity.Value;
                if (visible.HasValue) _uiVisible = visible.Value;

                var next = PerformancePolicy.Resolve(new PerformanceContext(_leagueActivity, _uiVisible));
                if (!ReferenceEquals(next, _current))
                {
                    _current = next;
                    changed = next;
                }
            }

            var handler = BudgetChanged;
            if (changed != null && handler != null) handler(changed);
        }
    }
}
