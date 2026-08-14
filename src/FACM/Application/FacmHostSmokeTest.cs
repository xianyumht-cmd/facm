using System;
using System.Collections.Generic;
using System.Linq;
using FACM.AppHost.Modules;
using FACM.League;

namespace FACM.AppHost
{
    internal static class FacmHostSmokeTest
    {
        public static int Run()
        {
            try
            {
                ValidateInitializationAndReverseDisposeOrder();
                ValidateMissingDependency();
                ValidateDuplicateModuleId();
                ValidateCircularDependency();
                ValidateInitializationFailureRollback();
                ValidateFirstModuleFailureReport();
                ValidateShellFeatureDependencyContract();
                LeagueClientSmokeTest.Validate();
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 4;
            }
        }

        private static void ValidateInitializationAndReverseDisposeOrder()
        {
            var events = new List<string>();
            using (var host = new FacmHost())
            {
                host.Register(new TestModule("a", Array.Empty<string>(), events));
                host.Register(new TestModule("b", new[] { "a" }, events));
                host.Register(new TestModule("c", new[] { "b" }, events));
                host.Initialize();

                Require(
                    events.SequenceEqual(new[] { "init:a", "init:b", "init:c" }),
                    "FACM host initialization order is not dependency deterministic.");
                Require(
                    host.Report.InitializationOrder.SequenceEqual(new[] { "a", "b", "c" }),
                    "FACM host report lost initialization order.");
                Require(host.Report.Timings.Count == 3, "FACM host report did not record all module timings.");
                Require(
                    !string.IsNullOrWhiteSpace(host.Report.SlowestModuleId),
                    "FACM host report did not identify a slowest module.");
            }

            Require(
                events.SequenceEqual(new[]
                {
                    "init:a", "init:b", "init:c",
                    "dispose:c", "dispose:b", "dispose:a"
                }),
                "FACM host did not dispose modules in reverse dependency order.");
        }

        private static void ValidateMissingDependency()
        {
            using (var host = new FacmHost())
            {
                host.Register(new TestModule("a", new[] { "missing" }, new List<string>()));
                RequireThrows(
                    delegate { host.Initialize(); },
                    "depends on missing module",
                    "FACM host accepted a missing dependency.");
            }
        }

        private static void ValidateDuplicateModuleId()
        {
            using (var host = new FacmHost())
            {
                host.Register(new TestModule("a", Array.Empty<string>(), new List<string>()));
                RequireThrows(
                    delegate { host.Register(new TestModule("a", Array.Empty<string>(), new List<string>())); },
                    "Duplicate FACM module ID",
                    "FACM host accepted a duplicate module ID.");
            }
        }

        private static void ValidateCircularDependency()
        {
            using (var host = new FacmHost())
            {
                host.Register(new TestModule("a", new[] { "b" }, new List<string>()));
                host.Register(new TestModule("b", new[] { "a" }, new List<string>()));
                RequireThrows(
                    delegate { host.Initialize(); },
                    "Circular FACM module dependency detected",
                    "FACM host accepted a circular dependency.");
            }
        }

        private static void ValidateInitializationFailureRollback()
        {
            var events = new List<string>();
            using (var host = new FacmHost())
            {
                host.Register(new TestModule("a", Array.Empty<string>(), events));
                host.Register(new TestModule("b", new[] { "a" }, events, true));

                RequireThrows(
                    delegate { host.Initialize(); },
                    "Failed to initialize FACM module: b",
                    "FACM host did not surface module initialization failure.");

                Require(
                    events.SequenceEqual(new[] { "init:a", "init:b", "dispose:b", "dispose:a" }),
                    "FACM host did not dispose the partially initialized failing module and roll back prior modules.");
                Require(host.Report.Timings.Count == 2, "FACM host failure report lost timing diagnostics.");
                Require(!host.Report.Timings[1].Succeeded, "FACM host failure timing was marked successful.");
            }
        }

        private static void ValidateFirstModuleFailureReport()
        {
            var events = new List<string>();
            using (var host = new FacmHost())
            {
                host.Register(new TestModule("first", Array.Empty<string>(), events, true));

                RequireThrows(
                    delegate { host.Initialize(); },
                    "Failed to initialize FACM module: first",
                    "FACM host did not surface first-module initialization failure.");

                Require(
                    events.SequenceEqual(new[] { "init:first", "dispose:first" }),
                    "FACM host did not dispose a first module that failed during initialization.");
                Require(host.Report.Timings.Count == 1, "FACM first-module failure report lost timing diagnostics.");
                Require(host.Report.SlowestModuleId == "first", "FACM first-module failure report lost slowest module identity.");
            }
        }

        private static void ValidateShellFeatureDependencyContract()
        {
            var settings = new SettingsModule();
            var tools = new ToolsModule();
            var online = new OnlineModule();
            var pets = new PetsModule();
            var performance = new FACM.Performance.PerformanceModule();
            var leagueClient = new LeagueClientModule();
            var leagueDashboard = new LeagueDashboardModule(leagueClient, performance);
            var leaguePlayer = new LeaguePlayerModule(leagueClient, performance);
            var mayhem = new MayhemModule(leagueClient);
            var cleanup = new CleanupModule();
            var shell = new ShellModule(false, settings, tools, online, pets, leagueDashboard, leaguePlayer, mayhem, cleanup);

            Require(
                mayhem.Dependencies.SequenceEqual(new[] { LeagueClientModule.ModuleId }),
                "FACM Phase 5 Mayhem -> LeagueClient dependency contract changed unexpectedly.");
            Require(
                leagueDashboard.Dependencies.SequenceEqual(new[] { LeagueClientModule.ModuleId, FACM.Performance.PerformanceModule.ModuleId }),
                "League Dashboard must depend on LeagueClient and Performance.");
            Require(
                leaguePlayer.Dependencies.SequenceEqual(new[] { LeagueClientModule.ModuleId, FACM.Performance.PerformanceModule.ModuleId }),
                "League Player must depend on LeagueClient and Performance.");

            var expected = new[]
            {
                CompactMenuEnhancerModule.ModuleId,
                SettingsModule.ModuleId,
                ToolsModule.ModuleId,
                OnlineModule.ModuleId,
                PetsModule.ModuleId,
                LeagueDashboardModule.ModuleId,
                LeaguePlayerModule.ModuleId,
                MayhemModule.ModuleId,
                CleanupModule.ModuleId
            };

            Require(
                shell.Dependencies.SequenceEqual(expected),
                "FACM shell direct dependency contract changed unexpectedly.");
        }

        private static void RequireThrows(Action action, string expectedText, string failureMessage)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                if (exception.Message.IndexOf(expectedText, StringComparison.Ordinal) >= 0) return;
                throw new InvalidOperationException(
                    failureMessage + " Unexpected exception: " + exception.Message,
                    exception);
            }

            throw new InvalidOperationException(failureMessage);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class TestModule : IFacmModule
        {
            private readonly IList<string> _events;
            private readonly bool _failInitialization;

            public TestModule(
                string id,
                IReadOnlyList<string> dependencies,
                IList<string> events,
                bool failInitialization = false)
            {
                Id = id;
                Dependencies = dependencies;
                _events = events;
                _failInitialization = failInitialization;
            }

            public string Id { get; private set; }

            public IReadOnlyList<string> Dependencies { get; private set; }

            public void Initialize()
            {
                _events.Add("init:" + Id);
                if (_failInitialization) throw new InvalidOperationException("planned init failure");
            }

            public void Dispose()
            {
                _events.Add("dispose:" + Id);
            }
        }
    }
}
