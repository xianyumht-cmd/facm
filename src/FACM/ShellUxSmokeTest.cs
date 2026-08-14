using System;
using System.Linq;
using System.Windows.Forms;

namespace FACM
{
    internal static class ShellUxSmokeTest
    {
        internal static void Validate()
        {
            using (var menu = BuildRoot())
            {
                ShellMenuGroups.ValidateRootContract(menu);
                Require(ShellMenuGroups.ActionableRootCount(menu) == 5, "Shell root action count drifted.");

                Require(ShellMenuGroups.AddLeagueAction(menu, "dashboard", "Dashboard", ShellMenuGroups.DashboardOrder, null),
                    "Dashboard action was not registered.");
                Require(ShellMenuGroups.AddLeagueAction(menu, "player", "Player", ShellMenuGroups.PlayerOrder, null),
                    "Player action was not registered.");
                Require(ShellMenuGroups.AddLeagueAction(menu, "live", "Live", ShellMenuGroups.LiveOrder, null),
                    "Live action was not registered.");
                Require(ShellMenuGroups.AddLeagueAction(menu, "advisor", "Advisor", ShellMenuGroups.AdvisorOrder, null),
                    "Advisor action was not registered.");
                Require(ShellMenuGroups.AddLeagueAction(menu, "apply", "Apply", ShellMenuGroups.ApplyOrder, null),
                    "Apply action was not registered.");
                Require(ShellMenuGroups.AddLeagueAction(menu, "itemset", "ItemSet", ShellMenuGroups.ItemSetOrder, null),
                    "Future ItemSet action was not accepted by the fixed League group.");
                Require(ShellMenuGroups.AddLeagueAction(menu, ShellMenuGroups.MayhemActionName, "Mayhem", ShellMenuGroups.MayhemOrder, null),
                    "Mayhem action was not registered.");

                Require(!ShellMenuGroups.AddLeagueAction(menu, "player", "Duplicate Player", ShellMenuGroups.PlayerOrder, null),
                    "Duplicate League actions must not grow the menu.");
                ShellMenuGroups.ValidateRootContract(menu);
                Require(ShellMenuGroups.ActionableRootCount(menu) == 5,
                    "Registering business modules must never grow the tray root.");

                foreach (var actionName in new[] { "dashboard", "player", "live", "advisor", "apply", "itemset", ShellMenuGroups.MayhemActionName })
                    Require(!ShellMenuGroups.RootContainsAction(menu, actionName), "Business action leaked into the tray root: " + actionName);

                var league = ShellMenuGroups.FindGroup(menu, ShellMenuGroups.LeagueGroupName);
                Require(league != null, "League group is missing.");
                var actualOrder = league.DropDownItems.Cast<ToolStripItem>().Select(item => item.Name).ToArray();
                var expectedOrder = new[] { "dashboard", "player", "live", "advisor", "apply", "itemset", ShellMenuGroups.MayhemActionName };
                Require(actualOrder.SequenceEqual(expectedOrder), "League submenu ordering drifted.");
                Require(league.DropDownItems.Cast<ToolStripItem>().All(item => !(item is ToolStripMenuItem) || ((ToolStripMenuItem)item).DropDownItems.Count == 0),
                    "League Shell must stay at two levels; third-level menus are not allowed.");

                Require(ShellMenuGroups.AddMoreAction(menu, "theme", "Theme", 10, null), "More/theme registration failed.");
                Require(ShellMenuGroups.AddMoreAction(menu, "update", "Update", 20, null), "More/update registration failed.");
                Require(ShellMenuGroups.ActionableRootCount(menu) == 5, "More actions grew the tray root.");
            }
        }

        private static ContextMenuStrip BuildRoot()
        {
            var menu = new ContextMenuStrip { ShowImageMargin = false };
            menu.Items.Add(new ToolStripMenuItem("Open") { Name = ShellMenuGroups.OpenRootName });
            menu.Items.Add(new ToolStripMenuItem("Cleanup") { Name = ShellMenuGroups.CleanupRootName });
            menu.Items.Add(ShellMenuGroups.CreateRootGroup(ShellMenuGroups.LeagueGroupName, "League"));
            menu.Items.Add(ShellMenuGroups.CreateRootGroup(ShellMenuGroups.MoreGroupName, "More"));
            menu.Items.Add(new ToolStripMenuItem("Exit") { Name = ShellMenuGroups.ExitRootName });
            return menu;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
