using System;
using System.Collections.Generic;

namespace FACM.AppHost
{
    internal interface IFacmModule : IDisposable
    {
        string Id { get; }

        IReadOnlyList<string> Dependencies { get; }

        void Initialize();
    }
}
