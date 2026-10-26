using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal sealed class LeagueChatMessageEvent
    {
        internal string ConversationId;
        internal string MessageId;
        internal long SenderId;
    }

    internal static class LeagueChatEventStream
    {
        // net48 has no per-ClientWebSocket certificate callback. Keep the self-signed
        // certificate exception on this loopback TLS stream, never on the process.
        internal static async Task RunAsync(LeagueClientSession session,
            Func<LeagueChatMessageEvent, CancellationToken, Task> receive, CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var tcp = new TcpClient())
            using (timeout.Token.Register(() => tcp.Close()))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                await tcp.ConnectAsync(IPAddress.Loopback, session.Port).ConfigureAwait(false);
                using (var tls = new SslStream(tcp.GetStream(), false, (sender, certificate, chain, errors) => certificate != null))
                {
                    await tls.AuthenticateAsClientAsync("127.0.0.1", null, SslProtocols.Tls12, false).ConfigureAwait(false);
                    var nonce = new byte[16];
                    using (var random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
                    var key = Convert.ToBase64String(nonce);
                    var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + session.Password));
                    var request = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: 127.0.0.1:" + session.Port +
                        "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: " + key +
                        "\r\nAuthorization: Basic " + auth + "\r\n\r\n");
                    await tls.WriteAsync(request, 0, request.Length, token).ConfigureAwait(false);
                    var header = new List<byte>();
                    var one = new byte[1];
                    while (header.Count < 8192)
                    {
                        if (await tls.ReadAsync(one, 0, 1, token).ConfigureAwait(false) == 0) throw new IOException("LCU event handshake closed.");
                        header.Add(one[0]);
                        var n = header.Count;
                        if (n >= 4 && header[n - 4] == 13 && header[n - 3] == 10 && header[n - 2] == 13 && header[n - 1] == 10) break;
                    }
                    if (!ValidHandshake(Encoding.ASCII.GetString(header.ToArray()), key)) throw new IOException("LCU event handshake rejected.");
                    timeout.CancelAfter(Timeout.Infinite);
                    using (var socket = WebSocket.CreateClientWebSocket(tls, null, 4096, 4096,
                        TimeSpan.FromSeconds(30), false, WebSocket.CreateClientBuffer(4096, 4096)))
                    {
                        var subscription = Encoding.UTF8.GetBytes("[5,\"OnJsonApiEvent\"]");
                        await socket.SendAsync(new ArraySegment<byte>(subscription), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                        await ReadMessagesAsync(socket, receive, token).ConfigureAwait(false);
                    }
                }
            }
        }

        internal static async Task ReadMessagesAsync(WebSocket socket,
            Func<LeagueChatMessageEvent, CancellationToken, Task> receive, CancellationToken token)
        {
            var buffer = new byte[4096];
            while (!token.IsCancellationRequested)
            {
                using (var message = new MemoryStream())
                {
                    WebSocketReceiveResult part;
                    var ignored = false;
                    do
                    {
                        part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                        if (part.MessageType == WebSocketMessageType.Close) return;
                        if (part.MessageType != WebSocketMessageType.Text || message.Length + part.Count > 65536) ignored = true;
                        if (!ignored) message.Write(buffer, 0, part.Count);
                    } while (!part.EndOfMessage);
                    if (ignored) continue;
                    LeagueChatMessageEvent incoming;
                    if (TryParse(Encoding.UTF8.GetString(message.ToArray()), out incoming))
                        await receive(incoming, token).ConfigureAwait(false);
                }
            }
        }

        internal static bool ValidHandshake(string header, string key)
        {
            if (!header.StartsWith("HTTP/1.1 101 ", StringComparison.Ordinal) || !header.EndsWith("\r\n\r\n", StringComparison.Ordinal)) return false;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in header.Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                var colon = line.IndexOf(':');
                if (colon > 0) fields[line.Substring(0, colon)] = line.Substring(colon + 1).Trim();
            }
            string accept, upgrade, connection;
            string expected;
            using (var sha = SHA1.Create()) expected = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            return fields.TryGetValue("Sec-WebSocket-Accept", out accept) && accept == expected &&
                fields.TryGetValue("Upgrade", out upgrade) && string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase) &&
                fields.TryGetValue("Connection", out connection) && Array.Exists(connection.Split(','), value => value.Trim().Equals("Upgrade", StringComparison.OrdinalIgnoreCase));
        }

        internal static bool TryParse(string json, out LeagueChatMessageEvent message)
        {
            message = null;
            if (string.IsNullOrEmpty(json) || json.Length > 65536) return false;
            try
            {
                var envelope = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 20 }.DeserializeObject(json) as object[];
                if (envelope == null || envelope.Length != 3 || Convert.ToString(envelope[0]) != "8" ||
                    Convert.ToString(envelope[1]) != "OnJsonApiEvent") return false;
                var root = envelope[2] as Dictionary<string, object>;
                if (Text(root, "eventType") != "Create") return false;
                var uri = Text(root, "uri");
                var parts = uri.Split('/');
                if (parts.Length != 7 || parts[1] != "lol-chat" || parts[2] != "v1" || parts[3] != "conversations" ||
                    parts[5] != "messages") return false;
                var conversationId = Uri.UnescapeDataString(parts[4]);
                var messageId = Uri.UnescapeDataString(parts[6]);
                if (!SafeId(conversationId) || !SafeId(messageId)) return false;
                object raw;
                var data = root.TryGetValue("data", out raw) ? raw as Dictionary<string, object> : null;
                long senderId;
                if (Text(data, "type") != "chat" || Text(data, "isHistorical").Equals("True", StringComparison.OrdinalIgnoreCase) ||
                    !long.TryParse(Text(data, "fromSummonerId"), out senderId) || senderId <= 0) return false;
                message = new LeagueChatMessageEvent { ConversationId = conversationId, MessageId = messageId, SenderId = senderId };
                return true;
            }
            catch (Exception) { return false; }
        }

        internal static bool SafeId(string id)
        {
            return id != "." && id != ".." && LeagueMatchmakingWriteApiClient.IsSafeConversationId(id);
        }

        internal static string Text(Dictionary<string, object> root, string key)
        {
            object value;
            return root != null && root.TryGetValue(key, out value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "" : "";
        }
    }
}
