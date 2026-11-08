using System;
using System.Web.Script.Serialization;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceGameflowAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private LeagueDashboardPhaseState _state;

        internal UnifiedWorkspaceGameflowAdapter(Func<bool> canPublish, Action<string> publish)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        }

        internal void Update(LeagueDashboardPhaseState state)
        {
            _state = state;
            Sync();
        }

        internal void Sync()
        {
            if (_canPublish()) _publish(Serialize(_state));
        }

        internal void PostPageStatus(string type, string status)
        {
            if (_canPublish()) _publish(new JavaScriptSerializer().Serialize(new { type, status }));
        }

        private static string Serialize(LeagueDashboardPhaseState state)
        {
            var connected = state != null && state.Connected;
            return new JavaScriptSerializer().Serialize(new
            {
                type = "status", connected, phase = connected ? (state.Phase ?? string.Empty) : string.Empty
            });
        }

        internal static void ValidateForSmokeTest()
        {
            if (!Serialize(null).Contains("\"connected\":false") ||
                !Serialize(null).Contains("\"phase\":\"\""))
                throw new InvalidOperationException("Disconnected gameflow status contract changed.");
            if (!Serialize(new LeagueDashboardPhaseState { Connected = true, Phase = "ChampSelect" })
                    .Contains("\"phase\":\"ChampSelect\"") ||
                !Serialize(new LeagueDashboardPhaseState { Connected = false, Phase = "ChampSelect" })
                    .Contains("\"phase\":\"\""))
                throw new InvalidOperationException("Gameflow status retained a disconnected phase.");
            var posted = 0;
            var hidden = new UnifiedWorkspaceGameflowAdapter(() => false,
                delegate { throw new InvalidOperationException("Hidden gameflow status published."); });
            var visible = new UnifiedWorkspaceGameflowAdapter(() => true,
                delegate(string json)
                {
                    if (!json.Contains("\"type\":\"status\"") &&
                        !json.Contains("\"type\":\"mayhem-failed\""))
                        throw new InvalidOperationException("Gameflow status message type changed.");
                    posted++;
                });
            hidden.Sync();
            visible.Sync();
            hidden.PostPageStatus("mayhem-failed", "ready");
            visible.PostPageStatus("mayhem-failed", "ready");
            if (posted != 2) throw new InvalidOperationException("Gameflow adapter instance isolation failed.");
        }
    }
}
