using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal static class LeagueRegaliaCustomizationSmokeTest
    {
        public static void Validate()
        {
            ValidateParsing();
            ValidatePayloadPreservesBannerType();
            ValidateApplyUsesOneWriteAndBoundedReadback();
            ValidateUnavailableFailsClosed();
            ValidateOverrideDoesNotRewriteLoop();
            ValidateDedicatedWriterFence();
        }

        private static void ValidateParsing()
        {
            var fake = new FakeRegaliaApi();
            var service = CreateService(fake);
            var parsed = service.ParseForSmokeTest(Encoding.UTF8.GetBytes(
                "{\"bannerType\":\"ranked\",\"crestType\":\"level\",\"preferredBannerType\":\"ranked\",\"preferredCrestType\":\"level\",\"selectedPrestigeCrest\":7,\"summonerLevel\":600}"));
            Require(parsed != null && parsed.Connected, "Regalia parser did not mark a valid document connected.");
            Require(parsed.BannerType == "ranked" && parsed.PreferredBannerType == "ranked",
                "Regalia parser lost banner fields.");
            Require(parsed.SelectedPrestigeCrest == 7 && parsed.SummonerLevel == 600,
                "Regalia parser lost numeric fields.");
        }

        private static void ValidatePayloadPreservesBannerType()
        {
            var fake = new FakeRegaliaApi();
            var service = CreateService(fake);
            var payload = service.BuildRemovalPayloadForSmokeTest("ranked");
            var root = new JavaScriptSerializer().DeserializeObject(payload) as Dictionary<string, object>;
            Require(root != null, "Regalia payload could not be parsed.");
            Require(ReadString(root, "preferredCrestType") == "prestige",
                "Regalia payload did not select the verified prestige crest owner.");
            Require(ReadString(root, "preferredBannerType") == "ranked",
                "Regalia payload did not preserve the current banner type.");
            Require(ReadInt(root, "selectedPrestigeCrest") == LeagueRegaliaCustomizationService.PrestigeCrestRemovalValue,
                "Regalia payload lost the verified fixed prestige crest value.");
            Require(service.BuildRemovalPayloadForSmokeTest(string.Empty) == null,
                "Regalia payload must fail closed without a current banner type.");
        }

        private static void ValidateApplyUsesOneWriteAndBoundedReadback()
        {
            var fake = new FakeRegaliaApi();
            var service = CreateService(fake);
            var result = service.RemovePrestigeCrestAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "success", "Regalia change did not verify successfully.");
            Require(fake.WriteCount == 1, "Regalia apply must emit exactly one PUT.");
            Require(fake.ReadCount == 3, "Regalia apply must use one pre-read plus bounded first + settled readback.");
            Require(result.Observed != null &&
                    result.Observed.SelectedPrestigeCrest == LeagueRegaliaCustomizationService.PrestigeCrestRemovalValue,
                "Regalia readback lost the requested crest state.");
        }

        private static void ValidateUnavailableFailsClosed()
        {
            var fake = new FakeRegaliaApi { ReturnUnavailable = true };
            var service = CreateService(fake);
            var result = service.RemovePrestigeCrestAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "unavailable", "Unavailable regalia did not fail closed.");
            Require(fake.WriteCount == 0, "Unavailable regalia must not write.");
        }

        private static void ValidateOverrideDoesNotRewriteLoop()
        {
            var fake = new FakeRegaliaApi { OverrideOnSettledRead = true };
            var service = CreateService(fake);
            var result = service.RemovePrestigeCrestAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result != null && result.Status == "overridden", "Regalia overwrite must be reported honestly.");
            Require(fake.WriteCount == 1, "FACM must not fight the League client with a regalia rewrite loop.");
            Require(fake.ReadCount == 3, "Regalia override escaped the bounded verification contract.");
        }

        private static void ValidateDedicatedWriterFence()
        {
            Require(LeagueRegaliaWriteApiClient.IsAllowedTargetForSmokeTest(
                    "PUT", LeagueRegaliaWriteApiClient.RegaliaPath),
                "Regalia writer blocked its exact endpoint.");
            Require(!LeagueRegaliaWriteApiClient.IsAllowedTargetForSmokeTest(
                    "POST", LeagueRegaliaWriteApiClient.RegaliaPath),
                "Regalia writer accepted the wrong HTTP method.");
            Require(!LeagueRegaliaWriteApiClient.IsAllowedTargetForSmokeTest(
                    "PUT", LeagueRegaliaWriteApiClient.RegaliaPath + "?force=true"),
                "Regalia writer accepted a query-string escape hatch.");
            Require(!LeagueRegaliaWriteApiClient.IsAllowedTargetForSmokeTest("PUT", "/lol-chat/v1/me"),
                "Regalia writer escaped into chat presence.");
            Require(!LeagueRegaliaWriteApiClient.IsAllowedTargetForSmokeTest(
                    "PUT", "/lol-summoner/v1/current-summoner/summoner-profile"),
                "Regalia writer escaped into summoner-profile mutation.");
        }

        private static LeagueRegaliaCustomizationService CreateService(FakeRegaliaApi fake)
        {
            return new LeagueRegaliaCustomizationService(fake, fake, TimeSpan.Zero, TimeSpan.Zero);
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

        private sealed class FakeRegaliaApi : ILeagueClientApi, ILeagueRegaliaWriteApi
        {
            private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
            private string _bannerType = "ranked";
            private string _preferredBannerType = "ranked";
            private string _preferredCrestType = "level";
            private int _selectedPrestigeCrest = 7;

            public bool ReturnUnavailable { get; set; }
            public bool OverrideOnSettledRead { get; set; }
            public int ReadCount { get; private set; }
            public int WriteCount { get; private set; }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(path, LeagueRegaliaCustomizationService.CurrentRegaliaPath, StringComparison.Ordinal))
                    return Task.FromResult<byte[]>(null);
                ReadCount++;
                if (ReturnUnavailable) return Task.FromResult<byte[]>(null);
                if (OverrideOnSettledRead && ReadCount >= 3)
                {
                    _preferredCrestType = "level";
                    _selectedPrestigeCrest = 7;
                }
                var json = "{\"bannerType\":\"" + _bannerType + "\",\"crestType\":\"level\",\"preferredBannerType\":\"" +
                           _preferredBannerType + "\",\"preferredCrestType\":\"" + _preferredCrestType +
                           "\",\"selectedPrestigeCrest\":" + _selectedPrestigeCrest + ",\"summonerLevel\":600}";
                return Task.FromResult(Encoding.UTF8.GetBytes(json));
            }

            public Task<LeagueClientWriteResponse> TrySetRegaliaAsync(string json, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteCount++;
                var root = _json.DeserializeObject(json ?? string.Empty) as Dictionary<string, object>;
                if (root != null)
                {
                    _preferredBannerType = ReadString(root, "preferredBannerType");
                    _preferredCrestType = ReadString(root, "preferredCrestType");
                    _selectedPrestigeCrest = ReadInt(root, "selectedPrestigeCrest");
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
