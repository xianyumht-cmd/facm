using System;
using FACM.Performance;

namespace FACM.League
{
    internal enum LeagueShellContextAction
    {
        Home,
        Dashboard,
        Efficiency,
        Live
    }

    /// <summary>
    /// Pure navigation policy for the desktop entry. It consumes the already-owned dashboard state;
    /// it never polls LCU and never performs writes. This keeps shell navigation contextual without
    /// creating a second Gameflow owner.
    /// </summary>
    internal static class LeagueShellContextRouter
    {
        public static LeagueShellContextAction Resolve(LeagueDashboardPhaseState state)
        {
            if (state == null || (!state.Connected && !state.ClientProcessDetected && !state.GameProcessDetected))
                return LeagueShellContextAction.Home;

            if (state.Activity == LeagueActivityLevel.ChampSelect)
                return LeagueShellContextAction.Live;

            if (state.Activity == LeagueActivityLevel.InGame)
                return LeagueShellContextAction.Live;

            if (state.Activity == LeagueActivityLevel.Queueing)
                return LeagueShellContextAction.Efficiency;

            var phase = (state.Phase ?? string.Empty).Trim();
            if (IsPhase(phase, "Lobby") ||
                IsPhase(phase, "Matchmaking") ||
                IsPhase(phase, "ReadyCheck") ||
                IsPhase(phase, "EndOfGame") ||
                IsPhase(phase, "PreEndOfGame") ||
                IsPhase(phase, "WaitingForStats") ||
                IsPhase(phase, "TerminatedInError"))
                return LeagueShellContextAction.Efficiency;

            return LeagueShellContextAction.Dashboard;
        }

        public static string ResolveHubView(LeagueShellContextAction action)
        {
            if (action == LeagueShellContextAction.Live) return LeagueHubNavigation.Live;
            if (action == LeagueShellContextAction.Efficiency) return LeagueHubNavigation.Efficiency;
            if (action == LeagueShellContextAction.Dashboard) return LeagueHubNavigation.Dashboard;
            return string.Empty;
        }

        internal static void ValidateForSmokeTest()
        {
            Require(Resolve(null) == LeagueShellContextAction.Home, "Null Gameflow must keep the generic FACM home entry.");
            Require(Resolve(new LeagueDashboardPhaseState()) == LeagueShellContextAction.Home, "No League presence must keep the generic FACM home entry.");
            Require(Resolve(new LeagueDashboardPhaseState { ClientProcessDetected = true, Activity = LeagueActivityLevel.Client }) == LeagueShellContextAction.Dashboard,
                "Detected League client must route the desktop entry to current status.");
            Require(Resolve(new LeagueDashboardPhaseState { Connected = true, Phase = "Lobby", Activity = LeagueActivityLevel.Client }) == LeagueShellContextAction.Efficiency,
                "Lobby must route the desktop entry to next-game automation.");
            Require(Resolve(new LeagueDashboardPhaseState { Connected = true, Phase = "Matchmaking", Activity = LeagueActivityLevel.Queueing }) == LeagueShellContextAction.Efficiency,
                "Matchmaking must route the desktop entry to next-game automation.");
            Require(Resolve(new LeagueDashboardPhaseState { Connected = true, Phase = "ReadyCheck", Activity = LeagueActivityLevel.Queueing }) == LeagueShellContextAction.Efficiency,
                "ReadyCheck must route the desktop entry to next-game automation.");
            Require(Resolve(new LeagueDashboardPhaseState { Connected = true, Phase = "ChampSelect", Activity = LeagueActivityLevel.ChampSelect }) == LeagueShellContextAction.Live,
                "ChampSelect must route the desktop entry to the live assistant.");
            Require(Resolve(new LeagueDashboardPhaseState { Connected = true, Phase = "EndOfGame", Activity = LeagueActivityLevel.Client }) == LeagueShellContextAction.Efficiency,
                "Post-game must route the desktop entry to next-game automation.");
            Require(Resolve(new LeagueDashboardPhaseState { GameProcessDetected = true, Activity = LeagueActivityLevel.InGame }) == LeagueShellContextAction.Live,
                "Game-process fallback must retain the live-context route.");
            Require(string.Equals(ResolveHubView(LeagueShellContextAction.Live), LeagueHubNavigation.Live, StringComparison.Ordinal),
                "Live shell action lost its Hub route.");
            Require(string.Equals(ResolveHubView(LeagueShellContextAction.Efficiency), LeagueHubNavigation.Efficiency, StringComparison.Ordinal),
                "Efficiency shell action lost its Hub route.");
            Require(string.IsNullOrEmpty(ResolveHubView(LeagueShellContextAction.Home)),
                "Generic home action must not claim a League Hub view.");
        }

        private static bool IsPhase(string actual, string expected)
        {
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
