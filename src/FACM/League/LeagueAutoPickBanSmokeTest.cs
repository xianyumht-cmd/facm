using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueAutoPickBanSmokeTest
    {
        public static void Validate()
        {
            Require(!new AppSettings().LeagueAutoPickEnabled && !new AppSettings().LeagueAutoBanEnabled,
                "Automatic pick/ban must default disabled.");
            var settings = AppSettings.ParseLines(new[]
            {
                "LeagueAutoPickEnabled=True", "LeagueAutoBanEnabled=True",
                "LeagueAutoPickChampionId=22", "LeagueAutoBanChampionId=67"
            });
            Require(settings.LeagueAutoPickEnabled && settings.LeagueAutoBanEnabled &&
                    settings.LeagueAutoPickChampionId == 22 && settings.LeagueAutoBanChampionId == 67,
                "Auto-select preferences did not parse.");
            Require(string.Join("\n", settings.BuildLines()).Contains("LeagueAutoPickChampionId=22"),
                "Auto-select preferences did not serialize.");
            Require(LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "pick", 0, 22) &&
                    LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "ban", 3, 67) &&
                    !LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("DELETE", "pick", 0, 22) &&
                    !LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "trade", 3, 67) &&
                    !LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "ban", -1, 67) &&
                    !LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "pick", 0, 0),
                "ChampSelect action writer escaped its method/type/action/champion fence.");
            ValidateActionParsing();
            ValidatePickAndBanAsync().GetAwaiter().GetResult();
            ValidateFailureAndCancellationAsync().GetAwaiter().GetResult();
        }

        private static void ValidateActionParsing()
        {
            var action = LeagueAutoPickBanController.ParseCurrentAction(Bytes(Session("pick", 0, 0)));
            Require(action != null && action.Id == 0 && action.ChampionId == 0 && action.Type == "pick",
                "Valid first-player/first-action zero IDs should be accepted.");
            Require(LeagueAutoPickBanController.ParseCurrentAction(Bytes(Session("pick", 0, 0, false))) == null,
                "Inactive local action should never be issued.");
            Require(LeagueAutoPickBanController.ParseCurrentAction(Bytes(Session("pick", 0, 0, true, "FINALIZATION"))) == null,
                "Non-ban-pick timer phase should fail closed.");
            Require(LeagueAutoPickBanController.ParseCurrentAction(Bytes(Session("pick", 0, 0, true, "BAN_PICK", true))) == null,
                "Completed action should never be issued.");
            Require(LeagueAutoPickBanController.ParseCurrentAction(Bytes("not json")) == null,
                "Malformed session must not create a write target.");
            Require(LeagueAutoPickBanController.ParseCurrentAction(Bytes(
                    "{\"timer\":{\"phase\":\"BAN_PICK\"},\"localPlayerCellId\":0," +
                    "\"actions\":[[{\"id\":0,\"type\":\"pick\",\"championId\":0," +
                    "\"isInProgress\":true,\"completed\":false}]]}")) == null,
                "A missing actorCellId must not accidentally match player zero.");
            Require(LeagueAutoPickBanController.ParseCurrentAction(Bytes(
                    "{\"timer\":{\"phase\":\"BAN_PICK\"},\"localPlayerCellId\":0," +
                    "\"actions\":[[{\"id\":0,\"actorCellId\":0,\"type\":\"pick\"," +
                    "\"championId\":0,\"isInProgress\":true}]]}")) == null,
                "A session without the completed flag must fail closed.");
            Require(LeagueAutoPickBanController.IsAvailable(Bytes("[1,22,67]"), 22) &&
                    !LeagueAutoPickBanController.IsAvailable(Bytes("[1,67]"), 22) &&
                    !LeagueAutoPickBanController.IsAvailable(Bytes("{\"ids\":[22]}"), 22),
                "Champion availability must use the validated LCU list.");
        }

        private static async Task ValidatePickAndBanAsync()
        {
            var pickRead = new FakeRead { SessionJson = Session("pick", 0, 0) };
            var pickWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(pickRead, pickWrite, new FakeClock()))
            {
                controller.Configure(true, 22, false, 0);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None);
                Require(pickWrite.Calls.Count == 1 && pickWrite.Calls[0] == "pick:0:22:True",
                    "Pick write must only target the active local action.");
                Require(pickRead.Paths.Count == 3 && pickRead.Paths[1] == LeagueAutoPickBanController.PickablePath,
                    "Pick must read active session, pickable list and live preflight.");
            }

            var banRead = new FakeRead { SessionJson = Session("ban", 4, 0) };
            var banWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(banRead, banWrite, new FakeClock()))
            {
                controller.Configure(false, 0, true, 67);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None);
                Require(banWrite.Calls.Count == 1 && banWrite.Calls[0] == "ban:4:67:True",
                    "Ban write must only target the active local action.");
                Require(banRead.Paths.Count == 3 && banRead.Paths[1] == LeagueAutoPickBanController.BannablePath,
                    "Ban must read the bannable list, not the pickable list.");
            }

            var manualRead = new FakeRead { SessionJson = Session("pick", 3, 67) };
            var manualWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(manualRead, manualWrite, new FakeClock()))
            {
                controller.Configure(true, 22, false, 0);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None);
                Require(manualWrite.Calls.Count == 0 && manualRead.Paths.Count == 1,
                    "An existing manually selected different champion must not be overridden.");
            }
        }

        private static async Task ValidateFailureAndCancellationAsync()
        {
            var idleClock = new FakeClock();
            var idleRead = new FakeRead { SessionJson = Session("pick", 1, 0) };
            var idleWrite = new FakeWrite();
            using (var idle = new LeagueAutoPickBanController(idleRead, idleWrite, idleClock))
            {
                idle.Observe(Selecting());
                Require(idleRead.Paths.Count == 0 && idleClock.Delays == 0 && idleWrite.Calls.Count == 0,
                    "Disabled automation must not start a network poller.");
            }

            var changed = new FakeRead
            {
                SessionJson = Session("pick", 2, 0),
                NextSessionJson = Session("pick", 2, 99)
            };
            var changedWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(changed, changedWrite, new FakeClock()))
            {
                controller.Configure(true, 22, false, 0);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None);
                Require(changedWrite.Calls.Count == 0, "Preflight must respect a later manual champion change.");
            }

            var unavailable = new FakeRead { SessionJson = Session("pick", 2, 0), Pickable = "[67]" };
            var unavailableWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(unavailable, unavailableWrite, new FakeClock()))
            {
                controller.Configure(true, 22, false, 0);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None);
                Require(unavailableWrite.Calls.Count == 0, "Unavailable champion must not be selected.");
            }

            var clock = new FakeClock();
            var read = new FakeRead { SessionJson = Session("pick", 3, 0) };
            var write = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(read, write, clock))
            {
                controller.Configure(true, 22, false, 0);
                controller.Observe(Selecting());
                var task = controller.RunningForSmokeTest;
                controller.Observe(Selecting());
                Require(write.Calls.Count == 1 && clock.Delays == 1,
                    "Repeated Gameflow events must not duplicate the action or polling loop.");
                controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "Lobby" });
                await task;
                Require(clock.Cancelled && write.Calls.Count == 1,
                    "Leaving ChampSelect failed to cancel the bounded polling loop.");
            }

            var disabledClock = new FakeClock();
            using (var controller = new LeagueAutoPickBanController(
                new FakeRead { SessionJson = Session("pick", 9, 0) }, new FakeWrite(), disabledClock))
            {
                controller.Configure(true, 22, false, 0);
                controller.Observe(Selecting());
                var task = controller.RunningForSmokeTest;
                controller.Configure(false, 22, false, 0);
                await task;
                Require(disabledClock.Cancelled, "Disabling the setting must cancel the active task.");
            }
        }

        private static string Session(string type, int id, int championId,
            bool inProgress = true, string phase = "BAN_PICK", bool completed = false)
        {
            return "{\"timer\":{\"phase\":\"" + phase + "\"},\"localPlayerCellId\":0,\"actions\":[" +
                   "[{\"id\":" + id + ",\"actorCellId\":0,\"type\":\"" + type +
                   "\",\"championId\":" + championId + ",\"isInProgress\":" +
                   (inProgress ? "true" : "false") + ",\"completed\":" +
                   (completed ? "true" : "false") + "}]]}";
        }

        private static LeagueDashboardPhaseState Selecting()
        {
            return new LeagueDashboardPhaseState
            {
                Connected = true, Phase = "ChampSelect", Activity = LeagueActivityLevel.ChampSelect
            };
        }

        private static byte[] Bytes(string value) { return Encoding.UTF8.GetBytes(value); }

        private static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        private sealed class FakeRead : ILeagueClientApi
        {
            public string SessionJson;
            public string NextSessionJson;
            public string Pickable = "[22,67]";
            public string Bannable = "[22,67]";
            public readonly List<string> Paths = new List<string>();
            private int _sessionCount;

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Paths.Add(path);
                if (path == LeagueAutoPickBanController.SessionPath)
                    return Task.FromResult(Bytes(++_sessionCount > 1 && NextSessionJson != null
                        ? NextSessionJson : SessionJson));
                if (path == LeagueAutoPickBanController.PickablePath) return Task.FromResult(Bytes(Pickable));
                if (path == LeagueAutoPickBanController.BannablePath) return Task.FromResult(Bytes(Bannable));
                throw new InvalidOperationException("Unexpected auto-select read target.");
            }
        }

        private sealed class FakeWrite : ILeagueChampSelectActionWriteApi
        {
            public readonly List<string> Calls = new List<string>();

            public Task<LeagueClientWriteResponse> TryApplyAsync(
                string type, int id, int championId, bool completed, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls.Add(type + ":" + id + ":" + championId + ":" + completed);
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 204 });
            }
        }

        private sealed class FakeClock : ILeagueMatchmakingClock
        {
            public int Delays;
            public bool Cancelled;

            public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
            {
                Require(delay == LeagueAutoPickBanController.PollInterval, "Unexpected auto-select polling cadence.");
                Delays++;
                var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() =>
                {
                    Cancelled = true;
                    pending.TrySetCanceled();
                });
                return pending.Task;
            }
        }
    }
}
