using System.Net;
using System.Text;
using FACM.Core.Mayhem;
using FACM.Infrastructure.Mayhem;

internal static class MayhemBaseBalanceSmoke
{
    public static async Task RunAsync()
    {
        ValidateCurrentPatchParse();
        ValidatePatchMismatchFailsClosed();
        ValidateUnknownSignedModifierFailsClosed();
        ValidateLayeredProjection();
        await ValidateTypedFallbackAndSnapshotCacheAsync();
    }

    private static void ValidateCurrentPatchParse()
    {
        var snapshot = MayhemBaseAramBalanceService.ParseForSmoke(
            "Patch 15.18 Balance adjustment Damage Dealt +5% Damage Taken -10% Ability Haste +20 Summoner Spells",
            "25.18");
        Require(snapshot.Status == "ok" && snapshot.Complete && snapshot.CurrentPatchVerified,
            "Base ARAM current-patch parse must produce a complete verified snapshot.");
        Require(snapshot.DisplayPatch == "25.18" && snapshot.Changes.Count == 3,
            "Base ARAM patch display conversion or known field parsing changed from 3.5.");
        Require(snapshot.Summary.Contains("造成伤害 +5%", StringComparison.Ordinal) &&
                snapshot.Summary.Contains("承受伤害 -10%", StringComparison.Ordinal),
            "Base ARAM localized balance summary was not preserved.");
        var taken = snapshot.Changes.Single(change => change.Key == "damage_taken");
        Require(taken.Direction == "buff", "Damage Taken negative modifier must remain a buff direction.");
    }

    private static void ValidatePatchMismatchFailsClosed()
    {
        var snapshot = MayhemBaseAramBalanceService.ParseForSmoke(
            "Patch 15.18 Balance adjustment Damage Dealt +5% Summoner Spells",
            "25.19");
        Require(snapshot.Status == "syncing" && !snapshot.Complete && snapshot.Changes.Count == 0,
            "Stale base ARAM page must be syncing and hide old complete values.");
        Require(snapshot.ErrorClass == "patch_mismatch" && snapshot.Summary.Contains("旧完整数值已隐藏", StringComparison.Ordinal),
            "Patch mismatch did not preserve the 3.5 fail-closed explanation.");
    }

    private static void ValidateUnknownSignedModifierFailsClosed()
    {
        var snapshot = MayhemBaseAramBalanceService.ParseForSmoke(
            "Patch 15.18 Balance adjustment Damage Dealt +5% Mystery Power +7% Summoner Spells",
            "25.18");
        Require(snapshot.Status == "unavailable" && !snapshot.Complete,
            "Unknown signed base-balance modifier must not be presented as a complete snapshot.");
        Require(snapshot.ErrorClass == "unparsed_balance_values",
            "Unknown signed modifier must retain the fail-closed error class.");
    }

    private static void ValidateLayeredProjection()
    {
        var result = new MayhemChampionResult
        {
            ChampionSlug = "ashe",
            MayhemBalanceSummary = "造成伤害 +3%",
            SourceNote = "国服版本：腾讯官网已校验"
        };
        var snapshot = MayhemBaseAramBalanceService.ParseForSmoke(
            "Patch 15.18 Balance adjustment Damage Taken -10% Summoner Spells",
            "25.18");
        MayhemBaseAramBalanceService.ApplySnapshotForSmoke(result, snapshot);

        Require(result.BaseBalanceComplete && result.BaseBalanceStatus == "ok" && result.BaseBalancePatch == "25.18",
            "Base ARAM snapshot metadata was not projected into the public Mayhem result.");
        Require(result.BalanceSummary.Contains("基础 ARAM（完整）", StringComparison.Ordinal) &&
                result.BalanceSummary.Contains("Mayhem：造成伤害 +3%", StringComparison.Ordinal),
            "Base ARAM and Mayhem modifiers must remain separate layers rather than being numerically combined.");
        Require(result.SourceNote.Contains("基础平衡：OP.GG ARAM", StringComparison.Ordinal),
            "Base ARAM source attribution was not appended.");
    }

    private static async Task ValidateTypedFallbackAndSnapshotCacheAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-mayhem-base-balance-" + Guid.NewGuid().ToString("N"));
        var now = new DateTimeOffset(2026, 8, 28, 11, 0, 0, TimeSpan.Zero);
        try
        {
            var handler = new RouteHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("/zh-cn/", StringComparison.OrdinalIgnoreCase))
                    return Html("Patch 15.18 no balance section here");
                return Html("Patch 15.18 Balance adjustment Damage Dealt +4% Summoner Spells");
            });
            using var transport = new MayhemCachedPublicDataTransport(root, handler, () => now);
            var service = new MayhemBaseAramBalanceService(transport, () => now);
            var first = new MayhemChampionResult
            {
                ChampionSlug = "ashe",
                Patch = "25.18",
                BalanceSummary = "Mayhem only"
            };
            await service.EnrichAsync(first);

            Require(handler.Calls == 2,
                "Base ARAM enrichment must try localized then global typed resources when localized parsing is unusable.");
            Require(first.BaseBalanceComplete && first.BaseBalanceSummary.Contains("造成伤害 +4%", StringComparison.Ordinal),
                "Global typed fallback did not populate a valid base ARAM snapshot.");

            var second = new MayhemChampionResult
            {
                ChampionSlug = "ashe",
                Patch = "25.18",
                BalanceSummary = "Mayhem next"
            };
            await service.EnrichAsync(second);
            Require(handler.Calls == 2,
                "Usable base ARAM snapshot must be served from the 10-minute service cache without another transport call.");
            Require(second.BaseBalanceComplete,
                "Cached base ARAM snapshot was not reapplied to the next query.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static HttpResponseMessage Html(string value) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(Encoding.UTF8.GetBytes(value))
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(route(request));
        }
    }
}
