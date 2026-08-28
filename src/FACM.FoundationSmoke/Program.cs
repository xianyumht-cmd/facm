using FACM.Core.Application;
using FACM.Core.Cleanup;
using FACM.Core.League;
using FACM.Core.Online;
using FACM.Core.Performance;
using FACM.Core.Settings;
using FACM.Core.Text;
using FACM.Infrastructure.Settings;
using FACM.Infrastructure.Text;

var tests = new (string Name, Func<Task> Run)[]
{
    ("module host topology and rollback", () => { TestHost(); return Task.CompletedTask; }),
    ("performance contract", () => { TestPerformance(); return Task.CompletedTask; }),
    ("settings.ini compatibility", () => { TestSettings(); return Task.CompletedTask; }),
    ("ui text adapter", () => { TestUiText(); return Task.CompletedTask; }),
    ("cleanup application boundary", TestCleanupAsync),
    ("league write capability boundary", () => { TestLeagueWritePolicy(); return Task.CompletedTask; }),
    ("online update decision", () => { TestUpdateDecision(); return Task.CompletedTask; }),
    ("settings repository adapter", TestSettingsRepositoryAsync),
    ("gate3 runtime and transport", Gate3Smoke.RunAsync),
    ("gate4 Settings 2.0 migration and atomic persistence", Settings2Smoke.RunAsync),
    ("gate5 Product State and observability", Gate5Smoke.RunAsync),
    ("gate6 design system and shell text", Gate6Smoke.RunAsync),
    ("gate7 desktop anchor placement", Gate7Smoke.RunAsync),
    ("gate8 state-driven League Workbench", Gate8Smoke.RunAsync),
    ("productization League Build Advisor", LeagueBuildAdvisorSmoke.RunAsync),
    ("productization repair parity", () => { RepairParitySmoke.Run(); return Task.CompletedTask; }),
    ("productization personalization catalogs", () => { PersonalizationSmoke.Run(); return Task.CompletedTask; }),
    ("gate9 sanitized Diagnostics Center", Gate9Smoke.RunAsync),
    ("gate10 DPI and mixed-monitor accessibility contract", Gate10Smoke.RunAsync),
    ("gate11 recovery and monotonic feature policy", Gate11Smoke.RunAsync),
    ("gate12 release evidence and performance matrix", Gate12Smoke.RunAsync),
    ("gate13 production cutover guard", Gate13Smoke.RunAsync)
};

foreach (var test in tests)
{
    await test.Run();
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
    Equal(15, LegacySettingsCodec.Serialize(parsed).Count, "stable key count");
    var fallback = LegacySettingsCodec.Parse(["ThemeId=unknown", "PetStyleId=unknown"]);
    Equal(LegacySettingsSnapshot.DefaultThemeId, fallback.ThemeId, "unknown theme fallback");
    Equal(LegacySettingsSnapshot.DefaultPetId, fallback.PetStyleId, "unknown pet fallback");
}

static void TestUiText()
{
    var overrides = LegacyUiTextOverrideCodec.Parse(["[Text]", "ControlCenter=我的中心", "[Replace]", "ControlCenter=bad"]);
    var provider = new DictionaryUiTextProvider(overrides);
    Equal("我的中心", provider.Get(UiTextKeys.ControlCenter), "text override");
    Equal("LOL 工作台", provider.Get(UiTextKeys.ShellLeague), "text fallback");
}

static async Task TestCleanupAsync()
{
    var planner = new FakeCleanupPlanner();
    var executor = new FakeCleanupExecutor();
    var service = new CleanupApplicationService(planner, executor);
    var plan = await service.PreviewAsync("C:\\League");
    Equal(1, plan.DeletableTargets.Count, "deletable target count");
    try
    {
        await service.ExecuteConfirmedAsync(plan, false);
        throw new Exception("Unconfirmed cleanup should fail.");
    }
    catch (InvalidOperationException) { }
    await service.ExecuteConfirmedAsync(plan, true);
    Equal(1, executor.Calls, "confirmed cleanup call count");
}

static void TestLeagueWritePolicy()
{
    var selection = new LeagueWriteCommand(LeagueWriteCapability.ApplyMySelection, null, "{}");
    True(LeagueWriteTargetPolicy.Matches(selection, "PATCH", "/lol-champ-select/v1/session/my-selection"), "selection allowlist");
    True(!LeagueWriteTargetPolicy.Matches(selection, "POST", "/lol-champ-select/v1/session/actions/1"), "selection must reject arbitrary action");
    var page = new LeagueWriteCommand(LeagueWriteCapability.UpdatePerkPage, 42, "{}");
    True(LeagueWriteTargetPolicy.Matches(page, "PUT", "/lol-perks/v1/pages/42"), "perk page allowlist");
    try
    {
        LeagueWriteTargetPolicy.Resolve(new LeagueWriteCommand(LeagueWriteCapability.UpdatePerkPage, 0, "{}"));
        throw new Exception("Invalid perk page id should fail.");
    }
    catch (ArgumentException) { }
}

static void TestUpdateDecision()
{
    var manifest = new UpdateManifestSnapshot(true, "4.0.0", "4.0.0", true, "https://example.invalid/facm.exe", "ABC", "test", "2026-08-27");
    var decision = UpdateDecisionService.Evaluate(new Version(3, 5, 15), manifest);
    True(decision.UpdateAvailable, "update available");
    True(decision.ForceUpdateRequired, "force update below minimum");
    var disabled = UpdateDecisionService.Evaluate(new Version(3, 5, 15), manifest with { Enabled = false });
    True(!disabled.UpdateAvailable, "disabled update must not surface availability");
}

static async Task TestSettingsRepositoryAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "facm4-smoke-" + Guid.NewGuid().ToString("N"));
    try
    {
        var path = Path.Combine(root, "settings.ini");
        var repository = new IniSettingsRepository(path);
        var settings = new LegacySettingsSnapshot { BallX = 77, ThemeId = "obsidian-gold" };
        await repository.SaveAsync(settings);
        var loaded = await repository.LoadAsync();
        Equal(77, loaded.BallX, "repository BallX");
        Equal("obsidian-gold", loaded.ThemeId, "repository ThemeId");
        Equal(15, File.ReadAllLines(path).Length, "repository stable line count");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
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

sealed class FakeCleanupPlanner : ICleanupPlanner
{
    public Task<CleanupPlan> CreatePlanAsync(string selectedPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CleanupTarget[] targets =
        [
            new(Path.Combine(selectedPath, "Logs", "a.log"), "日志", CleanupRuleKind.LogFile, CleanupTargetKind.File, 20, 1, 0, false, string.Empty),
            new(Path.Combine(selectedPath, "keep"), "保护", CleanupRuleKind.ContainerChild, CleanupTargetKind.Directory, 0, 0, 0, true, "blocked")
        ];
        return Task.FromResult(new CleanupPlan(selectedPath, targets));
    }
}

sealed class FakeCleanupExecutor : ICleanupExecutor
{
    public int Calls { get; private set; }
    public Task<CleanupResult> ExecuteAsync(CleanupPlan plan, IProgress<CleanupProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(new CleanupResult(1, 0, Array.Empty<string>()));
    }
}
