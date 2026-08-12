using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.Application.Modules
{
    internal sealed class ToolsModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> NoDependencies = Array.Empty<string>();

        public const string ModuleId = "tools";

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
        }

        public Task WarmupAsync()
        {
            return Task.Run((Action)ToolBundleLoader.Prepare);
        }

        public void RunStandaloneToolA()
        {
            ToolRunner.RunStandaloneToolA();
        }

        public void RunFixLcu(int mode)
        {
            ToolRunner.RunFixLcu(mode);
        }

        public void Dispose()
        {
        }
    }
}
