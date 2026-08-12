using System;
using System.Collections.Generic;
using FACM.AppHost;

namespace FACM.AppHost.Modules
{
    internal sealed class ShellModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[]
        {
            CompactMenuEnhancerModule.ModuleId
        };
        private readonly bool _startCleanup;

        public ShellModule(bool startCleanup)
        {
            _startCleanup = startCleanup;
        }

        public const string ModuleId = "shell";

        public string Id
        {
            get { return ModuleId; }
        }

        public IReadOnlyList<string> Dependencies
        {
            get { return ModuleDependencies; }
        }

        public MainForm MainForm { get; private set; }

        public void Initialize()
        {
            MainForm = new MainForm(_startCleanup);
        }

        public void Dispose()
        {
            var form = MainForm;
            MainForm = null;
            if (form == null || form.IsDisposed) return;
            form.Dispose();
        }
    }
}
