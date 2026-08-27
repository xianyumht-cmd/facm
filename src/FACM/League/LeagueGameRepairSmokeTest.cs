using System;
using System.Drawing;

namespace FACM.League
{
    internal static class LeagueGameRepairSmokeTest
    {
        public static void Validate()
        {
            var hdWork = new Rectangle(0, 0, 1920, 1040);
            Require(LeagueWindowRepairPlanner.IsSane(new Rectangle(120, 80, 1280, 720), hdWork),
                "Native League repair rejected a normal 16:9 client window.");
            Require(!LeagueWindowRepairPlanner.IsSane(new Rectangle(120, 80, 500, 900), hdWork),
                "Native League repair accepted an obviously corrupted client window.");

            var preserveWidth = LeagueWindowRepairPlanner.Plan(
                new Rectangle(80, 60, 1500, 500), hdWork, null, 1.0);
            Require(preserveWidth.TargetBounds.Width == 1500 && Math.Abs(preserveWidth.TargetBounds.Height - 844) <= 1,
                "Native League repair did not preserve a trustworthy current width.");
            Require(string.Equals(preserveWidth.Reason, "preserve-current-width", StringComparison.Ordinal),
                "Native League repair lost width-preserving recovery diagnostics.");

            var largeWork = new Rectangle(1920, 0, 2560, 1400);
            var remembered = LeagueWindowRepairPlanner.Plan(
                new Rectangle(2200, 100, 420, 900), largeWork, new Size(1600, 900), 1.0);
            Require(remembered.TargetBounds.Size == new Size(1600, 900),
                "Native League repair did not prefer the last known sane size.");
            Require(string.Equals(remembered.Reason, "remembered-sane-size", StringComparison.Ordinal),
                "Native League repair lost remembered-size diagnostics.");

            var leftMonitor = new Rectangle(-2560, 0, 2560, 1400);
            var recentered = LeagueWindowRepairPlanner.Plan(
                new Rectangle(4000, 3000, 200, 200), leftMonitor, null, 1.0);
            Require(recentered.TargetBounds.Left >= leftMonitor.Left && recentered.TargetBounds.Right <= leftMonitor.Right,
                "Native League repair moved a client outside a negative-coordinate monitor.");
            Require(recentered.TargetBounds.Top >= leftMonitor.Top && recentered.TargetBounds.Bottom <= leftMonitor.Bottom,
                "Native League repair did not clamp to the target monitor working area.");

            Require(LeagueClientUxRepairWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/riotclient/kill-and-restart-ux"),
                "Client UX repair writer rejected its single allowed route.");
            Require(!LeagueClientUxRepairWriteApiClient.IsAllowedTargetForSmokeTest("GET", "/riotclient/kill-and-restart-ux"),
                "Client UX repair writer accepted an unexpected method.");
            Require(!LeagueClientUxRepairWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-champ-select/v1/session/actions/1"),
                "Client UX repair writer leaked into Champ Select writes.");
            Require(LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeaguePostGameWriteApiClient.PlayAgainPath),
                "Manual skip-settlement can no longer reuse the existing bounded post-game writer.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
