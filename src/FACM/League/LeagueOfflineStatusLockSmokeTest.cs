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
            ValidateInitialSessionFence();

            var api = new Presence { Availability = "chat" };
            using (var controller = NewController(api))
            {
                Observe(controller, true);
                Require(api.Reads == 0 && api.Writes == 0,
                    "Disabled offline lock must not make LCU requests.");
                controller.Configure(true);
                Observe(controller, true);
                Require(api.Writes == 1 && api.Availability == "offline",
                    "Enablement must correct an initially online session.");
                Observe(controller, true);
                Require(api.Writes == 1, "Stable offline state must not repeatedly write.");
                api.Availability = "dnd";
                Observe(controller, true);
                Require(api.Writes == 2 && api.Availability == "offline", "DND was not corrected.");
                Observe(controller, false);
                api.Availability = "mobile";
                Observe(controller, true);
                Require(api.Writes == 3 && api.Availability == "offline", "Reconnect must apply enabled preference.");
                controller.Configure(false);
                api.Availability = "chat";
                var reads = api.Reads;
                Observe(controller, true);
                Require(api.Reads == reads && api.Writes == 3, "Disabled lock performed network work.");
            }
        }

        private static void ValidateDirectEvents()
        {
            var now = new DateTime(2026, 11, 28, 0, 0, 0, DateTimeKind.Utc);
            var api = new Presence { Availability = "chat" };
            using (var controller = NewController(api, () => now))
            {
                controller.Configure(true);
                controller.ObserveConnection(new LeagueDashboardPhaseState { Connected = true });
                controller.ObserveEventAsync("chat", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 1 && api.Availability == "offline", "Initial presence snapshot did not apply lock.");
                controller.ObserveEventAsync("chat", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 1, "Stale event rewrote offline status.");
                api.Availability = "dnd";
                controller.ObserveEventAsync("away", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 1, "Stale event changed a different current state.");
                controller.ObserveEventAsync("dnd", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 2 && api.Availability == "offline", "DND event did not correct.");
                api.Availability = "mobile";
                controller.ObserveEventAsync("mobile", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 3, "Mobile event did not correct.");
                api.Availability = "away";
                controller.ResetEventBaseline();
                controller.ObserveEventAsync("away", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 3, "Event reconnect bypassed correction budget.");
                now = now.AddSeconds(31);
                controller.ObserveEventAsync("away", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 4 && api.Availability == "offline", "New budget window did not recover.");
                api.Availability = "chat";
                api.Status = 403;
                controller.ObserveEventAsync("chat", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 5 && api.Availability == "chat", "Rejection fixture did not run.");
                controller.ResetEventBaseline();
                controller.ObserveEventAsync("chat", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Writes == 5, "Rejected write was retried on stream reconnect.");
                controller.Configure(false);
                var reads = api.Reads;
                controller.ObserveEventAsync("chat", CancellationToken.None).GetAwaiter().GetResult();
                Require(api.Reads == reads, "Disabled event correction performed reads.");
            }

            api = new Presence { Availability = "chat" };
            api.BeforeRead = count => { if (count == 2) api.Availability = "dnd"; };
            using (var controller = NewController(api))
            {
                controller.Configure(true);
                Observe(controller, true);
                Require(api.Writes == 0 && api.Availability == "dnd",
                    "Changed availability between observation and write must veto correction.");
            }
        }

        private static void ValidateInitialSessionFence()
        {
            var first = new LeagueClientSession("fixture", 1, 12345, "fixture-only", "https", "smoke");
            var second = new LeagueClientSession("fixture", 2, 12345, "fixture-only", "https", "smoke");
            var current = first;
            var api = new Presence { Availability = "chat", CurrentSession = () => current };
            var phase = new LeagueDashboardPhaseState { Connected = true, Phase = "Lobby" };
            using (var controller = new LeagueOfflineStatusLockController(
                new LeaguePresenceService(api, api, TimeSpan.Zero, TimeSpan.Zero), sessionProvider: () => current))
            {
                controller.Configure(true);
                api.BeforeRead = count => { if (count == 2) current = second; };
                controller.ObserveInitial(phase);
                controller.AwaitCurrentForSmokeTestAsync().GetAwaiter().GetResult();
                Require(api.Writes == 0, "Obsolete session must not receive a presence write.");

                api.BeforeRead = null;
                controller.ObserveInitial(phase);
                controller.AwaitCurrentForSmokeTestAsync().GetAwaiter().GetResult();
                Require(api.Writes == 1 && api.ScopedWrites == 1 && api.Availability == "offline",
                    "Replacement session must apply initial preference without waiting for the event stream.");
                var reads = api.Reads;
                controller.ObserveInitial(phase);
                controller.AwaitCurrentForSmokeTestAsync().GetAwaiter().GetResult();
                Require(api.Reads == reads, "Stable session must not add a periodic presence poll.");
                api.Availability = "away";
                controller.ObserveEventAsync("away", CancellationToken.None, first).GetAwaiter().GetResult();
                Require(api.Reads == reads && api.Writes == 1, "Old stream must not write into a new session.");
                controller.ObserveEventAsync("away", CancellationToken.None, second).GetAwaiter().GetResult();
                Require(api.Writes == 2 && api.ScopedWrites == 2 && api.Availability == "offline",
                    "Current stream must retain event-driven correction.");
            }
        }

        private static LeagueOfflineStatusLockController NewController(Presence api, Func<DateTime> utcNow = null)
        {
            return new LeagueOfflineStatusLockController(
                new LeaguePresenceService(api, api, TimeSpan.Zero, TimeSpan.Zero), utcNow);
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

        private sealed class Presence : ILeagueClientApi, ILeaguePresenceWriteApi, ILeagueScopedPresenceWriteApi
        {
            internal string Availability = "offline";
            internal int Reads;
            internal int Writes;
            internal int ScopedWrites;
            internal Func<LeagueClientSession> CurrentSession;
            internal int Status = 204;
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

            public Task<LeagueClientWriteResponse> TrySetPresenceAsync(string json, LeagueClientSession expected, CancellationToken cancellationToken)
            {
                Require(CurrentSession != null && LeagueEncounterReplayService.SessionMatches(expected, CurrentSession()),
                    "Scoped write targeted an obsolete session.");
                ScopedWrites++;
                return TrySetPresenceAsync(json, cancellationToken);
            }

            public Task<LeagueClientWriteResponse> TrySetPresenceAsync(string json, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                Require(root != null && (string)root["availability"] == "offline" &&
                    (string)root["statusMessage"] == "preserve", "Offline lock changed unrelated presence fields.");
                Writes++;
                if (Status >= 200 && Status < 300) Availability = (string)root["availability"];
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = Status, Body = Array.Empty<byte>() });
            }
        }
    }
}
