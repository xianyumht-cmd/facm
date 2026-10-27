using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal enum LeaguePlayerDetailsStatus { Ready, Partial, Unavailable, UnsupportedRegion, AccountChanged }

    internal sealed class LeaguePlayerProfileDetails
    {
        internal LeaguePlayerDetailsStatus Status;
        internal LeaguePlayerProfile Profile;
        internal Dictionary<string, object> Ranked, Cosmetics;
    }

    internal sealed class LeaguePlayerMasteryDetails
    {
        internal LeaguePlayerDetailsStatus Status;
        internal List<Dictionary<string, object>> Masteries;
    }

    internal sealed partial class LeaguePlayerDataService
    {
        internal async Task<LeaguePlayerProfileDetails> LoadPlayerDetailsAsync(string selfPuuid, string targetPuuid,
            LeagueClientSession expectedSession, string serverId, LeaguePlayerHistorySource source, CancellationToken token)
        {
            var result = new LeaguePlayerProfileDetails { Status = LeaguePlayerDetailsStatus.Unavailable };
            if (!IsSafePuuid(selfPuuid) || !IsSafePuuid(targetPuuid) || !Enum.IsDefined(typeof(LeaguePlayerHistorySource), source)) return result;
            await _requestGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(16));
                    if (!await VerifyDetailsOwnerAsync(selfPuuid, expectedSession, deadline.Token).ConfigureAwait(false))
                    { result.Status = LeaguePlayerDetailsStatus.AccountChanged; return result; }
                    result.Profile = await LoadTargetProfileAsync(targetPuuid, source, serverId, deadline.Token).ConfigureAwait(false);
                    var crossRegion = IsDetailsCrossRegion(expectedSession, serverId);
                    if (!crossRegion)
                    {
                        result.Ranked = ParseDetailsObject(await ReadDetailsBytesAsync(
                            "/lol-ranked/v1/ranked-stats/" + targetPuuid, deadline.Token).ConfigureAwait(false));
                        result.Cosmetics = ParseDetailsObject(await ReadDetailsBytesAsync(
                            "/lol-summoner/v1/summoner-profile?puuid=" + targetPuuid, deadline.Token).ConfigureAwait(false));
                    }
                    if (!await VerifyDetailsOwnerAsync(selfPuuid, expectedSession, deadline.Token).ConfigureAwait(false))
                        return new LeaguePlayerProfileDetails { Status = LeaguePlayerDetailsStatus.AccountChanged };
                    if (result.Profile == null || !string.Equals(result.Profile.PuuId, targetPuuid, StringComparison.OrdinalIgnoreCase))
                        return new LeaguePlayerProfileDetails { Status = LeaguePlayerDetailsStatus.Unavailable };
                    result.Status = crossRegion ? LeaguePlayerDetailsStatus.UnsupportedRegion :
                        result.Ranked != null && result.Cosmetics != null ? LeaguePlayerDetailsStatus.Ready : LeaguePlayerDetailsStatus.Partial;
                    return result;
                }
            }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return new LeaguePlayerProfileDetails { Status = LeaguePlayerDetailsStatus.Unavailable }; }
            finally { _requestGate.Release(); }
        }

        internal async Task<LeaguePlayerMasteryDetails> LoadPlayerMasteriesAsync(string selfPuuid, string targetPuuid,
            LeagueClientSession expectedSession, string serverId, CancellationToken token)
        {
            var result = new LeaguePlayerMasteryDetails { Status = LeaguePlayerDetailsStatus.Unavailable };
            if (!IsSafePuuid(selfPuuid) || !IsSafePuuid(targetPuuid)) return result;
            if (IsDetailsCrossRegion(expectedSession, serverId))
            { result.Status = LeaguePlayerDetailsStatus.UnsupportedRegion; return result; }
            await _requestGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(10));
                    if (!await VerifyDetailsOwnerAsync(selfPuuid, expectedSession, deadline.Token).ConfigureAwait(false))
                    { result.Status = LeaguePlayerDetailsStatus.AccountChanged; return result; }
                    var bytes = await ReadDetailsBytesAsync("/lol-champion-mastery/v1/" + targetPuuid +
                        "/champion-mastery", deadline.Token).ConfigureAwait(false);
                    result.Masteries = ParsePlayerMasteries(bytes, targetPuuid);
                    if (!await VerifyDetailsOwnerAsync(selfPuuid, expectedSession, deadline.Token).ConfigureAwait(false))
                        return new LeaguePlayerMasteryDetails { Status = LeaguePlayerDetailsStatus.AccountChanged };
                    result.Status = result.Masteries == null ? LeaguePlayerDetailsStatus.Unavailable : LeaguePlayerDetailsStatus.Ready;
                    return result;
                }
            }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return new LeaguePlayerMasteryDetails { Status = LeaguePlayerDetailsStatus.Unavailable }; }
            finally { _requestGate.Release(); }
        }

        private async Task<bool> VerifyDetailsOwnerAsync(string self, LeagueClientSession expected, CancellationToken token)
        {
            if (_session != null && !LeagueEncounterReplayService.SessionMatches(expected, _session())) return false;
            var profile = ParseProfile(await ReadDetailsBytesAsync(LeagueDashboardDetailsService.SummonerPath, token).ConfigureAwait(false));
            return (_session == null || LeagueEncounterReplayService.SessionMatches(expected, _session())) &&
                profile != null && string.Equals(profile.PuuId, self, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDetailsCrossRegion(LeagueClientSession session, string serverId)
        {
            return !string.IsNullOrEmpty(serverId) && !string.Equals(serverId,
                LeagueSgpMatchHistoryApiClient.CurrentServerId(session), StringComparison.OrdinalIgnoreCase);
        }

        private async Task<byte[]> ReadDetailsBytesAsync(string path, CancellationToken token)
        {
            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                attempt.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    var bytes = await _client.TryGetBytesAsync(path, attempt.Token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return bytes != null && bytes.Length <= 2 * 1024 * 1024 ? bytes : null;
                }
                catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return null; }
            }
        }

        internal static Dictionary<string, object> ParseDetailsObject(byte[] bytes)
        {
            return ParseDetails(bytes) as Dictionary<string, object>;
        }

        private static object ParseDetails(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024) return null;
            try { return new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024, RecursionLimit = 32 }.DeserializeObject(Encoding.UTF8.GetString(bytes)); }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        internal static List<Dictionary<string, object>> ParsePlayerMasteries(byte[] bytes, string targetPuuid)
        {
            var array = ParseDetails(bytes) as object[];
            if (array == null || array.Length > 512) return null;
            var result = new List<Dictionary<string, object>>();
            var seen = new HashSet<long>();
            foreach (var item in array)
            {
                var row = item as Dictionary<string, object>;
                var id = LeagueMatchTimelineParser.Number(row, "championId");
                var owner = LeagueMatchTimelineParser.Text(row, "puuid");
                if (!(id > 0) || id > int.MaxValue || !seen.Add(id.Value) ||
                    (!string.IsNullOrEmpty(owner) && !string.Equals(owner, targetPuuid, StringComparison.OrdinalIgnoreCase))) return null;
                result.Add(row);
            }
            return result.OrderByDescending(row => LeagueMatchTimelineParser.Number(row, "championPoints") ?? -1)
                .ThenBy(row => LeagueMatchTimelineParser.Number(row, "championId")).ToList();
        }

        internal static Dictionary<string, object> RankedEntry(Dictionary<string, object> ranked, string queue)
        {
            var map = ReadObject(ranked, "queueMap");
            var entry = ReadObject(map, queue);
            if (entry != null)
            {
                var declared = ReadString(entry, "queueType");
                return string.IsNullOrEmpty(declared) || declared == queue ? entry : null;
            }
            var entries = ReadObjects(ranked, "queues").Where(row => ReadString(row, "queueType") == queue).Take(2).ToArray();
            return entries.Length == 1 ? entries[0] : null;
        }
    }
}
