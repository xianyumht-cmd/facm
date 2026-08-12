using System;
using System.Collections.Generic;

namespace FACM.Application.Modules
{
    internal sealed class CompactMenuEnhancerModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> NoDependencies = Array.Empty<string>();

        public string Id
        {
            get { return "shell.compact-menu-enhancer"; }
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
