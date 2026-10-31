using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueOfflineStatusLockSmokeTest
    {
        internal static void Validate()
        {
            var settings = new AppSettings();
            Require(!settings.LeagueLockOfflineStatusEnabled, "Offline lock must default disabled.");
            settings.LeagueLockOfflineStatusEnabled = true;
            Require(AppSettings.ParseLines(settings.BuildLinesForSmokeTest()).LeagueLockOfflineStatusEnabled,
                "Offline lock INI roundtrip failed.");
            var cloud = CloudSettingsSnapshot.Capture(settings);
            var restored = new AppSettings();
            cloud.ApplyTo(restored);
            Require(restored.LeagueLockOfflineStatusEnabled, "Offline lock cloud roundtrip failed.");
            GgmanPortableSettingsStore.Validate(new GgmanPortableSettingsProfile { Settings = cloud });
            ValidateDirectEvents();

            var api = new Presence();
            using (var controller = NewController(api))
            {
                Observe(controller, true);
                Require(api.Reads == 0 && api.Writes == 0,
                    "Disabled offline lock must not make LCU requests.");
                controller.Configure(true);
                Observe(controller, true);
                Require(api.Reads == 1 && api.Writes == 0,
                    "First offline observation must establish a baseline only.");
                api.Availability = "chat";
                Observe(controller, true);
                Require(api.Writes == 1 && api.Availability == "offline" && api.Reads == 6,
                    "Offline->chat transition should generate one verified correction.");
                Observe(controller, true);
                Require(api.Writes == 1 && api.Reads == 7,
                    "Stable offline availability must not repeatedly write.");
                api.Availability = "away";
                Observe(controller, true);
                Require(api.Writes == 2 && api.Availability == "offline",
                    "Offline->away transition should also be corrected.");
                controller.Configure(false);
                api.Availability = "chat";
                var reads = api.Reads;
                Observe(controller, true);
                Require(api.Reads == reads && api.Writes == 2,
                    "Disabling lock must immediately stop network reads and writes.");
            }

            api = new Presence { Availability = "chat" };
            using (var controller = NewController(api))
            {
                controller.Configure(true);
                Observe(controller, true);
                api.Availability = "away";
                Observe(controller, true);
                Require(api.Writes == 0 && api.Availability == "away",
                    "A session without an observed offline state must never be forcibly hidden.");
                Observe(controller, false);
                api.Availability = "chat";
                Observe(controller, true);
                Require(api.Writes == 0,
                    "Reconnect must clear the previous connection's offline baseline.");
            }

            api = new Presence();
            api.BeforeRead = count => { if (count == 4) api.Availability = "dnd"; };
            using (var controller = NewController(api))
            {
                controller.Configure(true);
                Observe(controller, true);
                api.Availability = "chat";
                Observe(controller, true);
                Require(api.Writes == 0 && api.Availability == "dnd",
                    "Offline lock must abandon a correction when availability changed before PUT.");
            }
        }

        private static void ValidateDirectEvents()
        {
            var api = new Presence();
            using (var controller = NewController(api))
            {
                controller.Configure(true);
                controller.ObserveConnection(new LeagueDashboardPhaseState { Connected = true });
                controller.ObserveEventAsync("offline", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Reads == 0, "Presence event baseline must not poll LCU.");
                api.Availability = "away";
                controller.ObserveEventAsync("away", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 1 && api.Availability == "offline", "Direct offline-to-away event did not correct.");
                controller.ObserveEventAsync("away", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 1, "Duplicate presence event rewrote status.");
                controller.ObserveEventAsync("offline", CancellationToken.None).GetAwaiter().GetResult();
                controller.ObserveEventAsync("dnd", CancellationToken.None).GetAwaiter().GetResult();
                api.Availability = "chat";
                controller.ObserveEventAsync("chat", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 1, "Intervening DND state must break the offline transition.");
                controller.ObserveEventAsync("offline", CancellationToken.None).GetAwaiter().GetResult();
                controller.ResetEventBaseline();
                controller.ObserveEventAsync("chat", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 1, "Reconnected stream reused an old offline baseline.");
                controller.ObserveEventAsync("offline", CancellationToken.None).GetAwaiter().GetResult();
                api.Availability = "dnd";
                controller.ObserveEventAsync("away", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 1, "Manual presence change must veto event correction.");
                controller.Configure(false);
                var reads = api.Reads;
                controller.ObserveEventAsync("offline", CancellationToken.None).GetAwaiter().GetResult();
                controller.ObserveEventAsync("chat", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Reads == reads, "Disabled event correction performed reads.");
            }
        }

        private static LeagueOfflineStatusLockController NewController(Presence api)
        {
            return new LeagueOfflineStatusLockController(
                new LeaguePresenceService(api, api, TimeSpan.Zero, TimeSpan.Zero));
        }

        private static void Observe(LeagueOfflineStatusLockController controller, bool connected)
        {
            controller.Observe(new LeagueDashboardPhaseState { Connected = connected, Phase = "Lobby" });
            controller.AwaitCurrentForSmokeTestAsync().GetAwaiter().GetResult();
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private sealed class Presence : ILeagueClientApi, ILeaguePresenceWriteApi
        {
            internal string Availability = "offline";
            internal int Reads;
            internal int Writes;
            internal Action<int> BeforeRead;

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Require(path == LeaguePresenceService.PresencePath, "Offline lock accessed another endpoint.");
                Reads++;
                if (BeforeRead != null) BeforeRead(Reads);
                var json = new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                {
                    { "availability", Availability },
                    { "statusMessage", "preserve" },
                    { "lol", new Dictionary<string, object> { { "gameStatus", "outOfGame" } } }
                });
                return Task.FromResult(Encoding.UTF8.GetBytes(json));
            }

            public Task<LeagueClientWriteResponse> TrySetPresenceAsync(string json, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                Require(root != null && (string)root["availability"] == "offline" &&
                    (string)root["statusMessage"] == "preserve", "Offline lock changed unrelated presence fields.");
                Writes++;
                Availability = (string)root["availability"];
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 204, Body = Array.Empty<byte>() });
            }
        }
    }
}
