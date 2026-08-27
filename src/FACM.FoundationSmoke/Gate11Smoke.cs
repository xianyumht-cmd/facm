using FACM.Core.Cleanup;
using FACM.Core.League;
using FACM.Core.Observability;
using FACM.Core.Online;
using FACM.Core.Recovery;
using FACM.Core.Runtime;
using FACM.Core.Settings;
using FACM.Infrastructure.Recovery;
using FACM.Infrastructure.Settings;

internal static class Gate11Smoke
{
    public static async Task RunAsync()
    {
        await FeaturePolicyIsMonotonicAndFailClosedAsync();
        RecoveryStateMachineIsDeterministic();
        await RecoveryStateStoreIsBoundedAndAtomicAsync();
        await SettingsRecoveryUsesValidatedLastKnownGoodAsync();
        UpdateRecoveryKeepsOldVersionOnFailure();
        RecoveryPathsStayUnderDistributionRuntime();
    }

    private static async Task FeaturePolicyIsMonotonicAndFailClosedAsync()
    {
        var baseline = FeaturePolicyEvaluator.Evaluate(
            FeatureBaseline.GetApprovedCapabilities(),
            FeatureKillSwitch.None);
        var reduced = FeaturePolicyEvaluator.Evaluate(
            FeatureBaseline.GetApprovedCapabilities(),
            new FeatureKillSwitch([
                FacmFeatureCapability.DiagnosticsExport,
                FacmFeatureCapability.UpdateCheck,
                FacmFeatureCapability.LeagueApplyMySelection,
                FacmFeatureCapability.CleanupExecute,
                FacmFeatureCapability.UpdateInstall
            ]));

        True(FeaturePolicyEvaluator.IsNoMorePermissive(reduced, baseline), "kill switch must only reduce baseline");
        True(!reduced.IsEnabled(FacmFeatureCapability.DiagnosticsExport), "diagnostics disabled");
        True(!reduced.IsEnabled(FacmFeatureCapability.LeagueApplyMySelection), "League selection writer disabled");
        True(reduced.IsEnabled(FacmFeatureCapability.LeagueCreatePerkPage), "unrelated approved capability remains enabled");

        var validDocument = FeatureKillSwitchFileSource.Parse(
            "{\"schemaVersion\":1,\"disabled\":[\"DiagnosticsExport\"]}");
        Equal(FeatureKillSwitchLoadOrigin.Loaded, validDocument.Origin, "valid kill-switch origin");
        True(validDocument.KillSwitch.Disables(FacmFeatureCapability.DiagnosticsExport), "parsed disabled feature");

        var unknown = FeatureKillSwitchFileSource.Parse(
            "{\"schemaVersion\":1,\"disabled\":[\"FutureMagicWriter\"]}");
        Equal(FeatureKillSwitchLoadOrigin.FailClosed, unknown.Origin, "unknown feature must fail closed");
        var failClosed = FeaturePolicyEvaluator.Evaluate(FeatureBaseline.GetApprovedCapabilities(), unknown.KillSwitch);
        Equal(0, failClosed.EnabledCapabilities.Count, "fail-closed policy must disable every approved capability");

        var fakeLeague = new FakeLeagueWriteGateway();
        var gatedLeague = new FeatureGatedLeagueWriteGateway(fakeLeague, reduced);
        await ThrowsAsync<FeatureDisabledException>(
            () => gatedLeague.ExecuteAsync(
                new LeagueWriteCommand(LeagueWriteCapability.ApplyMySelection, null, "{}"),
                CancellationToken.None),
            "disabled League writer");
        Equal(0, fakeLeague.Calls, "disabled League writer must not reach transport");

        var fakeUpdateSource = new FakeUpdateManifestSource();
        var gatedUpdateSource = new FeatureGatedUpdateManifestSource(fakeUpdateSource, reduced);
        var manifest = await gatedUpdateSource.GetAsync(CancellationToken.None);
        True(manifest is null, "disabled update check returns no manifest");
        Equal(0, fakeUpdateSource.Calls, "disabled update check must not reach network source");

        var fakeCleanup = new FakeCleanupExecutor();
        var gatedCleanup = new FeatureGatedCleanupExecutor(fakeCleanup, reduced);
        await ThrowsAsync<FeatureDisabledException>(
            () => gatedCleanup.ExecuteAsync(
                new CleanupPlan("C:\\FACM", Array.Empty<CleanupTarget>()),
                null,
                CancellationToken.None),
            "disabled cleanup execute");
        Equal(0, fakeCleanup.Calls, "disabled cleanup must not reach executor");

        var fakeInstaller = new FakeUpdateInstaller();
        var gatedInstaller = new FeatureGatedUpdateInstaller(fakeInstaller, reduced);
        await ThrowsAsync<FeatureDisabledException>(
            () => gatedInstaller.InstallAsync(CreateManifest(), CancellationToken.None),
            "disabled update install");
        Equal(0, fakeInstaller.Calls, "disabled installer must not be invoked");

        var fakeExporter = new FakeDiagnosticsExporter();
        var gatedExporter = new FeatureGatedDiagnosticsBundleExporter(fakeExporter, reduced);
        await ThrowsAsync<FeatureDisabledException>(
            () => gatedExporter.ExportAsync(null!, CancellationToken.None),
            "disabled diagnostics export");
        Equal(0, fakeExporter.Calls, "disabled diagnostics exporter must not be invoked");
    }

    private static void RecoveryStateMachineIsDeterministic()
    {
        var t0 = new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);
        var initial = RecoveryStateSnapshot.CreateInitial(t0);
        var starting = RecoveryStateMachine.BeginStart(initial, "4.0.0", t0.AddSeconds(1));
        Equal(RecoveryPhase.Starting, starting.Phase, "first start phase");
        Equal(0, starting.ConsecutiveFailures, "first start failure count");

        var interruptedRestart = RecoveryStateMachine.BeginStart(starting, "4.0.0", t0.AddSeconds(2));
        Equal(1, interruptedRestart.ConsecutiveFailures, "incomplete previous start increments failure count");
        Equal("previous-start-incomplete", interruptedRestart.Reason, "interrupted start reason");

        var failed = RecoveryStateMachine.MarkFailed(interruptedRestart, "startup-component-failed", t0.AddSeconds(3));
        Equal(RecoveryPhase.Failed, failed.Phase, "failed phase");
        Equal(2, failed.ConsecutiveFailures, "explicit failure increments count");

        var recovering = RecoveryStateMachine.BeginRecovery(failed, "settings-lkg", t0.AddSeconds(4));
        Equal(RecoveryPhase.Recovering, recovering.Phase, "recovering phase");
        var running = RecoveryStateMachine.MarkRunning(recovering, t0.AddSeconds(5));
        Equal(RecoveryPhase.Running, running.Phase, "running after recovery");
        Equal("4.0.0", running.LastKnownGoodAppVersion, "last-known-good app version");
        Equal(0, running.ConsecutiveFailures, "successful run resets failure count");
    }

    private static async Task RecoveryStateStoreIsBoundedAndAtomicAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-gate11-recovery", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "state.json");
        Directory.CreateDirectory(root);
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero));
        var store = new JsonRecoveryStateStore(path, clock);
        try
        {
            await File.WriteAllTextAsync(path, "{not-json");
            var malformed = await store.LoadAsync();
            Equal(RecoveryLoadOrigin.Malformed, malformed.Origin, "malformed recovery metadata");
            Equal(RecoveryPhase.Clean, malformed.State.Phase, "malformed metadata safe phase");

            var state = RecoveryStateMachine.BeginStart(malformed.State, "4.0.0", clock.UtcNow.AddSeconds(1));
            await store.SaveAsync(state);
            var loaded = await store.LoadAsync();
            Equal(RecoveryLoadOrigin.Existing, loaded.Origin, "recovery metadata round-trip");
            Equal(RecoveryPhase.Starting, loaded.State.Phase, "recovery state round-trip phase");
            Equal(0, Directory.GetFiles(root, "*.tmp", SearchOption.TopDirectoryOnly).Length, "recovery temp cleanup");

            await File.WriteAllTextAsync(path, new string('x', 70 * 1024));
            var oversized = await store.LoadAsync();
            Equal(RecoveryLoadOrigin.Malformed, oversized.Origin, "oversized recovery metadata must fail closed");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SettingsRecoveryUsesValidatedLastKnownGoodAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-gate11-settings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var primaryPath = Path.Combine(root, "settings.v2.json");
            var legacyPath = Path.Combine(root, "settings.ini");
            var lkgPath = Path.Combine(root, "runtime", "recovery", "settings.v2.lkg.json");
            var strict = new Settings2Repository(primaryPath, legacyPath);
            var valid = Settings2Document.CreateDefault();
            valid.Appearance.ThemeId = "obsidian-gold";
            valid.Online.AutoUpdateEnabled = false;
            await strict.SaveAsync(valid);

            var recovering = new RecoveringSettings2Repository(strict, new JsonSettings2RecoveryStore(lkgPath));
            var first = await recovering.LoadAsync();
            Equal(SettingsLoadOrigin.ExistingV2, first.Origin, "valid primary origin");
            True(File.Exists(lkgPath), "valid primary should seed LKG");

            const string corrupt = "{ definitely-not-json";
            await File.WriteAllTextAsync(primaryPath, corrupt);
            var recovered = await recovering.LoadAsync();
            Equal(SettingsLoadOrigin.RecoveredLastKnownGood, recovered.Origin, "corrupt primary uses LKG");
            Equal("obsidian-gold", recovered.Settings.Appearance.ThemeId, "LKG value");
            Equal(corrupt, await File.ReadAllTextAsync(primaryPath), "corrupt primary must remain untouched for diagnosis");

            var root2 = Path.Combine(root, "no-lkg");
            Directory.CreateDirectory(root2);
            var corruptPath = Path.Combine(root2, "settings.v2.json");
            const string corruptNoLkg = "{ broken-again";
            await File.WriteAllTextAsync(corruptPath, corruptNoLkg);
            var noLkg = new RecoveringSettings2Repository(
                new Settings2Repository(corruptPath, Path.Combine(root2, "settings.ini")),
                new JsonSettings2RecoveryStore(Path.Combine(root2, "runtime", "recovery", "settings.v2.lkg.json")));
            var defaults = await noLkg.LoadAsync();
            Equal(SettingsLoadOrigin.RecoveryDefaults, defaults.Origin, "missing LKG uses recovery defaults");
            True(!defaults.Settings.Online.AutoUpdateEnabled, "recovery defaults disable auto-update");
            Equal(corruptNoLkg, await File.ReadAllTextAsync(corruptPath), "recovery defaults must not overwrite corrupt primary");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void UpdateRecoveryKeepsOldVersionOnFailure()
    {
        var unvalidated = UpdateRecoveryPolicy.Evaluate(new UpdateRecoveryEvidence(
            "3.5.15", "4.0.0", false, true, true,
            UpdateReplacementOutcome.ValidatedReady, "candidate"));
        True(!unvalidated.PermitReplacement, "unvalidated receipt must block replacement");
        True(unvalidated.KeepCurrentVersion, "unvalidated update keeps current version");

        var ready = UpdateRecoveryPolicy.Evaluate(new UpdateRecoveryEvidence(
            "3.5.15", "4.0.0", true, true, true,
            UpdateReplacementOutcome.ValidatedReady, "candidate"));
        True(ready.PermitReplacement, "validated candidate may enter replacement");
        True(ready.KeepCurrentVersion, "old version stays until replacement commits");

        var failed = UpdateRecoveryPolicy.Evaluate(new UpdateRecoveryEvidence(
            "3.5.15", "4.0.0", true, true, true,
            UpdateReplacementOutcome.ReplacementFailed, "planned-failure"));
        True(!failed.PermitReplacement, "failed replacement cannot continue");
        True(failed.KeepCurrentVersion, "failed replacement keeps old version");
        True(failed.RequireRollback, "failed replacement with rollback evidence requires rollback path");
    }

    private static void RecoveryPathsStayUnderDistributionRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-gate11-layout");
        var executable = Path.Combine(root, "FACM.App.exe");
        var layout = RuntimePathLayout.From(new FakeExecutablePaths(executable, Path.Combine(Path.GetTempPath(), ".net", "facm")));
        Equal(Path.Combine(root, "runtime", "recovery"), layout.RecoveryDirectory, "recovery directory");
        True(layout.RecoveryStatePath.StartsWith(layout.RuntimeDirectory, StringComparison.OrdinalIgnoreCase), "recovery state under runtime");
        True(layout.Settings2LastKnownGoodPath.StartsWith(layout.RecoveryDirectory, StringComparison.OrdinalIgnoreCase), "settings LKG under recovery");
        True(layout.FeatureKillSwitchPath.StartsWith(layout.RecoveryDirectory, StringComparison.OrdinalIgnoreCase), "kill switch under recovery");
    }

    private static UpdateManifestSnapshot CreateManifest() => new(
        true,
        "4.0.0",
        "3.0.0",
        false,
        "https://example.invalid/facm.exe",
        "ABC",
        "test",
        "2026-08-27");

    private static async Task ThrowsAsync<TException>(Func<Task> action, string name) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(name + ": expected " + typeof(TException).Name);
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

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed record FakeExecutablePaths(string ExecutablePath, string BaseDirectory) : IExecutablePathProvider;

    private sealed class FakeLeagueWriteGateway : ILeagueWriteGateway
    {
        public int Calls { get; private set; }
        public Task<LeagueWriteResult?> ExecuteAsync(LeagueWriteCommand command, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<LeagueWriteResult?>(new LeagueWriteResult(200, Array.Empty<byte>()));
        }
    }

    private sealed class FakeUpdateManifestSource : IUpdateManifestSource
    {
        public int Calls { get; private set; }
        public Task<UpdateManifestSnapshot?> GetAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<UpdateManifestSnapshot?>(CreateManifest());
        }
    }

    private sealed class FakeCleanupExecutor : ICleanupExecutor
    {
        public int Calls { get; private set; }
        public Task<CleanupResult> ExecuteAsync(CleanupPlan plan, IProgress<CleanupProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new CleanupResult(0, 0, Array.Empty<string>()));
        }
    }

    private sealed class FakeUpdateInstaller : IUpdateInstaller
    {
        public int Calls { get; private set; }
        public Task InstallAsync(UpdateManifestSnapshot manifest, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDiagnosticsExporter : IDiagnosticsBundleExporter
    {
        public int Calls { get; private set; }
        public Task<DiagnosticsExportReceipt> ExportAsync(DiagnosticsSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Exporter should not be reached when feature is disabled.");
        }
    }
}
