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
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }
}
