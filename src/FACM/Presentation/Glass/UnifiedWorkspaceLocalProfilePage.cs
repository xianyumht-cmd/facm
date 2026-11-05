using System;
using System.Linq;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // Local profile read is entirely from the existing PersonalStatsStore snapshot.
    // No GGman email account, CloudBase client or additional timer is required.
    internal sealed partial class UnifiedWorkspaceForm
    {
        private LeaguePersonalStatsModule _statsObserver;

        private void HookLocalStats()
        {
            var next = LeagueHubUiBridge.UnifiedPersonalStats;
            if (ReferenceEquals(_statsObserver, next)) return;
            UnhookLocalStats();
            _statsObserver = next;
            if (_statsObserver != null) _statsObserver.StatsChanged += HandleLocalStatsChanged;
        }

        private void UnhookLocalStats()
        {
            var prior = _statsObserver;
            _statsObserver = null;
            if (prior != null) prior.StatsChanged -= HandleLocalStatsChanged;
        }

        private void HandleLocalStatsChanged()
        {
            if (_closing || IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(SyncLocalProfile));
            }
            catch (InvalidOperationException) { }
        }

        private void OpenLocalProfilePage(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Profile);
            _content.Visible = false;
            _browser.Dock = System.Windows.Forms.DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            HookLocalStats();
            SyncLocalProfile();
        }

        private void SyncLocalProfile()
        {
            if (_navigation.Current != LeagueHubNavigation.Profile || !_ready || IsDisposed ||
                _closing || _browser.CoreWebView2 == null) return;
            HookLocalStats();
            var module = LeagueHubUiBridge.UnifiedPersonalStats;
            if (module == null)
            {
                _browser.CoreWebView2.PostWebMessageAsJson(
                    new JavaScriptSerializer().Serialize(new { type = "local-profile-unavailable" }));
                return;
            }
            var snapshot = module.GetSnapshot();
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "local-profile",
                localOnly = _settings.LocalOnlyMode,
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
            }));
        }
    }
}
