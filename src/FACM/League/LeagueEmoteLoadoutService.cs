using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueEmoteLoadoutSnapshot
    {
        public bool Connected { get; set; }
        public string LoadoutId { get; set; }
        public Dictionary<string, int> EmoteItemIds { get; set; }
    }

    internal sealed class LeagueEmoteLoadoutApplyResult
    {
        public string Status { get; set; }
        public LeagueEmoteLoadoutSnapshot Observed { get; set; }
    }

    /// <summary>
    /// Explicit emote-wheel cleanup. The account-scope collection is read only on demand. FACM
    /// selects exactly one account loadout that exposes the complete emote-slot contract, emits one
    /// fenced PATCH that touches only those slots, then performs bounded first + settled readback.
    /// Ambiguous/missing ownership fails closed and FACM never runs a rewrite loop.
    /// </summary>
    internal sealed class LeagueEmoteLoadoutService
    {
        internal const string AccountScopePath = "/lol-loadouts/v4/loadouts/scope/account";
        internal static readonly string[] EmoteSlots =
        {
            "EMOTES_ACE",
            "EMOTES_FIRST_BLOOD",
            "EMOTES_VICTORY",
            "EMOTES_WHEEL_CENTER",
            "EMOTES_WHEEL_UPPER",
            "EMOTES_WHEEL_RIGHT",
            "EMOTES_WHEEL_UPPER_RIGHT",
            "EMOTES_WHEEL_UPPER_LEFT",
            "EMOTES_WHEEL_LOWER",
            "EMOTES_START",
            "EMOTES_WHEEL_LEFT",
            "EMOTES_WHEEL_LOWER_RIGHT",
            "EMOTES_WHEEL_LOWER_LEFT"
        };

        private readonly ILeagueClientApi _client;
        private readonly ILeagueEmoteLoadoutWriteApi _writer;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 512 * 1024 };
        private readonly TimeSpan _firstVerificationDelay;
        private readonly TimeSpan _settleVerificationDelay;

        public LeagueEmoteLoadoutService(ILeagueClientApi client, ILeagueEmoteLoadoutWriteApi writer)
            : this(client, writer, TimeSpan.FromMilliseconds(180), TimeSpan.FromMilliseconds(320))
        {
        }

        internal LeagueEmoteLoadoutService(
            ILeagueClientApi client,
            ILeagueEmoteLoadoutWriteApi writer,
            TimeSpan firstVerificationDelay,
            TimeSpan settleVerificationDelay)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _firstVerificationDelay = firstVerificationDelay < TimeSpan.Zero ? TimeSpan.Zero : firstVerificationDelay;
            _settleVerificationDelay = settleVerificationDelay < TimeSpan.Zero ? TimeSpan.Zero : settleVerificationDelay;
        }

        public async Task<LeagueEmoteLoadoutSnapshot> ReadCurrentAsync(CancellationToken cancellationToken)
        {
            var bytes = await ReadWithTimeoutAsync(AccountScopePath, cancellationToken).ConfigureAwait(false);
            return ParseAccountScope(bytes);
        }

        public async Task<LeagueEmoteLoadoutApplyResult> ClearEmotesAsync(CancellationToken cancellationToken)
        {
            var current = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (current == null || !current.Connected || !LeagueEmoteLoadoutWriteApiClient.IsSafeLoadoutId(current.LoadoutId))
            {
                AppLog.Info("League account emote loadout was missing or ambiguous; cleanup skipped.");
                return Result("unavailable", current);
            }

            var payload = BuildClearPayload();
            var response = await _writer.TryPatchEmotesAsync(current.LoadoutId, payload, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                AppLog.Info("League emote cleanup PATCH rejected; status=" +
                            (response == null ? 0 : response.StatusCode));
                return Result("write-failed", current);
            }

            if (_firstVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_firstVerificationDelay, cancellationToken).ConfigureAwait(false);
            var first = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (!CanVerifySameLoadout(current, first))
            {
                AppLog.Info("League emote cleanup first readback unavailable or ownership changed.");
                return Result("unverified", first);
            }
            if (!AllEmotesCleared(first))
            {
                AppLog.Info("League emote cleanup first readback did not match cleared state.");
                return Result("overridden", first);
            }

            if (_settleVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_settleVerificationDelay, cancellationToken).ConfigureAwait(false);
            var settled = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (!CanVerifySameLoadout(current, settled))
            {
                AppLog.Info("League emote cleanup settled readback unavailable or ownership changed.");
                return Result("unverified", settled);
            }
            if (!AllEmotesCleared(settled))
            {
                AppLog.Info("League client restored one or more emote slots after cleanup.");
                return Result("overridden", settled);
            }

            AppLog.Info("League emote loadout cleared and verified.");
            return Result("success", settled);
        }

        internal LeagueEmoteLoadoutSnapshot ParseAccountScopeForSmokeTest(byte[] bytes)
        {
            return ParseAccountScope(bytes);
        }

        internal string BuildClearPayloadForSmokeTest()
        {
            return BuildClearPayload();
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

        private LeagueEmoteLoadoutSnapshot ParseAccountScope(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return new LeagueEmoteLoadoutSnapshot();
            try
            {
                var root = _json.DeserializeObject(Encoding.UTF8.GetString(bytes));
                var enumerable = root as IEnumerable;
                if (enumerable == null || root is string) return new LeagueEmoteLoadoutSnapshot();

                LeagueEmoteLoadoutSnapshot selected = null;
                var candidateCount = 0;
                foreach (var item in enumerable)
                {
                    var entry = item as Dictionary<string, object>;
                    if (entry == null) continue;
                    var scope = ReadScalar(entry, "scope");
                    if (!string.Equals(scope, "account", StringComparison.OrdinalIgnoreCase)) continue;

                    var loadoutId = ReadScalar(entry, "id");
                    if (!LeagueEmoteLoadoutWriteApiClient.IsSafeLoadoutId(loadoutId)) continue;
                    var loadout = ReadDictionary(entry, "loadout");
                    Dictionary<string, int> emotes;
                    if (!TryReadCompleteEmoteSlots(loadout, out emotes)) continue;

                    candidateCount++;
                    selected = new LeagueEmoteLoadoutSnapshot
                    {
                        Connected = true,
                        LoadoutId = loadoutId,
                        EmoteItemIds = emotes
                    };
                }

                if (candidateCount == 1) return selected;
                if (candidateCount > 1)
                    AppLog.Info("League account-scope loadout selection is ambiguous; emote cleanup will fail closed.");
                return new LeagueEmoteLoadoutSnapshot();
            }
            catch
            {
                return new LeagueEmoteLoadoutSnapshot();
            }
        }

        private string BuildClearPayload()
        {
            var loadout = new Dictionary<string, object>(StringComparer.Ordinal);
            for (var index = 0; index < EmoteSlots.Length; index++)
            {
                loadout[EmoteSlots[index]] = new Dictionary<string, object>
                {
                    { "inventoryType", "EMOTE" },
                    { "itemId", -1 }
                };
            }
            return _json.Serialize(new Dictionary<string, object> { { "loadout", loadout } });
        }

        private static bool CanVerifySameLoadout(
            LeagueEmoteLoadoutSnapshot expected,
            LeagueEmoteLoadoutSnapshot observed)
        {
            return expected != null && expected.Connected &&
                   observed != null && observed.Connected &&
                   string.Equals(expected.LoadoutId ?? string.Empty, observed.LoadoutId ?? string.Empty,
                       StringComparison.Ordinal);
        }

        private static bool AllEmotesCleared(LeagueEmoteLoadoutSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Connected || snapshot.EmoteItemIds == null) return false;
            for (var index = 0; index < EmoteSlots.Length; index++)
            {
                int itemId;
                if (!snapshot.EmoteItemIds.TryGetValue(EmoteSlots[index], out itemId) || itemId != -1)
                    return false;
            }
            return true;
        }

        private static bool TryReadCompleteEmoteSlots(
            Dictionary<string, object> loadout,
            out Dictionary<string, int> emotes)
        {
            emotes = new Dictionary<string, int>(StringComparer.Ordinal);
            if (loadout == null) return false;
            for (var index = 0; index < EmoteSlots.Length; index++)
            {
                object slotValue;
                if (!loadout.TryGetValue(EmoteSlots[index], out slotValue)) return false;
                var slot = slotValue as Dictionary<string, object>;
                if (slot == null) return false;
                object itemIdValue;
                if (!slot.TryGetValue("itemId", out itemIdValue) || itemIdValue == null) return false;
                try
                {
                    emotes[EmoteSlots[index]] = Convert.ToInt32(itemIdValue, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value)
                ? value as Dictionary<string, object>
                : null;
        }

        private static string ReadScalar(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty;
        }

        private static LeagueEmoteLoadoutApplyResult Result(string status, LeagueEmoteLoadoutSnapshot observed)
        {
            return new LeagueEmoteLoadoutApplyResult
            {
                Status = status,
                Observed = observed
            };
        }
    }
}
