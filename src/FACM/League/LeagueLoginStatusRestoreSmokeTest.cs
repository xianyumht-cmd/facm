using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueLoginStatusRestoreSmokeTest
    {
        internal static void Validate()
        {
            ValidateSettings();
            ValidateOffByDefault();
            ValidateOneRestorePerConnection();
            ValidateManualChangesWin();
            ValidateAlreadyCorrectDoesNotWrite();
            ValidateRankedRestoreAndCombinedFields();
            ValidateRankedManualChangesWin();
            ValidateRankedAlreadyCorrectDoesNotWrite();
        }

        private static void ValidateSettings()
        {
            var settings = new AppSettings();
            Require(!settings.LeagueAutoLoginStatusMessageEnabled &&
                settings.LeagueAutoLoginStatusMessage == string.Empty, "Login signature should default off.");
            AppSettings.ApplyLineForSmokeTest(settings, "LeagueAutoLoginStatusMessageEnabled=True");
            AppSettings.ApplyLineForSmokeTest(settings, "LeagueAutoLoginStatusMessage=登录后展示此签名");
            var local = AppSettings.ParseLines(settings.BuildLinesForSmokeTest());
            Require(local.LeagueAutoLoginStatusMessageEnabled &&
                local.LeagueAutoLoginStatusMessage == "登录后展示此签名", "Login signature INI failed to roundtrip.");
            var cloud = CloudSettingsSnapshot.Capture(settings);
            var restored = new AppSettings();
            cloud.ApplyTo(restored);
            Require(restored.LeagueAutoLoginStatusMessageEnabled &&
                restored.LeagueAutoLoginStatusMessage == settings.LeagueAutoLoginStatusMessage,
                "Login signature cloud failed to roundtrip.");
            GgmanPortableSettingsStore.Validate(new GgmanPortableSettingsProfile { Settings = cloud });
            cloud.LeagueAutoLoginStatusMessage = new string('a', 513);
            try
            {
                GgmanPortableSettingsStore.Validate(new GgmanPortableSettingsProfile { Settings = cloud });
                throw new InvalidOperationException("Oversized login signature was accepted.");
            }
            catch (System.IO.InvalidDataException) { }
            Require(LeagueLoginStatusRestoreController.NormalizeSavedMessage("a\nb\rc\0d") == "a b cd",
                "Login signature must be single-line and strip NUL.");

            Require(!settings.LeagueAutoLoginRankedStatusEnabled, "Login ranked appearance must default off.");
            var ranked = new AppSettings();
            ranked.LeagueAutoLoginRankedStatusEnabled = true;
            ranked.LeagueAutoLoginRankedQueue = "RANKED_FLEX_SR";
            ranked.LeagueAutoLoginRankedTier = "EMERALD";
            ranked.LeagueAutoLoginRankedDivision = "III";
            var parsed = AppSettings.ParseLines(ranked.BuildLinesForSmokeTest());
            Require(parsed.LeagueAutoLoginRankedStatusEnabled &&
                parsed.LeagueAutoLoginRankedQueue == "RANKED_FLEX_SR" &&
                parsed.LeagueAutoLoginRankedTier == "EMERALD" &&
                parsed.LeagueAutoLoginRankedDivision == "III", "Login rank INI roundtrip failed.");
            var cloudRank = CloudSettingsSnapshot.Capture(ranked);
            var afterCloud = new AppSettings();
            cloudRank.ApplyTo(afterCloud);
            Require(afterCloud.LeagueAutoLoginRankedStatusEnabled &&
                afterCloud.LeagueAutoLoginRankedDivision == "III", "Login rank cloud roundtrip failed.");
            GgmanPortableSettingsStore.Validate(new GgmanPortableSettingsProfile { Settings = cloudRank });
            cloudRank.LeagueAutoLoginRankedTier = "FAKE_TIER";
            try
            {
                GgmanPortableSettingsStore.Validate(new GgmanPortableSettingsProfile { Settings = cloudRank });
                throw new InvalidOperationException("Invalid login rank was accepted in portable settings.");
            }
            catch (System.IO.InvalidDataException) { }
            cloudRank.ApplyTo(afterCloud);
            Require(!afterCloud.LeagueAutoLoginRankedStatusEnabled,
                "Invalid cloud rank must disable automatic restore.");
        }

        private static void ValidateOffByDefault()
        {
            var api = new Presence();
            using (var controller = NewController(api))
            {
                Run(controller);
                Require(api.Reads == 0 && api.Writes == 0,
                    "Disabled login signature automation must incur no LCU requests.");
            }
        }

        private static void ValidateOneRestorePerConnection()
        {
            var api = new Presence();
            using (var controller = NewController(api))
            {
                controller.Configure(true, "恢复后的签名");
                Run(controller);
                Run(controller);
                Require(api.Writes == 1 && api.Current == "恢复后的签名" && api.Reads == 5,
                    "Chat-ready restore must write and verify once per connection.");
                controller.Observe(new LeagueDashboardPhaseState { Connected = false, Phase = "None" });
                api.Current = "客户端新会话";
                Run(controller);
                Require(api.Writes == 2 && api.Current == "恢复后的签名",
                    "A new connection must be eligible for another single restore.");
            }
        }

        private static void ValidateManualChangesWin()
        {
            var api = new Presence();
            api.BeforeRead = count => { if (count == 2) api.Current = "手动签名"; };
            using (var controller = NewController(api))
            {
                controller.Configure(true, "自动签名");
                Run(controller);
                Require(api.Writes == 0 && api.Current == "手动签名",
                    "A manual change during chat settle must suppress automatic overwrite.");
            }

            api = new Presence();
            api.BeforeRead = count => { if (count == 3) api.Current = "写入前的手动修改"; };
            using (var controller = NewController(api))
            {
                controller.Configure(true, "自动签名");
                Run(controller);
                Require(api.Writes == 0 && api.Current == "写入前的手动修改",
                    "Final LCU prewrite read must detect manual signature changes.");
            }
        }

        private static void ValidateAlreadyCorrectDoesNotWrite()
        {
            var api = new Presence { Current = "已经设置" };
            using (var controller = NewController(api))
            {
                controller.Configure(true, "已经设置");
                Run(controller);
                Run(controller);
                Require(api.Writes == 0 && api.Reads == 2,
                    "Correct signature must never be rewritten on each Gameflow observation.");
            }
        }

        private static void ValidateRankedRestoreAndCombinedFields()
        {
            var api = new Presence();
            using (var controller = NewController(api))
            {
                controller.Configure(false, string.Empty, true, "RANKED_FLEX_SR", "DIAMOND", "IV");
                Run(controller);
                Run(controller);
                Require(api.Writes == 1 && api.Current == "原始签名" &&
                    api.RankedQueue == "RANKED_FLEX_SR" && api.RankedTier == "DIAMOND" &&
                    api.RankedDivision == "IV" && api.Reads == 5,
                    "Ranked-only restore must preserve signature and write once.");
                controller.Observe(new LeagueDashboardPhaseState { Connected = false, Phase = "None" });
                api.RankedTier = "SILVER";
                Run(controller);
                Require(api.Writes == 2 && api.RankedTier == "DIAMOND",
                    "New login should allow one new ranked restore.");
            }

            api = new Presence();
            using (var controller = NewController(api))
            {
                controller.Configure(true, "统一签名", true, "RANKED_SOLO_5x5", "MASTER", "I");
                Run(controller);
                Require(api.Writes == 2 && api.Reads == 8 && api.Current == "统一签名" &&
                    api.RankedTier == "MASTER" && api.RankedDivision == string.Empty,
                    "Combined login restore must serialize both writes and omit apex division.");
                Run(controller);
                Require(api.Writes == 2 && api.Reads == 8,
                    "Completed combined restore must not repeat writes or reads.");
            }
        }

        private static void ValidateRankedManualChangesWin()
        {
            var api = new Presence();
            api.BeforeRead = count => { if (count == 2) api.RankedTier = "EMERALD"; };
            using (var controller = NewController(api))
            {
                controller.Configure(false, string.Empty, true, "RANKED_SOLO_5x5", "DIAMOND", "I");
                Run(controller);
                Require(api.Writes == 0 && api.RankedTier == "EMERALD",
                    "Manual rank change during settle must prevent restore.");
            }

            api = new Presence();
            api.BeforeRead = count => { if (count == 3) api.RankedTier = "EMERALD"; };
            using (var controller = NewController(api))
            {
                controller.Configure(false, string.Empty, true, "RANKED_SOLO_5x5", "DIAMOND", "I");
                Run(controller);
                Require(api.Writes == 0 && api.RankedTier == "EMERALD",
                    "Final ranked prewrite comparison must detect user changes.");
            }

            api = new Presence();
            api.BeforeRead = count => { if (count == 2) api.Current = "手动更新"; };
            using (var controller = NewController(api))
            {
                controller.Configure(true, "签名目标", true, "RANKED_SOLO_5x5", "DIAMOND", "I");
                Run(controller);
                Require(api.Writes == 1 && api.Current == "手动更新" && api.RankedTier == "DIAMOND",
                    "A manual signature change should not prevent an independent unchanged rank restore.");
            }

            api = new Presence();
            api.BeforeRead = count => { if (count == 6) api.RankedTier = "PLATINUM"; };
            using (var controller = NewController(api))
            {
                controller.Configure(true, "签名目标", true, "RANKED_SOLO_5x5", "DIAMOND", "I");
                Run(controller);
                Require(api.Writes == 1 && api.Current == "签名目标" && api.RankedTier == "PLATINUM",
                    "A rank change after signature write must abort only the later ranked write.");
            }
        }

        private static void ValidateRankedAlreadyCorrectDoesNotWrite()
        {
            var api = new Presence { RankedTier = "DIAMOND", RankedDivision = "I" };
            using (var controller = NewController(api))
            {
                controller.Configure(false, string.Empty, true, "RANKED_SOLO_5x5", "DIAMOND", "I");
                Run(controller);
                Run(controller);
                Require(api.Writes == 0 && api.Reads == 2,
                    "Already-correct ranked appearance must not write or poll again.");
            }
        }

        private static LeagueLoginStatusRestoreController NewController(Presence api)
        {
            return new LeagueLoginStatusRestoreController(
                new LeaguePresenceService(api, api, TimeSpan.Zero, TimeSpan.Zero), TimeSpan.Zero);
        }

        private static void Run(LeagueLoginStatusRestoreController controller)
        {
            controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "Lobby" });
            controller.AwaitCurrentForSmokeTestAsync().GetAwaiter().GetResult();
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private sealed class Presence : ILeagueClientApi, ILeaguePresenceWriteApi
        {
            internal string Current = "原始签名";
            internal string RankedQueue = "RANKED_SOLO_5x5";
            internal string RankedTier = "GOLD";
            internal string RankedDivision = "II";
            internal int Reads;
            internal int Writes;
            internal Action<int> BeforeRead;

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Require(path == LeaguePresenceService.PresencePath, "Unexpected endpoint used by login restore.");
                Reads++;
                if (BeforeRead != null) BeforeRead(Reads);
                var json = new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                {
                    { "availability", "chat" },
                    { "statusMessage", Current },
                    { "name", "test" },
                    { "lol", new Dictionary<string, object>
                        {
                            { "gameStatus", "outOfGame" },
                            { "rankedLeagueQueue", RankedQueue },
                            { "rankedLeagueTier", RankedTier },
                            { "rankedLeagueDivision", RankedDivision }
                        }
                    }
                });
                return Task.FromResult(Encoding.UTF8.GetBytes(json));
            }

            public Task<LeagueClientWriteResponse> TrySetPresenceAsync(string json, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                Require(root != null && (string)root["availability"] == "chat", "Restore changed unrelated presence.");
                Writes++;
                Current = (string)root["statusMessage"];
                var lol = root["lol"] as Dictionary<string, object>;
                RankedQueue = (string)lol["rankedLeagueQueue"];
                RankedTier = (string)lol["rankedLeagueTier"];
                RankedDivision = lol.ContainsKey("rankedLeagueDivision") ? (string)lol["rankedLeagueDivision"] : string.Empty;
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 204, Body = Array.Empty<byte>() });
            }
        }
    }
}
