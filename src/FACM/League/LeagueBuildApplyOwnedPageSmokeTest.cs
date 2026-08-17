using System;
using System.Collections.Generic;

namespace FACM.League
{
    /// <summary>
    /// Small deterministic policy fixture for the 2026-08-18 Tencent real-machine regression.
    /// The executable smoke remains in LeagueBuildApplySmokeTest; this helper keeps the ownership
    /// rule explicit for review and future integration without introducing any LCU writes itself.
    /// </summary>
    internal static class LeagueBuildApplyOwnedPageSmokeTest
    {
        internal static void ValidateNames()
        {
            Require(IsOwned("[FACM] 疾风剑豪 - mid"), "FACM exact rune page was not recognized as owned.");
            Require(IsOwned("[facm] old"), "FACM rune-page ownership must be case-insensitive.");
            Require(!IsOwned("我的常用符文"), "User rune page was incorrectly classified as FACM-owned.");
            Require(!IsOwned("FACM test"), "Unprefixed rune page was incorrectly classified as FACM-owned.");
        }

        private static bool IsOwned(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && name.StartsWith("[FACM]", StringComparison.OrdinalIgnoreCase);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
