using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using FACM.Performance;

namespace FACM.League
{
    internal sealed class LeagueChampionSpellPreset
    {
        public int ChampionId { get; set; }
        public string Group { get; set; }
        public int Spell1Id { get; set; }
        public int Spell2Id { get; set; }
    }

    /// <summary>
    /// Small, account-local champion config catalog. Group names follow Akari 1.5.1's
    /// CLASSIC/ranked position distinction; no remote lookup is required to resolve a preset.
    /// </summary>
    internal static class LeagueChampionSpellPresets
    {
        private const int MaxLength = 4096;
        private const int MaxPresets = 32;
        private static readonly string[] Groups =
        {
            "normal", "ranked-default", "ranked-top", "ranked-jungle",
            "ranked-middle", "ranked-bottom", "ranked-utility", "aram", "urf",
            "nexusblitz", "ultbook"
        };

        internal static IReadOnlyList<LeagueChampionSpellPreset> Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<LeagueChampionSpellPreset>();
            if (value.Length > MaxLength) return Array.Empty<LeagueChampionSpellPreset>();
            try
            {
                var rows = new JavaScriptSerializer { MaxJsonLength = MaxLength }
                    .Deserialize<List<LeagueChampionSpellPreset>>(value);
                if (rows == null || rows.Count > MaxPresets || rows.Any(row => !Valid(row)) ||
                    rows.Select(row => Key(row)).Distinct(StringComparer.Ordinal).Count() != rows.Count)
                    return Array.Empty<LeagueChampionSpellPreset>();
                return rows;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (InvalidCastException) { }
            catch (FormatException) { }
            catch (OverflowException) { }
            return Array.Empty<LeagueChampionSpellPreset>();
        }

        internal static string Serialize(IEnumerable<LeagueChampionSpellPreset> source)
        {
            var rows = source == null ? new List<LeagueChampionSpellPreset>() : source.ToList();
            if (rows.Count > MaxPresets || rows.Any(row => !Valid(row)) ||
                rows.Select(row => Key(row)).Distinct(StringComparer.Ordinal).Count() != rows.Count)
                throw new ArgumentException("Invalid champion spell presets.");
            var encoded = new JavaScriptSerializer().Serialize(rows
                .OrderBy(row => row.ChampionId).ThenBy(row => row.Group, StringComparer.Ordinal).ToList());
            if (encoded.Length > MaxLength) throw new ArgumentException("Champion spell presets are too large.");
            return encoded;
        }

        internal static string Upsert(string source, LeagueChampionSpellPreset preset)
        {
            if (!Valid(preset)) throw new ArgumentException("Invalid champion spell preset.");
            var rows = Parse(source).ToList();
            rows.RemoveAll(row => Key(row) == Key(preset));
            rows.Add(preset);
            return Serialize(rows);
        }

        internal static string Remove(string source, int championId, string group)
        {
            var rows = Parse(source).Where(row => row.ChampionId != championId || row.Group != group);
            return Serialize(rows);
        }

        internal static LeagueChampionSpellPreset Resolve(string source, int championId, string group)
        {
            if (championId <= 0 || string.IsNullOrEmpty(group)) return null;
            var rows = Parse(source);
            var exact = rows.FirstOrDefault(row => row.ChampionId == championId && row.Group == group);
            if (exact != null) return exact;
            return group.StartsWith("ranked-", StringComparison.Ordinal) ?
                rows.FirstOrDefault(row => row.ChampionId == championId && row.Group == "ranked-default") : null;
        }

        internal static string GroupFor(int queueId, string mode, string position,
            string gameMode = null, string queueType = null)
        {
            if (LeagueQueueModePolicy.IsAramMayhem(queueId, gameMode)) return null;
            var rankedQueueType = !string.IsNullOrWhiteSpace(queueType) &&
                queueType.StartsWith("RANKED_", StringComparison.OrdinalIgnoreCase);
            if (queueId == 420 || queueId == 440 ||
                rankedQueueType && string.Equals(gameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase))
            {
                switch ((position ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "top": return "ranked-top";
                    case "jungle": return "ranked-jungle";
                    case "mid": case "middle": return "ranked-middle";
                    case "adc": case "bottom": return "ranked-bottom";
                    case "support": case "utility": return "ranked-utility";
                    default: return "ranked-default";
                }
            }
            if (string.Equals(mode, "aram", StringComparison.OrdinalIgnoreCase)) return "aram";
            if (string.Equals(mode, "urf", StringComparison.OrdinalIgnoreCase)) return "urf";
            if (string.Equals(mode, "nexusblitz", StringComparison.OrdinalIgnoreCase)) return "nexusblitz";
            if (string.Equals(mode, "ultbook", StringComparison.OrdinalIgnoreCase)) return "ultbook";
            if (string.Equals(gameMode, "NEXUSBLITZ", StringComparison.OrdinalIgnoreCase)) return "nexusblitz";
            if (string.Equals(gameMode, "ULTBOOK", StringComparison.OrdinalIgnoreCase)) return "ultbook";
            if (string.Equals(gameMode, "URF", StringComparison.OrdinalIgnoreCase)) return "urf";
            if (queueId == 400 || queueId == 430 ||
                (queueId == 0 && string.Equals(mode, "ranked", StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(gameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(queueType) && !rankedQueueType)
                return "normal";
            return null;
        }

        internal static LeagueBuildAdvisorSnapshot FromLive(LeagueLiveSnapshot live)
        {
            var local = live == null ? null : live.Players.FirstOrDefault(row => row.IsLocalPlayer);
            return new LeagueBuildAdvisorSnapshot
            {
                Connected = live != null && live.Connected,
                Activity = live == null ? LeagueActivityLevel.None : live.Activity,
                GameId = live == null ? 0 : live.GameId,
                QueueId = live == null ? 0 : live.QueueId,
                QueueType = live == null ? null : live.QueueType,
                ChampionId = LeagueBuildAdvisorDataService.ResolveChampionId(live, local),
                Mode = live == null ? null : LeagueBuildAdvisorDataService.ResolveOpggMode(live.QueueId, live.GameMode),
                GameMode = live == null ? null : live.GameMode,
                Position = LeagueBuildAdvisorDataService.ResolveOpggPosition(local == null ? null : local.Position, "ranked")
            };
        }

        private static bool Valid(LeagueChampionSpellPreset row)
        {
            return row != null && row.ChampionId > 0 && row.ChampionId <= 10000 &&
                   ValidGroup(row.Group) && row.Spell1Id > 0 && row.Spell1Id <= 1000 &&
                   row.Spell2Id > 0 && row.Spell2Id <= 1000 && row.Spell1Id != row.Spell2Id;
        }

        internal static bool ValidGroup(string group)
        {
            return Groups.Contains(group);
        }

        private static string Key(LeagueChampionSpellPreset row)
        {
            return row.ChampionId + ":" + row.Group;
        }
    }

    internal sealed class LeagueChampionSpellPresetCoordinator
    {
        private string _pending;
        private DateTime _since;
        private string _attempted;

        internal LeagueAutoApplyDecision Observe(LeagueBuildAdvisorSnapshot snapshot,
            LeagueBuildApplyPlan plan, bool enabled, DateTime now)
        {
            if (!enabled || plan == null || (!plan.HasSpells && !plan.HasRunes) || snapshot == null || !snapshot.Connected ||
                snapshot.Activity != LeagueActivityLevel.ChampSelect || snapshot.ChampionId != plan.ChampionId)
            {
                _pending = null;
                _attempted = null;
                return new LeagueAutoApplyDecision { Reason = "inactive" };
            }

            var fingerprint = snapshot.GameId + ":" + snapshot.QueueId + ":" +
                              plan.ChampionId + ":" + plan.Spell1Id + ":" + plan.Spell2Id + ":" +
                              plan.PrimaryStyleId + ":" + plan.SecondaryStyleId + ":" +
                              string.Join(",", plan.GetSelectedPerkIds());
            if (fingerprint == _attempted)
                return new LeagueAutoApplyDecision { Reason = "already-applied", Fingerprint = fingerprint };
            if (fingerprint != _pending)
            {
                _pending = fingerprint;
                _since = now;
                return new LeagueAutoApplyDecision { Reason = "stabilizing", Fingerprint = fingerprint };
            }
            if (now - _since < LeagueAutoApplyCoordinator.DefaultStabilityWindow)
                return new LeagueAutoApplyDecision { Reason = "stabilizing", Fingerprint = fingerprint };

            _attempted = fingerprint;
            _pending = null;
            return new LeagueAutoApplyDecision { ShouldExecute = true, Reason = "stable", Fingerprint = fingerprint };
        }

        internal void Cancel() { _pending = null; _attempted = null; }
    }
}
