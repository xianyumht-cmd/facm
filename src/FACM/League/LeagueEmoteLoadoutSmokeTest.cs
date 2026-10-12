using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal static class LeagueEmoteLoadoutSmokeTest
    {
        public static void Validate()
        {
            ValidateUniqueAccountLoadoutParsing();
            ValidateAmbiguousLoadoutFailsClosed();
            ValidateClearPayloadIsNarrow();
            ValidateClearUsesOneWriteAndBoundedReadback();
            ValidateIncompleteLoadoutFailsClosed();
            ValidateClientRestoreDoesNotRewriteLoop();
            ValidateDedicatedWriterFence();
        }

        private static void ValidateUniqueAccountLoadoutParsing()
        {
            var fake = new FakeEmoteApi();
            var service = CreateService(fake);
            var snapshot = service.ParseAccountScopeForSmokeTest(fake.BuildAccountScopeBytes());
            Require(snapshot != null && snapshot.Connected && snapshot.LoadoutId == FakeEmoteApi.LoadoutId,
                "Emote account-scope parser did not select the unique complete account loadout.");
            Require(snapshot.EmoteItemIds != null && snapshot.EmoteItemIds.Count == LeagueEmoteLoadoutService.EmoteSlots.Length,
                "Emote account-scope parser lost one or more emote slots.");
        }

        private static void ValidateAmbiguousLoadoutFailsClosed()
        {
            var fake = new FakeEmoteApi { AddSecondCompleteAccountLoadout = true };
            var service = CreateService(fake);
            var snapshot = service.ParseAccountScopeForSmokeTest(fake.BuildAccountScopeBytes());
            Require(snapshot != null && !snapshot.Connected,
                "Multiple complete account emote loadouts must be treated as ambiguous.");

            var result = service.ClearEmotesAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "unavailable" && fake.WriteCount == 0,
                "Ambiguous account loadout selection must fail closed without a PATCH.");
        }

        private static void ValidateClearPayloadIsNarrow()
        {
            var fake = new FakeEmoteApi();
            var service = CreateService(fake);
            var root = new JavaScriptSerializer().DeserializeObject(service.BuildClearPayloadForSmokeTest()) as Dictionary<string, object>;
            Require(root != null && root.Count == 1 && root.ContainsKey("loadout"),
                "Emote cleanup payload must contain only the loadout patch wrapper.");
            var loadout = root["loadout"] as Dictionary<string, object>;
            Require(loadout != null && loadout.Count == LeagueEmoteLoadoutService.EmoteSlots.Length,
                "Emote cleanup payload changed the audited emote slot set.");
            Require(!loadout.ContainsKey("COMPANION_SLOT"),
                "Emote cleanup payload must not mutate unrelated account loadout slots.");

            foreach (var slotName in LeagueEmoteLoadoutService.EmoteSlots)
            {
                var slot = loadout[slotName] as Dictionary<string, object>;
                Require(slot != null && Convert.ToString(slot["inventoryType"], CultureInfo.InvariantCulture) == "EMOTE" &&
                        Convert.ToInt32(slot["itemId"], CultureInfo.InvariantCulture) == -1,
                    "Emote cleanup payload did not clear " + slotName + ".");
            }
        }

        private static void ValidateClearUsesOneWriteAndBoundedReadback()
        {
            var fake = new FakeEmoteApi();
            var service = CreateService(fake);
            var result = service.ClearEmotesAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "success",
                "Emote cleanup did not verify successfully.");
            Require(fake.WriteCount == 1,
                "Emote cleanup must emit exactly one PATCH.");
            Require(fake.ReadCount == 3,
                "Emote cleanup must use one pre-read plus bounded first + settled readback.");
            Require(fake.EmoteItemIds.Values.All(value => value == -1),
                "Emote cleanup did not clear every audited slot.");
            Require(fake.CompanionItemId == 42,
                "Emote cleanup mutated an unrelated account loadout slot.");
        }

        private static void ValidateIncompleteLoadoutFailsClosed()
        {
            var fake = new FakeEmoteApi { OmitOneEmoteSlot = true };
            var service = CreateService(fake);
            var result = service.ClearEmotesAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "unavailable",
                "Incomplete emote loadout did not fail closed.");
            Require(fake.WriteCount == 0,
                "Incomplete emote ownership evidence must not produce a PATCH.");
        }

        private static void ValidateClientRestoreDoesNotRewriteLoop()
        {
            var fake = new FakeEmoteApi { RestoreOneEmoteOnSettledRead = true };
            var service = CreateService(fake);
            var result = service.ClearEmotesAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "overridden",
                "Client-restored emote state must be reported honestly.");
            Require(fake.WriteCount == 1 && fake.ReadCount == 3,
                "Emote override detection must remain one-write and bounded-readback only.");
        }

        private static void ValidateDedicatedWriterFence()
        {
            Require(LeagueEmoteLoadoutWriteApiClient.IsAllowedTargetForSmokeTest("PATCH", FakeEmoteApi.LoadoutId),
                "Emote writer rejected a safe loadout id.");
            Require(!LeagueEmoteLoadoutWriteApiClient.IsAllowedTargetForSmokeTest("PUT", FakeEmoteApi.LoadoutId),
                "Emote writer accepted the wrong HTTP method.");
            Require(!LeagueEmoteLoadoutWriteApiClient.IsAllowedTargetForSmokeTest("PATCH", "../../lol-chat/v1/me"),
                "Emote writer accepted path traversal.");
            Require(!LeagueEmoteLoadoutWriteApiClient.IsAllowedTargetForSmokeTest("PATCH", FakeEmoteApi.LoadoutId + "?force=true"),
                "Emote writer accepted a query-string escape hatch.");
        }

        private static LeagueEmoteLoadoutService CreateService(FakeEmoteApi fake)
        {
            return new LeagueEmoteLoadoutService(fake, fake, TimeSpan.Zero, TimeSpan.Zero);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeEmoteApi : ILeagueClientApi, ILeagueEmoteLoadoutWriteApi
        {
            internal const string LoadoutId = "11111111-2222-3333-4444-555555555555";
            private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
            private readonly Dictionary<string, int> _emotes = new Dictionary<string, int>(StringComparer.Ordinal);

            public FakeEmoteApi()
            {
                for (var index = 0; index < LeagueEmoteLoadoutService.EmoteSlots.Length; index++)
                    _emotes[LeagueEmoteLoadoutService.EmoteSlots[index]] = 1000 + index;
            }

            public bool AddSecondCompleteAccountLoadout { get; set; }
            public bool OmitOneEmoteSlot { get; set; }
            public bool RestoreOneEmoteOnSettledRead { get; set; }
            public int ReadCount { get; private set; }
            public int WriteCount { get; private set; }
            public int CompanionItemId { get; private set; } = 42;
            public IReadOnlyDictionary<string, int> EmoteItemIds { get { return _emotes; } }

            public byte[] BuildAccountScopeBytes()
            {
                var entries = new List<object> { BuildEntry(LoadoutId) };
                if (AddSecondCompleteAccountLoadout)
                    entries.Add(BuildEntry("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
                return Encoding.UTF8.GetBytes(_json.Serialize(entries.ToArray()));
            }

            private Dictionary<string, object> BuildEntry(string id)
            {
                var loadout = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "COMPANION_SLOT", Slot("COMPANION", CompanionItemId) }
                };
                for (var index = 0; index < LeagueEmoteLoadoutService.EmoteSlots.Length; index++)
                {
                    if (OmitOneEmoteSlot && index == LeagueEmoteLoadoutService.EmoteSlots.Length - 1) continue;
                    var slotName = LeagueEmoteLoadoutService.EmoteSlots[index];
                    loadout[slotName] = Slot("EMOTE", _emotes[slotName]);
                }
                return new Dictionary<string, object>
                {
                    { "id", id },
                    { "itemId", null },
                    { "loadout", loadout },
                    { "name", "ACCOUNT" },
                    { "refreshTime", "" },
                    { "scope", "account" }
                };
            }

            private static Dictionary<string, object> Slot(string inventoryType, int itemId)
            {
                return new Dictionary<string, object>
                {
                    { "inventoryType", inventoryType },
                    { "itemId", itemId }
                };
            }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(path, LeagueEmoteLoadoutService.AccountScopePath, StringComparison.Ordinal))
                    return Task.FromResult<byte[]>(null);
                ReadCount++;
                if (RestoreOneEmoteOnSettledRead && ReadCount >= 3)
                    _emotes[LeagueEmoteLoadoutService.EmoteSlots[0]] = 777;
                return Task.FromResult(BuildAccountScopeBytes());
            }

            public Task<LeagueClientWriteResponse> TryPatchEmotesAsync(
                string loadoutId,
                string json,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteCount++;
                Require(string.Equals(loadoutId, LoadoutId, StringComparison.Ordinal),
                    "Emote service targeted the wrong loadout id.");
                var root = _json.DeserializeObject(json ?? string.Empty) as Dictionary<string, object>;
                var loadout = root == null ? null : root["loadout"] as Dictionary<string, object>;
                Require(loadout != null, "Emote writer fake received an invalid patch payload.");
                foreach (var slotName in LeagueEmoteLoadoutService.EmoteSlots)
                {
                    var slot = loadout[slotName] as Dictionary<string, object>;
                    _emotes[slotName] = Convert.ToInt32(slot["itemId"], CultureInfo.InvariantCulture);
                }
                return Task.FromResult(new LeagueClientWriteResponse
                {
                    StatusCode = 200,
                    Body = Encoding.UTF8.GetBytes("{}")
                });
            }
        }
    }
}
