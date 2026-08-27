using FACM.Core.League;
using FACM.Core.Runtime;
using FACM.Platform.Windows.League;
using FACM.Platform.Windows.Runtime;

var executablePaths = new WindowsExecutablePathProvider();
var layout = RuntimePathLayout.From(executablePaths);
var expectedDistribution = Path.GetDirectoryName(Path.GetFullPath(executablePaths.ExecutablePath))
    ?? throw new InvalidOperationException("Distribution directory unavailable.");
Equal(expectedDistribution, layout.DistributionDirectory, "distribution path provider");
Equal(Path.Combine(expectedDistribution, "settings.ini"), layout.SettingsPath, "stable settings path");

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
