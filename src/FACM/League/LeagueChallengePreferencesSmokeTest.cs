using System;
using System.Collections.Generic;
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
            ValidatePayloadIsNarrow();
            ValidateApplyUsesOneWriteAndBoundedReadback();
            ValidateUnavailableFailsClosed();
            ValidateOverrideDoesNotRewriteLoop();
            ValidateDedicatedWriterFence();
        }

        private static void ValidateParsing()
        {
            var fake = new FakeChallengePreferencesApi();
            var service = CreateService(fake);
            var topLevel = service.ParseForSmokeTest(Encoding.UTF8.GetBytes("{\"bannerAccent\":2}"));
            Require(topLevel != null && topLevel.Connected && topLevel.BannerAccent == "2",
                "Challenge-preferences parser did not accept numeric top-level bannerAccent.");

            var nested = service.ParseForSmokeTest(Encoding.UTF8.GetBytes(
                "{\"preferences\":{\"bannerAccent\":\"7\"}}"));
            Require(nested != null && nested.Connected && nested.BannerAccent == "7",
                "Challenge-preferences parser did not accept nested preference bannerAccent fallback.");
        }

        private static void ValidatePayloadIsNarrow()
        {
            var fake = new FakeChallengePreferencesApi();
            var service = CreateService(fake);
            var payload = service.BuildBannerPayloadForSmokeTest(LeagueChallengePreferencesService.LastSeasonBannerAccent);
            var root = new JavaScriptSerializer().DeserializeObject(payload) as Dictionary<string, object>;
            Require(root != null && root.Count == 1,
                "Last-season banner payload must mutate only bannerAccent.");
            Require(Convert.ToString(root["bannerAccent"]) == LeagueChallengePreferencesService.LastSeasonBannerAccent,
                "Last-season banner payload lost the audited bannerAccent value.");
            Require(service.BuildBannerPayloadForSmokeTest("not-a-number") == null,
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
        }

        private static void ValidateUnavailableFailsClosed()
        {
            var fake = new FakeChallengePreferencesApi { ReturnUnavailable = true };
            var service = CreateService(fake);
            var result = service.ApplyLastSeasonBannerAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "unavailable",
                "Unavailable challenge summary did not fail closed.");
            Require(fake.WriteCount == 0,
                "Unavailable challenge summary must not write player preferences.");
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

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeChallengePreferencesApi : ILeagueClientApi, ILeagueChallengePreferencesWriteApi
        {
            private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
            private string _bannerAccent = "1";

            public bool ReturnUnavailable { get; set; }
            public bool OverrideOnSettledRead { get; set; }
            public int ReadCount { get; private set; }
            public int WriteCount { get; private set; }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(path, LeagueChallengePreferencesService.SummaryPath, StringComparison.Ordinal))
                    return Task.FromResult<byte[]>(null);

                ReadCount++;
                if (ReturnUnavailable) return Task.FromResult<byte[]>(null);
                if (OverrideOnSettledRead && ReadCount >= 3) _bannerAccent = "1";

                return Task.FromResult(Encoding.UTF8.GetBytes(
                    "{\"bannerAccent\":\"" + _bannerAccent + "\",\"title\":{\"itemId\":123},\"topChallenges\":[{\"id\":456}]}"));
            }

            public Task<LeagueClientWriteResponse> TryUpdatePlayerPreferencesAsync(
                string json,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteCount++;
                var root = _json.DeserializeObject(json ?? string.Empty) as Dictionary<string, object>;
                object value;
                if (root != null && root.TryGetValue("bannerAccent", out value) && value != null)
                    _bannerAccent = Convert.ToString(value);

                return Task.FromResult(new LeagueClientWriteResponse
                {
                    StatusCode = 200,
                    Body = Encoding.UTF8.GetBytes("{}")
                });
            }
        }
    }
}
