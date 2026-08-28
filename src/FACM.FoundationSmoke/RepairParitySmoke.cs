using FACM.Core.League;
using FACM.Core.Text;

internal static class RepairParitySmoke
{
    public static void Run()
    {
        TestNarrowRepairWrites();
        TestWindowPlanner();
        TestRepairTextDefaults();
    }

    private static void TestNarrowRepairWrites()
    {
        var playAgain = new LeagueWriteCommand(LeagueWriteCapability.PlayAgain, null, null);
        var restartUx = new LeagueWriteCommand(LeagueWriteCapability.RestartClientUx, null, null);

        True(LeagueWriteTargetPolicy.Matches(playAgain, "POST", "/lol-lobby/v2/play-again"), "play-again exact target");
        True(LeagueWriteTargetPolicy.Matches(restartUx, "POST", "/riotclient/kill-and-restart-ux"), "restart-ux exact target");
        True(!LeagueWriteTargetPolicy.Matches(playAgain, "POST", "/lol-lobby/v2/queue"), "play-again arbitrary target rejection");
        True(!LeagueWriteTargetPolicy.Matches(restartUx, "DELETE", "/riotclient/kill-and-restart-ux"), "restart-ux verb rejection");
    }

    private static void TestWindowPlanner()
    {
        var work = new LeagueWindowBounds(0, 0, 1920, 1040);
        var sane = new LeagueWindowBounds(120, 80, 1280, 720);
        True(LeagueWindowRepairPlanner.IsSane(sane, work), "16:9 sane window");
        var sanePlan = LeagueWindowRepairPlanner.Plan(sane, work, null, 1.0);
        True(sanePlan.CurrentIsSane, "sane plan flag");
        Equal(sane, sanePlan.TargetBounds, "sane window remains unchanged");

        var offscreen = new LeagueWindowBounds(1750, 980, 1280, 720);
        var offscreenPlan = LeagueWindowRepairPlanner.Plan(offscreen, work, new LeagueWindowSize(1280, 720), 1.0);
        True(offscreenPlan.TargetBounds.Left >= work.Left, "offscreen left clamp");
        True(offscreenPlan.TargetBounds.Top >= work.Top, "offscreen top clamp");
        True(offscreenPlan.TargetBounds.Right <= work.Right, "offscreen right clamp");
        True(offscreenPlan.TargetBounds.Bottom <= work.Bottom, "offscreen bottom clamp");

        var broken = new LeagueWindowBounds(200, 100, 400, 900);
        var rememberedPlan = LeagueWindowRepairPlanner.Plan(broken, work, new LeagueWindowSize(1280, 720), 1.0);
        Equal(new LeagueWindowSize(1280, 720), rememberedPlan.TargetBounds.Size, "remembered sane size");
        Equal("remembered-sane-size", rememberedPlan.Reason, "remembered reason");

        var tinyWork = new LeagueWindowBounds(-1600, 0, 1600, 900);
        var fallback = LeagueWindowRepairPlanner.Plan(new LeagueWindowBounds(0, 0, 10, 10), tinyWork, null, 1.25);
        True(fallback.TargetBounds.Width <= (int)Math.Floor(tinyWork.Width * 0.96) + 1, "fallback width fits monitor");
        True(fallback.TargetBounds.Height <= (int)Math.Floor(tinyWork.Height * 0.96) + 1, "fallback height fits monitor");
        True(Math.Abs(fallback.TargetBounds.Width / (double)fallback.TargetBounds.Height - 16.0 / 9.0) < 0.01, "fallback aspect");
    }

    private static void TestRepairTextDefaults()
    {
        foreach (var key in new[]
        {
            UiTextKeys.RepairPrivilegeLabel,
            UiTextKeys.RepairPrivilegeAdministrator,
            UiTextKeys.RepairPrivilegeStandard,
            UiTextKeys.RepairDriverCleanup,
            UiTextKeys.RepairDriverCleanupHint,
            UiTextKeys.RepairGameRepair,
            UiTextKeys.RepairGameRepairHint,
            UiTextKeys.RepairFixWindow,
            UiTextKeys.RepairFixWindowHint,
            UiTextKeys.RepairAutoWindow,
            UiTextKeys.RepairAutoWindowDisable,
            UiTextKeys.RepairAutoWindowHint,
            UiTextKeys.RepairSkipSettlement,
            UiTextKeys.RepairSkipSettlementHint,
            UiTextKeys.RepairRestartClientUx,
            UiTextKeys.RepairRestartClientUxHint,
            UiTextKeys.RepairExitGame,
            UiTextKeys.RepairExitGameHint,
            UiTextKeys.RepairGameRepairReady
        })
        {
            True(!string.Equals(FoundationUiTextDefaults.Get(key), key, StringComparison.Ordinal), "repair UI text default " + key);
        }
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }
}
