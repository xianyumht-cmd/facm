using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace FACM.Mayhem
{
    internal static class MayhemSourceSmokeTest
    {
        public static int Run()
        {
            try
            {
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    var result = OpggMayhemService.QueryAsync("yasuo", cancellation.Token).GetAwaiter().GetResult();
                    if (result == null) throw new InvalidOperationException("Mayhem query returned null.");
                    if (!string.IsNullOrWhiteSpace(result.ErrorMessage)) throw new InvalidOperationException(result.ErrorMessage);
                    if (string.IsNullOrWhiteSpace(result.ChampionName)) throw new InvalidOperationException("Champion name is missing.");
                    if (string.IsNullOrWhiteSpace(result.Patch)) throw new InvalidOperationException("Patch is missing.");
                    if (string.IsNullOrWhiteSpace(result.Tier)) throw new InvalidOperationException("Tier is missing.");
                    if (!result.WinRate.HasValue) throw new InvalidOperationException("Win rate is missing.");
                    if (!result.Rank.HasValue) throw new InvalidOperationException("Rank is missing.");
                    if (string.IsNullOrWhiteSpace(result.SkillOrder) || result.SkillOrder.IndexOf("Q", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException("Skill order is missing.");
                    if (result.CoreItems == null || result.CoreItems.Count < 3)
                        throw new InvalidOperationException("Core items are incomplete.");
                    if (result.TopTen == null || result.TopTen.Count < 10)
                        throw new InvalidOperationException("Top-ten ranking is incomplete.");

                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        RiotGameDataService.EnrichAsync(result, cancellation.Token).GetAwaiter().GetResult();
                        var skillsReady = result.SkillIconUrls != null &&
                                          result.SkillIconUrls.Count >= 4 &&
                                          new[] { "Q", "W", "E", "R" }.All(key =>
                                              result.SkillIconUrls.ContainsKey(key) &&
                                              !string.IsNullOrWhiteSpace(result.SkillIconUrls[key]));
                        if (skillsReady) break;
                        if (attempt < 2) Thread.Sleep(450);
                    }

                    // Keep the live source gate separate from the Yasuo ranking probe.
                    // Seraphine is intentionally used because her base ARAM page has
                    // long-lived non-neutral modifiers, so disappearance of the balance
                    // section is a meaningful parser/source regression instead of a
                    // harmless champion-with-no-adjustments case.
                    var baseProbe = new MayhemChampionResult
                    {
                        ChampionName = "萨勒芬妮",
                        ChampionSlug = "seraphine",
                        Patch = result.Patch,
                        BalanceSummary = "Mayhem probe"
                    };
                    OpggAramBaseBalanceService.EnrichAsync(baseProbe, cancellation.Token).GetAwaiter().GetResult();
                    if (string.IsNullOrWhiteSpace(baseProbe.BaseBalanceStatus) ||
                        string.Equals(baseProbe.BaseBalanceStatus, "unavailable", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Live Seraphine base ARAM balance source is unavailable or no longer parseable.");
                    if (!string.Equals(baseProbe.BaseBalanceStatus, "syncing", StringComparison.OrdinalIgnoreCase) && !baseProbe.BaseBalanceComplete)
                        throw new InvalidOperationException("Live Seraphine base ARAM balance source returned a non-complete state.");
                    if (string.IsNullOrWhiteSpace(baseProbe.BaseBalanceSummary) ||
                        baseProbe.BalanceSummary.IndexOf("基础 ARAM", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException("Base ARAM balance was not composed into the card summary.");

                    MayhemRankedAugmentService.EnrichAsync(result, cancellation.Token).GetAwaiter().GetResult();

                    if (string.IsNullOrWhiteSpace(result.ChampionIconUrl))
                        throw new InvalidOperationException("Champion image URL is missing.");
                    if (result.SkillIconUrls == null ||
                        result.SkillIconUrls.Count < 4 ||
                        !new[] { "Q", "W", "E", "R" }.All(key =>
                            result.SkillIconUrls.ContainsKey(key) &&
                            !string.IsNullOrWhiteSpace(result.SkillIconUrls[key])))
                        throw new InvalidOperationException("Skill image URLs are incomplete after retries.");
                    if (result.TopTen.Count(item => !string.IsNullOrWhiteSpace(item.IconUrl)) < 8)
                        throw new InvalidOperationException("Top-ten champion image URLs are incomplete.");
                    if (result.Augments == null || result.Augments.Count < 5 || result.Augments.Any(value => !value.StartsWith("#", StringComparison.Ordinal)))
                        throw new InvalidOperationException("Ranked augment queue is incomplete.");
                    if (result.AugmentIconUrls == null || result.AugmentIconUrls.Count < 5 || result.AugmentIconUrls.Any(string.IsNullOrWhiteSpace))
                        throw new InvalidOperationException("Ranked augment image URLs are incomplete.");

                    for (var index = 0; index < 5; index++)
                    {
                        using (var augmentImage = MayhemImageCache.GetAsync(result.AugmentIconUrls[index], cancellation.Token).GetAwaiter().GetResult())
                        {
                            if (augmentImage == null || augmentImage.Width < 24 || augmentImage.Height < 24)
                                throw new InvalidOperationException("Ranked augment image #" + (index + 1) + " could not be decoded: " + result.AugmentIconUrls[index]);
                        }
                    }

                    using (var image = MayhemCardRenderer.RenderForSmokeTest(result))
                    {
                        if (image.Width != MayhemCardRenderer.CardWidth || image.Height != MayhemCardRenderer.CardHeight)
                            throw new InvalidOperationException("Mayhem image card dimensions are invalid.");
                        using (var stream = new MemoryStream())
                        {
                            image.Save(stream, ImageFormat.Png);
                            if (stream.Length < 20000)
                                throw new InvalidOperationException("Mayhem image card PNG is unexpectedly empty.");
                        }
                    }
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