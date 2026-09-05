using FACM.Performance;

namespace FACM.League
{
    internal enum DesktopEntryGameflowAction
    {
        None = 0,
        Hide = 1,
        Restore = 2
    }

    /// <summary>
    /// Keeps the 3.5 shell lightweight while preserving the useful 4.x behavior:
    /// desktop entry surfaces are suppressed only while Gameflow is in-game, and
    /// they are restored only when Gameflow itself hid something that was visible.
    /// </summary>
    internal sealed class DesktopEntryGameflowPolicy
    {
        private bool _suppressed;
        private bool _hiddenByGameflow;

        public bool IsSuppressed
        {
            get { return _suppressed; }
        }

        public bool HiddenByGameflow
        {
            get { return _hiddenByGameflow; }
        }

        public DesktopEntryGameflowAction Observe(LeagueDashboardPhaseState state, bool desktopEntryVisible)
        {
            var inGame = state != null && state.Activity == LeagueActivityLevel.InGame;
            if (inGame)
            {
                if (_suppressed) return DesktopEntryGameflowAction.None;
                _suppressed = true;
                _hiddenByGameflow = desktopEntryVisible;

                // Entering suppression always emits one hide action. MainForm also owns a transient
                // control-center window that can be open from the tray even when the ball/pet itself
                // was manually hidden. The action closes that surface, while restore ownership stays
                // tied only to desktopEntryVisible so a manually hidden launcher is never force-shown.
                return DesktopEntryGameflowAction.Hide;
            }

            if (!_suppressed) return DesktopEntryGameflowAction.None;

            _suppressed = false;
            var restore = _hiddenByGameflow;
            _hiddenByGameflow = false;
            return restore ? DesktopEntryGameflowAction.Restore : DesktopEntryGameflowAction.None;
        }
    }
}
