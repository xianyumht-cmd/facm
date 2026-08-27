using FACM.Core.Online;

namespace FACM.Infrastructure.Online;

/// <summary>
/// Gate 2 composition adapter. The WinUI shell can consume the update contract without
/// creating an HttpClient or touching production metadata before the Gate 3 transport lands.
/// </summary>
public sealed class UnavailableUpdateManifestSource : IUpdateManifestSource
{
    public Task<UpdateManifestSnapshot?> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<UpdateManifestSnapshot?>(null);
    }
}
