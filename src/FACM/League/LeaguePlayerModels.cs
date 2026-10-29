using System;
using System.Collections.Generic;

namespace FACM.League
{
    internal enum LeaguePlayerHistorySource
    {
        Automatic,
        Lcu,
        Sgp
    }

    internal sealed class LeaguePlayerProfile
    {
        public string PuuId { get; set; }
        public long SummonerId { get; set; }
        public long AccountId { get; set; }
        public string GameName { get; set; }
        public string TagLine { get; set; }
        public string DisplayName { get; set; }
        public int SummonerLevel { get; set; }
        public int ProfileIconId { get; set; }

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

    internal sealed class LeaguePlayerMatchSummary
    {
        public LeaguePlayerHistorySource PreferredSource { get; set; }
        public LeaguePlayerHistorySource DataSource { get; set; }
        public string SgpServerId { get; set; }
        public long GameId { get; set; }
        public DateTime GameCreationLocal { get; set; }
        public int GameDurationSeconds { get; set; }
        public string GameMode { get; set; }
        public string GameType { get; set; }
        public long GameCreationEpochMs { get; set; }
        public int QueueId { get; set; }
        public int ChampionId { get; set; }
        public int MapId { get; set; }
        public int TeamId { get; set; }
        public int ParticipantId { get; set; }
        public int? PlayerSubteamId { get; set; }
        public int? SubteamPlacement { get; set; }
        public string PuuId { get; set; }
        public bool HasSmite { get; set; }
        public string ChampionName { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public int CreepScore { get; set; }
        public bool Win { get; set; }
        // Source-classified outcome: win, loss, remake or abort; null means unavailable.
        public string WinResult { get; set; }
        public bool? IsSurrender { get; set; }
        public bool ParticipantResolved { get; set; }
        public bool StatisticsAvailable { get; set; }
        public string Position { get; set; }
        public double? SoloKills { get; set; }
        public double? VisionScore { get; set; }
        public double? ChampionDamageShare { get; set; }
        public double? DamageTakenShare { get; set; }
        public double? GoldShare { get; set; }
        public double? KillParticipation { get; set; }
        public double? DamageGoldEfficiency { get; set; }
        public double? TotalPings { get; set; }
        public double? EnemyMissingPings { get; set; }
        public double? Level { get; set; }
        public double? GoldEarned { get; set; }
        public double? GoldSpent { get; set; }
        public double? DamageDealtToChampions { get; set; }
        public double? PhysicalDamageToChampions { get; set; }
        public double? MagicDamageToChampions { get; set; }
        public double? TrueDamageToChampions { get; set; }
        public double? DamageTaken { get; set; }
        public double? PhysicalDamageTaken { get; set; }
        public double? MagicDamageTaken { get; set; }
        public double? TrueDamageTaken { get; set; }
        public double? DamageToTowers { get; set; }
        public double? TotalHeal { get; set; }
        public double? TimeCCingOthers { get; set; }
        public double? DoubleKills { get; set; }
        public double? TripleKills { get; set; }
        public double? QuadraKills { get; set; }
        public double? PentaKills { get; set; }
        // Per-source-statistic baselines: team sum/max, then game sum/max. Missing team scope uses NaN.
        public Dictionary<string, double[]> MetricBaselines { get; set; }
        // Bounded, independently owned evidence for this match's participant-scope predicates.
        public LeaguePlayerMatchSummary[] ParticipantRoster { get; set; }
        public bool RosterIdentitiesComplete { get; set; }
        // Available only when the match includes verified participant identities and team IDs.
        public string[] SameTeamPuuids { get; set; }
        // Populated from the match's participant roster; null means the source supplied no reliable champion set.
        public int[] MatchChampionIds { get; set; }
        // Verified match-wide participant identities; absent/ambiguous PUUIDs are never inferred.
        public LeaguePlayerMatchIdentity[] MatchPlayers { get; set; }
        // Self participant slots. -1 means the upstream field was unavailable; 0 is a known empty slot.
        public int[] ItemIds { get; set; }
        public int? RoleBoundItemId { get; set; }
        public int[] SpellIds { get; set; }
        public int[] PerkIds { get; set; }
        public int[] PerkStyleIds { get; set; }
        public int[] AugmentIds { get; set; }

    }

    internal sealed class LeaguePlayerMatchIdentity
    {
        public string PuuId { get; set; }
        public string PlayerName { get; set; }
    }

    internal enum LeagueRiotPlayerSearchStatus
    {
        Found,
        InvalidQuery,
        Unavailable,
        NotFound,
        AccountChanged
    }

    internal sealed class LeagueRiotPlayerSearchResult
    {
        public LeagueRiotPlayerSearchStatus Status { get; set; }
        public LeaguePlayerMatchIdentity Player { get; set; }
    }

    internal sealed class LeagueRemotePlayerHistoryResult
    {
        public LeagueRiotPlayerSearchStatus Status { get; set; }
        public LeaguePlayerProfile Profile { get; set; }
        public LeaguePlayerMatchPage Page { get; set; }
    }

    internal sealed class LeagueMatchCollectionSettings
    {
        internal int CountPerIteration = 20;
        internal int ExpectedCount = 20;
        internal int MaxIterations = 20;

        internal bool IsValid
        {
            get { return CountPerIteration >= 1 && CountPerIteration <= 200 &&
                    ExpectedCount >= 1 && ExpectedCount <= 1000 &&
                    MaxIterations >= 1 && MaxIterations <= 100; }
        }
    }

    internal enum LeagueMatchCollectionStatus
    {
        Completed,
        Stopped,
        AccountChanged,
        Unavailable
    }

    internal sealed class LeagueMatchCollectionProgress
    {
        internal int Iterations;
        internal int ScannedCount;
        internal int CollectedCount;
    }

    internal sealed class LeagueMatchCollectionResult
    {
        internal LeagueMatchCollectionStatus Status;
        internal LeaguePlayerMatchPage Page = new LeaguePlayerMatchPage();
        internal int Iterations;
        internal int ScannedCount;
    }

    internal sealed class LeaguePlayerMatchParticipant
    {
        public int ParticipantId { get; set; }
        public int TeamId { get; set; }
        public int? PlayerSubteamId { get; set; }
        public int? SubteamPlacement { get; set; }
        public string PuuId { get; set; }
        public string PlayerName { get; set; }
        public int ChampionId { get; set; }
        public string ChampionName { get; set; }
        public int[] ItemIds { get; set; }
        public int? RoleBoundItemId { get; set; }
        public int[] SpellIds { get; set; }
        public int[] PerkIds { get; set; }
        public int PrimaryPerkStyleId { get; set; }
        public int SecondaryPerkStyleId { get; set; }
        public double?[][] PerkEndOfGameValues { get; set; }
        public int?[] StatPerkIds { get; set; }
        public int[] AugmentIds { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public int? DoubleKills { get; set; }
        public int? TripleKills { get; set; }
        public int? QuadraKills { get; set; }
        public int? PentaKills { get; set; }
        public int CreepScore { get; set; }
        public bool CreepScoreAvailable { get; set; }
        public int DamageToChampions { get; set; }
        public bool DamageAvailable { get; set; }
        public int? PhysicalDamageToChampions { get; set; }
        public int? MagicDamageToChampions { get; set; }
        public int? TrueDamageToChampions { get; set; }
        public int? TotalDamageTaken { get; set; }
        public int? PhysicalDamageTaken { get; set; }
        public int? MagicDamageTaken { get; set; }
        public int? TrueDamageTaken { get; set; }
        public int? TotalHeal { get; set; }
        public double? TimeCCingOthers { get; set; }
        public int? TotalDamageToTowers { get; set; }
        public int? TotalDamageShieldedOnTeammates { get; set; }
        public int? SoloKills { get; set; }
        public double? MaxCsAdvantageOnLaneOpponent { get; set; }
        public int? KnockEnemyIntoTeamAndKill { get; set; }
        public int? KillsNearEnemyTurret { get; set; }
        public int? KillsUnderOwnTurret { get; set; }
        public int GoldEarned { get; set; }
        public bool GoldAvailable { get; set; }
        public int VisionScore { get; set; }
        public bool Win { get; set; }
        public bool IsTarget { get; set; }
    }

    internal sealed class LeaguePlayerMatchBreakdown
    {
        public LeaguePlayerMatchSummary Match { get; set; }
        public List<LeaguePlayerMatchParticipant> Participants { get; set; }
        public List<LeagueMatchTeamScore> Teams { get; set; }
        public Dictionary<int, string> ChampionNames { get; set; }
        public LeagueBuildAdvisorCatalog BuildCatalog { get; set; }
    }

    internal sealed class LeagueMatchTeamBan
    {
        public int ChampionId { get; set; }
        public int? PickTurn { get; set; }
    }

    internal sealed class LeagueMatchTeamScore
    {
        public int TeamId { get; set; }
        public bool IsCherry { get; set; }
        public int? SubteamPlacement { get; set; }
        public int PlayerCount { get; set; }
        public bool? Win { get; set; }
        public long Kills { get; set; }
        public long Deaths { get; set; }
        public long Assists { get; set; }
        public long? DamageToChampions { get; set; }
        public long? GoldEarned { get; set; }
        public int? BaronKills { get; set; }
        public int? DragonKills { get; set; }
        public int? TowerKills { get; set; }
        public int? RiftHeraldKills { get; set; }
        public int? InhibitorKills { get; set; }
        public int? HordeKills { get; set; }
        public bool? FirstBaron { get; set; }
        public bool? FirstDragon { get; set; }
        public bool? FirstTower { get; set; }
        public bool? FirstInhibitor { get; set; }
        public List<LeagueMatchTeamBan> Bans { get; set; }
    }

    internal sealed class LeaguePlayerChampionStat
    {
        public int ChampionId { get; set; }
        public string ChampionName { get; set; }
        public int Games { get; set; }
        public int Wins { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }

        public double WinRate
        {
            get { return Games <= 0 ? 0d : Wins * 100d / Games; }
        }

        public double AverageKills
        {
            get { return Games <= 0 ? 0d : (double)Kills / Games; }
        }

        public double AverageDeaths
        {
            get { return Games <= 0 ? 0d : (double)Deaths / Games; }
        }

        public double AverageAssists
        {
            get { return Games <= 0 ? 0d : (double)Assists / Games; }
        }
    }

    internal sealed class LeaguePlayerMatchPage
    {
        internal LeagueClientSession ClientSession;
        public LeaguePlayerMatchPage()
        {
            Matches = new List<LeaguePlayerMatchSummary>();
        }

        public List<LeaguePlayerMatchSummary> Matches { get; private set; }
        public int StartIndex { get; set; }
        public int RequestedCount { get; set; }
        public int ReportedGameCount { get; set; }

        // LCU match-history gameCount describes the returned window on current Tencent snapshots,
        // not a reliable all-time total. A full requested window is therefore the only safe signal
        // that another explicit page may exist.
        public bool HasMore
        {
            get { return RequestedCount > 0 && Matches.Count >= RequestedCount; }
        }
    }
}
