using System;
using System.Linq;

namespace FACM.Online
{
    internal static class UpdateMirrorSmokeTest
    {
        public static int Run()
        {
            try
            {
                Validate();
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 4;
            }
        }

        private static void Validate()
        {
            var builtIns = UpdateMirrorRouter.GetBuiltInSources();
            Require(builtIns.Length >= 4, "Expected at least three mirrors plus direct GitHub.");
            Require(builtIns.Any(item => item.Name == "github" && string.IsNullOrEmpty(item.Prefix)),
                "Direct GitHub fallback is missing.");

            var rawOrigin = "https://raw.githubusercontent.com/xianyumht-cmd/facm/main/online/version.json";
            var releaseOrigin = "https://github.com/xianyumht-cmd/facm/releases/download/v3.4.5/FACM.exe";

            var ghfast = new UpdateMirrorSource
            {
                Name = "ghfast",
                Prefix = "https://ghfast.top/",
                Enabled = true,
                Priority = 10
            };
            Require(
                UpdateMirrorRouter.BuildUrl(ghfast, rawOrigin) ==
                "https://ghfast.top/https://raw.githubusercontent.com/xianyumht-cmd/facm/main/online/version.json",
                "Raw GitHub mirror URL was not composed correctly.");
            Require(
                UpdateMirrorRouter.BuildUrl(ghfast, releaseOrigin) ==
                "https://ghfast.top/https://github.com/xianyumht-cmd/facm/releases/download/v3.4.5/FACM.exe",
                "Release mirror URL was not composed correctly.");

            Require(!UpdateMirrorRouter.IsSafeMirrorPrefix("http://mirror.example/"),
                "HTTP mirror prefix must be rejected.");
            Require(!UpdateMirrorRouter.IsSafeMirrorPrefix("https://localhost/"),
                "localhost mirror prefix must be rejected.");
            Require(!UpdateMirrorRouter.IsSafeMirrorPrefix("https://127.0.0.1/"),
                "IP mirror prefix must be rejected.");
            Require(UpdateMirrorRouter.IsSafeMirrorPrefix("https://mirror.example/"),
                "Public HTTPS mirror prefix should be accepted.");

            var catalog = new UpdateMirrorCatalog
            {
                Schema = UpdateMirrorRouter.CatalogSchema,
                Sources = new[] { ghfast }
            };
            Require(UpdateMirrorRouter.IsValidCatalog(catalog), "Valid mirror catalog was rejected.");
            catalog.Schema = "wrong-schema";
            Require(!UpdateMirrorRouter.IsValidCatalog(catalog), "Wrong mirror catalog schema was accepted.");

            var merged = UpdateMirrorRouter.MergeWithBuiltIns(new[]
            {
                new UpdateMirrorSource
                {
                    Name = "custom",
                    Prefix = "https://mirror.example/",
                    Enabled = true,
                    Priority = 1
                },
                new UpdateMirrorSource
                {
                    Name = "custom-duplicate",
                    Prefix = "https://mirror.example/",
                    Enabled = true,
                    Priority = 2
                }
            });
            Require(merged.Count(item => item.Prefix == "https://mirror.example/") == 1,
                "Duplicate mirror prefixes were not removed.");
            Require(merged.Any(item => item.Name == "github" && string.IsNullOrEmpty(item.Prefix)),
                "Remote catalog removed direct GitHub fallback.");

            var candidates = UpdateMirrorRouter.BuildCandidates(releaseOrigin, merged);
            Require(candidates.Any(item => item.Url == releaseOrigin),
                "Direct GitHub release candidate is missing.");
            Require(candidates.Any(item => item.Url.StartsWith("https://ghfast.top/https://github.com/", StringComparison.Ordinal)),
                "ghfast release candidate is missing.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
