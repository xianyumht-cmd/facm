using System;

namespace FACM
{
    internal static class ShellUxSmokeTest
    {
        internal static void Validate()
        {
            // PerformanceContractSmokeTest runs before Application.EnableVisualStyles/Application.Run.
            // Keep this contract smoke pure: runtime menu objects are validated when actions register,
            // while CI validates the stable five-root definition and deterministic League ordering.
            ShellMenuGroups.ValidateDefinitionForSmokeTest();

            Require(ShellMenuGroups.DashboardOrder < ShellMenuGroups.PlayerOrder, "Dashboard must stay before Player.");
            Require(ShellMenuGroups.PlayerOrder < ShellMenuGroups.LiveOrder, "Player must stay before Live.");
            Require(ShellMenuGroups.LiveOrder < ShellMenuGroups.AdvisorOrder, "Live must stay before OP.GG Advisor.");
            Require(ShellMenuGroups.AdvisorOrder < ShellMenuGroups.ApplyOrder, "Advisor must stay before Apply.");
            Require(ShellMenuGroups.ApplyOrder < ShellMenuGroups.ItemSetOrder, "Apply must stay before ItemSet.");
            Require(ShellMenuGroups.ItemSetOrder < ShellMenuGroups.MayhemOrder, "ItemSet must stay before Mayhem.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
