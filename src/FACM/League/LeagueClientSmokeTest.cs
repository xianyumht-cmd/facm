using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using FACM.AppHost.Modules;

namespace FACM.League
{
    internal static class LeagueClientSmokeTest
    {
        public static void Validate()
        {
            ValidateLockfileParser();
            ValidateSharedLockfileRead();
            ValidateCommandLineParser();
            ValidateSessionRefreshBoundary();
            ValidateDisconnectedModuleIsNonFatal();
        }

        private static void ValidateLockfileParser()
        {
            LeagueClientSession session;
            Require(
                LeagueClientSessionParser.TryParseLockfile(
                    "LeagueClientUx:4321:54321:secret-token:https",
                    out session),
                "LeagueClient lockfile parser rejected a valid lockfile.");
            Require(session.Port == 54321, "LeagueClient lockfile parser lost app port.");
            Require(session.ProcessId == 4321, "LeagueClient lockfile parser lost process id.");
            Require(session.BaseUri.ToString() == "https://127.0.0.1:54321/", "LeagueClient lockfile parser produced the wrong loopback BaseUri.");
            Require(session.Source == "lockfile", "LeagueClient lockfile parser lost discovery source.");

            var auth = LeagueClientSessionParser.CreateBasicAuthorizationParameter(session);
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth));
            Require(decoded == "riot:secret-token", "LeagueClient Basic Auth formation changed unexpectedly.");

            Require(
                !LeagueClientSessionParser.TryParseLockfile("LeagueClientUx:1:not-a-port:token:https", out session),
                "LeagueClient lockfile parser accepted an invalid port.");
            Require(
                !LeagueClientSessionParser.TryParseLockfile("LeagueClientUx:1:70000:token:https", out session),
                "LeagueClient lockfile parser accepted an out-of-range port.");
            Require(
                !LeagueClientSessionParser.TryParseLockfile("LeagueClientUx:1:12345", out session),
                "LeagueClient lockfile parser accepted a malformed lockfile.");
            Require(
                !LeagueClientSessionParser.TryParseLockfile("LeagueClientUx:1:12345:token:ftp", out session),
                "LeagueClient lockfile parser accepted an unsupported protocol.");
        }

        private static void ValidateSharedLockfileRead()
        {
            var path = Path.Combine(Path.GetTempPath(), "facm-league-lockfile-" + Guid.NewGuid().ToString("N") + ".txt");
            const string expected = "LeagueClientUx:4321:54321:secret-token:https";
            try
            {
                using (var owner = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    var payload = Encoding.UTF8.GetBytes(expected);
                    owner.Write(payload, 0, payload.Length);
                    owner.Flush(true);

                    string content;
                    Require(
                        ResilientLeagueClientSessionDiscovery.TryReadSharedText(path, out content),
                        "LeagueClient active lockfile could not be read with compatible sharing.");
                    Require(content == expected, "LeagueClient active lockfile shared read changed its content.");
                }
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch { }
            }
        }

        private static void ValidateCommandLineParser()
        {
            LeagueClientSession session;
            Require(
                LeagueClientSessionParser.TryParseCommandLine(
                    "LeagueClientUx.exe --app-port=55123 --remoting-auth-token=abc123 --app-pid=7711 --rso-platform-id=HN1 --region=HN1",
                    out session),
                "LeagueClient command-line parser rejected the Akari-compatible credential shape.");
            Require(session.Port == 55123, "LeagueClient command-line parser lost app port.");
            Require(session.ProcessId == 7711, "LeagueClient command-line parser lost app pid.");
            Require(session.PlatformId == "HN1", "LeagueClient command-line parser lost platform id.");
            Require(session.Region == "HN1", "LeagueClient command-line parser lost region.");
            Require(session.Source == "command-line", "LeagueClient command-line parser lost discovery source.");
        }

        private static void ValidateSessionRefreshBoundary()
        {
            var first = new LeagueClientSession("LeagueClientUx", 1, 50001, "one", "https", "test");
            var second = new LeagueClientSession("LeagueClientUx", 2, 50002, "two", "https", "test");
            var discovery = new SequenceDiscovery(first, second);
            var provider = new LeagueClientSessionProvider(discovery, TimeSpan.Zero);

            var resolved = provider.GetSession();
            Require(resolved != null && resolved.Matches(first), "LeagueClient session provider did not return the first discovered session.");
            Require(discovery.Calls == 1, "LeagueClient session provider did not cache a healthy session.");
            Require(provider.GetSession().Matches(first), "LeagueClient session provider changed a cached session unexpectedly.");
            Require(discovery.Calls == 1, "LeagueClient session provider rediscovered while a cached session was still valid.");

            provider.Invalidate(first);
            var refreshed = provider.GetSession(true);
            Require(refreshed != null && refreshed.Matches(second), "LeagueClient session provider did not refresh after invalidation.");
            Require(discovery.Calls == 2, "LeagueClient session provider refresh did not invoke discovery exactly once.");
        }

        private static void ValidateDisconnectedModuleIsNonFatal()
        {
            using (var module = new LeagueClientModule(new SequenceDiscovery((LeagueClientSession)null)))
            {
                module.Initialize();
                var bytes = module.TryGetBytesAsync(
                    "/lol-game-data/assets/v1/champion-summary.json",
                    CancellationToken.None).GetAwaiter().GetResult();
                Require(bytes == null, "LeagueClient module should return null when League is not running.");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class SequenceDiscovery : ILeagueClientSessionDiscovery
        {
            private readonly Queue<LeagueClientSession> _sessions;

            public SequenceDiscovery(params LeagueClientSession[] sessions)
            {
                _sessions = new Queue<LeagueClientSession>(sessions ?? new LeagueClientSession[0]);
            }

            public int Calls { get; private set; }

            public LeagueClientSession TryDiscover()
            {
                Calls++;
                return _sessions.Count == 0 ? null : _sessions.Dequeue();
            }
        }
    }
}
