using System;
using System.Collections.Generic;
using FACM.AppHost;
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

        public bool IsRelatedProcessRunning(string gamePath)
        {
            return ProcessGuard.IsRelatedProcessRunning(gamePath);
        }

        public string ResolveGameRoot(string path)
        {
            return GameLocator.ResolveGameRoot(path);
        }

        public CleanupPlan CreatePlan(string gameRoot)
        {
            return SafeCleanupService.CreatePlan(gameRoot);
        }

        public void Execute(CleanupPlan plan)
        {
            SafeCleanupService.Execute(plan);
        }

        public void Dispose()
        {
        }
    }
}
