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
    internal static class LeagueChallengePreferencesSmokeTest
    {
        public static void Validate()
        {
            ValidateParsing();
            ValidatePayloadPreservesCurrentPreferences();
            ValidateApplyUsesOneWriteAndBoundedReadback();
            ValidateIncompleteSummaryFailsClosed();
            ValidateOverrideDoesNotRewriteLoop();
            ValidatePreservationDriftIsNotSuccess();
            ValidateDedicatedWriterFence();
        }

        private static void ValidateParsing()
        {
            var fake = new FakeChallengePreferencesApi();
            var service = CreateService(fake);
            var topLevel = service.ParseForSmokeTest(Encoding.UTF8.GetBytes(
                "{\"bannerId\":1,\"title\":{\"itemId\":123},\"crestId\":\"9\",\"prestigeCrestBorderLevel\":500," +
                "\"topChallenges\":[{\"id\":456},{\"id\":789}],\"signedJWTPayload\":{\"tokensByType\":{\"a\":\"b\"}}}"));
            Require(topLevel != null && topLevel.Connected && topLevel.CanPreservePreferences,
                "Challenge-preferences parser did not reconstruct top-level preservation state.");
            Require(topLevel.BannerAccent == "1" && topLevel.Title == "123" && topLevel.CrestBorder == "9" &&
                    topLevel.PrestigeCrestBorderLevel == 500 && topLevel.ChallengeIds.SequenceEqual(new long[] { 456, 789 }),
                "Challenge-preferences parser lost top-level preference values.");
            Require(topLevel.SignedJwtPayload != null,
                "Challenge-preferences parser did not preserve signedJWTPayload when exposed.");

            var nested = service.ParseForSmokeTest(Encoding.UTF8.GetBytes(
                "{\"preferences\":{\"bannerAccent\":\"7\",\"title\":\"234\",\"crestBorder\":\"8\"," +
                "\"prestigeCrestBorderLevel\":300,\"challengeIds\":[111,222]}}"));
            Require(nested != null && nested.CanPreservePreferences && nested.BannerAccent == "7" && nested.Title == "234" &&
                    nested.CrestBorder == "8" && nested.PrestigeCrestBorderLevel == 300 &&
                    nested.ChallengeIds.SequenceEqual(new long[] { 111, 222 }),
                "Challenge-preferences parser did not accept nested preference fallback.");
        }

        private static void ValidatePayloadPreservesCurrentPreferences()
        {
            var fake = new FakeChallengePreferencesApi();
            var service = CreateService(fake);
            var current = service.ParseForSmokeTest(fake.BuildSummaryBytes());
            var payload = service.BuildBannerPayloadForSmokeTest(
                current,
                LeagueChallengePreferencesService.LastSeasonBannerAccent);
            var root = new JavaScriptSerializer().DeserializeObject(payload) as Dictionary<string, object>;

            Require(root != null,
                "Last-season banner payload was not valid JSON.");
            Require(Convert.ToString(root["bannerAccent"], CultureInfo.InvariantCulture) ==
                    LeagueChallengePreferencesService.LastSeasonBannerAccent,
                "Last-season banner payload lost the requested bannerAccent value.");
            Require(Convert.ToString(root["title"], CultureInfo.InvariantCulture) == "123",
                "Last-season banner payload did not preserve the current title.");
            Require(Convert.ToString(root["crestBorder"], CultureInfo.InvariantCulture) == "9",
                "Last-season banner payload did not preserve the current crest border.");
            Require(Convert.ToInt32(root["prestigeCrestBorderLevel"], CultureInfo.InvariantCulture) == 500,
                "Last-season banner payload did not preserve prestige crest level.");
            Require(ReadIds(root["challengeIds"]).SequenceEqual(new long[] { 456, 789 }),
                "Last-season banner payload did not preserve challenge tokens.");
            Require(root.ContainsKey("signedJWTPayload"),
                "Last-season banner payload dropped signedJWTPayload that was exposed by the current summary.");
            Require(!root.ContainsKey("topChallenges"),
                "Last-season banner payload leaked summary-only topChallenges into the write document.");
            Require(service.BuildBannerPayloadForSmokeTest(current, "not-a-number") == null,
                "Invalid bannerAccent should not produce a write payload.");
        }

        private static void ValidateApplyUsesOneWriteAndBoundedReadback()
        {
            var fake = new FakeChallengePreferencesApi();
            var service = CreateService(fake);
            var result = service.ApplyLastSeasonBannerAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "success",
                "Last-season banner change did not verify successfully.");
            Require(fake.WriteCount == 1,
                "Last-season banner apply must emit exactly one POST.");
            Require(fake.ReadCount == 3,
                "Last-season banner apply must use one pre-read plus bounded first + settled readback.");
            Require(result.Observed != null &&
                    result.Observed.BannerAccent == LeagueChallengePreferencesService.LastSeasonBannerAccent,
                "Last-season banner readback lost the requested bannerAccent.");
            Require(fake.Title == "123" && fake.CrestBorder == "9" && fake.PrestigeLevel == 500 &&
                    fake.ChallengeIds.SequenceEqual(new long[] { 456, 789 }),
                "Replacement-style banner update changed unrelated challenge preferences.");
        }

        private static void ValidateIncompleteSummaryFailsClosed()
        {
            var fake = new FakeChallengePreferencesApi { OmitPreservationEvidence = true };
            var service = CreateService(fake);
            var result = service.ApplyLastSeasonBannerAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "unavailable",
                "Incomplete challenge summary did not fail closed.");
            Require(fake.WriteCount == 0,
                "Incomplete challenge summary must not risk a replacement-style preference write.");
        }

        private static void ValidateOverrideDoesNotRewriteLoop()
        {
            var fake = new FakeChallengePreferencesApi { OverrideOnSettledRead = true };
            var service = CreateService(fake);
            var result = service.ApplyLastSeasonBannerAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "overridden",
                "Banner-accent overwrite must be reported honestly.");
            Require(fake.WriteCount == 1,
                "FACM must not fight the League client with a banner-accent rewrite loop.");
            Require(fake.ReadCount == 3,
                "Banner-accent override escaped the bounded verification contract.");
        }

        private static void ValidatePreservationDriftIsNotSuccess()
        {
            var fake = new FakeChallengePreferencesApi { ChangeTitleOnSettledRead = true };
            var service = CreateService(fake);
            var result = service.ApplyLastSeasonBannerAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "overridden",
                "Unrelated challenge-preference drift was incorrectly reported as verified success.");
            Require(fake.WriteCount == 1,
                "Preservation drift must not start a rewrite loop.");
        }

        private static void ValidateDedicatedWriterFence()
        {
            Require(LeagueChallengePreferencesWriteApiClient.IsAllowedTargetForSmokeTest(
                    "POST", LeagueChallengePreferencesWriteApiClient.UpdatePath),
                "Challenge-preferences writer blocked its exact endpoint.");
            Require(!LeagueChallengePreferencesWriteApiClient.IsAllowedTargetForSmokeTest(
                    "PUT", LeagueChallengePreferencesWriteApiClient.UpdatePath),
                "Challenge-preferences writer accepted the wrong HTTP method.");
            Require(!LeagueChallengePreferencesWriteApiClient.IsAllowedTargetForSmokeTest(
                    "POST", LeagueChallengePreferencesWriteApiClient.UpdatePath + "?force=true"),
                "Challenge-preferences writer accepted a query-string escape hatch.");
            Require(!LeagueChallengePreferencesWriteApiClient.IsAllowedTargetForSmokeTest(
                    "POST", "/lol-chat/v1/me"),
                "Challenge-preferences writer escaped into chat presence.");
            Require(!LeagueChallengePreferencesWriteApiClient.IsAllowedTargetForSmokeTest(
                    "POST", "/lol-regalia/v2/current-summoner/regalia"),
                "Challenge-preferences writer escaped into regalia mutation.");
        }

        private static LeagueChallengePreferencesService CreateService(FakeChallengePreferencesApi fake)
        {
            return new LeagueChallengePreferencesService(fake, fake, TimeSpan.Zero, TimeSpan.Zero);
        }

        private static List<long> ReadIds(object value)
        {
            var result = new List<long>();
            var enumerable = value as IEnumerable;
            if (enumerable == null || value is string) return result;
            foreach (var item in enumerable)
                result.Add(Convert.ToInt64(item, CultureInfo.InvariantCulture));
            return result;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeChallengePreferencesApi : ILeagueClientApi, ILeagueChallengePreferencesWriteApi
        {
            private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
            private string _bannerAccent = "1";
            private string _title = "123";
            private string _crestBorder = "9";
            private int _prestigeLevel = 500;
            private List<long> _challengeIds = new List<long> { 456, 789 };
            private Dictionary<string, object> _signedJwtPayload = new Dictionary<string, object>
            {
                { "tokensByType", new Dictionary<string, object> { { "sample", "value" } } }
            };

            public bool ReturnUnavailable { get; set; }
            public bool OmitPreservationEvidence { get; set; }
            public bool OverrideOnSettledRead { get; set; }
            public bool ChangeTitleOnSettledRead { get; set; }
            public int ReadCount { get; private set; }
            public int WriteCount { get; private set; }
            public string Title { get { return _title; } }
            public string CrestBorder { get { return _crestBorder; } }
            public int PrestigeLevel { get { return _prestigeLevel; } }
            public IReadOnlyList<long> ChallengeIds { get { return _challengeIds; } }

            public byte[] BuildSummaryBytes()
            {
                if (OmitPreservationEvidence)
                    return Encoding.UTF8.GetBytes(_json.Serialize(new Dictionary<string, object>
                    {
                        { "bannerAccent", _bannerAccent }
                    }));

                var topChallenges = _challengeIds.Select(id => (object)new Dictionary<string, object> { { "id", id } }).ToArray();
                var root = new Dictionary<string, object>
                {
                    { "bannerAccent", _bannerAccent },
                    { "title", new Dictionary<string, object> { { "itemId", _title } } },
                    { "crestId", _crestBorder },
                    { "prestigeCrestBorderLevel", _prestigeLevel },
                    { "topChallenges", topChallenges },
                    { "signedJWTPayload", _signedJwtPayload }
                };
                return Encoding.UTF8.GetBytes(_json.Serialize(root));
            }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(path, LeagueChallengePreferencesService.SummaryPath, StringComparison.Ordinal))
                    return Task.FromResult<byte[]>(null);

                ReadCount++;
                if (ReturnUnavailable) return Task.FromResult<byte[]>(null);
                if (OverrideOnSettledRead && ReadCount >= 3) _bannerAccent = "1";
                if (ChangeTitleOnSettledRead && ReadCount >= 3) _title = "999";
                return Task.FromResult(BuildSummaryBytes());
            }

            public Task<LeagueClientWriteResponse> TryUpdatePlayerPreferencesAsync(
                string json,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteCount++;
                var root = _json.DeserializeObject(json ?? string.Empty) as Dictionary<string, object>;

                object value;
                _bannerAccent = root != null && root.TryGetValue("bannerAccent", out value) && value != null
                    ? Convert.ToString(value, CultureInfo.InvariantCulture)
                    : string.Empty;
                _title = root != null && root.TryGetValue("title", out value) && value != null
                    ? Convert.ToString(value, CultureInfo.InvariantCulture)
                    : string.Empty;
                _crestBorder = root != null && root.TryGetValue("crestBorder", out value) && value != null
                    ? Convert.ToString(value, CultureInfo.InvariantCulture)
                    : string.Empty;
                _prestigeLevel = root != null && root.TryGetValue("prestigeCrestBorderLevel", out value) && value != null
                    ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    : 0;
                _challengeIds = root != null && root.TryGetValue("challengeIds", out value)
                    ? ReadIds(value)
                    : new List<long>();
                _signedJwtPayload = root != null && root.TryGetValue("signedJWTPayload", out value)
                    ? value as Dictionary<string, object>
                    : null;

                return Task.FromResult(new LeagueClientWriteResponse
                {
                    StatusCode = 200,
                    Body = Encoding.UTF8.GetBytes("{}")
                });
            }
        }
    }
}
