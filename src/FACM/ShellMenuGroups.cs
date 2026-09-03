using System;
using System.Linq;
using System.Windows.Forms;
using FACM.League;
using FACM.Services;

namespace FACM
{
    /// <summary>
    /// Stable Shell information-architecture boundary.
    /// League owns one direct launcher; other business modules register only in fixed non-League groups.
    /// </summary>
    internal static class ShellMenuGroups
    {
        public const string OpenRootName = "FACM.Shell.Open";
        public const string CleanupRootName = "FACM.Shell.Cleanup";
        public const string LeagueGroupName = "FACM.Shell.League";
        public const string MoreGroupName = "FACM.Shell.More";
        public const string ExitRootName = "FACM.Shell.Exit";
        public const string MayhemActionName = "FACM.Mayhem";

        // Legacy ordering constants are kept for compatibility with old bridge files. League Hub no longer
        // exposes these as Shell submenu items.
        public const int DashboardOrder = 10;
        public const int PlayerOrder = 20;
        public const int LiveOrder = 30;
        public const int AdvisorOrder = 40;
        public const int ApplyOrder = 50;
        public const int ItemSetOrder = 60;
        public const int EfficiencyOrder = 70;
        public const int MayhemOrder = 90;

        private static readonly string[] RootContractNames =
        {
            OpenRootName,
            CleanupRootName,
            LeagueGroupName,
            MoreGroupName,
            ExitRootName
        };

        public static ToolStripMenuItem CreateRootGroup(string name, string text)
        {
            var item = new ToolStripMenuItem(text ?? string.Empty) { Name = name ?? string.Empty };
            if (string.Equals(name, LeagueGroupName, StringComparison.Ordinal))
                item.Click += delegate { LeagueHubUiBridge.RequestOpen(); };
            else if (string.Equals(name, MoreGroupName, StringComparison.Ordinal))
                item.DropDownItems.Add(DiagnosticsShellAction.CreateMenuItem());
            return item;
        }

        public static bool AddLeagueAction(System.Windows.Forms.ContextMenuStrip root, string name, string text, int order, EventHandler click)
        {
            // League navigation is intentionally centralized in League Hub. Keeping this method as a
            // no-op prevents a legacy UiBridge from silently rebuilding the old multi-button submenu.
            return false;
        }

        public static bool AddMoreAction(System.Windows.Forms.ContextMenuStrip root, string name, string text, int order, EventHandler click)
        {
            return AddGroupAction(root, MoreGroupName, name, text, order, click);
        }

        public static bool HasLeagueAction(System.Windows.Forms.ContextMenuStrip root, string name)
        {
            return false;
        }

        public static ToolStripMenuItem FindGroup(System.Windows.Forms.ContextMenuStrip root, string groupName)
        {
            if (root == null || root.IsDisposed || string.IsNullOrWhiteSpace(groupName)) return null;
            if (string.Equals(groupName, LeagueGroupName, StringComparison.Ordinal))
            {
                // CompactMenuForm still asks MainForm to show the old League dropdown. Route that legacy
                // call to the same direct Hub launcher and return no dropdown target.
                LeagueHubUiBridge.RequestOpen();
                return null;
            }
            return root.Items.OfType<ToolStripMenuItem>()
                .FirstOrDefault(item => string.Equals(item.Name, groupName, StringComparison.Ordinal));
        }

        internal static int ActionableRootCount(System.Windows.Forms.ContextMenuStrip root)
        {
            if (root == null) return 0;
            return root.Items.Cast<ToolStripItem>().Count(item => !(item is ToolStripSeparator));
        }

        internal static bool RootContainsAction(System.Windows.Forms.ContextMenuStrip root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name)) return false;
            return root.Items.Cast<ToolStripItem>().Any(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        }

        internal static void ValidateRootContract(System.Windows.Forms.ContextMenuStrip root)
        {
            if (root == null) throw new InvalidOperationException("Shell root menu is missing.");
            if (ActionableRootCount(root) != RootContractNames.Length)
                throw new InvalidOperationException("Shell root must expose exactly five novice-facing actions/groups.");
            foreach (var name in RootContractNames)
            {
                if (!RootContainsAction(root, name))
                    throw new InvalidOperationException("Shell root contract is missing: " + name);
            }
        }

        internal static void ValidateDefinitionForSmokeTest()
        {
            if (RootContractNames.Length != 5)
                throw new InvalidOperationException("Shell root definition must contain exactly five entries.");
            if (RootContractNames.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Shell root definition contains an empty name.");
            if (RootContractNames.Distinct(StringComparer.Ordinal).Count() != RootContractNames.Length)
                throw new InvalidOperationException("Shell root definition contains duplicate names.");

            var businessActions = new[]
            {
                "FACM.LeagueDashboard",
                "FACM.LeaguePlayer",
                "FACM.LeagueLive",
                "FACM.LeagueBuildAdvisor",
                "FACM.LeagueBuildApply",
                "FACM.LeagueItemSet",
                "FACM.LeagueEfficiency",
                MayhemActionName
            };
            if (businessActions.Any(action => RootContractNames.Contains(action, StringComparer.Ordinal)))
                throw new InvalidOperationException("A business action name leaked into the fixed Shell root contract.");

            var more = CreateRootGroup(MoreGroupName, "More");
            try
            {
                var diagnostics = more.DropDownItems.Cast<ToolStripItem>()
                    .FirstOrDefault(item => string.Equals(item.Name, DiagnosticsShellAction.ActionName, StringComparison.Ordinal));
                if (diagnostics == null || !(diagnostics.Tag is int) || (int)diagnostics.Tag != DiagnosticsShellAction.Order)
                    throw new InvalidOperationException("Shell More group lost its diagnostics support action.");
            }
            finally
            {
                more.Dispose();
            }

            LeagueHubNavigation.ValidateForSmokeTest();
        }

        private static bool AddGroupAction(System.Windows.Forms.ContextMenuStrip root, string groupName, string name, string text, int order, EventHandler click)
        {
            if (root == null || root.IsDisposed) return false;
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Shell action name is required.", nameof(name));
            ValidateRootContract(root);

            var group = FindGroup(root, groupName);
            if (group == null) throw new InvalidOperationException("Shell root group is missing: " + groupName);
            if (group.DropDownItems.Cast<ToolStripItem>().Any(item => string.Equals(item.Name, name, StringComparison.Ordinal)))
                return false;

            var item = new ToolStripMenuItem(text ?? string.Empty) { Name = name, Tag = order };
            if (click != null) item.Click += click;

            var insertAt = group.DropDownItems.Count;
            for (var index = 0; index < group.DropDownItems.Count; index++)
            {
                var existing = group.DropDownItems[index];
                var existingOrder = existing.Tag is int ? (int)existing.Tag : int.MaxValue;
                if (order < existingOrder)
                {
                    insertAt = index;
                    break;
                }
            }
            group.DropDownItems.Insert(insertAt, item);
            ValidateRootContract(root);
            return true;
        }
    }
}
