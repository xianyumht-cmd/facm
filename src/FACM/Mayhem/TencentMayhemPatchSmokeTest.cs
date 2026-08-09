using System;
using System.Linq;

namespace FACM.Mayhem
{
    internal static class TencentMayhemPatchSmokeTest
    {
        public static int Run()
        {
            try
            {
                const string fixture = @"
<html><body>
<p>LOL将在维护后发布26.15版本。</p>
<p>还有海克斯大乱斗和竞技场更新！</p>
<h3>英雄</h3>
<p>普通模式英雄</p><li>错误字段：1 ⇒ 2</li>
<h3>海克斯大乱斗</h3>
<h4>英雄</h4>
<p>阿狸</p>
<ul><li>治疗效果：90% ⇒ 100%</li></ul>
<p>阿克尚</p>
<ul>
<li>造成伤害：105% ⇒ 100%</li>
<li>承受伤害：5% ⇒ 0%</li>
</ul>
<h4>强化符文</h4>
<p>阿狸</p><li>这不是英雄调整：1 ⇒ 2</li>
<h3>斗魂竞技场</h3>
</body></html>";

                var snapshot = TencentMayhemPatchService.ParseArticleForSmokeTest(fixture);
                if (snapshot == null) throw new InvalidOperationException("Tencent Mayhem fixture was not parsed.");
                if (!string.Equals(snapshot.Patch, "26.15", StringComparison.Ordinal))
                    throw new InvalidOperationException("Tencent Mayhem patch was parsed incorrectly: " + snapshot.Patch);
                if (snapshot.ChampionChanges.ContainsKey("普通模式英雄"))
                    throw new InvalidOperationException("Parser started at an early prose mention instead of the Mayhem heading.");

                var ahri = snapshot.FindChampionChanges("阿狸");
                if (ahri.Count != 1 || !ahri[0].Contains("90% → 100%"))
                    throw new InvalidOperationException("Ahri Mayhem change was parsed incorrectly.");

                var akshan = snapshot.FindChampionChanges("阿克尚");
                if (akshan.Count != 2 || !akshan.Any(value => value.Contains("造成伤害")) || !akshan.Any(value => value.Contains("承受伤害")))
                    throw new InvalidOperationException("Multiple Mayhem changes for one champion were not preserved.");

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 12;
            }
        }
    }
}
