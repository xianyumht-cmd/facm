using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal static class LeagueProfileCustomizationSmokeTest
    {
        public static void Validate()
        {
            ValidateChampionSummaryParsing();
            ValidateSkinParsingAndQuestTierDeduplication();
            ValidateBackgroundPayload();
            ValidateBackgroundApplyUsesOneWriteAndReadback();
            ValidateInvalidBackgroundFailsClosed();
            ValidateBackgroundOverrideDoesNotRewriteLoop();
            ValidateDedicatedWriterFence();
            ValidateUiTextDefaults();
        }

        private static void ValidateChampionSummaryParsing()
        {
            var fake = new FakeProfileApi();
            var service = CreateService(fake);
            var parsed = service.ParseChampionSummaryForSmokeTest(Encoding.UTF8.GetBytes(
                "[{\"id\":2,\"name\":\"Olaf\"},{\"id\":1,\"name\":\"Annie\"},{\"id\":-1,\"name\":\"None\"},{\"id\":1,\"name\":\"Duplicate\"}]"));
            Require(parsed.Count == 2, "Profile champion parser did not filter invalid/duplicate IDs.");
            Require(parsed[0].Id == 1 && parsed[0].Name == "Annie", "Profile champion parser did not keep deterministic name ordering.");
            Require(parsed[1].Id == 2 && parsed[1].Name == "Olaf", "Profile champion parser lost the second valid champion.");
        }

        private static void ValidateSkinParsingAndQuestTierDeduplication()
        {
            var fake = new FakeProfileApi();
            var service = CreateService(fake);
            var parsed = service.ParseChampionSkinsForSmokeTest(Encoding.UTF8.GetBytes(
                "{\"id\":1,\"skins\":[" +
                "{\"id\":1000,\"name\":\"经典\",\"uncenteredSplashPath\":\"/classic.jpg\"}," +
                "{\"id\":1001,\"name\":\"任务皮肤\",\"questSkinInfo\":{\"tiers\":[" +
                "{\"id\":1001,\"name\":\"重复层级\"},{\"id\":1002,\"name\":\"任务二阶\",\"uncenteredSplashPath\":\"/tier2.jpg\"}]}}]}"));
            Require(parsed.Count == 3, "Profile skin parser did not retain base skins plus unique quest tiers.");
            Require(parsed[0].Id == 1000 && parsed[1].Id == 1001 && parsed[2].Id == 1002,
                "Profile skin parser changed source order or failed quest-tier de-duplication.");
            Require(parsed[2].SplashPath == "/tier2.jpg", "Profile skin parser lost local splash metadata.");
        }

        private static void ValidateBackgroundPayload()
        {
            var fake = new FakeProfileApi();
            var service = CreateService(fake);
            var payload = service.BuildBackgroundSkinPayloadForSmokeTest(266001);
            var root = new JavaScriptSerializer().DeserializeObject(payload) as Dictionary<string, object>;
            Require(root != null, "Profile background payload could not be parsed.");
            Require(ReadString(root, "key") == "backgroundSkinId", "Profile background payload used the wrong profile key.");
            Require(ReadInt(root, "value") == 266001, "Profile background payload lost the selected skin ID.");
            Require(service.BuildBackgroundSkinPayloadForSmokeTest(0) == null,
                "Invalid profile skin ID should not produce a write payload.");
        }

        private static void ValidateBackgroundApplyUsesOneWriteAndReadback()
        {
            var fake = new FakeProfileApi();
            var service = CreateService(fake);
            var result = service.ApplyBackgroundSkinAsync(266001, CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "success", "Profile background did not verify successfully.");
            Require(fake.WriteCount == 1, "A profile background apply must produce exactly one POST.");
            Require(fake.ProfileReadCount == 2, "Profile background apply must use bounded first + settled readback.");
            Require(result.ObservedSkinId == 266001, "Profile background readback lost the selected skin ID.");
        }

        private static void ValidateInvalidBackgroundFailsClosed()
        {
            var fake = new FakeProfileApi();
            var service = CreateService(fake);
            var result = service.ApplyBackgroundSkinAsync(0, CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "invalid", "Invalid profile skin ID did not fail closed.");
            Require(fake.WriteCount == 0, "Invalid profile skin ID must not write to the client.");
            Require(fake.ProfileReadCount == 0, "Invalid profile skin ID must not cause verification traffic.");
        }

        private static void ValidateBackgroundOverrideDoesNotRewriteLoop()
        {
            var fake = new FakeProfileApi { OverrideOnSettledRead = true };
            var service = CreateService(fake);
            var result = service.ApplyBackgroundSkinAsync(266001, CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "overridden", "Client profile-background overwrite must be reported honestly.");
            Require(fake.WriteCount == 1, "FACM must not fight the League client with a profile-background rewrite loop.");
            Require(fake.ProfileReadCount == 2, "Profile-background override detection escaped the bounded verification contract.");
        }

        private static void ValidateDedicatedWriterFence()
        {
            Require(LeagueProfileWriteApiClient.IsAllowedTargetForSmokeTest(
                    "POST", "/lol-summoner/v1/current-summoner/summoner-profile"),
                "Profile writer blocked its exact endpoint.");
            Require(!LeagueProfileWriteApiClient.IsAllowedTargetForSmokeTest(
                    "PUT", "/lol-summoner/v1/current-summoner/summoner-profile"),
                "Profile writer accepted the wrong HTTP method.");
            Require(!LeagueProfileWriteApiClient.IsAllowedTargetForSmokeTest(
                    "POST", "/lol-summoner/v1/current-summoner/summoner-profile?force=true"),
                "Profile writer accepted a query-string escape hatch.");
            Require(!LeagueProfileWriteApiClient.IsAllowedTargetForSmokeTest(
                    "POST", "/lol-summoner/v1/current-summoner/icon"),
                "Profile writer escaped into profile-icon mutation.");
            Require(!LeagueProfileWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-chat/v1/me"),
                "Profile writer escaped into chat presence.");
        }

        private static void ValidateUiTextDefaults()
        {
            foreach (var pair in LeagueProfileCustomizationText.DefaultsForSmokeTest())
                Require(!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value),
                    "Profile customization UI text contains an empty key/default.");
        }

        private static LeagueProfileCustomizationService CreateService(FakeProfileApi fake)
        {
            return new LeagueProfileCustomizationService(fake, fake, TimeSpan.Zero, TimeSpan.Zero);
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : string.Empty;
        }

        private static int ReadInt(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return 0;
            return Convert.ToInt32(value);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeProfileApi : ILeagueClientApi, ILeagueProfileWriteApi
        {
            private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
            private int _backgroundSkinId = 1000;

            public bool OverrideOnSettledRead { get; set; }
            public int WriteCount { get; private set; }
            public int ProfileReadCount { get; private set; }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(path, LeagueProfileCustomizationService.ChampionSummaryPath, StringComparison.Ordinal))
                {
                    return Task.FromResult(Encoding.UTF8.GetBytes(
                        "[{\"id\":1,\"name\":\"安妮\"},{\"id\":2,\"name\":\"奥拉夫\"}]"));
                }
                if (string.Equals(path, LeagueProfileCustomizationService.ChampionDetailsPathPrefix + "1.json", StringComparison.Ordinal))
                {
                    return Task.FromResult(Encoding.UTF8.GetBytes(
                        "{\"id\":1,\"skins\":[{\"id\":1000,\"name\":\"经典\"},{\"id\":1001,\"name\":\"皮肤A\"}]}"));
                }
                if (!string.Equals(path, LeagueProfileCustomizationService.CurrentSummonerProfilePath, StringComparison.Ordinal))
                    return Task.FromResult<byte[]>(null);

                ProfileReadCount++;
                if (OverrideOnSettledRead && ProfileReadCount >= 2) _backgroundSkinId = 1000;
                return Task.FromResult(Encoding.UTF8.GetBytes(
                    "{\"backgroundSkinId\":" + _backgroundSkinId + ",\"backgroundSkinAugments\":\"\",\"regalia\":\"keep\"}"));
            }

            public Task<LeagueClientWriteResponse> TrySetSummonerProfileAsync(string json, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteCount++;
                var root = _json.DeserializeObject(json ?? string.Empty) as Dictionary<string, object>;
                if (root != null && ReadString(root, "key") == "backgroundSkinId")
                    _backgroundSkinId = ReadInt(root, "value");
                return Task.FromResult(new LeagueClientWriteResponse
                {
                    StatusCode = 200,
                    Body = Encoding.UTF8.GetBytes("{}")
                });
            }
        }
    }
}
