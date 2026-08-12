using System;
using System.Collections.Generic;

namespace FACM.Application
{
    internal interface IFacmModule : IDisposable
    {
        string Id { get; }

        IReadOnlyList<string> Dependencies { get; }

        void Initialize();
    }
}
