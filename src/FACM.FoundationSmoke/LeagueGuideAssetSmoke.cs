using FACM.Infrastructure.League;

internal static class LeagueGuideAssetSmoke
{
    public static void Run()
    {
        var itemUris = LeagueGuideAssetService.ResolveUris("items", 3078, "/lol-game-data/assets/v1/items/icons2d/3078.png");
        True(itemUris.Count == 2, "guide item has Tencent and CommunityDragon routes");
        True(itemUris[0].Host.Equals("game.gtimg.cn", StringComparison.OrdinalIgnoreCase), "Tencent is first asset route");
        True(itemUris[1].Host.Equals("raw.communitydragon.org", StringComparison.OrdinalIgnoreCase), "CommunityDragon is fixed fallback");

        var traversal = LeagueGuideAssetService.ResolveUris("items", 3078, "/lol-game-data/assets/v1/items/../secret.png");
        True(traversal[1].AbsoluteUri.Contains("items/icons2d/3078.png", StringComparison.Ordinal), "invalid asset path falls back to typed item route");

        var augmentUris = LeagueGuideAssetService.ResolveUris(
            "augments",
            1037,
            "https://opgg-static.akamaized.net/meta/images/lol/latest/aram-augment/FirstAidKit_small.png");
        True(augmentUris.Count == 1 && augmentUris[0].Host.Equals("opgg-static.akamaized.net", StringComparison.OrdinalIgnoreCase),
            "augment fallback must allow only the fixed OP.GG icon host and path");

        var invalidAugment = LeagueGuideAssetService.ResolveUris(
            "augments",
            1037,
            "https://example.test/FirstAidKit_small.png");
        True(invalidAugment.Count == 0, "augment fallback must reject arbitrary icon hosts");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }
}
