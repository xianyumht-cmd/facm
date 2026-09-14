using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueProfileChampionOption
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    internal sealed class LeagueProfileSkinOption
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string SplashPath { get; set; }
    }

    internal sealed class LeagueProfileBackgroundApplyResult
    {
        public string Status { get; set; }
        public int SkinId { get; set; }
        public int ObservedSkinId { get; set; }
    }

    /// <summary>
    /// Explicit, user-directed summoner profile customization. Reads local LCU game-data only when
    /// the profile UI is opened or the user chooses a champion. Writes are delegated to the narrow
    /// ILeagueProfileWriteApi owner and are verified against the current-summoner profile document.
    /// There is no background polling, external provider or inventory mutation.
    /// </summary>
    internal sealed class LeagueProfileCustomizationService
    {
        internal const string ChampionSummaryPath = "/lol-game-data/assets/v1/champion-summary.json";
        internal const string ChampionDetailsPathPrefix = "/lol-game-data/assets/v1/champions/";
        internal const string CurrentSummonerProfilePath = "/lol-summoner/v1/current-summoner/summoner-profile";

        private readonly ILeagueClientApi _client;
        private readonly ILeagueProfileWriteApi _writer;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
        private readonly TimeSpan _firstVerificationDelay;
        private readonly TimeSpan _settleVerificationDelay;

        public LeagueProfileCustomizationService(ILeagueClientApi client, ILeagueProfileWriteApi writer)
            : this(client, writer, TimeSpan.FromMilliseconds(180), TimeSpan.FromMilliseconds(320))
        {
        }

        internal LeagueProfileCustomizationService(
            ILeagueClientApi client,
            ILeagueProfileWriteApi writer,
            TimeSpan firstVerificationDelay,
            TimeSpan settleVerificationDelay)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _firstVerificationDelay = firstVerificationDelay < TimeSpan.Zero ? TimeSpan.Zero : firstVerificationDelay;
            _settleVerificationDelay = settleVerificationDelay < TimeSpan.Zero ? TimeSpan.Zero : settleVerificationDelay;
        }

        public async Task<IReadOnlyList<LeagueProfileChampionOption>> LoadChampionsAsync(CancellationToken cancellationToken)
        {
            var bytes = await ReadWithTimeoutAsync(ChampionSummaryPath, cancellationToken).ConfigureAwait(false);
            return ParseChampionSummary(bytes);
        }

        public async Task<IReadOnlyList<LeagueProfileSkinOption>> LoadSkinsAsync(int championId, CancellationToken cancellationToken)
        {
            if (championId <= 0) return Array.Empty<LeagueProfileSkinOption>();
            var bytes = await ReadWithTimeoutAsync(
                ChampionDetailsPathPrefix + championId.ToString(CultureInfo.InvariantCulture) + ".json",
                cancellationToken).ConfigureAwait(false);
            return ParseChampionSkins(bytes);
        }

        public async Task<int> ReadCurrentBackgroundSkinIdAsync(CancellationToken cancellationToken)
        {
            var bytes = await ReadWithTimeoutAsync(CurrentSummonerProfilePath, cancellationToken).ConfigureAwait(false);
            return ParseBackgroundSkinId(bytes);
        }

        public async Task<LeagueProfileBackgroundApplyResult> ApplyBackgroundSkinAsync(
            int skinId,
            CancellationToken cancellationToken)
        {
            if (skinId <= 0)
                return new LeagueProfileBackgroundApplyResult { Status = "invalid", SkinId = skinId };

            var payload = BuildBackgroundSkinPayload(skinId);
            var response = await _writer.TrySetSummonerProfileAsync(payload, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                AppLog.Info("League profile background write rejected; skinId=" + skinId +
                            "; status=" + (response == null ? 0 : response.StatusCode));
                return new LeagueProfileBackgroundApplyResult { Status = "write-failed", SkinId = skinId };
            }

            if (_firstVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_firstVerificationDelay, cancellationToken).ConfigureAwait(false);
            var first = await ReadCurrentBackgroundSkinIdAsync(cancellationToken).ConfigureAwait(false);
            if (first <= 0)
            {
                AppLog.Info("League profile background readback unavailable; skinId=" + skinId + "; stage=first");
                return new LeagueProfileBackgroundApplyResult
                {
                    Status = "unverified",
                    SkinId = skinId,
                    ObservedSkinId = first
                };
            }
            if (first != skinId)
            {
                AppLog.Info("League profile background readback did not match; requested=" + skinId + "; observed=" + first);
                return new LeagueProfileBackgroundApplyResult
                {
                    Status = "overridden",
                    SkinId = skinId,
                    ObservedSkinId = first
                };
            }

            if (_settleVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_settleVerificationDelay, cancellationToken).ConfigureAwait(false);
            var settled = await ReadCurrentBackgroundSkinIdAsync(cancellationToken).ConfigureAwait(false);
            if (settled <= 0)
            {
                AppLog.Info("League profile background settled readback unavailable; skinId=" + skinId);
                return new LeagueProfileBackgroundApplyResult
                {
                    Status = "unverified",
                    SkinId = skinId,
                    ObservedSkinId = settled
                };
            }
            if (settled != skinId)
            {
                AppLog.Info("League profile background was overwritten by client; requested=" + skinId + "; observed=" + settled);
                return new LeagueProfileBackgroundApplyResult
                {
                    Status = "overridden",
                    SkinId = skinId,
                    ObservedSkinId = settled
                };
            }

            AppLog.Info("League profile background applied and verified; skinId=" + skinId);
            return new LeagueProfileBackgroundApplyResult
            {
                Status = "success",
                SkinId = skinId,
                ObservedSkinId = settled
            };
        }

        internal IReadOnlyList<LeagueProfileChampionOption> ParseChampionSummaryForSmokeTest(byte[] bytes)
        {
            return ParseChampionSummary(bytes);
        }

        internal IReadOnlyList<LeagueProfileSkinOption> ParseChampionSkinsForSmokeTest(byte[] bytes)
        {
            return ParseChampionSkins(bytes);
        }

        internal int ParseBackgroundSkinIdForSmokeTest(byte[] bytes)
        {
            return ParseBackgroundSkinId(bytes);
        }

        internal string BuildBackgroundSkinPayloadForSmokeTest(int skinId)
        {
            return skinId <= 0 ? null : BuildBackgroundSkinPayload(skinId);
        }

        private async Task<byte[]> ReadWithTimeoutAsync(string path, CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    return await _client.TryGetBytesAsync(path, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    return null;
                }
            }
        }

        private IReadOnlyList<LeagueProfileChampionOption> ParseChampionSummary(byte[] bytes)
        {
            object root;
            if (!TryDeserialize(bytes, out root)) return Array.Empty<LeagueProfileChampionOption>();

            var result = new List<LeagueProfileChampionOption>();
            var seen = new HashSet<int>();
            foreach (var item in EnumerateDictionaries(root))
            {
                var id = ReadInt(item, "id");
                var name = ReadString(item, "name").Trim();
                if (id <= 0 || name.Length == 0 || !seen.Add(id)) continue;
                result.Add(new LeagueProfileChampionOption { Id = id, Name = name });
            }

            return result
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Id)
                .ToArray();
        }

        private IReadOnlyList<LeagueProfileSkinOption> ParseChampionSkins(byte[] bytes)
        {
            object rootValue;
            if (!TryDeserialize(bytes, out rootValue)) return Array.Empty<LeagueProfileSkinOption>();
            var root = rootValue as Dictionary<string, object>;
            if (root == null) return Array.Empty<LeagueProfileSkinOption>();

            var result = new List<LeagueProfileSkinOption>();
            var seen = new HashSet<int>();
            object skins;
            if (root.TryGetValue("skins", out skins))
            {
                foreach (var skin in EnumerateDictionaries(skins))
                {
                    AddSkin(result, seen, skin);
                    var quest = ReadDictionary(skin, "questSkinInfo");
                    if (quest == null) continue;
                    object tiers;
                    if (!quest.TryGetValue("tiers", out tiers)) continue;
                    foreach (var tier in EnumerateDictionaries(tiers)) AddSkin(result, seen, tier);
                }
            }

            return result.ToArray();
        }

        private static void AddSkin(
            ICollection<LeagueProfileSkinOption> target,
            ISet<int> seen,
            Dictionary<string, object> source)
        {
            var id = ReadInt(source, "id");
            if (id <= 0 || !seen.Add(id)) return;
            var name = ReadString(source, "name").Trim();
            if (name.Length == 0) name = "皮肤 " + id.ToString(CultureInfo.InvariantCulture);
            target.Add(new LeagueProfileSkinOption
            {
                Id = id,
                Name = name,
                SplashPath = ReadString(source, "uncenteredSplashPath")
            });
        }

        private int ParseBackgroundSkinId(byte[] bytes)
        {
            object rootValue;
            if (!TryDeserialize(bytes, out rootValue)) return 0;
            return ReadInt(rootValue as Dictionary<string, object>, "backgroundSkinId");
        }

        private string BuildBackgroundSkinPayload(int skinId)
        {
            return _json.Serialize(new Dictionary<string, object>
            {
                { "key", "backgroundSkinId" },
                { "value", skinId }
            });
        }

        private bool TryDeserialize(byte[] bytes, out object value)
        {
            value = null;
            if (bytes == null || bytes.Length == 0) return false;
            try
            {
                value = _json.DeserializeObject(Encoding.UTF8.GetString(bytes));
                return value != null;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<Dictionary<string, object>> EnumerateDictionaries(object value)
        {
            var dictionary = value as Dictionary<string, object>;
            if (dictionary != null)
            {
                yield return dictionary;
                yield break;
            }

            var enumerable = value as IEnumerable;
            if (enumerable == null || value is string) yield break;
            foreach (var item in enumerable)
            {
                var child = item as Dictionary<string, object>;
                if (child != null) yield return child;
            }
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value)
                ? value as Dictionary<string, object>
                : null;
        }

        private static int ReadInt(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return 0;
            if (value is int) return (int)value;
            if (value is long) return unchecked((int)(long)value);
            if (value is decimal) return (int)(decimal)value;
            int parsed;
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty;
        }
    }
}
