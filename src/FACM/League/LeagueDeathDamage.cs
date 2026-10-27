using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FACM.League
{
    internal sealed class LeagueDamageAmounts
    {
        internal double? Physical = 0, Magic = 0, True = 0;
        internal double? Total => Add(Add(Physical, Magic), True);
        internal void Include(LeagueDamageAmounts value)
        {
            Physical = Add(Physical, value.Physical);
            Magic = Add(Magic, value.Magic);
            True = Add(True, value.True);
        }
        private static double? Add(double? a, double? b)
        {
            if (!a.HasValue || !b.HasValue) return null;
            var sum = a.Value + b.Value;
            return double.IsInfinity(sum) ? (double?)null : sum;
        }
        internal static string Format(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.##", CultureInfo.CurrentCulture) : "—";
        }
    }

    internal sealed class LeagueDamageSource
    {
        internal int ParticipantId;
        internal string Type, Name;
        internal readonly LeagueDamageAmounts Amounts = new LeagueDamageAmounts();
        internal readonly Dictionary<string, LeagueDamageAmounts> Abilities = new Dictionary<string, LeagueDamageAmounts>();
    }

    internal sealed class LeagueDamageDirection
    {
        internal readonly List<LeagueDamageSource> Sources = new List<LeagueDamageSource>();
        internal LeagueDamageAmounts Total;
    }

    internal sealed class LeagueDeathDamage
    {
        internal int VictimId;
        internal LeagueDamageDirection Received, Dealt;

        internal static LeagueDeathDamage Project(LeagueMatchTimelineEvent entry)
        {
            if (entry == null || entry.Type != "CHAMPION_KILL" || entry.Data == null) return null;
            return new LeagueDeathDamage { VictimId = entry.VictimId,
                Received = Collect(entry.Data, "victimDamageReceived"),
                Dealt = Collect(entry.Data, "victimDamageDealt") };
        }

        private static LeagueDamageDirection Collect(Dictionary<string, object> data, string field)
        {
            object raw;
            var entries = data.TryGetValue(field, out raw) ? raw as object[] : null;
            var result = new LeagueDamageDirection();
            if (entries == null || entries.Length > 1024) return result;
            result.Total = new LeagueDamageAmounts();
            var groups = new Dictionary<string, LeagueDamageSource>(StringComparer.Ordinal);
            foreach (var item in entries)
            {
                var row = item as Dictionary<string, object>;
                if (row == null) { result.Total = null; continue; }
                var id = LeagueMatchTimelineParser.Number(row, "participantId");
                var participantId = id > 0 && id <= 20 ? (int)id.Value : 0;
                var type = LeagueMatchTimelineParser.Text(row, "type");
                type = participantId > 0 ? "champion" : type == "MINION" ? "minion" :
                    type == "MONSTER" ? "monster" : type == "TOWER" ? "tower" : "other";
                var key = participantId > 0 ? "champion:" + participantId : type;
                LeagueDamageSource source;
                if (!groups.TryGetValue(key, out source))
                {
                    source = new LeagueDamageSource { ParticipantId = participantId, Type = type,
                        Name = Clean(LeagueMatchTimelineParser.Text(row, "name")) };
                    groups.Add(key, source);
                }
                object basic;
                var slot = LeagueMatchTimelineParser.Number(row, "spellSlot");
                var ability = row.TryGetValue("basic", out basic) && basic is bool && (bool)basic ? "A" :
                    slot == 63 ? "P" : slot >= 0 && slot <= 3 ? new[] { "Q", "W", "E", "R" }[(int)slot.Value] : "?";
                LeagueDamageAmounts amounts;
                if (!source.Abilities.TryGetValue(ability, out amounts))
                {
                    amounts = new LeagueDamageAmounts();
                    source.Abilities.Add(ability, amounts);
                }
                var damage = new LeagueDamageAmounts { Physical = Number(row, "physicalDamage"),
                    Magic = Number(row, "magicDamage"), True = Number(row, "trueDamage") };
                amounts.Include(damage);
                source.Amounts.Include(damage);
                result.Total?.Include(damage);
            }
            result.Sources.AddRange(groups.Values.OrderByDescending(source => source.Amounts.Total ?? -1));
            return result;
        }

        private static double? Number(Dictionary<string, object> data, string key)
        {
            object raw;
            double number;
            return data.TryGetValue(key, out raw) && raw != null && !(raw is bool) &&
                double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out number) && number >= 0 &&
                !double.IsInfinity(number) && !double.IsNaN(number) ? (double?)number : null;
        }

        private static string Clean(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty :
                new string(value.Where(c => !char.IsControl(c)).Take(120).ToArray());
        }
    }
}
