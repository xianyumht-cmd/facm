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
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public int CreepScore { get; set; }
        public bool Win { get; set; }
        public bool ParticipantResolved { get; set; }
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
