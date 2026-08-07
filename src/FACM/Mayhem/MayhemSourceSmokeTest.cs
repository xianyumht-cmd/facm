using System;
using System.Threading;

namespace FACM.Mayhem
{
    internal static class MayhemSourceSmokeTest
    {
        public static int Run()
        {
            try
            {
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    var result = OpggMayhemService.QueryAsync("yasuo", cancellation.Token).GetAwaiter().GetResult();
                    if (result == null) throw new InvalidOperationException("Mayhem query returned null.");
                    if (!string.IsNullOrWhiteSpace(result.ErrorMessage)) throw new InvalidOperationException(result.ErrorMessage);
                    if (string.IsNullOrWhiteSpace(result.ChampionName)) throw new InvalidOperationException("Champion name is missing.");
                    if (string.IsNullOrWhiteSpace(result.Patch)) throw new InvalidOperationException("OP.GG patch is missing.");
                    if (string.IsNullOrWhiteSpace(result.Tier)) throw new InvalidOperationException("Tier is missing.");
                    if (!result.WinRate.HasValue) throw new InvalidOperationException("Win rate is missing.");
                    if (!result.Rank.HasValue) throw new InvalidOperationException("Rank is missing.");
                    if (string.IsNullOrWhiteSpace(result.SkillOrder) || result.SkillOrder.IndexOf("Q", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException("Skill order is missing.");
                    if (result.CoreItems == null || result.CoreItems.Count < 3)
                        throw new InvalidOperationException("Core items are incomplete.");
                    if (result.Augments == null || result.Augments.Count < 3)
                        throw new InvalidOperationException("Augments are incomplete.");
                    if (result.TopTen == null || result.TopTen.Count < 10)
                        throw new InvalidOperationException("Top-ten ranking is incomplete.");
                }
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 5;
            }
        }
    }
}
