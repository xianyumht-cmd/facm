using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    // Explicit clipboard exchange with Akari v1.5.1 AutoGameflowSettings.
    // Only settings represented by native GGman are accepted; no client writes occur here.
    internal static class LeagueAkariGameflowTransfer
    {
        internal const int MaximumJsonLength = 16 * 1024;

        private static readonly HashSet<string> BooleanKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "autoHonorEnabled", "playAgainEnabled", "autoAcceptEnabled", "autoReconnectEnabled",
            "autoMatchmakingEnabled", "autoMatchmakingWaitForInvitees", "autoSkipLeaderEnabled",
            "autoHandleInvitationsEnabled", "rejectInvitationWhenAway",
            "autoSendARAMTeamSideEnabled", "autoSendARAMTeamSideVisibleToTeam"
        };

        private static readonly Dictionary<string, double[]> NumberKeys =
            new Dictionary<string, double[]>(StringComparer.Ordinal)
            {
                { "autoAcceptDelaySeconds", new[] { 0d, 15d, 2d } },
                { "autoMatchmakingDelaySeconds", new[] { 0d, 60d, 1d } },
                { "autoMatchmakingMinimumMembers", new[] { 1d, 5d, 1d } },
                { "autoMatchmakingRematchFixedDuration", new[] { 1d, 600d, 1d } },
                { "autoMatchmakingMaximumMatchDuration", new[] { 0d, 0d, 1d } }
            };

        private static string SourceStopPolicy(string native)
        {
            var policy = LeagueMatchmakingStopPolicyCodec.Normalize(native);
            return policy == LeagueMatchmakingStopPolicyCodec.Fixed ? "fixed-duration" :
                policy == LeagueMatchmakingStopPolicyCodec.Estimated ? "estimated-duration" : "never";
        }

        private static string NativeStopPolicy(string source)
        {
            return source == "fixed-duration" ? LeagueMatchmakingStopPolicyCodec.Fixed :
                source == "estimated-duration" ? LeagueMatchmakingStopPolicyCodec.Estimated :
                LeagueMatchmakingStopPolicyCodec.Never;
        }

        internal static string Export(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var values = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "autoHonorEnabled", settings.LeagueAutoHonorTeammateEnabled },
                { "autoHonorStrategy", LeagueHonorStrategy.Normalize(settings.LeagueAutoHonorStrategy) },
                { "playAgainEnabled", settings.LeagueAutoReturnLobbyEnabled },
                { "autoAcceptEnabled", settings.LeagueAutoAcceptEnabled },
                { "autoAcceptDelaySeconds", settings.LeagueAutoAcceptDelayMs / 1000d },
                { "autoReconnectEnabled", settings.LeagueAutoReconnectEnabled },
                { "autoMatchmakingEnabled", settings.LeagueAutoMatchmakingEnabled },
                { "autoMatchmakingMaximumMatchDuration", 0 },
                { "autoMatchmakingRematchStrategy", SourceStopPolicy(settings.LeagueAutoMatchmakingStopPolicy) },
                { "autoMatchmakingRematchFixedDuration", settings.LeagueAutoMatchmakingStopAfterMs / 1000d },
                { "autoMatchmakingDelaySeconds", settings.LeagueAutoMatchmakingStartDelayMs / 1000d },
                { "autoMatchmakingMinimumMembers", settings.LeagueAutoMatchmakingMinPartySize },
                { "autoMatchmakingWaitForInvitees", settings.LeagueAutoMatchmakingWaitForInvitees },
                { "autoSkipLeaderEnabled", settings.LeagueAutoSkipLeaderEnabled },
                { "autoHandleInvitationsEnabled", settings.LeagueAutoHandleInvitationsEnabled },
                { "rejectInvitationWhenAway", settings.LeagueSkipInvitationsWhenAway },
                { "invitationHandlingStrategies", LeagueInvitationPolicies.Parse(settings.LeagueInvitationStrategies) },
                { "autoSendARAMTeamSideEnabled", settings.LeagueAutoAramTeamSideEnabled },
                { "autoSendARAMTeamSideVisibleToTeam", settings.LeagueAutoAramTeamSideVisibleToTeam }
            };
            return new JavaScriptSerializer().Serialize(values);
        }

        internal static bool TryParse(string json, out Dictionary<string, object> values, out string error)
        {
            values = null;
            error = "Invalid or unsupported Akari auto-gameflow settings.";
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumJsonLength) return false;
            Dictionary<string, object> source;
            try
            {
                source = new JavaScriptSerializer { MaxJsonLength = MaximumJsonLength }
                    .DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            if (source == null || source.Count == 0 || source.Count > 19) return false;

            var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in source)
            {
                if (BooleanKeys.Contains(pair.Key))
                {
                    if (!(pair.Value is bool)) return false;
                    normalized.Add(pair.Key, pair.Value);
                    continue;
                }
                double[] limits;
                if (NumberKeys.TryGetValue(pair.Key, out limits))
                {
                    if (!(pair.Value is int || pair.Value is long || pair.Value is double ||
                          pair.Value is decimal)) return false;
                    var number = Convert.ToDouble(pair.Value, CultureInfo.InvariantCulture);
                    if (double.IsNaN(number) || double.IsInfinity(number) ||
                        number < limits[0] || number > limits[1] ||
                        Math.Abs(number * limits[2] - Math.Round(number * limits[2])) > 0.000001)
                        return false;
                    normalized.Add(pair.Key, number);
                    continue;
                }
                if (pair.Key == "autoHonorStrategy")
                {
                    var value = pair.Value as string;
                    if (!LeagueHonorStrategy.Choices.Contains(value, StringComparer.Ordinal)) return false;
                    normalized.Add(pair.Key, value);
                    continue;
                }
                if (pair.Key == "autoMatchmakingRematchStrategy")
                {
                    var value = pair.Value as string;
                    if (value != "never" && value != "fixed-duration" && value != "estimated-duration")
                        return false;
                    normalized.Add(pair.Key, value);
                    continue;
                }
                if (pair.Key == "invitationHandlingStrategies")
                {
                    var policies = pair.Value as Dictionary<string, object>;
                    if (policies == null || policies.Count > LeagueInvitationPolicies.GameTypes.Length)
                        return false;
                    var canonical = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var policy in policies)
                    {
                        if (!LeagueInvitationPolicies.GameTypes.Contains(policy.Key, StringComparer.Ordinal) ||
                            !(policy.Value is string) ||
                            (string)policy.Value != LeagueInvitationPolicies.Accept &&
                            (string)policy.Value != LeagueInvitationPolicies.Decline &&
                            (string)policy.Value != LeagueInvitationPolicies.Ignore)
                            return false;
                        canonical.Add(policy.Key, (string)policy.Value);
                    }
                    normalized.Add(pair.Key, LeagueInvitationPolicies.Serialize(canonical));
                    continue;
                }
                return false;
            }
            values = normalized;
            error = null;
            return true;
        }

        internal static void ApplyValidated(AppSettings settings, Dictionary<string, object> values)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (values == null) throw new ArgumentNullException(nameof(values));
            foreach (var pair in values)
            {
                switch (pair.Key)
                {
                    case "autoHonorEnabled": settings.LeagueAutoHonorTeammateEnabled = (bool)pair.Value; break;
                    case "autoHonorStrategy": settings.LeagueAutoHonorStrategy = (string)pair.Value; break;
                    case "playAgainEnabled": settings.LeagueAutoReturnLobbyEnabled = (bool)pair.Value; break;
                    case "autoAcceptEnabled": settings.LeagueAutoAcceptEnabled = (bool)pair.Value; break;
                    case "autoAcceptDelaySeconds": settings.LeagueAutoAcceptDelayMs = (int)Math.Round((double)pair.Value * 1000); break;
                    case "autoReconnectEnabled": settings.LeagueAutoReconnectEnabled = (bool)pair.Value; break;
                    case "autoMatchmakingEnabled": settings.LeagueAutoMatchmakingEnabled = (bool)pair.Value; break;
                    case "autoMatchmakingMaximumMatchDuration": break; // Only the source's disabled value (0) is representable.
                    case "autoMatchmakingRematchStrategy": settings.LeagueAutoMatchmakingStopPolicy = NativeStopPolicy((string)pair.Value); break;
                    case "autoMatchmakingRematchFixedDuration": settings.LeagueAutoMatchmakingStopAfterMs = (int)Math.Round((double)pair.Value * 1000); break;
                    case "autoMatchmakingDelaySeconds": settings.LeagueAutoMatchmakingStartDelayMs = (int)Math.Round((double)pair.Value * 1000); break;
                    case "autoMatchmakingMinimumMembers": settings.LeagueAutoMatchmakingMinPartySize = (int)(double)pair.Value; break;
                    case "autoMatchmakingWaitForInvitees": settings.LeagueAutoMatchmakingWaitForInvitees = (bool)pair.Value; break;
                    case "autoSkipLeaderEnabled": settings.LeagueAutoSkipLeaderEnabled = (bool)pair.Value; break;
                    case "autoHandleInvitationsEnabled": settings.LeagueAutoHandleInvitationsEnabled = (bool)pair.Value; break;
                    case "rejectInvitationWhenAway": settings.LeagueSkipInvitationsWhenAway = (bool)pair.Value; break;
                    case "invitationHandlingStrategies": settings.LeagueInvitationStrategies = (string)pair.Value; break;
                    case "autoSendARAMTeamSideEnabled": settings.LeagueAutoAramTeamSideEnabled = (bool)pair.Value; break;
                    case "autoSendARAMTeamSideVisibleToTeam": settings.LeagueAutoAramTeamSideVisibleToTeam = (bool)pair.Value; break;
                    default: throw new ArgumentException("Settings must be validated before applying.", nameof(values));
                }
            }
        }
    }
}
