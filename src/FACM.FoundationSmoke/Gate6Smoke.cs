using FACM.Core.Text;
using FACM.Infrastructure.Text;

internal static class Gate6Smoke
{
    public static Task RunAsync()
    {
        TestShellTextCoverage();
        TestFileOverrides();
        return Task.CompletedTask;
    }

    private static void TestShellTextCoverage()
    {
        var provider = new DictionaryUiTextProvider();
        var keys = new[]
        {
            UiTextKeys.ShellRepairTools,
            UiTextKeys.ShellLeague,
            UiTextKeys.ShellPersonalization,
            UiTextKeys.ShellMoreSettings,
            UiTextKeys.ShellRepairSubtitle,
            UiTextKeys.ShellLeagueSubtitle,
            UiTextKeys.ShellPersonalizationSubtitle,
            UiTextKeys.ShellMoreSettingsSubtitle,
            UiTextKeys.ShellStatusLabel,
            UiTextKeys.ShellStatusReady,
            UiTextKeys.ShellStatusUpdateAvailable,
            UiTextKeys.ShellStatusUnavailable,
            UiTextKeys.ShellOverviewTitle,
            UiTextKeys.ShellOverviewBody,
            UiTextKeys.ShellStateTitle,
            UiTextKeys.ShellStateBody
        };

        foreach (var key in keys)
        {
            var value = provider.Get(key);
            True(!string.IsNullOrWhiteSpace(value), "shell UI text must not be blank: " + key);
            True(!string.Equals(value, key, StringComparison.Ordinal), "shell UI text default missing: " + key);
        }

        Equal("清理与修复", provider.Get(UiTextKeys.ShellRepairTools), "repair entry copy");
        Equal("LOL 工作台", provider.Get(UiTextKeys.ShellLeague), "league entry copy");
        Equal("个性化", provider.Get(UiTextKeys.ShellPersonalization), "personalization entry copy");
        Equal("更多设置", provider.Get(UiTextKeys.ShellMoreSettings), "settings entry copy");
    }

    private static void TestFileOverrides()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-ui-text-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "ui-text.ini");
            File.WriteAllLines(path,
            [
                "[Text]",
                "ShellLeague=自定义工作台",
                "ShellRepairTools=自定义修复",
                "[Replace]",
                "ShellLeague=ignored"
            ]);

            var provider = new FileUiTextProvider(path);
            Equal("自定义工作台", provider.Get(UiTextKeys.ShellLeague), "file league override");
            Equal("自定义修复", provider.Get(UiTextKeys.ShellRepairTools), "file repair override");
            Equal("个性化", provider.Get(UiTextKeys.ShellPersonalization), "file fallback");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }
}
