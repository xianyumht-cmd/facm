using System;
using FACM.Performance;

namespace FACM.League
{
    internal class LeagueDashboardPhaseState
    {
        public bool Connected { get; set; }
        public string Phase { get; set; }
        public LeagueActivityLevel Activity { get; set; }
        public string BudgetName { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    internal sealed class LeagueDashboardSnapshot : LeagueDashboardPhaseState
    {
        public string GameName { get; set; }
        public string TagLine { get; set; }
        public string DisplayName { get; set; }
        public int SummonerLevel { get; set; }
        public int ProfileIconId { get; set; }
        public string PlatformId { get; set; }
        public string PlatformName { get; set; }

        public string AccountName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(GameName))
                    return string.IsNullOrWhiteSpace(TagLine) ? GameName : GameName + "#" + TagLine;
                return DisplayName;
            }
        }
    }

    internal static class LeagueGameflowActivityMapper
    {
        public static LeagueActivityLevel Map(string phase, bool connected)
        {
            if (!connected) return LeagueActivityLevel.None;
            if (string.Equals(phase, "Matchmaking", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(phase, "ReadyCheck", StringComparison.OrdinalIgnoreCase))
                return LeagueActivityLevel.Queueing;
            if (string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
                return LeagueActivityLevel.ChampSelect;
            if (string.Equals(phase, "InProgress", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(phase, "WatchInProgress", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(phase, "Reconnect", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(phase, "GameStart", StringComparison.OrdinalIgnoreCase))
                return LeagueActivityLevel.InGame;
            return LeagueActivityLevel.Client;
        }
    }
}
