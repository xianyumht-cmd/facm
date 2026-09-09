using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    /// <summary>
    /// Read-only side-evidence sampler used by LeagueDodgeProbeService. It never sends chat,
    /// changes lobby state, or performs any LCU write. Ordinary player chat bodies are not logged.
    /// Only system/event-style messages relevant to live dodge diagnosis may be recorded locally.
    /// </summary>
    internal sealed class LeagueDodgeSideEvidenceProbe
    {
        internal const string ConversationsPath = "/lol-chat/v1/conversations";
        internal const string LobbyPath = "/lol-lobby/v2/lobby";

        private static readonly TimeSpan EvidenceFreshness = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan LobbySampleInterval = TimeSpan.FromMilliseconds(500);

        private readonly ILeagueClientApi _client;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
        private readonly long _episode;
        private readonly Dictionary<string, string> _conversationLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _baselineConversationIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _seenMessageKeys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _lastConversationMessageKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<long>> _lastParticipantIds = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        private readonly Dictionary<long, HashSet<string>> _participantNamesById = new Dictionary<long, HashSet<string>>();
        private readonly HashSet<string> _loggedConversationCandidates = new HashSet<string>(StringComparer.Ordinal);

        private HashSet<long> _lastObservedMyTeam = new HashSet<long>();
        private HashSet<long> _lastLobbyMemberIds = new HashSet<long>();
        private bool _conversationBaselineCaptured;
        private bool _conversationTypesLogged;
        private DateTime _nextLobbySampleUtc = DateTime.MinValue;
        private string _chatRoomName;
        private int _nextConversationLabel = 1;

        private long _recentRosterDropId;
        private DateTime _recentRosterDropUtc = DateTime.MinValue;
        private long _recentParticipantDropId;
        private DateTime _recentParticipantDropUtc = DateTime.MinValue;
        private long _recentLobbyDropId;
        private DateTime _recentLobbyDropUtc = DateTime.MinValue;
        private long _recentSystemActorId;
        private bool _recentSystemActorOnMyTeam;
        private bool _recentSystemBodyMatchesAlly;
        private bool _recentDepartureMessageSeen;
        private DateTime _recentSystemUtc = DateTime.MinValue;
        private string _recentSystemType;

        public LeagueDodgeSideEvidenceProbe(ILeagueClientApi client, long episode)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _episode = episode;
        }

        public void ObserveChampSelectSnapshot(LeagueDodgeTeamSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.SessionAvailable) return;

            if (!string.IsNullOrWhiteSpace(snapshot.ChatRoomName))
                _chatRoomName = snapshot.ChatRoomName.Trim();

            var current = new HashSet<long>(snapshot.MySummonerIds.Where(value => value > 0));
            if (_lastObservedMyTeam.Count >= 4 && current.Count >= 3 && current.Count == _lastObservedMyTeam.Count - 1)
            {
                var dropped = _lastObservedMyTeam.Where(value => !current.Contains(value)).ToArray();
                if (dropped.Length == 1)
                {
                    _recentRosterDropId = dropped[0];
                    _recentRosterDropUtc = DateTime.UtcNow;
                    AppLog.Info(
                        "League Dodge Probe: my-team-transient-drop; episode=" + EpisodeText +
                        "; summonerId=" + dropped[0].ToString(CultureInfo.InvariantCulture) +
                        "; remaining=" + current.Count.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (current.Count > 0)
                _lastObservedMyTeam = current;
        }

        public async Task SampleAsync(LeagueDodgeTeamSnapshot roster, CancellationToken cancellationToken, bool force = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            roster = roster ?? new LeagueDodgeTeamSnapshot();

            var conversationBytes = await _client.TryGetBytesAsync(ConversationsPath, cancellationToken).ConfigureAwait(false);
            var conversations = ParseArray(conversationBytes);
            if (conversations != null)
                await SampleConversationsAsync(conversations, roster, cancellationToken, force).ConfigureAwait(false);

            var now = DateTime.UtcNow;
            if (force || now >= _nextLobbySampleUtc)
            {
                _nextLobbySampleUtc = now + LobbySampleInterval;
                await SampleLobbyAsync(roster, cancellationToken).ConfigureAwait(false);
            }
        }

        public LeagueDodgeClassification Refine(LeagueDodgeClassification current, LeagueDodgeTeamSnapshot roster)
        {
            current = current ?? new LeagueDodgeClassification("unknown", "missing-base-classification");
            roster = roster ?? new LeagueDodgeTeamSnapshot();
            if (!string.Equals(current.Side, "unknown", StringComparison.OrdinalIgnoreCase)) return current;

            if (IsFresh(_recentRosterDropUtc) && _recentRosterDropId > 0 && roster.MySummonerIds.Contains(_recentRosterDropId))
                return new LeagueDodgeClassification("ally", "my-team-transient-drop");

            if (IsFresh(_recentParticipantDropUtc) && _recentParticipantDropId > 0 && roster.MySummonerIds.Contains(_recentParticipantDropId))
                return new LeagueDodgeClassification("ally", "chat-participant-drop");

            if (IsFresh(_recentSystemUtc) && _recentDepartureMessageSeen && _recentSystemActorId > 0 && _recentSystemActorOnMyTeam)
                return new LeagueDodgeClassification("ally", "chat-system-actor-id");

            if (IsFresh(_recentSystemUtc) && _recentDepartureMessageSeen && _recentSystemBodyMatchesAlly)
                return new LeagueDodgeClassification("ally", "chat-system-name-match");

            if (IsFresh(_recentLobbyDropUtc) && _recentLobbyDropId > 0 && roster.MySummonerIds.Contains(_recentLobbyDropId))
                return new LeagueDodgeClassification("ally-party", "lobby-member-drop");

            return current;
        }

        public string BuildSummary(LeagueDodgeTeamSnapshot roster)
        {
            roster = roster ?? new LeagueDodgeTeamSnapshot();
            return "chatRoomKnown=" + (!string.IsNullOrWhiteSpace(_chatRoomName)).ToString().ToLowerInvariant() +
                   ",departureSystem=" + (IsFresh(_recentSystemUtc) && _recentDepartureMessageSeen).ToString().ToLowerInvariant() +
                   ",systemType=" + Safe(_recentSystemType) +
                   ",systemActorId=" + (IsFresh(_recentSystemUtc) ? _recentSystemActorId : 0L).ToString(CultureInfo.InvariantCulture) +
                   ",systemActorOnMyTeam=" + (IsFresh(_recentSystemUtc) && _recentSystemActorOnMyTeam).ToString().ToLowerInvariant() +
                   ",systemNameMatch=" + (IsFresh(_recentSystemUtc) && _recentSystemBodyMatchesAlly).ToString().ToLowerInvariant() +
                   ",participantDropId=" + (IsFresh(_recentParticipantDropUtc) ? _recentParticipantDropId : 0L).ToString(CultureInfo.InvariantCulture) +
                   ",rosterDropId=" + (IsFresh(_recentRosterDropUtc) ? _recentRosterDropId : 0L).ToString(CultureInfo.InvariantCulture) +
                   ",lobbyDropId=" + (IsFresh(_recentLobbyDropUtc) ? _recentLobbyDropId : 0L).ToString(CultureInfo.InvariantCulture);
        }

        internal static void ValidateForSmokeTest()
        {
            var probe = new LeagueDodgeSideEvidenceProbe(new NoopLeagueClientApi(), 1);
            var roster = new LeagueDodgeTeamSnapshot { SessionAvailable = true, MyTeamSlots = 5 };
            foreach (var id in new long[] { 11, 12, 13, 14, 15 }) roster.MySummonerIds.Add(id);

            var first = new LeagueDodgeTeamSnapshot { SessionAvailable = true, MyTeamSlots = 5 };
            foreach (var id in new long[] { 11, 12, 13, 14, 15 }) first.MySummonerIds.Add(id);
            probe.ObserveChampSelectSnapshot(first);

            var second = new LeagueDodgeTeamSnapshot { SessionAvailable = true, MyTeamSlots = 4 };
            foreach (var id in new long[] { 11, 12, 14, 15 }) second.MySummonerIds.Add(id);
            probe.ObserveChampSelectSnapshot(second);

            var refined = probe.Refine(new LeagueDodgeClassification("unknown", "test"), roster);
            if (refined.Side != "ally" || refined.Basis != "my-team-transient-drop")
                throw new InvalidOperationException("Dodge side evidence roster-drop refinement regressed.");

            if (!IsDepartureLike("某某退出了组队房间", "system"))
                throw new InvalidOperationException("Dodge side evidence Chinese departure detection regressed.");
            if (!IsDepartureLike("A player has left the room", "system"))
                throw new InvalidOperationException("Dodge side evidence English departure detection regressed.");
            if (IsDepartureLike("今晚一起玩吗", "chat"))
                throw new InvalidOperationException("Dodge side evidence must not treat ordinary chat as a departure event.");
            if (IsSystemLike("chat"))
                throw new InvalidOperationException("Dodge side evidence must not log ordinary chat solely because sender identity is absent.");
        }

        private async Task SampleConversationsAsync(
            List<Dictionary<string, object>> conversations,
            LeagueDodgeTeamSnapshot roster,
            CancellationToken cancellationToken,
            bool includeDetails)
        {
            var currentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var conversation in conversations)
            {
                var id = ReadString(conversation, "id");
                if (!string.IsNullOrWhiteSpace(id)) currentIds.Add(id);
            }

            if (!_conversationBaselineCaptured)
            {
                _conversationBaselineCaptured = true;
                foreach (var id in currentIds) _baselineConversationIds.Add(id);
            }

            if (!_conversationTypesLogged)
            {
                _conversationTypesLogged = true;
                var types = conversations.Select(value => Safe(ReadString(value, "type")))
                    .Where(value => value != "-")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .ToArray();
                AppLog.Info(
                    "League Dodge Probe: chat-conversations; episode=" + EpisodeText +
                    "; count=" + conversations.Count.ToString(CultureInfo.InvariantCulture) +
                    "; chatRoomKnown=" + (!string.IsNullOrWhiteSpace(_chatRoomName)).ToString().ToLowerInvariant() +
                    "; types=" + (types.Length == 0 ? "-" : string.Join(",", types)));
            }

            var candidates = conversations
                .Select(value => new ConversationCandidate(value, ScoreConversation(value)))
                .Where(value => value.Score > 0)
                .OrderByDescending(value => value.Score)
                .ThenBy(value => ReadString(value.Value, "id"), StringComparer.Ordinal)
                .Take(2)
                .ToArray();

            foreach (var candidate in candidates)
            {
                var id = ReadString(candidate.Value, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                var label = GetConversationLabel(id);
                if (_loggedConversationCandidates.Add(id))
                {
                    AppLog.Info(
                        "League Dodge Probe: chat-candidate; episode=" + EpisodeText +
                        "; key=" + label +
                        "; type=" + Safe(ReadString(candidate.Value, "type")) +
                        "; score=" + candidate.Score.ToString(CultureInfo.InvariantCulture) +
                        "; newThisEpisode=" + (!_baselineConversationIds.Contains(id)).ToString().ToLowerInvariant());
                }

                var lastMessage = candidate.Value.ContainsKey("lastMessage")
                    ? candidate.Value["lastMessage"] as Dictionary<string, object>
                    : null;
                var lastMessageKey = MessageKey(lastMessage);
                string previousLastMessageKey;
                if (!string.IsNullOrWhiteSpace(lastMessageKey) &&
                    (!_lastConversationMessageKeys.TryGetValue(id, out previousLastMessageKey) ||
                     !string.Equals(previousLastMessageKey, lastMessageKey, StringComparison.Ordinal)))
                {
                    _lastConversationMessageKeys[id] = lastMessageKey;
                    ProcessMessage(lastMessage, label, roster, true);
                }

                var needsMessageBaseline = !_seenMessageKeys.ContainsKey(id);
                var needsParticipantBaseline = !_lastParticipantIds.ContainsKey(id);
                if (includeDetails || needsMessageBaseline)
                    await SampleConversationMessagesAsync(id, label, roster, cancellationToken).ConfigureAwait(false);
                if (includeDetails || needsParticipantBaseline)
                    await SampleConversationParticipantsAsync(id, label, roster, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task SampleConversationMessagesAsync(
            string conversationId,
            string label,
            LeagueDodgeTeamSnapshot roster,
            CancellationToken cancellationToken)
        {
            var path = ConversationsPath + "/" + Uri.EscapeDataString(conversationId) + "/messages";
            var bytes = await _client.TryGetBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var messages = ParseArray(bytes);
            if (messages == null) return;

            HashSet<string> seen;
            var firstRead = !_seenMessageKeys.TryGetValue(conversationId, out seen);
            if (firstRead)
            {
                seen = new HashSet<string>(StringComparer.Ordinal);
                _seenMessageKeys[conversationId] = seen;
            }

            foreach (var message in messages)
            {
                var key = MessageKey(message);
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key)) continue;
                var departure = IsDepartureLike(ReadString(message, "body"), ReadString(message, "type"));
                var historical = ReadBool(message, "isHistorical");
                if (!firstRead || (departure && !historical))
                    ProcessMessage(message, label, roster, false);
            }
        }

        private async Task SampleConversationParticipantsAsync(
            string conversationId,
            string label,
            LeagueDodgeTeamSnapshot roster,
            CancellationToken cancellationToken)
        {
            var path = ConversationsPath + "/" + Uri.EscapeDataString(conversationId) + "/participants";
            var bytes = await _client.TryGetBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var participants = ParseArray(bytes);
            if (participants == null) return;

            var current = new HashSet<long>();
            foreach (var participant in participants)
            {
                var id = FirstPositive(
                    ReadLong(participant, "summonerId"),
                    ReadLong(participant, "id"),
                    ReadLong(participant, "obfuscatedSummonerId"));
                if (id <= 0) continue;
                current.Add(id);
                RememberParticipantNames(id, participant);
            }

            HashSet<long> previous;
            if (_lastParticipantIds.TryGetValue(conversationId, out previous) && previous.Count >= 2 &&
                current.Count > 0 && current.Count == previous.Count - 1)
            {
                var dropped = previous.Where(value => !current.Contains(value)).ToArray();
                if (dropped.Length == 1)
                {
                    _recentParticipantDropId = dropped[0];
                    _recentParticipantDropUtc = DateTime.UtcNow;
                    AppLog.Info(
                        "League Dodge Probe: chat-participant-drop; episode=" + EpisodeText +
                        "; key=" + label +
                        "; summonerId=" + dropped[0].ToString(CultureInfo.InvariantCulture) +
                        "; onMyTeam=" + roster.MySummonerIds.Contains(dropped[0]).ToString().ToLowerInvariant() +
                        "; remaining=" + current.Count.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (current.Count > 0)
                _lastParticipantIds[conversationId] = current;
        }

        private async Task SampleLobbyAsync(LeagueDodgeTeamSnapshot roster, CancellationToken cancellationToken)
        {
            var bytes = await _client.TryGetBytesAsync(LobbyPath, cancellationToken).ConfigureAwait(false);
            var root = ParseObject(bytes);
            if (root == null) return;

            var current = new HashSet<long>();
            foreach (var member in EnumerateDictionaries(ReadValue(root, "members")))
            {
                var id = FirstPositive(
                    ReadLong(member, "summonerId"),
                    ReadLong(member, "id"),
                    ReadLong(member, "summonerIdInternal"));
                if (id > 0) current.Add(id);
            }

            if (_lastLobbyMemberIds.Count >= 2 && current.Count > 0 && current.Count == _lastLobbyMemberIds.Count - 1)
            {
                var dropped = _lastLobbyMemberIds.Where(value => !current.Contains(value)).ToArray();
                if (dropped.Length == 1)
                {
                    _recentLobbyDropId = dropped[0];
                    _recentLobbyDropUtc = DateTime.UtcNow;
                    AppLog.Info(
                        "League Dodge Probe: lobby-member-drop; episode=" + EpisodeText +
                        "; summonerId=" + dropped[0].ToString(CultureInfo.InvariantCulture) +
                        "; onMyTeam=" + roster.MySummonerIds.Contains(dropped[0]).ToString().ToLowerInvariant() +
                        "; remaining=" + current.Count.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (current.Count > 0)
                _lastLobbyMemberIds = current;
        }

        private void ProcessMessage(
            Dictionary<string, object> message,
            string label,
            LeagueDodgeTeamSnapshot roster,
            bool fromConversationMetadata)
        {
            if (message == null) return;
            var body = ReadString(message, "body") ?? string.Empty;
            var type = ReadString(message, "type") ?? string.Empty;
            var actorId = FirstPositive(
                ReadLong(message, "fromSummonerId"),
                ReadLong(message, "fromObfuscatedSummonerId"),
                ReadLong(message, "fromId"));
            var departure = IsDepartureLike(body, type);
            var systemLike = IsSystemLike(type);
            if (!systemLike && !departure) return;

            _recentSystemUtc = DateTime.UtcNow;
            _recentSystemType = type;
            _recentSystemActorId = actorId;
            _recentSystemActorOnMyTeam = actorId > 0 && roster.MySummonerIds.Contains(actorId);
            _recentSystemBodyMatchesAlly = BodyMatchesAllyName(body, roster);
            _recentDepartureMessageSeen = departure;

            AppLog.Info(
                "League Dodge Probe: chat-system; episode=" + EpisodeText +
                "; key=" + label +
                "; source=" + (fromConversationMetadata ? "lastMessage" : "messages") +
                "; type=" + Safe(type) +
                "; actorId=" + actorId.ToString(CultureInfo.InvariantCulture) +
                "; actorOnMyTeam=" + _recentSystemActorOnMyTeam.ToString().ToLowerInvariant() +
                "; nameMatch=" + _recentSystemBodyMatchesAlly.ToString().ToLowerInvariant() +
                "; departure=" + departure.ToString().ToLowerInvariant() +
                "; body=" + SafeBody(body));
        }

        private bool BodyMatchesAllyName(string body, LeagueDodgeTeamSnapshot roster)
        {
            if (string.IsNullOrWhiteSpace(body) || roster == null) return false;
            foreach (var id in roster.MySummonerIds)
            {
                HashSet<string> names;
                if (!_participantNamesById.TryGetValue(id, out names)) continue;
                foreach (var name in names)
                {
                    if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2) continue;
                    if (body.IndexOf(name.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            return false;
        }

        private void RememberParticipantNames(long id, Dictionary<string, object> participant)
        {
            if (id <= 0 || participant == null) return;
            HashSet<string> names;
            if (!_participantNamesById.TryGetValue(id, out names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _participantNamesById[id] = names;
            }

            foreach (var key in new[] { "name", "gameName", "displayName", "summonerName", "riotIdGameName" })
            {
                var value = ReadString(participant, key);
                if (!string.IsNullOrWhiteSpace(value)) names.Add(value.Trim());
            }
        }

        private int ScoreConversation(Dictionary<string, object> conversation)
        {
            if (conversation == null) return 0;
            var id = ReadString(conversation, "id") ?? string.Empty;
            if (id.Length == 0) return 0;
            var name = ReadString(conversation, "name") ?? string.Empty;
            var type = ReadString(conversation, "type") ?? string.Empty;

            if (MatchesChatRoom(id) || MatchesChatRoom(name)) return 100;
            if (ContainsAny(type, "champ", "select")) return 90;
            if (ContainsAny(type, "party", "lobby", "group")) return 70;
            if (_conversationBaselineCaptured && !_baselineConversationIds.Contains(id)) return 50;
            return 0;
        }

        private bool MatchesChatRoom(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(_chatRoomName)) return false;
            if (string.Equals(value.Trim(), _chatRoomName, StringComparison.OrdinalIgnoreCase)) return true;
            var at = _chatRoomName.IndexOf('@');
            var shortRoom = at > 0 ? _chatRoomName.Substring(0, at) : _chatRoomName;
            return shortRoom.Length >= 8 && value.IndexOf(shortRoom, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetConversationLabel(string id)
        {
            string label;
            if (_conversationLabels.TryGetValue(id, out label)) return label;
            label = "c" + _nextConversationLabel.ToString(CultureInfo.InvariantCulture);
            _nextConversationLabel++;
            _conversationLabels[id] = label;
            return label;
        }

        private List<Dictionary<string, object>> ParseArray(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            object value;
            try { value = _json.DeserializeObject(Encoding.UTF8.GetString(bytes)); }
            catch { return null; }
            var output = new List<Dictionary<string, object>>();
            foreach (var item in EnumerateValues(value))
            {
                var dictionary = item as Dictionary<string, object>;
                if (dictionary != null) output.Add(dictionary);
            }
            return output;
        }

        private Dictionary<string, object> ParseObject(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try { return _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch { return null; }
        }

        private static string MessageKey(Dictionary<string, object> message)
        {
            if (message == null) return null;
            var id = ReadString(message, "id");
            if (!string.IsNullOrWhiteSpace(id)) return "id:" + id.Trim();
            return "fallback:" + Safe(ReadString(message, "timestamp")) + "|" + Safe(ReadString(message, "type")) + "|" + Safe(ReadString(message, "body"));
        }

        private static bool IsSystemLike(string type)
        {
            return ContainsAny(type, "system", "event", "notification", "info");
        }

        private static bool IsDepartureLike(string body, string type)
        {
            if (string.IsNullOrWhiteSpace(body)) return false;
            var text = body.Trim();
            var chineseLeave = ContainsAny(text, "退出", "离开", "離開");
            var chineseRoom = ContainsAny(text, "房间", "房間", "组队", "組隊", "队伍", "隊伍", "队列", "隊列");
            if (chineseLeave && chineseRoom) return true;

            var lower = text.ToLowerInvariant();
            if (lower.Contains("has left") || lower.Contains("left the room") || lower.Contains("left the party") ||
                lower.Contains("left the lobby") || lower.Contains("left your party") || lower.Contains("left your lobby"))
                return true;

            return ContainsAny(type, "system", "event") &&
                   (lower.Contains(" left ") || lower.StartsWith("left ", StringComparison.Ordinal));
        }

        private static bool ContainsAny(string value, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(value) || tokens == null) return false;
            foreach (var token in tokens)
            {
                if (!string.IsNullOrWhiteSpace(token) && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static string SafeBody(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "-";
            var safe = value.Replace("\r", " ").Replace("\n", " ").Replace(";", ",").Trim();
            return safe.Length <= 160 ? safe : safe.Substring(0, 160) + "...";
        }

        private static string Safe(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "-";
            return value.Replace("\r", " ").Replace("\n", " ").Replace(";", ",").Trim();
        }

        private bool IsFresh(DateTime timestampUtc)
        {
            return timestampUtc != DateTime.MinValue && DateTime.UtcNow - timestampUtc <= EvidenceFreshness;
        }

        private string EpisodeText
        {
            get { return _episode.ToString(CultureInfo.InvariantCulture); }
        }

        private static object ReadValue(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static long ReadLong(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            if (value == null) return 0L;
            long output;
            return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out output)
                ? output
                : 0L;
        }

        private static bool ReadBool(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            if (value == null) return false;
            bool output;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out output) && output;
        }

        private static long FirstPositive(params long[] values)
        {
            if (values == null) return 0L;
            foreach (var value in values) if (value > 0) return value;
            return 0L;
        }

        private static IEnumerable<Dictionary<string, object>> EnumerateDictionaries(object value)
        {
            foreach (var item in EnumerateValues(value))
            {
                var dictionary = item as Dictionary<string, object>;
                if (dictionary != null) yield return dictionary;
            }
        }

        private static IEnumerable<object> EnumerateValues(object value)
        {
            if (value == null) yield break;
            var array = value as object[];
            if (array != null)
            {
                foreach (var item in array) yield return item;
                yield break;
            }

            var list = value as ArrayList;
            if (list != null)
            {
                foreach (var item in list) yield return item;
            }
        }

        private sealed class ConversationCandidate
        {
            public ConversationCandidate(Dictionary<string, object> value, int score)
            {
                Value = value;
                Score = score;
            }

            public Dictionary<string, object> Value { get; private set; }
            public int Score { get; private set; }
        }

        private sealed class NoopLeagueClientApi : ILeagueClientApi
        {
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                return Task.FromResult<byte[]>(null);
            }
        }
    }
}
