using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal interface ILeagueRiotAliasLookupApi
    {
        Task<byte[]> TryLookupAsync(string gameName, string tagLine, CancellationToken cancellationToken);
    }

    internal interface ILeagueRiotClientSessionDiscovery
    {
        LeagueClientSession TryDiscover(LeagueClientSession currentLeagueSession);
    }

    internal sealed class LeagueRiotClientSessionDiscovery : ILeagueRiotClientSessionDiscovery
    {
        internal static bool TryParseMatchingCommandLine(string commandLine,
            LeagueClientSession currentLeagueSession, out LeagueClientSession riotSession)
        {
            riotSession = null;
            LeagueClientSession leagueSession;
            return currentLeagueSession != null &&
                LeagueClientSessionParser.TryParseCommandLine(commandLine, out leagueSession) &&
                leagueSession.Matches(currentLeagueSession) &&
                LeagueClientSessionParser.TryParseRiotClientCommandLine(commandLine, out riotSession);
        }

        public LeagueClientSession TryDiscover(LeagueClientSession currentLeagueSession)
        {
            if (currentLeagueSession == null || Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
            var processes = Process.GetProcessesByName("LeagueClientUx");
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        string commandLine;
                        if (!WmiProcessImagePathReader.TryReadCommandLine(process.Id, out commandLine)) continue;
                        LeagueClientSession riotSession;
                        if (TryParseMatchingCommandLine(commandLine, currentLeagueSession, out riotSession))
                            return riotSession;
                    }
                    catch (Exception)
                    {
                        // An exiting or protected client process cannot establish a Riot session.
                    }
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
            return null;
        }
    }

    internal sealed class LeagueRiotAliasLookupApiClient : ILeagueRiotAliasLookupApi, IDisposable
    {
        internal const string AliasPath = "/player-account/aliases/v1/lookup";
        private readonly LeagueClientSessionProvider _sessions;
        private readonly ILeagueRiotClientSessionDiscovery _discovery;
        private readonly LeagueSessionHttpClientPool _clients =
            new LeagueSessionHttpClientPool(TimeSpan.FromSeconds(6), true);

        internal LeagueRiotAliasLookupApiClient(LeagueClientSessionProvider sessions,
            ILeagueRiotClientSessionDiscovery discovery = null)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _discovery = discovery ?? new LeagueRiotClientSessionDiscovery();
        }

        public async Task<byte[]> TryLookupAsync(string gameName, string tagLine, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(gameName) || string.IsNullOrEmpty(tagLine)) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var currentLeague = _sessions.GetSession();
            if (currentLeague == null) return null;

            LeagueClientSession riot;
            try
            {
                riot = await Task.Run(() => _discovery.TryDiscover(currentLeague), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return null; }
            if (riot == null || _sessions.GetSession()?.Matches(currentLeague) != true) return null;

            LeagueSessionHttpClientPool.Lease lease;
            try { lease = _clients.Acquire(riot); }
            catch (ObjectDisposedException) { return null; }
            using (lease)
            {
                try
                {
                    var path = AliasPath + "?gameName=" + Uri.EscapeDataString(gameName) +
                        "&tagLine=" + Uri.EscapeDataString(tagLine);
                    using (var response = await lease.Client.GetAsync(path,
                        HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode) return null;
                        var bytes = await CancelableHttpContentReader.ReadBytesAsync(
                            response.Content, cancellationToken, 64 * 1024).ConfigureAwait(false);
                        return _sessions.GetSession()?.Matches(currentLeague) == true ? bytes : null;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    return null;
                }
                catch (HttpRequestException) { return null; }
                catch (ObjectDisposedException) { return null; }
                catch (Exception) { return null; }
            }
        }

        public void Dispose() { _clients.Dispose(); }
    }
}
