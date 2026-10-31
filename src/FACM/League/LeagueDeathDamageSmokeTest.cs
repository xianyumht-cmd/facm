using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueDeathDamageSmokeTest
    {
        internal static void Validate()
        {
            var received = new object[] { Row(2, 100, 0, 0, 0, true), Row(2, 0, 80, 0, 0),
                Row(3, 0, 0, 25, 63), Row(0, 20, 30, 40, 7), Row(2, 0, 5, 0, 0) };
            var entry = new LeagueMatchTimelineEvent { Type = "CHAMPION_KILL", VictimId = 1,
                Data = new Dictionary<string, object> { { "victimDamageReceived", received },
                    { "victimDamageDealt", new object[] { Row(3, 10, 20, 30, 3) } } } };
            var result = LeagueDeathDamage.Project(entry);
            Require(result.VictimId == 1 && result.Received.Sources.Count == 3 &&
                result.Received.Total.Physical == 120 && result.Received.Total.Magic == 115 &&
                result.Received.Total.True == 65 && result.Received.Total.Total == 300 &&
                result.Received.Sources[0].ParticipantId == 2 &&
                result.Received.Sources[0].Abilities["A"].Physical == 100 &&
                result.Received.Sources[0].Abilities["Q"].Magic == 85 &&
                result.Received.Sources.Single(s => s.ParticipantId == 3).Abilities["P"].True == 25 &&
                result.Received.Sources.Single(s => s.ParticipantId == 0).Type == "other" &&
                result.Dealt.Sources[0].ParticipantId == 3 && result.Dealt.Total.Total == 60,
                "Death damage must separate directions, attribute peers, group basic/passive/spells and sort complete entity totals.");
            var incomplete = Row(2, 3, 4, 5, 1);
            incomplete.Remove("magicDamage");
            incomplete.Remove("participantId");
            var malformed = Row(4, 1, 2, 3, 2);
            malformed["physicalDamage"] = "NaN";
            malformed["trueDamage"] = -1;
            entry.Data["victimDamageReceived"] = new object[] { incomplete, malformed };
            entry.Data.Remove("victimDamageDealt");
            var partial = LeagueDeathDamage.Project(entry);
            Require(partial.Received.Total.Total == null && partial.Received.Total.Physical == null &&
                partial.Received.Total.Magic == null && partial.Received.Total.True == null &&
                partial.Received.Sources.Any(s => s.Type == "other" && s.Amounts.Physical == 3 && s.Amounts.Magic == null) &&
                partial.Dealt.Total == null && partial.Dealt.Sources.Count == 0,
                "Incomplete numbers and absent attacker fields must stay unknown, distinct from valid zero and missing directions.");
            entry.Data["victimDamageDealt"] = new object[0];
            Require(LeagueDeathDamage.Project(entry).Dealt.Total.Total == 0 && LeagueDeathDamage.Project(null) == null,
                "An explicitly empty source array is different from an absent damage array.");

            var sourceEvent = new Dictionary<string, object>(entry.Data) { { "type", "CHAMPION_KILL" },
                { "timestamp", 1000 }, { "victimId", 1 } };
            var json = new JavaScriptSerializer().Serialize(new { gameId = 71,
                frames = new[] { new { timestamp = 1000, events = new[] { sourceEvent } } } });
            foreach (var sgp in new[] { false, true })
            {
                var parsed = LeagueMatchTimelineParser.Parse(Encoding.UTF8.GetBytes(sgp ? "{\"json\":" + json + "}" : json), 71, sgp);
                Require(parsed != null && LeagueDeathDamage.Project(parsed.Events[0]).Received.Total.Magic == null,
                    "LCU and SGP timeline adapters must share the same incomplete-damage projection.");
            }
            var players = new List<LeaguePlayerMatchParticipant> { new LeaguePlayerMatchParticipant
                { ParticipantId = 1, ChampionId = 1, PlayerName = "victim" } };
            using (var dialog = new LeagueDeathDamageDialog(UiTextCatalog.Load(), result, players, null, null,
                () => true, CancellationToken.None))
            using (var bitmap = new Bitmap(790, 500))
            {
                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            }
            for (var i = 0; i < 10; i++)
                Require(LeagueDeathDamage.Project(entry).Received.Sources.Count == 2 &&
                    result.Received.Total.Total == 300,
                    "Repeated event projections cannot mutate the previously selected damage snapshot.");
        }

        private static Dictionary<string, object> Row(int id, double physical, double magic, double pure,
            int slot, bool basic = false)
        {
            return new Dictionary<string, object> { { "participantId", id }, { "type", "OTHER" },
                { "name", "fixture" }, { "physicalDamage", physical }, { "magicDamage", magic },
                { "trueDamage", pure }, { "spellSlot", slot }, { "basic", basic } };
        }
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
