using System.Net;
using System.Text;
using FACM.Infrastructure.Mayhem;

internal static class MayhemPublicDataTransportSmoke
{
    public static async Task RunAsync()
    {
        ValidateTypedResolvers();
        ValidateBudgetsAndCaps();
        await ValidateDirectAndFreshCacheAsync();
        await ValidateStaleFallbackAsync();
        await ValidateSingleFlightAsync();
    }

    private static void ValidateTypedResolvers()
    {
        var augments = MayhemCachedPublicDataTransport.Resolve(
            new MayhemPublicResourceRequest(MayhemPublicResourceKind.MayhemAugments, "Ashe"));
        Require(augments.AbsoluteUri == "https://op.gg/zh-cn/lol/modes/aram-mayhem/ashe/augments",
            "Mayhem augment resolver changed from the 3.5 source.");

        var localizedAram = MayhemCachedPublicDataTransport.Resolve(
            new MayhemPublicResourceRequest(MayhemPublicResourceKind.AramLocalizedBuild, "Dr. Mundo"));
        Require(localizedAram.AbsoluteUri == "https://op.gg/zh-cn/lol/modes/aram/dr-mundo/build",
            "Localized ARAM resolver did not normalize champion slug safely.");

        var detail = MayhemCachedPublicDataTransport.Resolve(
            new MayhemPublicResourceRequest(MayhemPublicResourceKind.CommunityDragonChampionDetail, ChampionId: 22));
        Require(detail.AbsoluteUri.EndsWith("/zh_cn/v1/champions/22.json", StringComparison.Ordinal),
            "CommunityDragon champion-detail resolver lost its numeric typed boundary.");

        try
        {
            _ = MayhemCachedPublicDataTransport.Resolve(
                new MayhemPublicResourceRequest(MayhemPublicResourceKind.CommunityDragonChampionDetail, ChampionId: 0));
            throw new InvalidOperationException("Invalid champion id should not resolve a public-data URL.");
        }
        catch (ArgumentException)
        {
        }
    }

    private static void ValidateBudgetsAndCaps()
    {
        Require(MayhemCachedPublicDataTransport.MaximumBodyBytes == 12L * 1024L * 1024L,
            "Mayhem public-data body cap must preserve the 3.5 12 MB limit.");
        Require(MayhemCachedPublicDataTransport.FreshCacheAge == TimeSpan.FromMinutes(15),
            "Mayhem fresh public-data cache must preserve the 3.5 15-minute age.");
        Require(MayhemCachedPublicDataTransport.StaleCacheAge == TimeSpan.FromHours(24),
            "Mayhem stale public-data fallback must preserve the 3.5 24-hour age.");
    }

    private static async Task ValidateDirectAndFreshCacheAsync()
    {
        var root = TempRoot();
        try
        {
            var handler = new FakeHandler(_ => Response("fresh-body"));
            using var transport = new MayhemCachedPublicDataTransport(root, handler);
            var request = new MayhemPublicResourceRequest(MayhemPublicResourceKind.MayhemBuild, "ashe");

            var direct = await transport.GetAsync(request, TimeSpan.FromSeconds(1), CancellationToken.None);
            Require(direct is { Route: "direct", FromCache: false, IsStale: false } && direct.ReadUtf8() == "fresh-body",
                "First public-data request did not return a direct response.");

            var cached = await transport.GetAsync(request, TimeSpan.FromSeconds(1), CancellationToken.None);
            Require(cached is { Route: "fresh-cache", FromCache: true, IsStale: false } && cached.ReadUtf8() == "fresh-body",
                "Second public-data request did not use the fresh disk cache.");
            Require(handler.Calls == 1, "Fresh cache caused an unnecessary second network request.");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static async Task ValidateStaleFallbackAsync()
    {
        var root = TempRoot();
        try
        {
            var handler = new FakeHandler(_ => Response("stale-body"));
            using var transport = new MayhemCachedPublicDataTransport(root, handler);
            var request = new MayhemPublicResourceRequest(MayhemPublicResourceKind.MayhemAugments, "ashe");
            var first = await transport.GetAsync(request, TimeSpan.FromSeconds(1), CancellationToken.None);
            Require(first?.Route == "direct", "Stale-cache fixture was not seeded by a direct response.");

            var cachePath = transport.GetCachePathForSmoke(request);
            File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.Subtract(TimeSpan.FromHours(1)));
            handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.BadGateway);

            var stale = await transport.GetAsync(request, TimeSpan.FromSeconds(1), CancellationToken.None, allowStale: true);
            Require(stale is { Route: "stale-cache", FromCache: true, IsStale: true } && stale.ReadUtf8() == "stale-body",
                "Network failure did not fall back to a cache entry inside the 24-hour stale window.");
            Require(handler.Calls == 2, "Stale fallback skipped the required direct retry.");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static async Task ValidateSingleFlightAsync()
    {
        var root = TempRoot();
        try
        {
            var handler = new FakeHandler(async (_, cancellationToken) =>
            {
                await Task.Delay(80, cancellationToken);
                return Response("single-flight");
            });
            using var transport = new MayhemCachedPublicDataTransport(root, handler);
            var request = new MayhemPublicResourceRequest(MayhemPublicResourceKind.RankingBuild, "ashe");

            var first = transport.GetAsync(request, TimeSpan.FromSeconds(1), CancellationToken.None);
            var second = transport.GetAsync(request, TimeSpan.FromSeconds(1), CancellationToken.None);
            var results = await Task.WhenAll(first, second);

            Require(results.All(result => result is not null && result.ReadUtf8() == "single-flight"),
                "Concurrent public-data requests did not receive the same resource body.");
            Require(results.Count(result => result!.Route == "direct") == 1 &&
                    results.Count(result => result!.Route == "fresh-cache") == 1,
                "Single-flight did not collapse concurrent requests into one direct fetch plus cache read.");
            Require(handler.Calls == 1, "Single-flight issued more than one network request for the same resource.");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static HttpResponseMessage Response(string value) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(Encoding.UTF8.GetBytes(value))
    };

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-mayhem-public-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
        private int _calls;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this((request, _) => Task.FromResult(responder(request)))
        {
        }

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        public int Calls => Volatile.Read(ref _calls);

        public Func<HttpRequestMessage, HttpResponseMessage> Responder
        {
            set => _responder = (request, _) => Task.FromResult(value(request));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return _responder(request, cancellationToken);
        }
    }
}
