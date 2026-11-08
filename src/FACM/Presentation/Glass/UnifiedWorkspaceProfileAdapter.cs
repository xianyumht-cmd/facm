using System;
using System.Linq;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceProfileAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly Action _scheduleRefresh;
        private LeaguePersonalStatsModule _observer;

        internal UnifiedWorkspaceProfileAdapter(Func<bool> canPublish,
            Action<string> publish, Action scheduleRefresh)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _scheduleRefresh = scheduleRefresh ?? throw new ArgumentNullException(nameof(scheduleRefresh));
        }

        internal void Observe(LeaguePersonalStatsModule module)
        {
            if (ReferenceEquals(_observer, module)) return;
            Stop();
            _observer = module;
            if (module != null) module.StatsChanged += OnStatsChanged;
        }

        private void OnStatsChanged()
        {
            _scheduleRefresh();
        }

        internal void Sync(LeaguePersonalStatsModule module, bool localOnly)
        {
            if (!_canPublish()) return;
            Observe(module);
            if (module == null)
            {
                _publish("{\"type\":\"local-profile-unavailable\"}");
                return;
            }
            _publish(Serialize(module.GetSnapshot(), localOnly));
        }

        private static string Serialize(LeaguePersonalStatsViewSnapshot snapshot, bool localOnly)
        {
            return new JavaScriptSerializer().Serialize(new
            {
                type = "local-profile",
                localOnly,
                played = snapshot.PlayedAccounts,
                activeDays = snapshot.ActiveDays,
                streak = snapshot.CurrentStreakDays,
                recent7 = snapshot.Recent7ActiveDays,
                recent30 = snapshot.Recent30ActiveDays,
                newThisMonth = snapshot.NewAccountsThisMonth,
                firstSeen = snapshot.FirstSeenUtc.HasValue
                    ? snapshot.FirstSeenUtc.Value.ToLocalTime().ToString("yyyy-MM-dd") : null,
                lastSeen = snapshot.LastSeenUtc.HasValue
                    ? snapshot.LastSeenUtc.Value.ToLocalTime().ToString("yyyy-MM-dd") : null,
                accounts = snapshot.RecentAccounts == null ? new object[0] :
                    snapshot.RecentAccounts.Take(30).Select(item => new
                    {
                        name = item.DisplayName,
                        region = item.Region,
                        seen = item.SeenCount,
                        first = item.FirstSeenUtc.HasValue
                            ? item.FirstSeenUtc.Value.ToLocalTime().ToString("yyyy-MM-dd") : null,
                        last = item.LastSeenUtc.HasValue
                            ? item.LastSeenUtc.Value.ToLocalTime().ToString("yyyy-MM-dd") : null
                    }).ToArray()
            });
        }

        internal void Stop()
        {
            var prior = _observer;
            _observer = null;
            if (prior != null) prior.StatsChanged -= OnStatsChanged;
        }

        internal static void ValidateForSmokeTest()
        {
            var json = Serialize(new LeaguePersonalStatsViewSnapshot(), true);
            if (!json.Contains("\"type\":\"local-profile\"") ||
                !json.Contains("\"localOnly\":true"))
                throw new InvalidOperationException("Local profile JSON contract changed.");
            var first = 0;
            var second = 0;
            var hidden = new UnifiedWorkspaceProfileAdapter(() => false,
                delegate { first++; }, () => { });
            var shown = new UnifiedWorkspaceProfileAdapter(() => true,
                delegate { second++; }, () => { });
            hidden.Sync(null, true);
            shown.Sync(null, false);
            hidden.Stop();
            shown.Stop();
            if (first != 0 || second != 1)
                throw new InvalidOperationException("Profile adapters leaked state across workbenches.");
        }
    }
}
