using System;
using System.Linq;

namespace FACM.Mayhem
{
    internal static class OpggAramBaseBalanceSmokeTest
    {
        public static int Run()
        {
            try
            {
                const string seraphine = @"
<html><body>
<div>Patch 16.15</div>
<section>
Balance adjustment
Damage Dealt -
Damage Taken +20%
Attack Speed -
Cooldown Reduction -20
Healing -20%
Tenacity -
Shield Amount -20%
Energy Regen -
</section>
<div>Build</div>
</body></html>";

                var parsed = OpggAramBaseBalanceService.ParseForSmokeTest(seraphine, "26.15");
                if (parsed == null || !parsed.Complete || parsed.Status != "ok")
                    throw new InvalidOperationException("Seraphine complete ARAM balance was not parsed.");
                if (!string.Equals(parsed.DisplayPatch, "26.15", StringComparison.Ordinal))
                    throw new InvalidOperationException("Legacy OP.GG patch label was not mapped to the public patch: " + parsed.DisplayPatch);
                if (!parsed.CurrentPatchVerified)
                    throw new InvalidOperationException("Mapped OP.GG patch did not verify against the current patch.");
                if (parsed.Changes.Count != 4)
                    throw new InvalidOperationException("Seraphine complete balance should contain 4 non-neutral modifiers, actual=" + parsed.Changes.Count);

                var expected = new[] { "damage_taken", "ability_haste", "healing", "shielding" };
                if (expected.Any(key => !parsed.Changes.Any(item => item.Key == key)))
                    throw new InvalidOperationException("Seraphine complete balance is missing an expected modifier.");
                if (parsed.Changes.Any(item => item.Direction != "debuff"))
                    throw new InvalidOperationException("Seraphine expected modifiers must all be debuffs.");
                if (!parsed.Summary.Contains("治疗 -20%") || !parsed.Summary.Contains("护盾 -20%"))
                    throw new InvalidOperationException("Seraphine healing/shielding values were not preserved in the complete summary.");

                const string localized = @"
<html><body>
<div>16.15 版本</div>
<section>
平衡调整
造成伤害 -
承伤 +20%
攻速 -
技能加速 -20
生命恢复 -20%
韧性 -
护盾吸收量 -20%
法力回复 -
</section>
<div>出装</div>
</body></html>";
                var localizedParsed = OpggAramBaseBalanceService.ParseForSmokeTest(localized, "26.15");
                if (localizedParsed == null || !localizedParsed.Complete || localizedParsed.Changes.Count != 4)
                    throw new InvalidOperationException("Current localized OP.GG balance labels are not fully supported.");

                const string yasuoLiveShape = @"
<html><body>
<div>Ver: 16.15</div>
<section>
Balance adjustment
Damage Dealt -
Damage Taken -
Attack Speed + 2.5%
Cooldown Reduction -
Healing -
Tenacity -
Shield Amount -
Energy Regen -
ADVERTISEMENT 300 250
</section>
<div>Summoner spells</div>
</body></html>";
                var yasuo = OpggAramBaseBalanceService.ParseForSmokeTest(yasuoLiveShape, "26.15");
                if (yasuo == null || !yasuo.Complete || yasuo.Status != "ok" || !yasuo.CurrentPatchVerified)
                    throw new InvalidOperationException("Yasuo live OP.GG balance shape was not parsed and patch-verified.");
                if (yasuo.Changes.Count != 1 || yasuo.Changes[0].Key != "attack_speed" ||
                    yasuo.Changes[0].Value != "+2.5%" || yasuo.Changes[0].Direction != "buff")
                    throw new InvalidOperationException("Yasuo spaced +2.5% attack-speed modifier was not normalized correctly.");

                const string corkiVersionShape = @"
<html><body>
<div>Version: 16.15</div>
<section>
Balance adjustment
Damage Dealt -
Damage Taken -10%
Attack Speed -
Cooldown Reduction -20
Healing -
Tenacity -
Shield Amount -
Energy Regen -
</section>
<div>Build</div>
</body></html>";
                var corki = OpggAramBaseBalanceService.ParseForSmokeTest(corkiVersionShape, "26.15");
                if (corki == null || !corki.Complete || corki.Status != "ok" || !corki.CurrentPatchVerified)
                    throw new InvalidOperationException("Corki Version: patch selector shape was not parsed and verified.");
                var corkiTaken = corki.Changes.FirstOrDefault(item => item.Key == "damage_taken");
                var corkiHaste = corki.Changes.FirstOrDefault(item => item.Key == "ability_haste");
                if (corkiTaken == null || corkiTaken.Value != "-10%" || corkiTaken.Direction != "buff")
                    throw new InvalidOperationException("Corki -10% damage-taken buff was not preserved.");
                if (corkiHaste == null || corkiHaste.Value != "-20" || corkiHaste.Direction != "debuff")
                    throw new InvalidOperationException("Corki -20 cooldown modifier was not preserved.");

                const string chineseVersionPrefix = @"
版本号：16.15
平衡调整
造成伤害 - 承受伤害 - 攻击速度 +2.5% 技能急速 - 治疗 - 护盾 - 韧性 - 资源回复 -
召唤师技能";
                var chineseVersion = OpggAramBaseBalanceService.ParseForSmokeTest(chineseVersionPrefix, "26.15");
                if (chineseVersion == null || !chineseVersion.Complete || !chineseVersion.CurrentPatchVerified || chineseVersion.Patch != "16.15")
                    throw new InvalidOperationException("Chinese version-prefix patch label was not extracted.");

                var inverse = OpggAramBaseBalanceService.ParseForSmokeTest(
                    "Balance adjustment Damage Dealt +5% Damage Taken -10% Healing +10% Summoner Spells",
                    null);
                if (inverse.Changes.First(item => item.Key == "damage_taken").Direction != "buff")
                    throw new InvalidOperationException("Negative damage taken must be treated as a buff.");

                var unknown = OpggAramBaseBalanceService.ParseForSmokeTest(
                    "Balance adjustment Damage Dealt - Future Modifier -15% Summoner Spells",
                    null);
                if (unknown.Complete || unknown.Status != "unavailable" || unknown.ErrorClass != "unparsed_balance_values")
                    throw new InvalidOperationException("Unknown signed balance fields must reject a false complete state.");

                var unknownSpaced = OpggAramBaseBalanceService.ParseForSmokeTest(
                    "Balance adjustment Damage Dealt - Future Modifier - 15% Summoner Spells",
                    null);
                if (unknownSpaced.Complete || unknownSpaced.Status != "unavailable" || unknownSpaced.ErrorClass != "unparsed_balance_values")
                    throw new InvalidOperationException("Unknown spaced signed balance fields must also reject a false complete state.");

                var oldPatch = OpggAramBaseBalanceService.ParseForSmokeTest(seraphine.Replace("16.15", "16.14"), "26.15");
                if (oldPatch.Status != "syncing" || oldPatch.Changes.Count != 0)
                    throw new InvalidOperationException("Outdated complete values must be hidden during patch transition.");

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 13;
            }
        }
    }
}