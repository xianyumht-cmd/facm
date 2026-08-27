using FACM.Core.Application;
using FACM.Core.Performance;
using FACM.Core.Settings;
using FACM.Core.Text;
using FACM.Infrastructure.Text;

var tests = new (string Name, Action Run)[]
{
    ("module host topology and rollback", TestHost),
    ("performance contract", TestPerformance),
    ("settings.ini compatibility", TestSettings),
    ("ui text adapter", TestUiText)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine("PASS: " + test.Name);
}
Console.WriteLine("FACM 4.0 foundation smoke: SUCCESS");
return;

static void TestHost()
{
    var events = new List<string>();
    using (var host = new FacmHost())
    {
        host.Register(new TestModule("consumer", ["core"], events));
        host.Register(new TestModule("core", [], events));
        host.Initialize();
        Equal("core,consumer", string.Join(',', host.Report.InitializationOrder), "topological init order");
    }
    Equal("init:core,init:consumer,dispose:consumer,dispose:core", string.Join(',', events), "reverse dispose order");

    events.Clear();
    try
    {
        using var host = new FacmHost();
        host.Register(new TestModule("core", [], events));
        host.Register(new TestModule("fail", ["core"], events, true));
        host.Initialize();
        throw new Exception("Expected host failure did not occur.");
    }
    catch (InvalidOperationException)
    {
        Equal("init:core,init:fail,dispose:fail,dispose:core", string.Join(',', events), "failure rollback order");
    }
}

static void TestPerformance()
{
    Equal("desktop", PerformancePolicy.Resolve(new(LeagueActivityLevel.None, true)).Name, "desktop budget");
    Equal("background", PerformancePolicy.Resolve(new(LeagueActivityLevel.Client, false)).Name, "hidden client budget");
    Equal("champ-select", PerformancePolicy.Resolve(new(LeagueActivityLevel.ChampSelect, false)).Name, "champ select overrides hidden");
    Equal("in-game", PerformancePolicy.Resolve(new(LeagueActivityLevel.InGame, false)).Name, "in-game overrides hidden");
    True(PerformancePolicy.IsNoMoreAggressiveThan(PerformancePolicy.InGame, PerformancePolicy.Desktop), "in-game must be no more aggressive than desktop");
}

static void TestSettings()
{
    var source = new[]
    {
        "BallX=123", "BallY=-456", "GamePath=C:\\Games\\League", "AutoUpdateEnabled=False",
        "LastAnnouncementId=notice-9", "ThemeId=obsidian-gold", "PetStyleId=vpet", "AnimalPetEnabled=True",
        "LeagueAutoApplyRecommended=True", "LeagueExitGameHotkey=Ctrl+F9", "LeagueCloseLobbyHotkey=Ctrl+F10",
        "LeagueAutoHonorTeammateEnabled=True", "LeagueAutoReturnLobbyEnabled=True",
        "LeagueAutoMatchmakingEnabled=True", "LeagueAutoAcceptEnabled=True", "UnknownKey=ignored"
    };
    var parsed = LegacySettingsCodec.Parse(source);
    Equal(123, parsed.BallX, "BallX");
    Equal(-456, parsed.BallY, "BallY");
    Equal("obsidian-gold", parsed.ThemeId, "ThemeId");
    Equal("vpet", parsed.PetStyleId, "PetStyleId");
    True(parsed.LeagueAutoAcceptEnabled, "LeagueAutoAcceptEnabled");
    var serialized = LegacySettingsCodec.Serialize(parsed);
    Equal(15, serialized.Count, "stable key count");
    var fallback = LegacySettingsCodec.Parse(["ThemeId=unknown", "PetStyleId=unknown"]);
    Equal(LegacySettingsSnapshot.DefaultThemeId, fallback.ThemeId, "unknown theme fallback");
    Equal(LegacySettingsSnapshot.DefaultPetId, fallback.PetStyleId, "unknown pet fallback");
}

static void TestUiText()
{
    var overrides = LegacyUiTextOverrideCodec.Parse(["[Text]", "ControlCenter=我的中心", "[Replace]", "ControlCenter=bad"]);
    var provider = new DictionaryUiTextProvider(overrides);
    Equal("我的中心", provider.Get(UiTextKeys.ControlCenter), "text override");
    Equal("英雄联盟", provider.Get(UiTextKeys.ShellLeague), "text fallback");
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

sealed class TestModule(string id, IReadOnlyList<string> dependencies, List<string> events, bool fail = false) : IFacmModule
{
    public string Id { get; } = id;
    public IReadOnlyList<string> Dependencies { get; } = dependencies;
    public void Initialize()
    {
        events.Add("init:" + Id);
        if (fail) throw new InvalidOperationException("planned failure");
    }
    public void Dispose() => events.Add("dispose:" + Id);
}
