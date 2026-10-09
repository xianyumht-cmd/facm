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
            UpdateInstaller.ValidateEmbeddedUpdaterForSmokeTest();

            var notes = "版本更新说明第一行\\n第二行：更新稳定性与界面提示";
            var checkedUpdate = new OnlineSnapshot
            {
                CurrentVersion = new Version(3, 5, 57),
                LatestVersion = new Version(3, 5, 58),
                UpdateAvailable = true,
                Update = new UpdateManifest { Version = "3.5.58", ReleaseNotes = notes }
            };
            Require(OnlineCenterForm.CanInstallUpdateForSmokeTest(checkedUpdate),
                "Verified available update must keep the install action accessible.");
            Require(OnlineCenterForm.ResolveReleaseNotesForSmokeTest(checkedUpdate) == notes,
                "Update Center must preserve complete multiline release notes.");
            checkedUpdate.Update.ReleaseNotes = string.Empty;
            Require(OnlineCenterForm.ResolveReleaseNotesForSmokeTest(checkedUpdate) ==
                    OnlineCenterUiText.ReleaseNotesMissing,
                "Update Center must clearly handle missing release notes.");
            checkedUpdate.Update.ReleaseNotes = notes;

            var failedRefresh = OnlineCenterForm.CreateFetchErrorSnapshotForSmokeTest(checkedUpdate);
            Require(failedRefresh.CurrentVersion == checkedUpdate.CurrentVersion &&
                    !failedRefresh.UpdateAvailable && failedRefresh.Update == null &&
                    !OnlineCenterForm.CanInstallUpdateForSmokeTest(failedRefresh) &&
                    !string.IsNullOrWhiteSpace(failedRefresh.ErrorMessage),
                "Failed metadata refresh must discard stale actionable update state.");
            Require(OnlineCenterForm.ResolveReleaseNotesForSmokeTest(failedRefresh) ==
                    OnlineCenterUiText.ReleaseNotesUnavailable,
                "Unavailable metadata must not display stale release notes.");
            checkedUpdate.ErrorMessage = "network unavailable";
            Require(!OnlineCenterForm.CanInstallUpdateForSmokeTest(checkedUpdate),
                "Update action must remain disabled whenever metadata verification has failed.");

            var builtIns = UpdateMirrorRouter.GetBuiltInSources();
            var mirrorCount = builtIns.Count(item =>
                !string.Equals(item.Name, "github", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.Prefix));
            Require(mirrorCount >= 10, "Expected at least ten built-in mirrors plus direct GitHub.");
            Require(builtIns.Where(item => !string.IsNullOrWhiteSpace(item.Prefix))
                    .Select(item => item.Prefix.TrimEnd('/'))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 10,
                "Built-in mirror prefixes must contain at least ten unique HTTPS routes.");
            Require(builtIns.Any(item => item.Name == "github" && string.IsNullOrEmpty(item.Prefix)),
                "Direct GitHub fallback is missing.");
            Require(builtIns.Any(item => item.Name == "gh-dpik" && item.Prefix == "https://gh.dpik.top/"),
                "Primary health-checked mirror is missing.");

            var rawOrigin = "https://raw.githubusercontent.com/xianyumht-cmd/facm/main/online/version.json";
            var releaseOrigin = "https://github.com/xianyumht-cmd/facm/releases/download/v3.4.6/FACM.exe";
            var giteeReleaseOrigin = "https://gitee.com/xymhtcmd/facm/releases/download/v4.0.3/FACM.exe";

            var ghfast = new UpdateMirrorSource
            {
                Name = "ghfast",
                Prefix = "https://ghfast.top/",
                Enabled = true,
                Priority = 15
            };
            Require(
                UpdateMirrorRouter.BuildUrl(ghfast, rawOrigin) ==
                "https://ghfast.top/https://raw.githubusercontent.com/xianyumht-cmd/facm/main/online/version.json",
                "Raw GitHub mirror URL was not composed correctly.");
            Require(
                UpdateMirrorRouter.BuildUrl(ghfast, releaseOrigin) ==
                "https://ghfast.top/https://github.com/xianyumht-cmd/facm/releases/download/v3.4.6/FACM.exe",
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

            var giteeCandidates = UpdateMirrorRouter.BuildCandidates(giteeReleaseOrigin, merged);
            Require(giteeCandidates.Any(item => item.Url == giteeReleaseOrigin),
                "Direct Gitee release candidate is missing.");
            Require(!giteeCandidates.Any(item => item.Url.IndexOf("https://gitee.com/https://", StringComparison.OrdinalIgnoreCase) >= 0),
                "Gitee release candidates must not be wrapped in GitHub proxy prefixes.");

            var stale = CreateManifest("3.5.18", true);
            var freshDisabled = CreateManifest("3.5.20", false);
            var fresh = CreateManifest("3.5.20", true);
            var selected = OnlineService.SelectNewestManifest(new[] { stale, freshDisabled, fresh });
            Require(ReferenceEquals(selected, fresh),
                "Update manifest selection must choose 3.5.20 over a faster stale 3.5.18 response and prefer enabled metadata on a tie.");

            Require(
                OnlineService.CompareProductVersions(new Version(3, 5, 20), new Version(3, 5, 20, 0)) == 0,
                "Three-part release versions and four-part assembly versions must compare as the same product version.");

            var protectedLatest = OnlineService.PreventLatestVersionRegression(
                new Version(3, 5, 20, 0),
                new Version(3, 5, 18));
            Require(protectedLatest != null && protectedLatest.Equals(new Version(3, 5, 20, 0)),
                "A stale manifest must never make the UI report a latest version older than the running client.");
        }

        private static UpdateManifest CreateManifest(string version, bool enabled)
        {
            return new UpdateManifest
            {
                Enabled = enabled,
                Version = version,
                MinimumVersion = "3.0.0",
                DownloadUrl = "https://github.com/xianyumht-cmd/facm/releases/download/v" + version + "/FACM.exe",
                Sha256 = new string('A', 64),
                ReleaseNotes = "smoke"
            };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
