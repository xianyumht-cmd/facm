using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.Online
{
    // Opt-in single-owner migration candidate. The recovery key is never embedded in the executable.
    internal static class GgmanD1Backend
    {
        internal const string OwnerId = "single-owner-primary";
        private const int MaxResponseBytes = 640 * 1024;
        private static readonly AsyncLocal<bool> ExplicitBootstrap = new AsyncLocal<bool>();

        internal static IDisposable BeginExplicitBootstrap()
        {
            var before = ExplicitBootstrap.Value;
            ExplicitBootstrap.Value = true;
            return new BootstrapScope(before);
        }

        private sealed class BootstrapScope : IDisposable
        {
            private readonly bool _before;
            internal BootstrapScope(bool before) { _before = before; }
            public void Dispose() { ExplicitBootstrap.Value = _before; }
        }
        private static readonly JavaScriptSerializer Json =
            new JavaScriptSerializer { MaxJsonLength = MaxResponseBytes };

        internal static bool IsEnabled
        {
            get { return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GGMAN_D1_ENDPOINT")); }
        }

        private static Uri Endpoint()
        {
            var value = Environment.GetEnvironmentVariable("GGMAN_D1_ENDPOINT");
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                uri.AbsolutePath != "/")
                throw new InvalidOperationException("新云端地址必须为 HTTPS 站点根地址。");
            return uri;
        }

        private static bool IsKey(string key)
        {
            if (key == null || key.Length != 43) return false;
            foreach (char c in key)
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return false;
            return true;
        }

        internal static async Task<GgmanAccountIdentity> SignInAsync(string key, CancellationToken cancel)
        {
            if (!IsKey(key)) throw new InvalidDataException("请输入新云端生成的 43 位恢复密钥。");
            var answer = await RequestAsync(HttpMethod.Get, "api/v1/whoami", key, null, cancel)
                .ConfigureAwait(false);
            if (!string.Equals(Convert.ToString(answer["subject"]), OwnerId, StringComparison.Ordinal))
                throw new InvalidDataException("新云端身份不匹配。");
            return new GgmanAccountIdentity
            {
                Email = "新云端个人账号",
                UserId = OwnerId,
                AccessToken = key
            };
        }

        internal static async Task<Dictionary<string, object>> InvokeAsync(
            string operation, GgmanAccountIdentity identity, object body, CancellationToken cancel)
        {
            if (!IsEnabled || identity == null || identity.UserId != OwnerId ||
                !IsKey(identity.AccessToken) || !GgmanAccountSession.IsCurrent(identity))
                throw new InvalidOperationException("新云端未登录或账号已切换。");

            string category;
            bool write;
            switch (operation)
            {
                case "ggman_get_registered_app_settings": category = "app_settings"; write = false; break;
                case "ggman_set_registered_app_settings": category = "app_settings"; write = true; break;
                case "ggman_get_ui_text_profile": category = "ui_text"; write = false; break;
                case "ggman_set_ui_text_profile": category = "ui_text"; write = true; break;
                case "ggman_get_esc_profile": category = "esc_profile"; write = false; break;
                case "ggman_set_esc_profile": category = "esc_profile"; write = true; break;
                case "ggman_get_game_profile": category = "game_profile"; write = false; break;
                case "ggman_set_game_profile": category = "game_profile"; write = true; break;
                default: throw new ArgumentException("Unknown single-owner cloud operation.");
            }

            object data = null;
            if (write)
            {
                var map = body as Dictionary<string, object>;
                if (map == null || !map.ContainsKey("p_payload") || !map.ContainsKey("p_expected_version"))
                    throw new InvalidDataException("写入内容缺少版本信息。");
                long revision;
                if (!long.TryParse(Convert.ToString(map["p_expected_version"]), out revision) || revision < 0)
                    throw new InvalidDataException("新云端版本号无效。");
                bool manualGameUpload = category == "esc_profile" || category == "game_profile";
                bool bootstrap = revision == 0 && (manualGameUpload || ExplicitBootstrap.Value);
                if (revision == 0 && !bootstrap)
                    throw new InvalidOperationException("新云端没有配置。请在界面中确认首次上传。");
                data = new Dictionary<string, object>
                {
                    { "payload", map["p_payload"] },
                    { "expectedVersion", revision },
                    { "explicitBootstrap", bootstrap }
                };
            }

            var response = await RequestAsync(write ? HttpMethod.Put : HttpMethod.Get,
                "api/v1/documents/" + category, identity.AccessToken, data, cancel).ConfigureAwait(false);
            GgmanAccountSession.RequireCurrent(identity);
            if (write)
                return new Dictionary<string, object> { { "version", response["remoteVersion"] } };

            if (!response.ContainsKey("exists") ||
                !string.Equals(Convert.ToString(response["exists"]), "True", StringComparison.OrdinalIgnoreCase))
                return new Dictionary<string, object>();

            return new Dictionary<string, object>
            {
                { "payload", response["payload"] },
                { "version", response["remoteVersion"] },
                { "updated_at", response["updatedAt"] }
            };
        }

        // Only the user-initiated "create cloud backup" action may call this.
        internal static Task<Dictionary<string, object>> CreateFirstAsync(
            string category, object payload, GgmanAccountIdentity identity, CancellationToken token)
        {
            if (identity == null || identity.UserId != OwnerId || !GgmanAccountSession.IsCurrent(identity))
                throw new InvalidOperationException("新云端身份已失效。");
            return RequestAsync(HttpMethod.Put, "api/v1/documents/" + category,
                identity.AccessToken, new Dictionary<string, object>
                {
                    { "payload", payload }, { "expectedVersion", 0 }, { "explicitBootstrap", true }
                }, token);
        }

        private static async Task<Dictionary<string, object>> RequestAsync(
            HttpMethod method, string path, string key, object data, CancellationToken token)
        {
            if (!IsKey(key)) throw new InvalidDataException("无效的个人云端密钥。");
            using (var http = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false, UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            }))
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var request = new HttpRequestMessage(method, new Uri(Endpoint(), path)))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                if (data != null)
                    request.Content = new StringContent(Json.Serialize(data), Encoding.UTF8, "application/json");

                using (var response = await http.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new InvalidOperationException("新云端密钥不正确或已被更换。");
                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == HttpStatusCode.Conflict)
                            throw new InvalidOperationException("云端版本已更新，请先重新读取后再操作。");
                        throw new InvalidOperationException("新云端请求失败（HTTP " +
                            (int)response.StatusCode + "）。");
                    }
                    if (response.Content.Headers.ContentLength > MaxResponseBytes)
                        throw new InvalidDataException("新云端响应超出限制。");
                    var raw = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (raw.Length > MaxResponseBytes)
                        throw new InvalidDataException("新云端响应超出限制。");
                    var value = Json.DeserializeObject(Encoding.UTF8.GetString(raw))
                        as Dictionary<string, object>;
                    if (value == null) throw new InvalidDataException("新云端响应格式不正确。");
                    return value;
                }
            }
        }
    }
}
