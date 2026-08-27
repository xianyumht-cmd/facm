using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FACM.Core.Observability;
using FACM.Core.State;
using FACM.Infrastructure.Observability;

internal static class Gate9Smoke
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static async Task RunAsync()
    {
        TestExportSanitizerAndSummary();
        await TestBoundedReaderAndBundleAsync();
        await TestTotalInputBoundAsync();
    }

    private static void TestExportSanitizerAndSummary()
    {
        var store = CreateStateStore();
        var diagnostic = new DiagnosticEvent(
            DateTimeOffset.Parse("2026-08-27T10:30:00Z"),
            "diag.test",
            "Gate9Smoke",
            12,
            DiagnosticResult.Failure,
            "authorization=Bearer raw-assignment; Basic QWxhZGRpbjpvcGVuLXNlc2FtZQ== at C:\\Users\\Alice\\FACM\\logs\\a.log",
            LeagueProductState.ClientError,
            "4.0.0",
            new Dictionary<string, string>
            {
                ["authToken"] = "never-export-token",
                ["note"] = "Bearer bearer-secret from \\server\\share\\private\\file.txt",
                ["plain"] = "safe"
            });
        var snapshot = new DiagnosticsSnapshot(
            DateTimeOffset.Parse("2026-08-27T10:31:00Z"),
            "4.0.0",
            store.Current,
            new Dictionary<string, string>
            {
                ["framework"] = ".NET 10",
                ["install"] = "C:\\Users\\Alice\\FACM"
            },
            new[] { diagnostic },
            0,
            0,
            false);

        var safe = DiagnosticsExportSanitizer.ScrubSnapshot(snapshot);
        Equal("[path]", safe.ProductState.Environment.DistributionDirectory, "Product State distribution path redaction");
        Equal("[path]", safe.RuntimeFacts["install"], "runtime fact path redaction");
        Equal("[redacted]", safe.Events[0].Data["authToken"], "sensitive data key redaction");

        var summaryA = DiagnosticsSummaryFormatter.Format(snapshot);
        var summaryB = DiagnosticsSummaryFormatter.Format(snapshot);
        Equal(summaryA, summaryB, "summary determinism");
        AssertNoSecrets(summaryA, "summary");
        True(summaryA.Contains("[redacted]", StringComparison.Ordinal), "summary redaction marker");
        True(summaryA.Contains("[path]", StringComparison.Ordinal), "summary path marker");
    }

    private static async Task TestBoundedReaderAndBundleAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-gate9-" + Guid.NewGuid().ToString("N"));
        var logs = Path.Combine(root, "logs");
        var current = Path.Combine(logs, "facm4-events.jsonl");
        var output = Path.Combine(root, "runtime", "diagnostics");
        Directory.CreateDirectory(logs);

        var policy = new DiagnosticsExportPolicy(
            MaxEvents: 2,
            MaxInputFileBytes: 4096,
            MaxTotalInputBytes: 8192,
            MaxZipEntries: 3,
            MaxEntryBytes: 4096,
            MaxBundleBytes: 8192,
            MaxSummaryChars: 2048).Validate();

        try
        {
            await File.WriteAllTextAsync(current + ".1", new string('x', 5000));

            var events = new[]
            {
                UnsafeEvent("one", "Basic QWxhZGRpbjpvcGVuLXNlc2FtZQ== at C:\\Users\\Alice\\one.log", "first-secret"),
                UnsafeEvent("two", "Bearer bearer-secret C:\\Users\\Alice\\two.log", "second-secret"),
                UnsafeEvent("three", "token=third-secret; C:\\Users\\Alice\\three.log", "third-secret")
            };
            var lines = events.Select(item => JsonSerializer.Serialize(item, JsonOptions)).ToList();
            lines.Insert(1, "{ malformed token=malformed-secret Basic QWxhZGRpbjpvcGVuLXNlc2FtZQ== C:\\Users\\Alice\\bad.log");
            await File.WriteAllLinesAsync(current, lines);

            var store = CreateStateStore();
            var source = new FileDiagnosticsSnapshotSource(
                store,
                current,
                "4.0.0",
                policy,
                () => DateTimeOffset.Parse("2026-08-27T10:40:00Z"),
                new Dictionary<string, string>
                {
                    ["framework"] = ".NET 10",
                    ["path"] = "C:\\Users\\Alice\\FACM"
                });
            var snapshot = await source.CaptureAsync();

            Equal(2, snapshot.Events.Count, "bounded recent event count");
            Equal(1, snapshot.MalformedLinesSkipped, "malformed line count");
            Equal(1, snapshot.InputFilesSkipped, "oversized rotation skip count");
            True(snapshot.EventsTruncated, "event count truncation flag");
            Equal("two", snapshot.Events[0].ActionId, "bounded queue retains recent event two");
            Equal("three", snapshot.Events[1].ActionId, "bounded queue retains recent event three");

            var exporter = new DiagnosticsBundleExporter(
                output,
                policy,
                () => DateTimeOffset.Parse("2026-08-27T10:41:00Z"));
            var receipt = await exporter.ExportAsync(snapshot);
            Equal(3, receipt.EntryCount, "ZIP allowlist entry count");
            True(receipt.BundleBytes <= policy.MaxBundleBytes, "ZIP output bound");
            True(File.Exists(receipt.BundlePath), "diagnostics bundle exists");
            True(Path.GetFileName(receipt.BundlePath).StartsWith("facm-diagnostics-", StringComparison.Ordinal), "privacy-safe bundle filename prefix");
            True(!Path.GetFileName(receipt.BundlePath).Contains("Alice", StringComparison.OrdinalIgnoreCase), "bundle filename user privacy");

            using var archive = ZipFile.OpenRead(receipt.BundlePath);
            var names = archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            var expected = new[]
            {
                DiagnosticsBundleExporter.EventsEntryName,
                DiagnosticsBundleExporter.ManifestEntryName,
                DiagnosticsBundleExporter.SummaryEntryName
            }.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Equal(string.Join('|', expected), string.Join('|', names), "ZIP exact entry allowlist");

            var combined = new StringBuilder();
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                combined.Append(await reader.ReadToEndAsync());
            }
            AssertNoSecrets(combined.ToString(), "bundle");
            True(combined.ToString().Contains("[redacted]", StringComparison.Ordinal), "bundle redaction marker");
            True(combined.ToString().Contains("[path]", StringComparison.Ordinal), "bundle path marker");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task TestTotalInputBoundAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-gate9-total-" + Guid.NewGuid().ToString("N"));
        var current = Path.Combine(root, "facm4-events.jsonl");
        Directory.CreateDirectory(root);
        var policy = new DiagnosticsExportPolicy(
            MaxEvents: 10,
            MaxInputFileBytes: 4096,
            MaxTotalInputBytes: 4096,
            MaxZipEntries: 3,
            MaxEntryBytes: 4096,
            MaxBundleBytes: 8192,
            MaxSummaryChars: 2048).Validate();
        try
        {
            await File.WriteAllTextAsync(current + ".1", new string(' ', 3000));
            await File.WriteAllTextAsync(current, new string(' ', 3000));
            var source = new FileDiagnosticsSnapshotSource(
                CreateStateStore(),
                current,
                "4.0.0",
                policy,
                () => DateTimeOffset.Parse("2026-08-27T10:50:00Z"));
            var snapshot = await source.CaptureAsync();
            Equal(1, snapshot.InputFilesSkipped, "total input byte bound skips second file");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ProductStateStore CreateStateStore()
    {
        var store = new ProductStateStore(() => DateTimeOffset.Parse("2026-08-27T10:20:00Z"));
        store.SetEnvironment(
            new ProductEnvironmentState("C:\\Users\\Alice\\FACM", true, false),
            "smoke");
        store.SetApplication(ApplicationProductState.Ready, "ready");
        store.SetLeague(LeagueProductState.Lobby, "lobby");
        return store;
    }

    private static DiagnosticEvent UnsafeEvent(string id, string reason, string secret) => new(
        DateTimeOffset.Parse("2026-08-27T10:30:00Z").AddSeconds(id.Length),
        id,
        "Gate9Smoke",
        1,
        DiagnosticResult.Success,
        reason,
        LeagueProductState.Lobby,
        "4.0.0",
        new Dictionary<string, string>
        {
            ["authorization"] = "Bearer " + secret,
            ["note"] = "C:\\Users\\Alice\\" + id + ".txt"
        });

    private static void AssertNoSecrets(string text, string scope)
    {
        foreach (var value in new[]
        {
            "QWxhZGRpbjpvcGVuLXNlc2FtZQ==",
            "bearer-secret",
            "first-secret",
            "second-secret",
            "third-secret",
            "malformed-secret",
            "never-export-token",
            "Alice",
            "C:\\Users"
        })
        {
            True(!text.Contains(value, StringComparison.OrdinalIgnoreCase), scope + " leaked " + value);
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
