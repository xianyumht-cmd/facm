using System;
using System.Collections.Generic;
using FACM.AppHost;
using FACM.Configuration;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class CleanupModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> NoDependencies = Array.Empty<string>();

        public const string ModuleId = "cleanup";

        public string Id
        {
            get { return ModuleId; }
        }

        public IReadOnlyList<string> Dependencies
        {
            get { return NoDependencies; }
        }

        public bool IsConfigured
        {
            get { return CleanupProfile.IsConfigured; }
        }

        public bool IsAdministrator
        {
            get { return ElevationService.IsAdministrator; }
        }

        public void Initialize()
        {
        }

        public bool RestartElevatedForCleanup()
        {
            return ElevationService.RestartElevatedForCleanup();
        }

        public IReadOnlyList<string> GetRunningRelatedProcesses()
        {
            return ProcessGuard.GetRunningRelatedProcesses();
        }

        public string FindGameRoot()
        {
            return GameLocator.FindGameRoot();
        }

        public string ResolveGameRoot(string path)
        {
            return GameLocator.ResolveGameRoot(path);
        }

        public bool IsValidGameRoot(string path)
        {
            return GameLocator.IsValidGameRoot(path);
        }

        public CleanupPlan CreatePlan(string gameRoot)
        {
            return SafeCleanupService.CreatePlan(gameRoot);
        }

        public CleanupResult Execute(CleanupPlan plan)
        {
            return SafeCleanupService.Execute(plan);
        }

        public void Dispose()
        {
        }
    }
}
