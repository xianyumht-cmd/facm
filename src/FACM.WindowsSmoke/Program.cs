using FACM.Core.Desktop;
using FACM.Core.League;
using FACM.Core.Runtime;
using FACM.Core.Settings;
using FACM.Infrastructure.Settings;
using FACM.Platform.Windows.Desktop;
using FACM.Platform.Windows.League;
using FACM.Platform.Windows.Runtime;

var executablePaths = new WindowsExecutablePathProvider();
var layout = RuntimePathLayout.From(executablePaths);
var expectedDistribution = Path.GetDirectoryName(Path.GetFullPath(executablePaths.ExecutablePath))
    ?? throw new InvalidOperationException("Distribution directory unavailable.");
Equal(expectedDistribution, layout.DistributionDirectory, "distribution path provider");
Equal(Path.Combine(expectedDistribution, "settings.ini"), layout.SettingsPath, "stable legacy settings path");
Equal(Path.Combine(expectedDistribution, "settings.v2.json"), layout.Settings2Path, "stable Settings 2.0 path");
Equal(Path.Combine(expectedDistribution, "runtime", "recovery"), layout.RecoveryDirectory, "stable recovery directory");
Equal(Path.Combine(expectedDistribution, "runtime", "recovery", "state.json"), layout.RecoveryStatePath, "stable recovery state path");
True(!layout.Settings2Path.StartsWith(Path.GetFullPath(executablePaths.BaseDirectory), StringComparison.OrdinalIgnoreCase) ||
     layout.Settings2Path.StartsWith(expectedDistribution, StringComparison.OrdinalIgnoreCase),
     "Settings 2.0 must derive from distribution path, not self-extract base directory");
True(!layout.RecoveryStatePath.StartsWith(Path.GetFullPath(executablePaths.BaseDirectory), StringComparison.OrdinalIgnoreCase) ||
     layout.RecoveryStatePath.StartsWith(expectedDistribution, StringComparison.OrdinalIgnoreCase),
     "Recovery metadata must derive from distribution path, not self-extract base directory");

VerifyWindowsDesktopFacts();
await VerifyPhysicalSettings2PersistenceAsync();

var discovered = new LeagueTransportSession(
    new LeagueSessionDescriptor(41, 29999, "https", "windows-smoke", "HN1", "HN"),
    "secret");
var discovery = new FakeDiscovery(discovered);
var source = new WindowsLeagueTransportSessionSource(discovery, TimeSpan.FromMilliseconds(750));

var first = source.GetSession();
var second = source.GetSession();
True(first is not null && ReferenceEquals(first, second), "session source must cache one owner session");
Equal(1, discovery.Calls, "cached session must not rediscover");
Equal(LeagueConnectionState.Connected, source.State, "connected state");
Equal(41, source.Current!.ProcessId, "public descriptor state");

source.Invalidate(first!);
Equal(LeagueConnectionState.Unavailable, source.State, "invalidated state");
var refreshed = source.GetSession(forceRefresh: true);
True(refreshed is not null, "forced refresh after invalidation");
Equal(2, discovery.Calls, "forced refresh discovery count");

Console.WriteLine("FACM 4.0 Windows runtime smoke: SUCCESS");
return;

static void VerifyWindowsDesktopFacts()
{
    var provider = new WindowsDesktopWorkAreaProvider();
    var areas = provider.GetWorkingAreas();
    True(areas.Count > 0, "Windows desktop must expose at least one work area");
    True(areas.Any(area => area.IsPrimary), "Windows desktop must expose a primary work area");
    foreach (var area in areas)
    {
        True(!string.IsNullOrWhiteSpace(area.Id), "desktop work area id");
        True(area.Bounds.IsValid, "desktop work area bounds");
        True(area.DpiScaleX > 0 && double.IsFinite(area.DpiScaleX), "desktop DPI scale X");
        True(area.DpiScaleY > 0 && double.IsFinite(area.DpiScaleY), "desktop DPI scale Y");
    }

    var primary = AnchorPlacementService.SelectWorkArea(areas, null);
    var placement = AnchorPlacementService.Place(new AnchorPlacementRequest(
        [primary],
        new DesktopSize(64 * primary.DpiScaleX, 64 * primary.DpiScaleY),
        null,
        DesktopAnchor.BottomRight,
        12 * primary.DpiScaleX));
    True(primary.Bounds.Contains(new DesktopPoint(
        placement.TopLeft.X + 1,
        placement.TopLeft.Y + 1)), "Windows placement must remain in primary work area");
}

static async Task VerifyPhysicalSettings2PersistenceAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "facm4-settings2-windows-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var legacyPath = Path.Combine(root, "settings.ini");
    var v2Path = Path.Combine(root, "settings.v2.json");
    var legacyText = "BallX=9\r\nBallY=10\r\nThemeId=glass-blue\r\nPetStyleId=greenfly\r\nAutoUpdateEnabled=True\r\n";
    try
    {
        await File.WriteAllTextAsync(legacyPath, legacyText);
        var repository = new Settings2Repository(v2Path, legacyPath);
        var migrated = await repository.LoadAsync();
        Equal(SettingsLoadOrigin.MigratedLegacy, migrated.Origin, "physical migration origin");
        Equal(9, migrated.Settings.Pets.BallX, "physical migration BallX");
        Equal(legacyText, await File.ReadAllTextAsync(legacyPath), "legacy INI must remain unchanged");

        migrated.Settings.Online.AutoUpdateEnabled = false;
        await repository.SaveAsync(migrated.Settings);
        var reloaded = await repository.LoadAsync();
        Equal(SettingsLoadOrigin.ExistingV2, reloaded.Origin, "physical second load origin");
        True(!reloaded.Settings.Online.AutoUpdateEnabled, "physical atomic replacement value");
        Equal(0, Directory.GetFiles(root, "*.tmp", SearchOption.TopDirectoryOnly).Length, "atomic temp cleanup");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
}

static void True(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name + " failed.");
}

sealed class FakeDiscovery(LeagueTransportSession session) : ILeagueSessionDiscovery
{
    public int Calls { get; private set; }
    public LeagueTransportSession? TryDiscover()
    {
        Calls++;
        return session;
    }
}
