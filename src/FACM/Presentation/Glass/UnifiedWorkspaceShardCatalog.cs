using System;
using System.Collections.Generic;
using FACM.League;

namespace FACM.Presentation.Glass
{
    // A workspace shard owns its route, commands and activation. Business state stays
    // with the existing FacmHost modules; the WebView2 shell only dispatches.
    internal interface IUnifiedWorkspaceShard
    {
        string Id { get; }
        string Route { get; }
        bool Recognizes(string message);
        void Open(UnifiedWorkspaceForm workspace, bool pushHistory);
        void Ready(UnifiedWorkspaceForm workspace);
        void Handle(UnifiedWorkspaceForm workspace, string message);
        // Called for every route change and on shell shutdown. A shard decides
        // whether its in-flight work is still valid for the destination route.
        void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute);
    }

    internal static class UnifiedWorkspaceShardCatalog
    {
        private static readonly Dictionary<string, IUnifiedWorkspaceShard> Routes = CreateRoutes();

        // To add a UI feature, implement IUnifiedWorkspaceShard and register it here.
        // Neither the central navigation switch nor the message dispatcher changes.
        private static Dictionary<string, IUnifiedWorkspaceShard> CreateRoutes()
        {
            var routes = new Dictionary<string, IUnifiedWorkspaceShard>(StringComparer.Ordinal);
            Register(routes, new QuickMessagesShard());
            Register(routes, new AdvancedToolsShard());
            Register(routes, new PlayerHistoryShard());
            Register(routes, new AdvancedHistoryShard());
            Register(routes, new MayhemGuideShard());
            Register(routes, new RecommendationShard());
            Register(routes, new LiveMatchShard());
            Register(routes, new LiveBenchShard());
            Register(routes, new DashboardShard());
            Register(routes, new AdvancedDashboardShard());
            Register(routes, new PresenceShard());
            Register(routes, new ProfileCustomizationShard());
            Register(routes, new LocalProfileShard());
            Register(routes, new RepairShard());
            Register(routes, new ClientStatusShard());
            Register(routes, new PostgameSettingsShard());
            Register(routes, new AutoSelectionShard());
            Register(routes, new MatchmakingSettingsShard());
            Register(routes, new EfficiencyShard());
            Register(routes, new AppearanceShard());
            Register(routes, new CleanupShard());
            Register(routes, new CloudSyncShard());
            Register(routes, new UpdatesShard());
            return routes;
        }

        private static void Register(IDictionary<string, IUnifiedWorkspaceShard> routes,
            IUnifiedWorkspaceShard shard)
        {
            if (shard == null || string.IsNullOrWhiteSpace(shard.Id) ||
                string.IsNullOrWhiteSpace(shard.Route) || shard.Route.IndexOf(':') >= 0 ||
                routes.ContainsKey(shard.Route))
                throw new InvalidOperationException("Invalid or duplicate workspace shard registration.");
            foreach (var existing in routes.Values)
                if (string.Equals(existing.Id, shard.Id, StringComparison.Ordinal))
                    throw new InvalidOperationException("Duplicate workspace shard ID: " + shard.Id);
            routes.Add(shard.Route, shard);
        }

        internal static bool HasRoute(string route)
        {
            return route != null && Routes.ContainsKey(route);
        }

        internal static bool TryOpen(UnifiedWorkspaceForm workspace, string route, bool pushHistory)
        {
            IUnifiedWorkspaceShard shard;
            if (!HasRoute(route) || !Routes.TryGetValue(route, out shard)) return false;
            shard.Open(workspace, pushHistory);
            return true;
        }

        internal static void Ready(UnifiedWorkspaceForm workspace, string route)
        {
            IUnifiedWorkspaceShard shard;
            if (HasRoute(route) && Routes.TryGetValue(route, out shard)) shard.Ready(workspace);
        }

        internal static bool TryHandle(UnifiedWorkspaceForm workspace, string activeRoute, string message)
        {
            IUnifiedWorkspaceShard shard;
            if (!HasRoute(activeRoute) || !Routes.TryGetValue(activeRoute, out shard) ||
                !shard.Recognizes(message)) return false;
            shard.Handle(workspace, message);
            return true;
        }

        internal static void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        {
            foreach (var shard in Routes.Values) shard.CancelOutside(workspace, nextRoute);
        }

        internal static void ValidateForSmokeTest()
        {
            if (!HasRoute("quick-messages") || !HasRoute("efficiency-advanced") ||
                !HasRoute(LeagueHubNavigation.Player) || !HasRoute("player-advanced") ||
                !HasRoute(LeagueHubNavigation.Mayhem) ||
                !HasRoute(LeagueHubNavigation.Recommendation) ||
                !HasRoute(LeagueHubNavigation.Live) || !HasRoute("live-advanced") ||
                !HasRoute(LeagueHubNavigation.Dashboard) || !HasRoute("dashboard-advanced") ||
                !HasRoute(LeagueHubNavigation.Presence) || !HasRoute("presence-advanced") ||
                !HasRoute(LeagueHubNavigation.Profile) || !HasRoute(LeagueHubNavigation.Repair) ||
                !HasRoute(LeagueHubNavigation.Efficiency) || !HasRoute("client-status") ||
                !HasRoute("postgame-settings") || !HasRoute("auto-selection") ||
                !HasRoute("matchmaking-settings") || !HasRoute("appearance") ||
                !HasRoute("cleanup") || !HasRoute("cloud-sync") || !HasRoute("updates") ||
                HasRoute("unknown") || HasRoute(null) ||
                !Routes["quick-messages"].Recognizes("quick-message-refresh") ||
                !Routes["quick-messages"].Recognizes("quick-message-save:{}") ||
                Routes["quick-messages"].Recognizes("advanced-save:{}") ||
                !Routes["efficiency-advanced"].Recognizes("advanced-refresh") ||
                !Routes["efficiency-advanced"].Recognizes("advanced-save:{}") ||
                Routes["efficiency-advanced"].Recognizes("quick-message-send:0") ||
                !Routes[LeagueHubNavigation.Player].Recognizes("history-more") ||
                Routes[LeagueHubNavigation.Player].Recognizes("history-advanced-refresh") ||
                !Routes["player-advanced"].Recognizes("history-region-search:{}") ||
                Routes["player-advanced"].Recognizes("history-more") ||
                !Routes[LeagueHubNavigation.Mayhem].Recognizes("mayhem-cancel") ||
                Routes[LeagueHubNavigation.Mayhem].Recognizes("recommendation-refresh") ||
                !Routes[LeagueHubNavigation.Recommendation].Recognizes("recommendation-apply:{}") ||
                Routes[LeagueHubNavigation.Recommendation].Recognizes("mayhem-query:test") ||
                !Routes[LeagueHubNavigation.Live].Recognizes("live-sgp") ||
                Routes[LeagueHubNavigation.Live].Recognizes("bench-refresh") ||
                !Routes["live-advanced"].Recognizes("bench-swap:1") ||
                Routes["live-advanced"].Recognizes("live-refresh") ||
                TryHandle(null, LeagueHubNavigation.Player, "bench-refresh") ||
                TryHandle(null, "player-advanced", "history-more") ||
                TryHandle(null, LeagueHubNavigation.Live, "history-refresh") ||
                TryHandle(null, "unknown", "live-refresh") ||
                !Routes["dashboard-advanced"].Recognizes("dashboard-respawn-refresh") ||
                Routes[LeagueHubNavigation.Dashboard].Recognizes("dashboard-respawn-refresh") ||
                !Routes["presence-advanced"].Recognizes("profile-customization-skin:1") ||
                Routes[LeagueHubNavigation.Presence].Recognizes("profile-customization-skin:1") ||
                !Routes["appearance"].Recognizes("theme:default") ||
                Routes["updates"].Recognizes("cleanup-confirm:token") ||
                Routes["cleanup"].Recognizes("updates-install") ||
                !Routes["updates"].Recognizes("updates-stop") ||
                !Routes["cleanup"].Recognizes("cleanup-confirm:token") ||
                TryHandle(null, "updates", "cloud-publish") ||
                TryHandle(null, "cleanup", "updates-install"))
                throw new InvalidOperationException("Workspace shard routing or command isolation changed.");
            var duplicate = new Dictionary<string, IUnifiedWorkspaceShard>(StringComparer.Ordinal);
            Register(duplicate, new QuickMessagesShard());
            try
            {
                Register(duplicate, new QuickMessagesShard());
                throw new InvalidOperationException("Duplicate workspace route registration was accepted.");
            }
            catch (InvalidOperationException exception)
            {
                if (!exception.Message.StartsWith("Invalid or duplicate workspace shard registration.",
                    StringComparison.Ordinal)) throw;
            }
        }
    }

    internal sealed class QuickMessagesShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.quick-messages"; } }
        public string Route { get { return "quick-messages"; } }

        public bool Recognizes(string message)
        {
            return message == "quick-message-refresh" ||
                (message != null && (message.StartsWith("quick-message-save:", StringComparison.Ordinal) ||
                                     message.StartsWith("quick-message-send:", StringComparison.Ordinal)));
        }

        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        {
            workspace.OpenQuickMessagePage(pushHistory);
        }

        public void Ready(UnifiedWorkspaceForm workspace)
        {
            workspace.SyncQuickMessageSettings();
        }

        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message == "quick-message-refresh") workspace.SyncQuickMessageSettings();
            else if (message.StartsWith("quick-message-save:", StringComparison.Ordinal))
                workspace.SaveQuickMessageSettings(message.Substring("quick-message-save:".Length));
            else if (message.StartsWith("quick-message-send:", StringComparison.Ordinal))
            {
                int index;
                if (int.TryParse(message.Substring("quick-message-send:".Length), out index))
                    workspace.SendQuickMessage(index);
            }
        }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        {
            if (nextRoute != Route) workspace.CancelQuickMessageSend();
        }
    }

    internal sealed class AdvancedToolsShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.advanced-tools"; } }
        public string Route { get { return "efficiency-advanced"; } }

        public bool Recognizes(string message)
        {
            return message == "advanced-refresh" ||
                (message != null && message.StartsWith("advanced-save:", StringComparison.Ordinal));
        }

        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        {
            workspace.OpenAdvancedTools(pushHistory);
        }

        public void Ready(UnifiedWorkspaceForm workspace)
        {
            workspace.SyncAdvancedTools();
        }

        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message == "advanced-refresh") workspace.SyncAdvancedTools();
            else if (message.StartsWith("advanced-save:", StringComparison.Ordinal))
                workspace.ApplyAdvancedTools(message.Substring("advanced-save:".Length));
        }

        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute) { }
    }
}
