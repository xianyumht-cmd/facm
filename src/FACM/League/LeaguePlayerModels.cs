using System;
using System.Collections.Generic;

namespace FACM.League
{
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
        public long GameId { get; set; }
        public DateTime GameCreationLocal { get; set; }
        public int GameDurationSeconds { get; set; }
        public string GameMode { get; set; }
        public int QueueId { get; set; }
        public int ChampionId { get; set; }
        public int MapId { get; set; }
        public int TeamId { get; set; }
        public int ParticipantId { get; set; }
        public bool HasSmite { get; set; }
        public string ChampionName { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public int CreepScore { get; set; }
        public bool Win { get; set; }
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
        // Available only when the match includes verified participant identities and team IDs.
        public string[] SameTeamPuuids { get; set; }

    }

    internal sealed class LeaguePlayerMatchParticipant
    {
        public int ParticipantId { get; set; }
        public int TeamId { get; set; }
        public string PuuId { get; set; }
        public string PlayerName { get; set; }
        public int ChampionId { get; set; }
        public string ChampionName { get; set; }
        public int[] ItemIds { get; set; }
        public int[] SpellIds { get; set; }
        public int[] PerkIds { get; set; }
        public int PrimaryPerkStyleId { get; set; }
        public int SecondaryPerkStyleId { get; set; }
        public int[] AugmentIds { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public int CreepScore { get; set; }
        public int DamageToChampions { get; set; }
        public bool DamageAvailable { get; set; }
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
        public LeagueBuildAdvisorCatalog BuildCatalog { get; set; }
    }

    internal sealed class LeagueMatchTeamScore
    {
        public int TeamId { get; set; }
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
