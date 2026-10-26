using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueQuickMessageSmokeTest
    {
        private const string ChampId = "lol-champ-select@CN1-room";
        private const string LobbyId = "lobby@CN1-room";

        internal static void Validate()
        {
            ValidatePresets();
            ValidateSending().GetAwaiter().GetResult();
            LeagueInGameMessageSmokeTest.Validate();
        }

        private static void ValidatePresets()
        {
            var settings = new AppSettings();
            Require(LeagueQuickMessagePresets.Parse(settings.LeagueQuickMessagePresets).Length == 4,
                "Quick messages must start with four empty slots.");
            Require(LeagueQuickMessagePresets.NormalizeMessage(" first \r\n\r\n second ") == "first\nsecond" &&
                LeagueQuickMessagePresets.NormalizeMessage("a\nb\nc\nd\ne") == string.Empty &&
                LeagueQuickMessagePresets.NormalizeMessage(new string('x', 301)) == string.Empty,
                "Quick-message bounds or line normalization regressed.");
            settings.LeagueQuickMessagePresets = LeagueQuickMessagePresets.Save(new[] { "hi", "one\ntwo", "", "four" });
            var restored = AppSettings.ParseLines(settings.BuildLinesForSmokeTest());
            Require(LeagueQuickMessagePresets.Parse(restored.LeagueQuickMessagePresets)[1] == "one\ntwo",
                "Quick messages failed INI roundtrip.");
            var cloud = CloudSettingsSnapshot.Capture(settings);
            var target = new AppSettings();
            cloud.ApplyTo(target);
            Require(LeagueQuickMessagePresets.Parse(target.LeagueQuickMessagePresets)[3] == "four",
                "Quick messages failed cloud roundtrip.");
            GgmanPortableSettingsStore.Validate(new GgmanPortableSettingsProfile { Settings = cloud });
        }

        private static async Task ValidateSending()
        {
            var read = new Read();
            var write = new Write();
            var phase = new LeagueDashboardPhaseState { Connected = true, Phase = "ChampSelect" };
            var session = NewSession("first");
            var sender = new LeagueQuickMessageSender(read, write, () => session, () => phase);
            read.Response = "[{\"type\":\"championSelect\",\"id\":\"" + ChampId + "\"}]";
            Require(await sender.SendChatAsync(" hello \r\n world ", CancellationToken.None) &&
                write.Calls == 1 && write.LastId == ChampId && write.LastMessage == "hello\nworld",
                "Champion-select quick chat routing failed.");

            phase.Phase = "Lobby";
            read.Response = "[{\"type\":\"customGame\",\"id\":\"" + LobbyId + "\"}]";
            Require(await sender.SendChatAsync("lobby", CancellationToken.None) && write.Calls == 2 && write.LastId == LobbyId,
                "Lobby quick chat routing failed.");

            phase.Phase = "InProgress";
            var before = read.Calls;
            Require(!await sender.SendChatAsync("not in game", CancellationToken.None) && read.Calls == before,
                "In-game messages must not be sent to lobby chat or issue LCU reads.");
            phase.Phase = "ChampSelect";
            read.Response = "[{\"type\":\"championSelect\",\"id\":\"" + ChampId + "\"}]";
            read.BeforeReturn = () => phase.Phase = "Lobby";
            Require(!await sender.SendChatAsync("stale", CancellationToken.None) && write.Calls == 2,
                "Stale phase must veto quick-message sends.");
            read.BeforeReturn = null;
            phase.Phase = "ChampSelect";
            read.BeforeReturn = () => session = NewSession("second");
            Require(!await sender.SendChatAsync("stale account", CancellationToken.None) && write.Calls == 2,
                "Account transition must veto quick-message sends.");
            read.BeforeReturn = null;
            read.Response = "[{\"type\":\"championSelect\",\"id\":\"../bad\"}]";
            Require(!await sender.SendChatAsync("bad room", CancellationToken.None) && write.Calls == 2,
                "Malformed conversation must veto quick-message sends.");
        }

        private static LeagueClientSession NewSession(string password)
        {
            return new LeagueClientSession("LeagueClientUx", 10, 2999, password, "https", "smoke");
        }

        private static void Require(bool condition, string error)
        {
            if (!condition) throw new InvalidOperationException(error);
        }

        private sealed class Read : ILeagueClientApi
        {
            internal string Response;
            internal int Calls;
            internal Action BeforeReturn;
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Require(path == "/lol-chat/v1/conversations", "Unexpected quick-chat read target.");
                Calls++;
                BeforeReturn?.Invoke();
                return Task.FromResult(Response == null ? null : Encoding.UTF8.GetBytes(Response));
            }
        }

        private sealed class Write : ILeagueQuickMessageWriteApi
        {
            internal int Calls;
            internal string LastId;
            internal string LastMessage;
            public Task<LeagueClientWriteResponse> TrySendQuickMessageAsync(
                LeagueClientSession session, string conversationId, string message, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Calls++;
                LastId = conversationId;
                LastMessage = message;
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 201, Body = Array.Empty<byte>() });
            }
        }
    }
}
