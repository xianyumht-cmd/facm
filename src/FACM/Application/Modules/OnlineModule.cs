using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FACM.AppHost;
using FACM.Online;

namespace FACM.AppHost.Modules
{
    internal sealed class OnlineModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> NoDependencies = Array.Empty<string>();

        public const string ModuleId = "online";

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
        }

        public Task<OnlineSnapshot> FetchSnapshotAsync(CancellationToken cancellationToken)
        {
            return OnlineService.FetchSnapshotAsync(cancellationToken);
        }

        public void Dispose()
        {
        }
    }
}
