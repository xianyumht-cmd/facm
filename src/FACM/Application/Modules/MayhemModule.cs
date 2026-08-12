using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.Mayhem;

namespace FACM.AppHost.Modules
{
    internal sealed class MayhemModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> NoDependencies = Array.Empty<string>();

        public const string ModuleId = "mayhem";

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

        public Form CreateLookupForm()
        {
            return new MayhemLookupForm();
        }

        public void Dispose()
        {
        }
    }
}
