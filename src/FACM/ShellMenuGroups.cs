using System;
using System.Linq;
using System.Windows.Forms;

namespace FACM
{
    /// <summary>
    /// Stable Shell information-architecture boundary.
    ///
    /// Business modules must register actions inside one of the fixed root groups instead of
    /// inserting new first-level tray items. This keeps the novice-facing root menu bounded even as
    /// FACM gains more capabilities.
    /// </summary>
    internal static class ShellMenuGroups
    {
        public const string OpenRootName = "FACM.Shell.Open";
        public const string CleanupRootName = "FACM.Shell.Cleanup";
        public const string LeagueGroupName = "FACM.Shell.League";
        public const string MoreGroupName = "FACM.Shell.More";
        public const string ExitRootName = "FACM.Shell.Exit";
        public const string MayhemActionName = "FACM.Mayhem";

        public const int DashboardOrder = 10;
        public const int PlayerOrder = 20;
        public const int LiveOrder = 30;
        public const int AdvisorOrder = 40;
        public const int ApplyOrder = 50;
        public const int ItemSetOrder = 60;
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
            return new ToolStripMenuItem(text ?? string.Empty) { Name = name ?? string.Empty };
        }

        public static bool AddLeagueAction(
            System.Windows.Forms.ContextMenuStrip root,
            string name,
            string text,
            int order,
            EventHandler click)
        {
            return AddGroupAction(root, LeagueGroupName, name, text, order, click);
        }

        public static bool AddMoreAction(
            System.Windows.Forms.ContextMenuStrip root,
            string name,
            string text,
            int order,
            EventHandler click)
        {
            return AddGroupAction(root, MoreGroupName, name, text, order, click);
        }

        public static bool HasLeagueAction(System.Windows.Forms.ContextMenuStrip root, string name)
        {
            return HasGroupAction(root, LeagueGroupName, name);
        }

        public static ToolStripMenuItem FindGroup(System.Windows.Forms.ContextMenuStrip root, string groupName)
        {
            if (root == null || root.IsDisposed || string.IsNullOrWhiteSpace(groupName)) return null;
            return root.Items
                .OfType<ToolStripMenuItem>()
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
            return root.Items.Cast<ToolStripItem>()
                .Any(item => string.Equals(item.Name, name, StringComparison.Ordinal));
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

        /// <summary>
        /// Pure contract validation for CI. The performance smoke runs before WinForms visual styles
        /// or a message loop exist, so it must never instantiate ContextMenuStrip/Timer/window state.
        /// Actual runtime registration still validates the live menu through ValidateRootContract.
        /// </summary>
        internal static void ValidateDefinitionForSmokeTest()
        {
            if (RootContractNames.Length != 5)
                throw new InvalidOperationException("Shell root definition must contain exactly five entries.");
            if (RootContractNames.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Shell root definition contains an empty name.");
            if (RootContractNames.Distinct(StringComparer.Ordinal).Count() != RootContractNames.Length)
                throw new InvalidOperationException("Shell root definition contains duplicate names.");

            var orders = new[]
            {
                DashboardOrder,
                PlayerOrder,
                LiveOrder,
                AdvisorOrder,
                ApplyOrder,
                ItemSetOrder,
                MayhemOrder
            };
            for (var index = 1; index < orders.Length; index++)
            {
                if (orders[index] <= orders[index - 1])
                    throw new InvalidOperationException("League submenu order contract is not strictly increasing.");
            }

            if (ItemSetOrder >= MayhemOrder)
                throw new InvalidOperationException("Future OP.GG item-set action must stay before Mayhem in the League group.");

            var businessActions = new[]
            {
                "FACM.LeagueDashboard",
                "FACM.LeaguePlayer",
                "FACM.LeagueLive",
                "FACM.LeagueBuildAdvisor",
                "FACM.LeagueBuildApply",
                "FACM.LeagueItemSet",
                MayhemActionName
            };
            if (businessActions.Any(action => RootContractNames.Contains(action, StringComparer.Ordinal)))
                throw new InvalidOperationException("A business action name leaked into the fixed Shell root contract.");
        }

        private static bool AddGroupAction(
            System.Windows.Forms.ContextMenuStrip root,
            string groupName,
            string name,
            string text,
            int order,
            EventHandler click)
        {
            if (root == null || root.IsDisposed) return false;
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Shell action name is required.", nameof(name));
            ValidateRootContract(root);

            var group = FindGroup(root, groupName);
            if (group == null)
                throw new InvalidOperationException("Shell root group is missing: " + groupName);

            if (group.DropDownItems.Cast<ToolStripItem>()
                .Any(item => string.Equals(item.Name, name, StringComparison.Ordinal)))
                return false;

            var item = new ToolStripMenuItem(text ?? string.Empty)
            {
                Name = name,
                Tag = order
            };
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

        private static bool HasGroupAction(
            System.Windows.Forms.ContextMenuStrip root,
            string groupName,
            string name)
        {
            var group = FindGroup(root, groupName);
            return group != null && group.DropDownItems.Cast<ToolStripItem>()
                .Any(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        }
    }
}
