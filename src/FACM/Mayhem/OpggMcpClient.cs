using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.Mayhem
{
    internal sealed class OpggMcpClient : IDisposable
    {
        private const string Endpoint = "https://mcp-api.op.gg/mcp";
        private readonly HttpClient _client;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 128
        };
        private string _sessionId;
        private int _requestId;

        public OpggMcpClient()
        {
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(18) };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM/3.1 (+https://github.com/xianyumht-cmd/facm)");
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }

        public async Task<IReadOnlyList<Dictionary<string, object>>> ListToolsAsync(CancellationToken cancellationToken)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            var response = await SendRequestAsync("tools/list", new Dictionary<string, object>(), cancellationToken).ConfigureAwait(false);
            var result = GetDictionary(response, "result");
            var tools = result == null ? null : GetList(result, "tools");
            if (tools == null) return new List<Dictionary<string, object>>();

            var output = new List<Dictionary<string, object>>();
            foreach (var item in tools)
            {
                var tool = item as Dictionary<string, object>;
                if (tool != null) output.Add(tool);
            }
            return output;
        }

        public async Task<string> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            var parameters = new Dictionary<string, object>
            {
                { "name", toolName },
                { "arguments", arguments ?? new Dictionary<string, object>() }
            };

            var response = await SendRequestAsync("tools/call", parameters, cancellationToken).ConfigureAwait(false);
            var result = GetDictionary(response, "result");
            if (result == null) return string.Empty;

            var content = GetList(result, "content");
            if (content == null) return _json.Serialize(result);

            var parts = new List<string>();
            foreach (var item in content)
            {
                var block = item as Dictionary<string, object>;
                if (block == null) continue;

                object text;
                if (block.TryGetValue("text", out text) && text != null)
                {
                    parts.Add(Convert.ToString(text));
                    continue;
                }

                object resource;
                if (block.TryGetValue("resource", out resource) && resource != null)
                    parts.Add(_json.Serialize(resource));
            }
            return string.Join(Environment.NewLine, parts);
        }

        public static Dictionary<string, object> BuildArguments(Dictionary<string, object> tool, string championInput, bool leaderboard)
        {
            var arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var schema = GetDictionary(tool, "inputSchema");
            var properties = schema == null ? null : GetDictionary(schema, "properties");
            var required = schema == null ? null : GetList(schema, "required");

            if (properties != null)
            {
                foreach (var pair in properties)
                {
                    var key = pair.Key;
                    var lower = key.ToLowerInvariant();
                    if (lower.Contains("desired_output"))
                    {
                        arguments[key] = leaderboard
                            ? new[]
                            {
                                "data.champions[].{rank,name,key,id,tier,win_rate,pick_rate}",
                                "champions[].{rank,name,key,id,tier,win_rate,pick_rate}",
                                "data.{version,patch}"
                            }
                            : new[]
                            {
                                "data.champion.{name,key,id}",
                                "data.{version,patch,tier,rank,win_rate,pick_rate}",
                                "data.items",
                                "data.skills",
                                "data.balance",
                                "data.aram_balance",
                                "data.augments"
                            };
                    }
                    else if (lower.Contains("champion") && !leaderboard) arguments[key] = championInput;
                    else if (lower == "mode" || lower.Contains("game_mode") || lower.Contains("queue")) arguments[key] = "aram-mayhem";
                    else if (lower.Contains("region")) arguments[key] = "global";
                    else if (lower.Contains("locale") || lower.Contains("language")) arguments[key] = "zh_CN";
                    else if (lower.Contains("tier")) arguments[key] = "all";
                    else if (lower.Contains("position") || lower.Contains("role")) arguments[key] = "all";
                    else if (lower.Contains("limit") || lower.Contains("count") || lower.Contains("size")) arguments[key] = leaderboard ? 10 : 5;
                    else if (lower.Contains("page")) arguments[key] = 1;
                }
            }

            if (required != null)
            {
                foreach (var item in required)
                {
                    var key = Convert.ToString(item);
                    if (string.IsNullOrWhiteSpace(key) || arguments.ContainsKey(key)) continue;
                    var lower = key.ToLowerInvariant();
                    if (lower.Contains("champion")) arguments[key] = championInput;
                    else if (lower.Contains("mode") || lower.Contains("queue")) arguments[key] = "aram-mayhem";
                    else if (lower.Contains("region")) arguments[key] = "global";
                    else if (lower.Contains("locale") || lower.Contains("language")) arguments[key] = "zh_CN";
                    else if (lower.Contains("tier")) arguments[key] = "all";
                    else if (lower.Contains("limit") || lower.Contains("count")) arguments[key] = leaderboard ? 10 : 5;
                    else arguments[key] = string.Empty;
                }
            }
            return arguments;
        }

        public static Dictionary<string, object> FindTool(IEnumerable<Dictionary<string, object>> tools, string preferredName)
        {
            if (tools == null) return null;
            foreach (var tool in tools)
            {
                object name;
                if (!tool.TryGetValue("name", out name) || name == null) continue;
                if (string.Equals(Convert.ToString(name), preferredName, StringComparison.OrdinalIgnoreCase)) return tool;
            }
            return null;
        }

        private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(_sessionId)) return;
            var initializeParams = new Dictionary<string, object>
            {
                { "protocolVersion", "2025-03-26" },
                { "capabilities", new Dictionary<string, object>() },
                {
                    "clientInfo",
                    new Dictionary<string, object>
                    {
                        { "name", "FACM" },
                        { "version", "3.1" }
                    }
                }
            };
            await SendRequestAsync("initialize", initializeParams, cancellationToken).ConfigureAwait(false);
            await SendNotificationAsync("notifications/initialized", new Dictionary<string, object>(), cancellationToken).ConfigureAwait(false);
        }

        private async Task<Dictionary<string, object>> SendRequestAsync(string method, Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            var request = new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" },
                { "id", ++_requestId },
                { "method", method },
                { "params", parameters ?? new Dictionary<string, object>() }
            };
            var body = await PostAsync(_json.Serialize(request), cancellationToken).ConfigureAwait(false);
            var jsonBody = ExtractJson(body);
            if (string.IsNullOrWhiteSpace(jsonBody)) return new Dictionary<string, object>();

            var parsed = _json.DeserializeObject(jsonBody) as Dictionary<string, object>;
            if (parsed == null) return new Dictionary<string, object>();
            var error = GetDictionary(parsed, "error");
            if (error != null)
            {
                object message;
                error.TryGetValue("message", out message);
                throw new InvalidOperationException("OP.GG MCP: " + Convert.ToString(message));
            }
            return parsed;
        }

        private async Task SendNotificationAsync(string method, Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            var request = new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" },
                { "method", method },
                { "params", parameters ?? new Dictionary<string, object>() }
            };
            await PostAsync(_json.Serialize(request), cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> PostAsync(string json, CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, Endpoint))
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(_sessionId)) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);

                using (var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    IEnumerable<string> sessionValues;
                    if (response.Headers.TryGetValues("Mcp-Session-Id", out sessionValues)) _sessionId = sessionValues.FirstOrDefault();
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException(
                            "OP.GG MCP HTTP " + (int)response.StatusCode + ": " +
                            (body.Length > 300 ? body.Substring(0, 300) : body));
                    }
                    return body;
                }
            }
        }

        private static string ExtractJson(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;
            var trimmed = body.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal)) return trimmed;
            var lines = trimmed.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                var value = line.Substring(5).Trim();
                if (value.StartsWith("{", StringComparison.Ordinal)) return value;
            }
            return string.Empty;
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> source, string key)
        {
            if (source == null) return null;
            object value;
            return source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static object[] GetList(Dictionary<string, object> source, string key)
        {
            if (source == null) return null;
            object value;
            if (!source.TryGetValue(key, out value) || value == null) return null;
            var array = value as object[];
            if (array != null) return array;
            var list = value as ArrayList;
            return list == null ? null : list.ToArray();
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
