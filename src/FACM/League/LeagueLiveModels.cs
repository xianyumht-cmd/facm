using System;
using System.Collections.Generic;
using System.Linq;
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

        public bool SgpEnriched { get; set; }
        public long GameId { get; set; }
        public int QueueId { get; set; }
        public string QueueName { get; set; }
        public string QueueType { get; set; }
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
        public string AutoSelectGroup { get; set; }
        public string AutoSelectContext { get; set; }
        public LeagueBenchQuickPickState()
        {
            ChampionIds = new List<int>();
            AllyBans = new List<int>();
            EnemyBans = new List<int>();
            Players = new List<LeagueLivePlayerRow>();
            SwapRoute = LeagueBenchSwapRoute.Legacy;
        }

        public bool SessionAvailable { get; set; }
        public bool ShowQuitButton { get; set; }
        public bool BenchEnabled { get; set; }
        public bool AllowsSubsetChampionPicks { get; set; }
        public long GameId { get; set; }
        public string SelectionId { get; set; }
        internal LeagueClientSession ClientSession { get; set; }
        public int? LocalPickActionId { get; set; }
        public List<int> SubsetChampionIds { get; } = new List<int>();
        public List<int> PickableChampionIds { get; } = new List<int>();
        public bool PickableKnown { get; set; }
        public DateTime ObservedAtUtc { get; set; }
        public bool IsSubsetPhase
        {
            get { return AllowsSubsetChampionPicks && string.Equals(TimerPhase, "BAN_PICK", StringComparison.Ordinal); }
        }
        public bool IsInitialCardSelection { get { return IsSubsetPhase && LocalChampionId <= 0; } }

        public IEnumerable<int> DisplayChampionIds
        {
            get { return (IsSubsetPhase ? SubsetChampionIds.Concat(ChampionIds) : ChampionIds)
                .Where(id => id > 0 && id != LocalChampionId).Distinct(); }
        }

        public bool CanChoose(int championId)
        {
            if (!SessionAvailable || !BenchEnabled || championId <= 0 || championId == LocalChampionId) return false;
            if (IsSubsetPhase)
                return PickableKnown && PickableChampionIds.Contains(championId) &&
                    SubsetChampionIds.Contains(championId) && (!IsInitialCardSelection || LocalPickActionId.HasValue);
            return string.Equals(TimerPhase, "FINALIZATION", StringComparison.Ordinal) &&
                ChampionIds.Contains(championId) &&
                (PickableKnown ? PickableChampionIds.Contains(championId) : !AllowsSubsetChampionPicks);
        }

        public bool SameDraft(LeagueBenchQuickPickState other)
        {
            if (other == null || !SessionAvailable || !other.SessionAvailable ||
                LocalPlayerCellId != other.LocalPlayerCellId || QueueId != other.QueueId) return false;
            if ((ClientSession != null || other.ClientSession != null) &&
                (ClientSession == null || other.ClientSession == null ||
                !LeagueEncounterReplayService.SessionMatches(ClientSession, other.ClientSession))) return false;
            if (GameId > 0 && other.GameId > 0 && GameId != other.GameId) return false;
            if (!string.IsNullOrEmpty(SelectionId) && !string.IsNullOrEmpty(other.SelectionId))
                return string.Equals(SelectionId, other.SelectionId, StringComparison.Ordinal);
            return GameId > 0 && GameId == other.GameId;
        }

        public int QueueId { get; set; }
        public string GameMode { get; set; }
        public int LocalPlayerCellId { get; set; }
        public int LocalChampionId { get; set; }
        public string TimerPhase { get; set; }
        public int TimerMillisecondsLeft { get; set; }
        public LeagueBenchSwapRoute SwapRoute { get; set; }
        public List<int> ChampionIds { get; private set; }
        public List<int> AllyBans { get; private set; }
        public List<int> EnemyBans { get; private set; }
        public List<LeagueLivePlayerRow> Players { get; private set; }
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

        // Optional presentation-only suffix used by defensively cloned consumers such as the
        // Runtime Companion. Source LeagueLive rows leave this empty, so account identity semantics
        // in the shared Live owner do not change.
        public string PresentationSuffix { get; set; }

        public string AccountName
        {
            get
            {
                string name;
                if (!string.IsNullOrWhiteSpace(GameName))
                    name = string.IsNullOrWhiteSpace(TagLine) ? GameName : GameName + "#" + TagLine;
                else
                    name = DisplayName;

                if (string.IsNullOrWhiteSpace(PresentationSuffix)) return name;
                if (!string.IsNullOrWhiteSpace(name)) return name + " · " + PresentationSuffix;

                // Do not let presentation-only spell text make a previously hidden/anonymous enemy
                // row visible by itself. The suffix may stand alone only when the champion is already
                // revealed by the client (or this is the signed-in local player).
                return ChampionId > 0 || IsLocalPlayer ? PresentationSuffix : name;
            }
        }
    }

    internal static class LeagueQueueModePolicy
    {
        internal const int BaseAramQueueId = 450;
        internal const int GlobalAramMayhemQueueId = 2400;
        internal const int TencentAramMayhemQueueId = 3270;

        public static bool IsAramMayhem(int queueId, string gameMode)
        {
            if (queueId == GlobalAramMayhemQueueId || queueId == TencentAramMayhemQueueId) return true;
            return string.Equals(gameMode, "KIWI", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(gameMode, "ARAM_MAYHEM", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsBaseAram(int queueId, string gameMode)
        {
            if (IsAramMayhem(queueId, gameMode)) return false;
            return queueId == BaseAramQueueId || string.Equals(gameMode, "ARAM", StringComparison.OrdinalIgnoreCase);
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
