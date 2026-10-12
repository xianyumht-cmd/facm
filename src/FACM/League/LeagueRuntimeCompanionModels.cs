using System;
using System.Collections.Generic;
using System.Globalization;
using FACM.Mayhem;

namespace FACM.League
{
    internal enum LeagueRuntimeCompanionApplyTarget
    {
        Runes,
        SummonerSpells
    }

    internal sealed class LeagueRuntimeCompanionApplyPreparation
    {
        public LeagueRuntimeCompanionApplyTarget Target { get; set; }
        public LeagueBuildApplyPlan Plan { get; set; }
        public LeagueBuildAdvisorSnapshot SourceSnapshot { get; set; }

        public bool IsUsable
        {
            get
            {
                if (Plan == null || SourceSnapshot == null) return false;
                return Target == LeagueRuntimeCompanionApplyTarget.Runes ? Plan.HasRunes : Plan.HasSpells;
            }
        }
    }

    internal sealed class LeagueRuntimeCompanionRecentChampion
    {
        public string Status { get; set; }
        public int ChampionId { get; set; }
        public int SampleMatches { get; set; }
        public int ResolvedMatches { get; set; }
        public int ChampionGames { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public double AverageKills { get; set; }
        public double AverageDeaths { get; set; }
        public double AverageAssists { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public bool HasStats
        {
            get
            {
                return ChampionGames > 0 && string.Equals(Status, "ready", StringComparison.OrdinalIgnoreCase);
            }
        }

        public LeagueRuntimeCompanionRecentChampion Clone()
        {
            return new LeagueRuntimeCompanionRecentChampion
            {
                Status = Status,
                ChampionId = ChampionId,
                SampleMatches = SampleMatches,
                ResolvedMatches = ResolvedMatches,
                ChampionGames = ChampionGames,
                Wins = Wins,
                Losses = Losses,
                AverageKills = AverageKills,
                AverageDeaths = AverageDeaths,
                AverageAssists = AverageAssists,
                UpdatedAtUtc = UpdatedAtUtc
            };
        }
    }

    internal static class LeagueRuntimeCompanionRecentChampionProjection
    {
        public static LeagueRuntimeCompanionRecentChampion Project(LeaguePlayerMatchPage page, int championId)
        {
            if (championId <= 0) return null;
            if (page == null)
            {
                return new LeagueRuntimeCompanionRecentChampion
                {
                    Status = "unavailable",
                    ChampionId = championId,
                    UpdatedAtUtc = DateTime.UtcNow
                };
            }

            var result = new LeagueRuntimeCompanionRecentChampion
            {
                Status = "empty",
                ChampionId = championId,
                SampleMatches = page.Matches == null ? 0 : page.Matches.Count,
                UpdatedAtUtc = DateTime.UtcNow
            };
            if (page.Matches == null || page.Matches.Count == 0) return result;

            var kills = 0;
            var deaths = 0;
            var assists = 0;
            foreach (var match in page.Matches)
            {
                if (match == null || !match.ParticipantResolved) continue;
                result.ResolvedMatches++;
                if (match.ChampionId != championId) continue;
                result.ChampionGames++;
                if (match.Win) result.Wins++;
                kills += match.Kills;
                deaths += match.Deaths;
                assists += match.Assists;
            }

            if (result.ChampionGames <= 0) return result;
            result.Losses = Math.Max(0, result.ChampionGames - result.Wins);
            result.AverageKills = (double)kills / result.ChampionGames;
            result.AverageDeaths = (double)deaths / result.ChampionGames;
            result.AverageAssists = (double)assists / result.ChampionGames;
            result.Status = "ready";
            return result;
        }
    }

    /// <summary>
    /// Network-free fallback labels for summoner-spell IDs already exposed by ChampSelect.
    /// This deliberately does not own or fetch Riot game-data. Unknown IDs remain explicit tokens
    /// instead of being guessed, and richer icon/name projection can reuse a shared catalog later.
    /// </summary>
    internal static class LeagueRuntimeCompanionSpellPresentation
    {
        public static string Format(int spell1Id, int spell2Id)
        {
            var labels = new List<string>(2);
            Add(labels, spell1Id);
            Add(labels, spell2Id);
            return string.Join("/", labels);
        }

        private static void Add(ICollection<string> labels, int spellId)
        {
            if (labels == null || spellId <= 0) return;
            labels.Add(Resolve(spellId));
        }

        private static string Resolve(int spellId)
        {
            switch (spellId)
            {
                case 1: return "Cleanse";
                case 3: return "Exhaust";
                case 4: return "Flash";
                case 6: return "Ghost";
                case 7: return "Heal";
                case 11: return "Smite";
                case 12: return "Teleport";
                case 13: return "Clarity";
                case 14: return "Ignite";
                case 21: return "Barrier";
                case 32: return "Mark";
                default: return "S" + spellId.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    internal sealed class LeagueRuntimeCompanionSnapshot
    {
        public bool SessionAvailable { get; set; }
        public bool BenchEnabled { get; set; }
        public int LocalChampionId { get; set; }
        public LeagueBenchSwapRoute SwapRoute { get; set; }
        public IReadOnlyList<int> BenchChampionIds { get; set; } = Array.Empty<int>();
        public string TimerPhase { get; set; }
        public int TimerMillisecondsLeft { get; set; }
        public IReadOnlyList<int> AllyBans { get; set; } = Array.Empty<int>();
        public IReadOnlyList<int> EnemyBans { get; set; } = Array.Empty<int>();
        public IReadOnlyList<LeagueLivePlayerRow> Players { get; set; } = Array.Empty<LeagueLivePlayerRow>();
        public LeagueRuntimeCompanionRecentChampion RecentChampion { get; set; }

        public bool BuildLoading { get; set; }
        public string BuildError { get; set; }
        public LeagueBuildAdvisorSnapshot Build { get; set; }

        public bool GuideLoading { get; set; }
        public int GuideChampionId { get; set; }
        public MayhemChampionResult Guide { get; set; }
        public string GuideError { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public bool HasBuild
        {
            get
            {
                return Build != null &&
                       Build.Connected &&
                       Build.Activity == Performance.LeagueActivityLevel.ChampSelect &&
                       Build.ChampionId > 0 &&
                       Build.Recommendation != null &&
                       string.Equals(Build.Status, "ready", StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool HasGuide
        {
            get { return Guide != null && string.IsNullOrWhiteSpace(GuideError); }
        }

        public bool HasVisibleChampSelectContext
        {
            get
            {
                if (SessionAvailable) return true;
                return Build != null && Build.Connected && Build.Activity == Performance.LeagueActivityLevel.ChampSelect;
            }
        }

        public LeagueRuntimeCompanionSnapshot Clone()
        {
            var players = ClonePlayers(Players);
            var build = CloneBuild(Build);
            PrioritizeRevealedEnemyCounters(build, players);

            return new LeagueRuntimeCompanionSnapshot
            {
                SessionAvailable = SessionAvailable,
                BenchEnabled = BenchEnabled,
                LocalChampionId = LocalChampionId,
                SwapRoute = SwapRoute,
                BenchChampionIds = BenchChampionIds == null
                    ? Array.Empty<int>()
                    : new List<int>(BenchChampionIds).AsReadOnly(),
                TimerPhase = TimerPhase,
                TimerMillisecondsLeft = TimerMillisecondsLeft,
                AllyBans = AllyBans == null ? Array.Empty<int>() : new List<int>(AllyBans).AsReadOnly(),
                EnemyBans = EnemyBans == null ? Array.Empty<int>() : new List<int>(EnemyBans).AsReadOnly(),
                Players = players,
                RecentChampion = RecentChampion == null ? null : RecentChampion.Clone(),
                BuildLoading = BuildLoading,
                BuildError = BuildError,
                Build = build,
                GuideLoading = GuideLoading,
                GuideChampionId = GuideChampionId,
                Guide = Guide,
                GuideError = GuideError,
                UpdatedAtUtc = UpdatedAtUtc
            };
        }

        private static IReadOnlyList<LeagueLivePlayerRow> ClonePlayers(IReadOnlyList<LeagueLivePlayerRow> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<LeagueLivePlayerRow>();
            var rows = new List<LeagueLivePlayerRow>(source.Count);
            foreach (var row in source)
            {
                if (row == null) continue;
                rows.Add(new LeagueLivePlayerRow
                {
                    Side = row.Side,
                    CellId = row.CellId,
                    IsLocalPlayer = row.IsLocalPlayer,
                    GameName = row.GameName,
                    TagLine = row.TagLine,
                    DisplayName = row.DisplayName,
                    PuuId = row.PuuId,
                    SummonerId = row.SummonerId,
                    Position = row.Position,
                    Role = row.Role,
                    ChampionId = row.ChampionId,
                    ChampionPickIntent = row.ChampionPickIntent,
                    Spell1Id = row.Spell1Id,
                    Spell2Id = row.Spell2Id,
                    PresentationSuffix = LeagueRuntimeCompanionSpellPresentation.Format(row.Spell1Id, row.Spell2Id)
                });
            }
            return rows.AsReadOnly();
        }

        internal static LeagueBuildAdvisorSnapshot CloneBuild(LeagueBuildAdvisorSnapshot source)
        {
            if (source == null) return null;
            return new LeagueBuildAdvisorSnapshot
            {
                Connected = source.Connected,
                Phase = source.Phase,
                Activity = source.Activity,
                BudgetName = source.BudgetName,
                QueueId = source.QueueId,
                ChampionId = source.ChampionId,
                ChampionName = source.ChampionName,
                Mode = source.Mode,
                Position = source.Position,
                Source = source.Source,
                Version = source.Version,
                Status = source.Status,
                FromCache = source.FromCache,
                UpdatedAtUtc = source.UpdatedAtUtc,
                Recommendation = CloneRecommendation(source.Recommendation)
            };
        }

        private static LeagueBuildRecommendation CloneRecommendation(LeagueBuildRecommendation source)
        {
            if (source == null) return null;
            var clone = new LeagueBuildRecommendation
            {
                Tier = source.Tier,
                Rank = source.Rank,
                WinRate = source.WinRate,
                PickRate = source.PickRate,
                BanRate = source.BanRate
            };
            foreach (var row in source.Rows)
            {
                if (row == null) continue;
                clone.Rows.Add(new LeagueBuildAdvisorRow
                {
                    Category = row.Category,
                    Recommendation = row.Recommendation,
                    Evidence = row.Evidence,
                    IconReferences = row.IconReferences == null
                        ? Array.Empty<string>()
                        : new List<string>(row.IconReferences).AsReadOnly(),
                    CounterStats = CloneCounterStats(row.CounterStats)
                });
            }
            return clone;
        }

        private static IReadOnlyList<LeagueBuildCounterStat> CloneCounterStats(IReadOnlyList<LeagueBuildCounterStat> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<LeagueBuildCounterStat>();
            var output = new List<LeagueBuildCounterStat>(source.Count);
            foreach (var item in source)
            {
                if (item != null) output.Add(item.Clone());
            }
            return output.AsReadOnly();
        }

        internal static void PrioritizeRevealedEnemyCountersForSmokeTest(
            LeagueBuildAdvisorSnapshot build,
            IReadOnlyList<LeagueLivePlayerRow> players)
        {
            PrioritizeRevealedEnemyCounters(build, players);
        }

        private static void PrioritizeRevealedEnemyCounters(
            LeagueBuildAdvisorSnapshot build,
            IReadOnlyList<LeagueLivePlayerRow> players)
        {
            if (build == null || build.Recommendation == null || build.Recommendation.Rows == null ||
                players == null || players.Count == 0) return;

            string localPosition = null;
            var revealedEnemies = new Dictionary<int, string>();
            foreach (var player in players)
            {
                if (player == null) continue;
                if (player.IsLocalPlayer)
                {
                    localPosition = NormalizeDraftPosition(player.Position);
                    continue;
                }
                if (player.ChampionId <= 0 ||
                    !string.Equals(player.Side, "enemy", StringComparison.OrdinalIgnoreCase)) continue;
                revealedEnemies[player.ChampionId] = NormalizeDraftPosition(player.Position);
            }
            if (revealedEnemies.Count == 0) return;

            foreach (var row in build.Recommendation.Rows)
            {
                if (row == null || !string.Equals(row.Category, "counters", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(row.Recommendation)) continue;

                var labels = row.Recommendation.Split(new[] { " · " }, StringSplitOptions.None);
                var icons = row.IconReferences ?? Array.Empty<string>();
                var stats = row.CounterStats ?? Array.Empty<LeagueBuildCounterStat>();
                var count = Math.Max(labels.Length, Math.Max(icons.Count, stats.Count));
                if (count <= 0) continue;

                var entries = new List<CounterEntry>(count);
                var hasRevealedCounter = false;
                for (var index = 0; index < count; index++)
                {
                    var reference = index < icons.Count ? icons[index] : null;
                    var stat = index < stats.Count ? stats[index] : null;
                    var championId = stat != null && stat.ChampionId > 0
                        ? stat.ChampionId
                        : ExtractChampionId(reference);
                    string enemyPosition = null;
                    var matched = championId > 0 && revealedEnemies.TryGetValue(championId, out enemyPosition);
                    var laneMatched = matched &&
                                      !string.IsNullOrWhiteSpace(localPosition) &&
                                      !string.IsNullOrWhiteSpace(enemyPosition) &&
                                      string.Equals(localPosition, enemyPosition, StringComparison.OrdinalIgnoreCase);
                    hasRevealedCounter |= matched;
                    entries.Add(new CounterEntry
                    {
                        Label = index < labels.Length ? labels[index] : string.Empty,
                        IconReference = reference,
                        CounterStat = stat == null ? null : stat.Clone(),
                        RevealedEnemy = matched,
                        LaneMatched = laneMatched,
                        MatchPosition = laneMatched ? localPosition : null,
                        SourceIndex = index
                    });
                }
                if (!hasRevealedCounter) continue;

                entries.Sort(delegate(CounterEntry left, CounterEntry right)
                {
                    if (left.LaneMatched != right.LaneMatched)
                        return left.LaneMatched ? -1 : 1;
                    if (left.RevealedEnemy != right.RevealedEnemy)
                        return left.RevealedEnemy ? -1 : 1;
                    return left.SourceIndex.CompareTo(right.SourceIndex);
                });

                var orderedLabels = new List<string>();
                var orderedIcons = new List<string>();
                var orderedStats = new List<LeagueBuildCounterStat>();
                CounterEntry leadingMatch = null;
                foreach (var entry in entries)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Label)) orderedLabels.Add(entry.Label);
                    orderedIcons.Add(entry.IconReference);
                    if (entry.CounterStat != null) orderedStats.Add(entry.CounterStat.Clone());
                    if (leadingMatch == null && entry.RevealedEnemy) leadingMatch = entry;
                }
                if (orderedLabels.Count > 0) row.Recommendation = string.Join(" · ", orderedLabels);
                row.IconReferences = orderedIcons.AsReadOnly();
                row.CounterStats = orderedStats.AsReadOnly();
                var focusedEvidence = BuildCounterEvidence(leadingMatch);
                if (!string.IsNullOrWhiteSpace(focusedEvidence)) row.Evidence = focusedEvidence;
            }
        }

        private static string BuildCounterEvidence(CounterEntry entry)
        {
            if (entry == null) return string.Empty;
            var parts = new List<string>();
            if (entry.LaneMatched && !string.IsNullOrWhiteSpace(entry.MatchPosition))
                parts.Add(entry.MatchPosition.ToUpperInvariant());
            var stat = entry.CounterStat;
            if (stat != null && stat.WinRate.HasValue)
                parts.Add("win " + (stat.WinRate.Value * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (stat != null && stat.Games > 0)
                parts.Add(stat.Games.ToString("N0", CultureInfo.InvariantCulture) + " games");
            return string.Join(" · ", parts);
        }

        private static string NormalizeDraftPosition(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            switch (value.Trim().ToUpperInvariant())
            {
                case "TOP": return "top";
                case "JUNGLE": return "jungle";
                case "MIDDLE":
                case "MID": return "mid";
                case "BOTTOM":
                case "ADC": return "adc";
                case "UTILITY":
                case "SUPPORT": return "support";
                default: return null;
            }
        }

        private sealed class CounterEntry
        {
            public string Label { get; set; }
            public string IconReference { get; set; }
            public LeagueBuildCounterStat CounterStat { get; set; }
            public bool RevealedEnemy { get; set; }
            public bool LaneMatched { get; set; }
            public string MatchPosition { get; set; }
            public int SourceIndex { get; set; }
        }

        private static int ExtractChampionId(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return 0;
            var value = reference.Trim();
            var query = value.IndexOfAny(new[] { '?', '#' });
            if (query >= 0) value = value.Substring(0, query);
            var slash = value.LastIndexOf('/');
            var start = slash >= 0 ? slash + 1 : 0;
            var dot = value.LastIndexOf('.');
            var end = dot > start ? dot : value.Length;
            if (end <= start) return 0;
            int championId;
            return int.TryParse(value.Substring(start, end - start), out championId) && championId > 0
                ? championId
                : 0;
        }
    }

    internal sealed class LeagueRuntimeCompanionUpdateEventArgs : EventArgs
    {
        public LeagueRuntimeCompanionUpdateEventArgs(LeagueRuntimeCompanionSnapshot snapshot)
        {
            Snapshot = snapshot == null ? null : snapshot.Clone();
        }

        public LeagueRuntimeCompanionSnapshot Snapshot { get; private set; }
    }
}