using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal interface ILeaguePrivateChatWriteApi
    {
        Task<LeagueClientWriteResponse> TryReplyAsync(LeagueClientSession session, string conversationId, string text, CancellationToken token);
    }

    internal sealed class LeagueAutoReplyController : IDisposable
    {
        private readonly ILeagueClientApi _reader;
        private readonly ILeaguePrivateChatWriteApi _writer;
        private readonly Func<LeagueClientSession> _session;
        private readonly LeagueOfflineStatusLockController _offlineLock;
        private readonly object _sync = new object();
        private CancellationTokenSource _active;
        private Task _running;
        private LeagueClientSession _observed;
        private bool _enabled, _awayOnly, _lockOffline, _disposed;
        private string _text = "";
        private DateTime _retryAfter;
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _seenOrder = new Queue<string>();

        internal LeagueAutoReplyController(ILeagueClientApi reader, ILeaguePrivateChatWriteApi writer, Func<LeagueClientSession> session,
            LeagueOfflineStatusLockController offlineLock = null)
        {
            _reader = reader; _writer = writer; _session = session; _offlineLock = offlineLock;
        }

        internal static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 500) return "";
            foreach (var c in text) if (char.IsControl(c)) return "";
            return text.Trim();
        }

        internal void Configure(bool enabled, bool awayOnly, string text, bool lockOffline = false)
        {
            text = NormalizeText(text);
            lock (_sync)
            {
                if (_enabled == enabled && _awayOnly == awayOnly && _text == text && _lockOffline == lockOffline) return;
                _enabled = enabled; _awayOnly = awayOnly; _text = text; _lockOffline = lockOffline;
                _active?.Cancel();
            }
        }

        internal void Observe(LeagueDashboardPhaseState state)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _offlineLock?.ObserveConnection(state);
                var session = state != null && state.Connected && (_enabled && _text.Length > 0 || _lockOffline) ? _session() : null;
                if (session == null || _observed != null && !_observed.Matches(session)) _active?.Cancel();
                if (_running != null && !_running.IsCompleted || session == null || DateTime.UtcNow < _retryAfter) return;
                if (_observed == null || !_observed.Matches(session)) { _seen.Clear(); _seenOrder.Clear(); }
                _observed = session;
                _active = new CancellationTokenSource();
                var active = _active;
                var text = _text;
                var away = _awayOnly;
                var replyEnabled = _enabled && text.Length > 0;
                var lockOffline = _lockOffline;
                _running = Task.Run(async () =>
                {
                    try
                    {
                        _offlineLock?.ResetEventBaseline();
                        await LeagueChatEventStream.RunAsync(session, (message, token) =>
                        {
                            if (!session.Matches(_session())) { active.Cancel(); return Task.CompletedTask; }
                            if (message.Availability != null)
                                return lockOffline && _offlineLock != null ? _offlineLock.ObserveEventAsync(message.Availability, token) : Task.CompletedTask;
                            return replyEnabled ? ProcessAsync(session, message, text, away, token) : Task.CompletedTask;
                        }, active.Token, async token =>
                        {
                            if (!lockOffline || _offlineLock == null) return;
                            var presence = await ReadAsync("/lol-chat/v1/me", token).ConfigureAwait(false);
                            if (session.Matches(_session()))
                                await _offlineLock.ObserveEventAsync(LeagueChatEventStream.Text(presence, "availability"), token).ConfigureAwait(false);
                        }).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        if (!active.IsCancellationRequested) AppLog.Info("League chat event stream stopped: " + exception.GetType().Name);
                    }
                    finally
                    {
                        lock (_sync)
                        {
                            if (ReferenceEquals(_active, active)) _active = null;
                            _retryAfter = DateTime.UtcNow.AddSeconds(10);
                        }
                        active.Dispose();
                    }
                });
            }
        }

        internal async Task ProcessAsync(LeagueClientSession session, LeagueChatMessageEvent incoming,
            string text, bool awayOnly, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (incoming == null || !LeagueChatEventStream.SafeId(incoming.ConversationId) ||
                !LeagueChatEventStream.SafeId(incoming.MessageId) || incoming.SenderId <= 0 ||
                NormalizeText(text) != text || string.IsNullOrEmpty(text) || !session.Matches(_session())) return;
            var key = incoming.ConversationId + "/" + incoming.MessageId;
            if (!_seen.Add(key)) return;
            _seenOrder.Enqueue(key);
            if (_seenOrder.Count > 512) _seen.Remove(_seenOrder.Dequeue());
            var path = "/lol-chat/v1/conversations/" + incoming.ConversationId;
            var me = await ReadAsync("/lol-summoner/v1/current-summoner", token).ConfigureAwait(false);
            long self;
            if (!long.TryParse(LeagueChatEventStream.Text(me, "summonerId"), out self) || self <= 0 || self == incoming.SenderId) return;
            for (var pass = 0; pass < 2; pass++)
            {
                var conversation = await ReadAsync(path, token).ConfigureAwait(false);
                if (LeagueChatEventStream.Text(conversation, "type") != "chat" ||
                    LeagueChatEventStream.Text(conversation, "id") != incoming.ConversationId) return;
                if (awayOnly)
                {
                    var presence = await ReadAsync("/lol-chat/v1/me", token).ConfigureAwait(false);
                    if (LeagueChatEventStream.Text(presence, "availability") != "away") return;
                }
            }
            var current = await ReadAsync("/lol-summoner/v1/current-summoner", token).ConfigureAwait(false);
            if (LeagueChatEventStream.Text(current, "summonerId") != self.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                !session.Matches(_session())) return;
            token.ThrowIfCancellationRequested();
            var response = await _writer.TryReplyAsync(session, incoming.ConversationId, text, token).ConfigureAwait(false);
            AppLog.Info("League private auto-reply: " + (response != null && response.StatusCode >= 200 && response.StatusCode < 300 ? "submitted" : "unverified"));
        }

        private async Task<Dictionary<string, object>> ReadAsync(string path, CancellationToken token)
        {
            var bytes = await _reader.TryGetBytesAsync(path, token).ConfigureAwait(false);
            if (bytes == null || bytes.Length > 65536) return null;
            try { return new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 20 }.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch (ArgumentException) { return null; }
        }

        public void Dispose()
        {
            lock (_sync) { _disposed = true; _active?.Cancel(); }
        }
    }
}
