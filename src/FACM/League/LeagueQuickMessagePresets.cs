using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal static class LeagueQuickMessagePresets
    {
        internal const int SlotCount = 4;
        internal const int MaximumMessageLength = 300;

        internal static string NormalizeMessage(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 600) return string.Empty;
            var lines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var result = new List<string>(SlotCount);
            foreach (var line in lines)
            {
                var normalized = line.Trim();
                if (normalized.Length == 0) continue;
                if (normalized.Any(char.IsControl) || result.Count == SlotCount) return string.Empty;
                result.Add(normalized);
            }
            var message = string.Join("\n", result);
            return message.Length <= MaximumMessageLength ? message : string.Empty;
        }

        internal static string[] Parse(string json)
        {
            var empty = new string[SlotCount];
            if (string.IsNullOrEmpty(json) || json.Length > 2500) return empty;
            try
            {
                var items = new JavaScriptSerializer().Deserialize<string[]>(json);
                if (items == null || items.Length != SlotCount) return empty;
                return items.Select(item => NormalizeMessage(item)).ToArray();
            }
            catch { return empty; }
        }

        internal static string NormalizeJson(string json)
        {
            return new JavaScriptSerializer().Serialize(Parse(json));
        }

        internal static string Save(string[] slots)
        {
            if (slots == null || slots.Length != SlotCount)
                throw new ArgumentException("Invalid quick-message slots.", nameof(slots));
            return new JavaScriptSerializer().Serialize(slots.Select(NormalizeMessage).ToArray());
        }
    }

    internal sealed class LeagueQuickMessageSender
    {
        private const string ConversationsPath = "/lol-chat/v1/conversations";
        private readonly ILeagueClientApi _read;
        private readonly ILeagueQuickMessageWriteApi _write;
        private readonly Func<LeagueClientSession> _session;
        private readonly Func<LeagueDashboardPhaseState> _phase;

        internal LeagueQuickMessageSender(ILeagueClientApi read, ILeagueQuickMessageWriteApi write,
            Func<LeagueClientSession> session, Func<LeagueDashboardPhaseState> phase)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _phase = phase ?? throw new ArgumentNullException(nameof(phase));
        }

        internal async Task<bool> SendChatAsync(string text, CancellationToken cancellationToken)
        {
            var message = LeagueQuickMessagePresets.NormalizeMessage(text);
            if (message.Length == 0) return false;
            var current = _phase();
            if (current == null || !current.Connected || !IsChatPhase(current.Phase)) return false;
            var session = _session();
            if (session == null) return false;

            var bytes = await _read.TryGetBytesAsync(ConversationsPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var id = current.Phase == "ChampSelect" ? LeagueAramTeamSideController.ParseConversation(bytes) :
                LeagueFriendInvitationController.ParseCustomGameConversation(bytes);
            if (id == null) return false;

            var latest = _phase();
            var activeSession = _session();
            if (latest == null || !latest.Connected || latest.Phase != current.Phase ||
                activeSession == null || !session.Matches(activeSession)) return false;
            var response = await _write.TrySendQuickMessageAsync(session, id, message, cancellationToken).ConfigureAwait(false);
            return response != null && response.IsSuccessStatusCode;
        }

        private static bool IsChatPhase(string phase)
        {
            return phase == "Lobby" || phase == "ChampSelect";
        }
    }
}
