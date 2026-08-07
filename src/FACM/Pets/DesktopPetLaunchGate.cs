using System;
using System.Threading;

namespace FACM.Pets
{
    internal static class DesktopPetLaunchGate
    {
        private static int _explicitUseDepth;

        public static bool ExplicitUseAllowed
        {
            get { return Volatile.Read(ref _explicitUseDepth) > 0; }
        }

        public static IDisposable BeginExplicitUse()
        {
            Interlocked.Increment(ref _explicitUseDepth);
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                Interlocked.Decrement(ref _explicitUseDepth);
            }
        }
    }
}
