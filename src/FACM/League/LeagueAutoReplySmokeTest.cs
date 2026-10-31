using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueAutoReplySmokeTest
    {
        internal static void Validate()
        {
            var settings = new AppSettings();
            Require(!settings.LeagueAutoReplyEnabled && !settings.LeagueAutoReplyAwayOnly, "Reply defaults changed.");
            settings.LeagueAutoReplyEnabled = true;
            settings.LeagueAutoReplyAwayOnly = true;
            settings.LeagueAutoReplyText = "稍后回复=ok";
            var restored = AppSettings.ParseLines(settings.BuildLinesForSmokeTest());
            Require(restored.LeagueAutoReplyEnabled && restored.LeagueAutoReplyAwayOnly && restored.LeagueAutoReplyText == settings.LeagueAutoReplyText,
                "Reply INI roundtrip failed.");
            var cloud = CloudSettingsSnapshot.Capture(settings);
            var cloudRestored = new AppSettings();
            cloud.ApplyTo(cloudRestored);
            Require(cloudRestored.LeagueAutoReplyText == settings.LeagueAutoReplyText && cloudRestored.LeagueAutoReplyEnabled,
                "Reply cloud roundtrip failed.");
            Require(LeagueAutoReplyController.NormalizeText("bad\ntext") == "" &&
                LeagueAutoReplyController.NormalizeText(new string('a', 501)) == "", "Invalid reply text accepted.");

            var frame = "[8,\"OnJsonApiEvent\",{\"eventType\":\"Create\",\"uri\":\"/lol-chat/v1/conversations/friend@chat/messages/123\",\"data\":{\"type\":\"chat\",\"fromSummonerId\":22,\"isHistorical\":false}}]";
            LeagueChatMessageEvent message;
            Require(LeagueChatEventStream.TryParse(frame, out message) && message.SenderId == 22, "LCU message envelope rejected.");
            Require(LeagueChatEventStream.TryParse(frame.Replace("friend@chat", "friend%40chat"), out message) &&
                message.ConversationId == "friend@chat", "Encoded conversation segment failed.");
            Require(LeagueChatEventStream.TryParse("[8,\"OnJsonApiEvent\",{\"eventType\":\"Update\",\"uri\":\"/lol-chat/v1/me\",\"data\":{\"availability\":\"dnd\"}}]", out message) &&
                message.Availability == "dnd", "Presence events must retain intervening status transitions.");
            Require(LeagueChatEventStream.TryParse("[8,\"OnJsonApiEvent\",{\"eventType\":\"Delete\",\"uri\":\"/lol-chat/v1/me\",\"data\":null}]", out message) &&
                message.Availability == "", "Deleted presence must clear its baseline.");
            foreach (var bad in new[] { frame.Replace("Create", "Update"), frame.Replace("Create", "Delete"),
                frame.Replace("false", "true"), frame.Replace("\"chat\"", "\"celebration\""),
                frame.Replace("friend@chat", ".."), frame.Replace("friend@chat", "x%2fy"), frame.Replace("22", "0"),
                frame.Replace("[8,", "[5,"), "{}", "[", new string('x', 65537) })
                Require(!LeagueChatEventStream.TryParse(bad, out message), "Unsafe LCU event accepted.");
            const string handshake = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=\r\n\r\n";
            Require(LeagueChatEventStream.ValidHandshake(handshake, "dGhlIHNhbXBsZSBub25jZQ==") &&
                !LeagueChatEventStream.ValidHandshake(handshake, "wrong") &&
                !LeagueChatEventStream.ValidHandshake(handshake.Replace("101", "200"), "dGhlIHNhbXBsZSBub25jZQ=="), "WebSocket upgrade validation failed.");
            ValidateReplyAsync().GetAwaiter().GetResult();
            ValidateFramesAsync(frame).GetAwaiter().GetResult();
        }

        private static async Task ValidateFramesAsync(string frame)
        {
            using (var wire = new MemoryStream())
            {
                WriteFrame(wire, 0x81, new byte[65537]);
                var bytes = Encoding.UTF8.GetBytes(frame);
                var split = bytes.Length / 2;
                var first = new byte[split];
                var second = new byte[bytes.Length - split];
                Array.Copy(bytes, 0, first, 0, first.Length);
                Array.Copy(bytes, split, second, 0, second.Length);
                WriteFrame(wire, 0x01, first);
                WriteFrame(wire, 0x80, second);
                WriteFrame(wire, 0x88, new byte[] { 3, 232 });
                using (var duplex = new FakeWire(wire.ToArray()))
                using (var socket = WebSocket.CreateClientWebSocket(duplex, null, 4096, 4096,
                    TimeSpan.Zero, false, WebSocket.CreateClientBuffer(4096, 4096)))
                {
                    var received = 0;
                    await LeagueChatEventStream.ReadMessagesAsync(socket, (message, token) =>
                    {
                        Require(message.ConversationId == "friend@chat", "Fragmented target changed.");
                        received++;
                        return Task.CompletedTask;
                    }, CancellationToken.None).ConfigureAwait(false);
                    Require(received == 1, "Oversized unrelated event or fragmentation broke dispatch.");
                }
            }
        }

        private static void WriteFrame(Stream stream, byte opcode, byte[] bytes)
        {
            stream.WriteByte(opcode);
            if (bytes.Length < 126) stream.WriteByte((byte)bytes.Length);
            else if (bytes.Length <= 65535)
            {
                stream.WriteByte(126); stream.WriteByte((byte)(bytes.Length >> 8)); stream.WriteByte((byte)bytes.Length);
            }
            else
            {
                stream.WriteByte(127);
                for (var shift = 56; shift >= 0; shift -= 8) stream.WriteByte((byte)((long)bytes.Length >> shift));
            }
            stream.Write(bytes, 0, bytes.Length);
        }

        private sealed class FakeWire : Stream
        {
            private readonly MemoryStream _input;
            internal FakeWire(byte[] bytes) { _input = new MemoryStream(bytes); }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) { return _input.Read(buffer, offset, Math.Min(count, 317)); }
            public override void Write(byte[] buffer, int offset, int count) { }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing) { if (disposing) _input.Dispose(); base.Dispose(disposing); }
        }

        private static async Task ValidateReplyAsync()
        {
            var api = new FakeApi();
            var session = new LeagueClientSession("fixture", 1, 12345, "fixture-only", "https", "smoke");
            var currentSession = session;
            var discoveries = 0;
            using (var controller = new LeagueAutoReplyController(api, api, () => { discoveries++; return currentSession; }))
            {
                controller.Observe(new LeagueDashboardPhaseState { Connected = true });
                Require(discoveries == 0 && api.Reads == 0, "Disabled reply started discovery or reads.");
                var incoming = new LeagueChatMessageEvent { ConversationId = "friend@chat", MessageId = "1", SenderId = 22 };
                await controller.ProcessAsync(session, incoming, "reply", true, CancellationToken.None).ConfigureAwait(false);
                await controller.ProcessAsync(session, incoming, "reply", true, CancellationToken.None).ConfigureAwait(false);
                Require(api.Writes == 1 && api.Text == "reply", "Incoming message must submit exactly once.");
                api.Status = 500;
                incoming.MessageId = "failed";
                await controller.ProcessAsync(session, incoming, "reply", true, CancellationToken.None).ConfigureAwait(false);
                await controller.ProcessAsync(session, incoming, "reply", true, CancellationToken.None).ConfigureAwait(false);
                Require(api.Writes == 2, "Uncertain write was retried.");
                api.Availability = "chat";
                incoming.MessageId = "2";
                await controller.ProcessAsync(session, incoming, "reply", true, CancellationToken.None).ConfigureAwait(false);
                Require(api.Writes == 2, "Away-only replied while online.");
                incoming.MessageId = "3";
                await controller.ProcessAsync(session, incoming, "reply", false, CancellationToken.None).ConfigureAwait(false);
                Require(api.Writes == 3, "Unrestricted reply incorrectly needs away status.");
                incoming.SenderId = 11; incoming.MessageId = "4";
                await controller.ProcessAsync(session, incoming, "reply", false, CancellationToken.None).ConfigureAwait(false);
                Require(api.Writes == 3, "Self echo caused reply loop.");
                incoming.SenderId = 22; incoming.MessageId = "5"; api.Type = "championSelect";
                await controller.ProcessAsync(session, incoming, "reply", false, CancellationToken.None).ConfigureAwait(false);
                Require(api.Writes == 3, "Group conversation received private reply.");
                api.Type = "chat"; api.OnRead = path => { if (path.Contains("conversations")) api.Self = 99; };
                incoming.MessageId = "6";
                await controller.ProcessAsync(session, incoming, "reply", false, CancellationToken.None).ConfigureAwait(false);
                Require(api.Writes == 3, "Changed account received stale reply.");
                api.Self = 11; api.OnRead = path => { currentSession = new LeagueClientSession("fixture", 2, 12346, "replacement", "https", "smoke"); };
                incoming.MessageId = "7";
                await controller.ProcessAsync(session, incoming, "reply", false, CancellationToken.None).ConfigureAwait(false);
                Require(api.Writes == 3, "Changed authentication received stale reply.");
                currentSession = session;
                using (var cancellation = new CancellationTokenSource())
                {
                    api.OnRead = path => cancellation.Cancel(); incoming.MessageId = "8";
                    try { await controller.ProcessAsync(session, incoming, "reply", false, cancellation.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    Require(api.Writes == 3, "Cancelled message wrote a reply.");
                }
            }
        }

        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        private sealed class FakeApi : ILeagueClientApi, ILeaguePrivateChatWriteApi
        {
            internal int Reads, Writes, Status = 200;
            internal long Self = 11;
            internal string Availability = "away", Type = "chat", Text;
            internal Action<string> OnRead;
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); Reads++;
                var json = path.Contains("current-summoner") ? "{\"summonerId\":" + Self + "}" :
                    path.EndsWith("/me") ? "{\"availability\":\"" + Availability + "\"}" :
                    "{\"id\":\"friend@chat\",\"type\":\"" + Type + "\"}";
                OnRead?.Invoke(path);
                return Task.FromResult(Encoding.UTF8.GetBytes(json));
            }
            public Task<LeagueClientWriteResponse> TryReplyAsync(LeagueClientSession session, string id, string text, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); Writes++; Text = text;
                Require(id == "friend@chat", "Reply target changed.");
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = Status });
            }
        }
    }
}
