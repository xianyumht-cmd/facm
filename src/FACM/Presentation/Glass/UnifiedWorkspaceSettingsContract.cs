using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedMatchmakingChange
    {
        internal bool AutoSearch, AutoAccept, WaitForInvitees, RoomNotices;
        internal int MinParty, QueueDelayMs, AcceptDelayMs, StopAfterMs;
        internal string StopPolicy;
    }

    internal sealed class UnifiedAutoSelectionChange
    {
        internal bool AutoPick, AutoBan;
        internal int PickId, BanId;
    }

    internal sealed class UnifiedPostgameChange
    {
        internal bool AutoHonor, AutoReturn;
        internal string Strategy;
    }

    internal sealed class UnifiedRecommendationApplyChange
    {
        internal bool Runes, Spells, Items;
    }

    // Accept only documented, bounded settings: no generic JavaScript-to-native setter.
    internal static class UnifiedWorkspaceSettingsContract
    {
        internal static bool TryParseMatchmaking(string json, out UnifiedMatchmakingChange result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json) || json.Length > 2048) return false;
            try
            {
                var fields = new JavaScriptSerializer { MaxJsonLength = 2048 }
                    .Deserialize<Dictionary<string, object>>(json);
                if (fields == null || fields.Count != 9) return false;
                bool autoSearch, autoAccept, wait, notices;
                int minParty, queueDelay, acceptDelay, stopAfter;
                string stop;
                if (!Bool(fields, "autoSearch", out autoSearch) ||
                    !Bool(fields, "autoAccept", out autoAccept) ||
                    !Bool(fields, "waitForInvitees", out wait) ||
                    !Bool(fields, "roomNotices", out notices) ||
                    !Integer(fields, "minParty", 1, 5, out minParty) ||
                    !Integer(fields, "queueDelayMs", 0, 60000, out queueDelay) ||
                    !Integer(fields, "acceptDelayMs", 0, 15000, out acceptDelay) ||
                    !Integer(fields, "stopAfterMs", 1000, 600000, out stopAfter) ||
                    !String(fields, "stopPolicy", out stop)) return false;
                if (stop != LeagueMatchmakingStopPolicyCodec.Never &&
                    stop != LeagueMatchmakingStopPolicyCodec.Fixed &&
                    stop != LeagueMatchmakingStopPolicyCodec.Estimated) return false;
                result = new UnifiedMatchmakingChange
                {
                    AutoSearch = autoSearch, AutoAccept = autoAccept,
                    WaitForInvitees = wait, RoomNotices = notices,
                    MinParty = minParty, QueueDelayMs = queueDelay,
                    AcceptDelayMs = acceptDelay, StopAfterMs = stopAfter,
                    StopPolicy = stop
                };
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (InvalidCastException) { return false; }
        }

        internal static bool TryParsePostgame(string json, out UnifiedPostgameChange result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json) || json.Length > 1024) return false;
            try
            {
                var fields = new JavaScriptSerializer { MaxJsonLength = 1024 }
                    .Deserialize<Dictionary<string, object>>(json);
                if (fields == null || fields.Count != 3) return false;
                bool honor, returnLobby;
                string strategy;
                if (!Bool(fields, "autoHonor", out honor) ||
                    !Bool(fields, "autoReturn", out returnLobby) ||
                    !String(fields, "strategy", out strategy) ||
                    Array.IndexOf(LeagueHonorStrategy.Choices, strategy) < 0) return false;
                result = new UnifiedPostgameChange
                { AutoHonor = honor, AutoReturn = returnLobby, Strategy = strategy };
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (InvalidCastException) { return false; }
        }

        internal static bool TryParseAutoSelection(string json, out UnifiedAutoSelectionChange result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json) || json.Length > 1024) return false;
            try
            {
                var fields = new JavaScriptSerializer { MaxJsonLength = 1024 }
                    .Deserialize<Dictionary<string, object>>(json);
                if (fields == null || fields.Count != 4) return false;
                bool autoPick, autoBan;
                int pickId, banId;
                if (!Bool(fields, "autoPick", out autoPick) ||
                    !Bool(fields, "autoBan", out autoBan) ||
                    !Integer(fields, "pickId", 0, 10000, out pickId) ||
                    !Integer(fields, "banId", 0, 10000, out banId)) return false;
                result = new UnifiedAutoSelectionChange
                { AutoPick = autoPick, AutoBan = autoBan, PickId = pickId, BanId = banId };
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (InvalidCastException) { return false; }
        }

        internal static bool TryParseRecommendationApply(string json, out UnifiedRecommendationApplyChange result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json) || json.Length > 256) return false;
            try
            {
                var fields = new JavaScriptSerializer { MaxJsonLength = 256 }
                    .Deserialize<Dictionary<string, object>>(json);
                bool runes, spells, items;
                if (fields == null || fields.Count != 3 ||
                    !Bool(fields, "runes", out runes) ||
                    !Bool(fields, "spells", out spells) ||
                    !Bool(fields, "items", out items) ||
                    (!runes && !spells && !items)) return false;
                result = new UnifiedRecommendationApplyChange { Runes = runes, Spells = spells, Items = items };
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (InvalidCastException) { return false; }
        }

        private static bool Bool(IDictionary<string, object> fields, string key, out bool value)
        {
            value = false;
            object raw;
            if (!fields.TryGetValue(key, out raw) || !(raw is bool)) return false;
            value = (bool)raw;
            return true;
        }

        private static bool String(IDictionary<string, object> fields, string key, out string value)
        {
            value = null;
            object raw;
            if (!fields.TryGetValue(key, out raw)) return false;
            value = raw as string;
            return value != null && value.Length < 40;
        }

        private static bool Integer(IDictionary<string, object> fields, string key, int min, int max, out int value)
        {
            value = 0;
            object raw;
            if (!fields.TryGetValue(key, out raw) || raw is bool || raw is string) return false;
            var number = Convert.ToString(raw, CultureInfo.InvariantCulture);
            return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
                   value >= min && value <= max;
        }

        internal static void ValidateForSmokeTest()
        {
            UnifiedRecommendationApplyChange applySelection;
            if (!TryParseRecommendationApply("{\"runes\":true,\"spells\":false,\"items\":true}", out applySelection) ||
                !applySelection.Runes || applySelection.Spells || !applySelection.Items ||
                TryParseRecommendationApply("{\"runes\":false,\"spells\":false,\"items\":false}", out applySelection) ||
                TryParseRecommendationApply("{\"runes\":\"yes\",\"spells\":false,\"items\":true}", out applySelection) ||
                TryParseRecommendationApply("{\"runes\":true,\"spells\":false,\"items\":true,\"other\":1}", out applySelection))
                throw new InvalidOperationException("Recommendation write contract must reject untyped or unrequested actions.");
            UnifiedPostgameChange postgame;
            var validPostgame = "{\"autoHonor\":true,\"autoReturn\":false,\"strategy\":\"" +
                LeagueHonorStrategy.Choices[0] + "\"}";
            if (!TryParsePostgame(validPostgame, out postgame) || !postgame.AutoHonor ||
                TryParsePostgame(validPostgame.Replace(LeagueHonorStrategy.Choices[0], "invalid-policy"), out postgame) ||
                TryParsePostgame(validPostgame.Replace("\"autoReturn\":false", "\"autoReturn\":\"false\""), out postgame))
                throw new InvalidOperationException("Postgame bridge must preserve source honor strategy and typed settings.");
            UnifiedAutoSelectionChange selection;
            if (!TryParseAutoSelection("{\"autoPick\":true,\"autoBan\":false,\"pickId\":22,\"banId\":0}", out selection) ||
                selection.PickId != 22 || selection.AutoBan ||
                TryParseAutoSelection("{\"autoPick\":true,\"autoBan\":false,\"pickId\":-1,\"banId\":0}", out selection) ||
                TryParseAutoSelection("{\"autoPick\":true,\"autoBan\":false,\"pickId\":22.5,\"banId\":0}", out selection) ||
                TryParseAutoSelection("{\"autoPick\":true,\"autoBan\":\"false\",\"pickId\":22,\"banId\":0}", out selection))
                throw new InvalidOperationException("Auto-selection bridge must preserve typed champion settings.");
            UnifiedMatchmakingChange parsed;
            var sample = "{\"autoSearch\":true,\"autoAccept\":false,\"waitForInvitees\":true," +
                         "\"roomNotices\":false,\"minParty\":2,\"queueDelayMs\":3000," +
                         "\"acceptDelayMs\":1500,\"stopAfterMs\":120000,\"stopPolicy\":\"" +
                         LeagueMatchmakingStopPolicyCodec.Never + "\"}";
            if (!TryParseMatchmaking(sample, out parsed) ||
                parsed.MinParty != 2 || parsed.QueueDelayMs != 3000 ||
                parsed.AutoAccept || parsed.StopPolicy != LeagueMatchmakingStopPolicyCodec.Never)
                throw new InvalidOperationException("Matchmaking bridge rejected valid saved settings.");
            foreach (var bad in new[]
            {
                sample.Replace("\"minParty\":2", "\"minParty\":0"),
                sample.Replace("\"queueDelayMs\":3000", "\"queueDelayMs\":80000"),
                sample.Replace("\"autoAccept\":false", "\"autoAccept\":\"false\""),
                sample.Replace(LeagueMatchmakingStopPolicyCodec.Never, "delete-account"),
                sample.Replace("\"roomNotices\":false,", ""),
                sample + "bad",
                new string('a', 2100)
            })
            {
                if (TryParseMatchmaking(bad, out parsed))
                    throw new InvalidOperationException("Matchmaking bridge accepted invalid native settings.");
            }
        }
    }
}
