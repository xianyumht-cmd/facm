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

        public async Task<OnlineSnapshot> FetchSnapshotAsync(CancellationToken cancellationToken)
        {
            var snapshot = await OnlineService.FetchSnapshotAsync(cancellationToken).ConfigureAwait(false);

            // A release announcement must never masquerade as an available update. MainForm treats
            // Announcement.Popup as a request to open the full Online Center, so normalize that flag
            // away whenever the installed binary is already current. The announcement itself remains
            // enabled and will still be surfaced by the normal tray notification path.
            if (snapshot != null &&
                !snapshot.UpdateAvailable &&
                !snapshot.ForceUpdateRequired &&
                snapshot.Announcement != null)
            {
                snapshot.Announcement.Popup = false;
            }

            return snapshot;
        }

        public void Dispose()
        {
        }
    }
}
