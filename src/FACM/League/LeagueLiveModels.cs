using System;
using System.Collections.Generic;
using FACM.Performance;

namespace FACM.League
{
    internal enum LeagueBenchSwapRoute
    {
        Legacy,
        TeamBuilder
    }

    internal sealed class LeagueLiveSnapshot
    {
        public LeagueLiveSnapshot()
        {
            Players = new List<LeagueLivePlayerRow>();
            AllyBans = new List<int>();
            EnemyBans = new List<int>();
            BenchChampionIds = new List<int>();
            BenchSwapRoute = LeagueBenchSwapRoute.Legacy;
        }

        public bool Connected { get; set; }
        public string Phase { get; set; }
        public LeagueActivityLevel Activity { get; set; }
        public string BudgetName { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public long GameId { get; set; }
        public int QueueId { get; set; }
        public string QueueName { get; set; }
        public int MapId { get; set; }
        public string MapName { get; set; }
        public string GameMode { get; set; }

        public int LocalPlayerCellId { get; set; }
        public string TimerPhase { get; set; }
        public int TimerMillisecondsLeft { get; set; }
        public string LocalActionType { get; set; }
        public int LocalActionChampionId { get; set; }
        public bool BenchEnabled { get; set; }
        public LeagueBenchSwapRoute BenchSwapRoute { get; set; }

        public List<int> AllyBans { get; private set; }
        public List<int> EnemyBans { get; private set; }
        public List<int> BenchChampionIds { get; private set; }
        public List<LeagueLivePlayerRow> Players { get; private set; }
    }

    internal sealed class LeagueBenchQuickPickState
    {
        public LeagueBenchQuickPickState()
        {
            ChampionIds = new List<int>();
            SwapRoute = LeagueBenchSwapRoute.Legacy;
        }

        public bool SessionAvailable { get; set; }
        public bool BenchEnabled { get; set; }
        public int LocalPlayerCellId { get; set; }
        public int LocalChampionId { get; set; }
        public LeagueBenchSwapRoute SwapRoute { get; set; }
        public List<int> ChampionIds { get; private set; }
    }

    internal sealed class LeagueLivePlayerRow
    {
        public string Side { get; set; }
        public int CellId { get; set; }
        public bool IsLocalPlayer { get; set; }
        public string GameName { get; set; }
        public string TagLine { get; set; }
        public string DisplayName { get; set; }
        public string PuuId { get; set; }
        public long SummonerId { get; set; }
        public string Position { get; set; }
        public string Role { get; set; }
        public int ChampionId { get; set; }
        public int ChampionPickIntent { get; set; }
        public int Spell1Id { get; set; }
        public int Spell2Id { get; set; }

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

    internal static class LeagueLivePolling
    {
        public static TimeSpan ResolveDelay(LeagueActivityLevel activity, bool minimized)
        {
            if (minimized) return TimeSpan.FromSeconds(10);
            if (activity == LeagueActivityLevel.ChampSelect) return TimeSpan.FromSeconds(2);
            if (activity == LeagueActivityLevel.InGame) return TimeSpan.FromSeconds(10);
            if (activity == LeagueActivityLevel.Queueing) return TimeSpan.FromSeconds(5);
            if (activity == LeagueActivityLevel.Client) return TimeSpan.FromSeconds(5);
            return TimeSpan.FromSeconds(10);
        }
    }
}
