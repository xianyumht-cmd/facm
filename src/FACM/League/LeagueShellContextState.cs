using System.Threading;

namespace FACM.League
{
    /// <summary>
    /// Read-only shell cache for the already-owned Gameflow state. Shell surfaces consume this
    /// snapshot for navigation only; the cache never starts polling or performs League writes.
    /// </summary>
    internal static class LeagueShellContextState
    {
        private static LeagueDashboardPhaseState _current;

        public static LeagueDashboardPhaseState Current
        {
            get { return Volatile.Read(ref _current); }
        }

        public static void Update(LeagueDashboardPhaseState state)
        {
            Volatile.Write(ref _current, state);
        }

        public static void Clear()
        {
            Volatile.Write(ref _current, null);
        }
    }
}
