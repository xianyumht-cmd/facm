using System;
using System.Collections.Generic;
using FACM.AppHost;

namespace FACM.AppHost.Modules
{
    internal sealed class CompactMenuEnhancerModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> NoDependencies = Array.Empty<string>();

        public const string ModuleId = "shell.compact-menu-enhancer";

        public string Id
        {
            get { return ModuleId; }
        }

        public IReadOnlyList<string> Dependencies
        {
            get { return NoDependencies; }
        }

        public void Initialize()
        {
            CompactMenuEnhancer.Install();
        }

        public void Dispose()
        {
            // CompactMenuEnhancer is process-scoped and currently has no teardown contract.
        }
    }
}
