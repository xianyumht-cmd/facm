using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FACM.AppHost;
using FACM.League;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class LeagueClientModule : IFacmModule, ILeagueClientApi, ILeagueClientWriteApi
    {
        private static readonly IReadOnlyList<string> NoDependencies = Array.Empty<string>();
        private readonly ILeagueClientSessionDiscovery _discovery;
        private LeagueClientSessionProvider _sessions;
        private LeagueClientApiClient _api;
        private LeagueClientWriteApiClient _writer;

        public LeagueClientModule()
            : this(new ResilientLeagueClientSessionDiscovery())
        {
        }

        internal LeagueClientModule(ILeagueClientSessionDiscovery discovery)
        {
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        }

        public const string ModuleId = "league-client";

        public string Id
        {
            get { return ModuleId; }
        }

        public IReadOnlyList<string> Dependencies
        {
            get { return NoDependencies; }
        }

        public void Initialize()
        {
            if (_api != null) return;
            _sessions = new LeagueClientSessionProvider(_discovery);
            _api = new LeagueClientApiClient(_sessions);
            _writer = new LeagueClientWriteApiClient(_sessions);
            AppLog.Info("LeagueClient module initialized; local LCU session discovery is on-demand.");
        }

        public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
        {
            var api = _api;
            return api == null
                ? Task.FromResult<byte[]>(null)
                : api.TryGetBytesAsync(path, cancellationToken);
        }

        public Task<LeagueClientWriteResponse> TrySendJsonAsync(
            string method,
            string path,
            string json,
            CancellationToken cancellationToken)
        {
            var writer = _writer;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TrySendJsonAsync(method, path, json, cancellationToken);
        }

        public void Dispose()
        {
            var writer = _writer;
            var api = _api;
            _writer = null;
            _api = null;
            _sessions = null;
            if (writer != null) writer.Dispose();
            if (api != null) api.Dispose();
        }
    }
}
