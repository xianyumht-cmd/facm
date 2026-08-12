using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueClientSession
    {
        public LeagueClientSession(
            string processName,
            int processId,
            int port,
            string password,
            string protocol,
            string source,
            string platformId = null,
            string region = null)
        {
            ProcessName = string.IsNullOrWhiteSpace(processName) ? "LeagueClientUx" : processName.Trim();
            ProcessId = processId;
            Port = port;
            Password = password ?? string.Empty;
            Protocol = string.IsNullOrWhiteSpace(protocol) ? "https" : protocol.Trim().ToLowerInvariant();
            Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim();
            PlatformId = string.IsNullOrWhiteSpace(platformId) ? null : platformId.Trim();
            Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
            BaseUri = new Uri(Protocol + "://127.0.0.1:" + Port + "/", UriKind.Absolute);
        }

        public string ProcessName { get; private set; }
        public int ProcessId { get; private set; }
        public int Port { get; private set; }
        public string Password { get; private set; }
        public string Protocol { get; private set; }
        public string Source { get; private set; }
        public string PlatformId { get; private set; }
        public string Region { get; private set; }
        public Uri BaseUri { get; private set; }

        internal string IdentityKey
        {
            get { return Protocol + ":" + Port + ":" + Password; }
        }

        internal bool Matches(LeagueClientSession other)
        {
            return other != null && string.Equals(IdentityKey, other.IdentityKey, StringComparison.Ordinal);
        }
    }

    internal static class LeagueClientSessionParser
    {
        public static bool TryParseLockfile(string content, out LeagueClientSession session)
        {
            session = null;
            if (string.IsNullOrWhiteSpace(content)) return false;

            var parts = content.Trim().Split(':');
            if (parts.Length < 5) return false;

            int port;
            if (!int.TryParse(parts[2], out port) || port <= 0 || port > 65535) return false;
            if (string.IsNullOrWhiteSpace(parts[3])) return false;

            var protocol = string.IsNullOrWhiteSpace(parts[4]) ? "https" : parts[4].Trim().ToLowerInvariant();
            if (protocol != "https" && protocol != "http") return false;

            int processId;
            if (!int.TryParse(parts[1], out processId) || processId < 0) processId = 0;

            try
            {
                session = new LeagueClientSession(parts[0], processId, port, parts[3], protocol, "lockfile");
                return true;
            }
            catch
            {
                session = null;
                return false;
            }
        }

        public static bool TryParseCommandLine(string commandLine, out LeagueClientSession session)
        {
            session = null;
            if (string.IsNullOrWhiteSpace(commandLine)) return false;

            var portText = ReadArgument(commandLine, "--app-port");
            var token = ReadArgument(commandLine, "--remoting-auth-token");
            int port;
            if (!int.TryParse(portText, out port) || port <= 0 || port > 65535 || string.IsNullOrWhiteSpace(token))
                return false;

            var processIdText = ReadArgument(commandLine, "--app-pid");
            int processId;
            if (!int.TryParse(processIdText, out processId) || processId < 0) processId = 0;

            var platformId = FirstNonEmpty(
                ReadArgument(commandLine, "--rso_platform_id"),
                ReadArgument(commandLine, "--rso-platform-id"));
            var region = ReadArgument(commandLine, "--region");

            try
            {
                session = new LeagueClientSession(
                    "LeagueClientUx",
                    processId,
                    port,
                    token,
                    "https",
                    "command-line",
                    platformId,
                    region);
                return true;
            }
            catch
            {
                session = null;
                return false;
            }
        }

        public static string CreateBasicAuthorizationParameter(LeagueClientSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            return Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + session.Password));
        }

        private static string ReadArgument(string commandLine, string key)
        {
            if (string.IsNullOrWhiteSpace(commandLine) || string.IsNullOrWhiteSpace(key)) return null;
            var marker = key + "=";
            var index = commandLine.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;

            var start = index + marker.Length;
            if (start >= commandLine.Length) return null;
            if (commandLine[start] == '"')
            {
                var endQuote = commandLine.IndexOf('"', start + 1);
                return endQuote > start ? commandLine.Substring(start + 1, endQuote - start - 1) : null;
            }

            var end = start;
            while (end < commandLine.Length && !char.IsWhiteSpace(commandLine[end])) end++;
            return commandLine.Substring(start, end - start).Trim().Trim('"');
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return null;
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return null;
        }
    }

    internal interface ILeagueClientSessionDiscovery
    {
        LeagueClientSession TryDiscover();
    }

    internal sealed class ProcessLockfileLeagueClientSessionDiscovery : ILeagueClientSessionDiscovery
    {
        private static readonly string[] ProcessNames = { "LeagueClientUx", "LeagueClient" };

        public LeagueClientSession TryDiscover()
        {
            foreach (var processName in ProcessNames)
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        var executable = process.MainModule == null ? null : process.MainModule.FileName;
                        var directory = string.IsNullOrWhiteSpace(executable) ? null : Path.GetDirectoryName(executable);
                        var lockfile = string.IsNullOrWhiteSpace(directory) ? null : Path.Combine(directory, "lockfile");
                        if (string.IsNullOrWhiteSpace(lockfile) || !File.Exists(lockfile)) continue;

                        LeagueClientSession session;
                        if (LeagueClientSessionParser.TryParseLockfile(File.ReadAllText(lockfile), out session))
                            return session;
                    }
                    catch
                    {
                        // League Client can exit or deny process-module access while discovery is running.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            return null;
        }
    }

    internal sealed class LeagueClientSessionProvider
    {
        private readonly object _sync = new object();
        private readonly ILeagueClientSessionDiscovery _discovery;
        private readonly TimeSpan _retryInterval;
        private LeagueClientSession _session;
        private DateTime _lastDiscoveryAttemptUtc = DateTime.MinValue;
        private string _lastLoggedSessionKey;
        private string _lastInvalidatedSessionKey;

        public LeagueClientSessionProvider(ILeagueClientSessionDiscovery discovery, TimeSpan? retryInterval = null)
        {
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _retryInterval = retryInterval ?? TimeSpan.FromMilliseconds(750);
        }

        public LeagueClientSession GetSession(bool forceRefresh = false)
        {
            lock (_sync)
            {
                if (!forceRefresh && _session != null) return _session;

                var now = DateTime.UtcNow;
                if (!forceRefresh && now - _lastDiscoveryAttemptUtc < _retryInterval) return null;
                _lastDiscoveryAttemptUtc = now;

                var discovered = _discovery.TryDiscover();
                _session = discovered;
                if (discovered != null && !string.Equals(_lastLoggedSessionKey, discovered.IdentityKey, StringComparison.Ordinal))
                {
                    _lastLoggedSessionKey = discovered.IdentityKey;
                    AppLog.Info(
                        "League client session discovered; source=" + discovered.Source +
                        "; protocol=" + discovered.Protocol +
                        "; port=" + discovered.Port +
                        (string.IsNullOrWhiteSpace(discovered.PlatformId) ? string.Empty : "; platform=" + discovered.PlatformId));
                }
                return _session;
            }
        }

        public void Invalidate(LeagueClientSession expected)
        {
            if (expected == null) return;
            lock (_sync)
            {
                if (_session == null || !_session.Matches(expected)) return;
                _session = null;
                _lastDiscoveryAttemptUtc = DateTime.UtcNow;
                if (!string.Equals(_lastInvalidatedSessionKey, expected.IdentityKey, StringComparison.Ordinal))
                {
                    _lastInvalidatedSessionKey = expected.IdentityKey;
                    AppLog.Info(
                        "League client session invalidated; source=" + expected.Source +
                        "; protocol=" + expected.Protocol +
                        "; port=" + expected.Port);
                }
            }
        }
    }

    internal interface ILeagueClientApi
    {
        Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken);
    }

    internal sealed class LeagueClientApiClient : ILeagueClientApi, IDisposable
    {
        private readonly object _sync = new object();
        private readonly LeagueClientSessionProvider _sessions;
        private readonly List<HttpClient> _retiredClients = new List<HttpClient>();
        private LeagueClientSession _clientSession;
        private HttpClient _client;
        private bool _disposed;

        public LeagueClientApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public async Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            cancellationToken.ThrowIfCancellationRequested();

            var session = _sessions.GetSession();
            if (session == null) return null;

            HttpClient client;
            try
            {
                client = GetOrCreateClient(session);
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            try
            {
                using (var response = await client.GetAsync(NormalizePath(path), cancellationToken).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        _sessions.Invalidate(session);
                    if (!response.IsSuccessStatusCode) return null;
                    return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                _sessions.Invalidate(session);
                return null;
            }
            catch (HttpRequestException)
            {
                _sessions.Invalidate(session);
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
            catch
            {
                _sessions.Invalidate(session);
                return null;
            }
        }

        private HttpClient GetOrCreateClient(LeagueClientSession session)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LeagueClientApiClient));
                if (_client != null && _clientSession != null && _clientSession.Matches(session)) return _client;

                if (_client != null) _retiredClients.Add(_client);
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                var client = new HttpClient(handler)
                {
                    BaseAddress = session.BaseUri,
                    Timeout = TimeSpan.FromSeconds(2)
                };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    LeagueClientSessionParser.CreateBasicAuthorizationParameter(session));

                _clientSession = session;
                _client = client;
                return client;
            }
        }

        private static string NormalizePath(string path)
        {
            var value = (path ?? string.Empty).Trim();
            if (value.Length == 0) return "/";
            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                if (_client != null) _client.Dispose();
                _client = null;
                _clientSession = null;
                foreach (var client in _retiredClients) client.Dispose();
                _retiredClients.Clear();
            }
        }
    }
}
