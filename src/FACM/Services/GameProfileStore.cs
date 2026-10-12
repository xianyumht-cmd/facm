using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace FACM.Services
{
    internal sealed class GameLobbySetting
    {
        public string File { get; set; }
        public string Section { get; set; }
        public string Key { get; set; }
        public bool Value { get; set; }
    }

    internal sealed class GameProfile
    {
        public int SchemaVersion { get; set; } = 1;
        public EscSettingsBundle Esc { get; set; }
        public List<GameLobbySetting> Lobby { get; set; } = new List<GameLobbySetting>();
    }

    internal static class GameProfileStore
    {
        private const int MaximumYamlBytes = 256 * 1024;
        private static readonly JavaScriptSerializer Json =
            new JavaScriptSerializer { MaxJsonLength = 600000 };

        // These are individual portable booleans confirmed by the local client YAML samples.
        // Installation, identity, microphone-device and rune-page fields are never copied.
        private static readonly string[] Allowed =
        {
            "LCUAccountPreferences.yaml|lol-notifications|blockNonFriendGameInvites",
            "LCUAccountPreferences.yaml|lol-premade-voice|autoJoin",
            "LCUAccountPreferences.yaml|lol-premade-voice|autoJoinTeamVoice",
            "LCUAccountPreferences.yaml|lol-premade-voice|muteOnConnect",
            "LCUAccountPreferences.yaml|lol-perks|playerFlashOnFPreference",
            "LCULocalPreferences.yaml|lol-user-experience|abilityPreviewsDisabled",
            "LCULocalPreferences.yaml|lol-user-experience|closeLeagueClientDuringGame",
            "LCULocalPreferences.yaml|lol-user-experience|motionEffectsDisabled",
            "LCULocalPreferences.yaml|lol-audio|masterSoundEnabled"
        };

        internal static string FindLobbyConfig(string escDirectory)
        {
            var gameConfig = new DirectoryInfo(EscSettingsBackup.FindConfigDirectory(escDirectory));
            var game = gameConfig.Parent;
            if (game == null || !game.Name.Equals("Game", StringComparison.OrdinalIgnoreCase) ||
                game.Parent == null)
                throw new DirectoryNotFoundException("无法从游戏配置目录确认英雄联盟安装根目录。");
            var client = Path.Combine(game.Parent.FullName, "LeagueClient", "Config");
            if (!Directory.Exists(client) ||
                (File.GetAttributes(client) & FileAttributes.ReparsePoint) != 0)
                throw new DirectoryNotFoundException("未发现安全的大厅 YAML 配置目录。");
            return client;
        }

        internal static GameProfile Capture(string escDirectory)
        {
            var client = FindLobbyConfig(escDirectory);
            var result = new GameProfile { Esc = EscSettingsBackup.Capture(escDirectory) };
            foreach (var file in Allowed.Select(k => k.Split('|')[0]).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var path = Path.Combine(client, file);
                if (!File.Exists(path))
                    throw new FileNotFoundException("缺少大厅配置文件：" + file);
                var values = ReadAllowed(path, file);
                result.Lobby.AddRange(values);
            }
            if (!result.Lobby.Any(x => x.Section == "lol-notifications" &&
                x.Key == "blockNonFriendGameInvites"))
                throw new InvalidDataException("缺少好友邀请配置，已取消上传。");
            Validate(result);
            return result;
        }

        private static List<GameLobbySetting> ReadAllowed(string path, string file)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(path).Length > MaximumYamlBytes)
                throw new InvalidDataException("大厅配置大小或路径不安全。");
            var values = new List<GameLobbySetting>();
            var sections = new HashSet<string>(StringComparer.Ordinal);
            var leaves = new HashSet<string>(StringComparer.Ordinal);
            var section = string.Empty;
            var inData = false;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (raw.Length == 0 || raw.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                var indent = raw.Length - raw.TrimStart(' ').Length;
                if (raw.IndexOf('\t') >= 0) throw new InvalidDataException("大厅 YAML 包含不支持的制表符。");
                var line = raw.Trim();
                var colon = line.IndexOf(':');
                if (colon < 0) continue;
                var key = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim();
                if (indent == 0)
                {
                    section = key;
                    if (!sections.Add(section))
                        throw new InvalidDataException("大厅 YAML 存在重复配置段。");
                    inData = false;
                }
                else if (indent == 4)
                    inData = key == "data" && value.Length == 0;
                else if (indent == 8 && inData)
                {
                    var id = file + "|" + section + "|" + key;
                    if (!Allowed.Contains(id, StringComparer.Ordinal)) continue;
                    if (!leaves.Add(id))
                        throw new InvalidDataException("大厅 YAML 存在重复的个人偏好。");
                    bool flag;
                    if (!bool.TryParse(value, out flag))
                        throw new InvalidDataException("大厅偏好不是布尔值：" + key);
                    values.Add(new GameLobbySetting { File = file, Section = section, Key = key, Value = flag });
                }
            }
            return values;
        }

        internal static void Validate(GameProfile value)
        {
            if (value == null || value.SchemaVersion != 1 || value.Esc == null ||
                value.Lobby == null || value.Lobby.Count < 1 || value.Lobby.Count > Allowed.Length)
                throw new InvalidDataException("游戏配置快照不完整。");
            EscSettingsBackup.Validate(value.Esc);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setting in value.Lobby)
            {
                if (setting == null || setting.File == null || setting.Section == null ||
                    setting.Key == null)
                    throw new InvalidDataException("大厅偏好字段无效。");
                var id = setting.File + "|" + setting.Section + "|" + setting.Key;
                if (!Allowed.Contains(id, StringComparer.Ordinal) || !seen.Add(id))
                    throw new InvalidDataException("大厅偏好包含未授权或重复字段。");
            }
            if (!seen.Contains("LCUAccountPreferences.yaml|lol-notifications|blockNonFriendGameInvites"))
                throw new InvalidDataException("大厅配置缺少好友邀请设置。");
        }

        internal static string Serialize(GameProfile value)
        {
            Validate(value);
            var text = Json.Serialize(value);
            if (Encoding.UTF8.GetByteCount(text) > 510000)
                throw new InvalidDataException("游戏配置超过云端大小限制。");
            return text;
        }

        internal static GameProfile Deserialize(object value)
        {
            var profile = Json.ConvertToType<GameProfile>(value);
            Validate(profile);
            return profile;
        }

        internal static string Fingerprint(GameProfile profile)
        {
            Validate(profile);
            var esc = string.Join("|", profile.Esc.Files.OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => x.Name + ":" + x.Sha256));
            var lobby = string.Join("|", profile.Lobby.OrderBy(x => x.File, StringComparer.Ordinal)
                .ThenBy(x => x.Section, StringComparer.Ordinal).ThenBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => x.File + ":" + x.Section + ":" + x.Key + "=" + x.Value));
            return esc + "|" + lobby;
        }

        internal static bool AreLeagueProcessesAbsent()
        {
            foreach (var name in new[] { "League of Legends", "LeagueClient", "LeagueClientUx", "LeagueClientUxRender" })
            {
                System.Diagnostics.Process[] processes = null;
                try
                {
                    processes = System.Diagnostics.Process.GetProcessesByName(name);
                    if (processes.Length != 0) return false;
                }
                catch { return false; }
                finally
                {
                    if (processes != null)
                        foreach (var process in processes) process.Dispose();
                }
            }
            return true;
        }

        internal static void Restore(GameProfile profile, string escDirectory)
        {
            Validate(profile);
            if (!AreLeagueProcessesAbsent())
                throw new InvalidOperationException("请完全退出英雄联盟及大厅客户端后恢复游戏配置。");
            var client = FindLobbyConfig(escDirectory);
            var staged = new List<Tuple<string, string, string>>();
            foreach (var group in profile.Lobby.GroupBy(x => x.File, StringComparer.OrdinalIgnoreCase))
            {
                var path = Path.Combine(client, group.Key);
                if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                    new FileInfo(path).Length > MaximumYamlBytes)
                    throw new InvalidDataException("目标大厅 YAML 文件缺失或不安全：" + group.Key);
                var oldText = File.ReadAllText(path, Encoding.UTF8);
                var patched = oldText;
                foreach (var item in group)
                {
                    var lines = patched.Replace("\r\n", "\n").Split('\n');
                    var section = string.Empty;
                    var inData = false;
                    var count = 0;
                    for (var i = 0; i < lines.Length; i++)
                    {
                        var raw = lines[i];
                        var indent = raw.Length - raw.TrimStart(' ').Length;
                        var line = raw.Trim();
                        var colon = line.IndexOf(':');
                        if (colon < 0) continue;
                        var key = line.Substring(0, colon).Trim();
                        if (indent == 0) { section = key; inData = false; }
                        else if (indent == 4) inData = key == "data";
                        else if (indent == 8 && inData && section == item.Section && key == item.Key)
                        {
                            count++;
                            lines[i] = raw.Substring(0, colon + 1) + " " +
                                (item.Value ? "true" : "false");
                        }
                    }
                    if (count != 1)
                        throw new InvalidDataException("目标客户端缺少或重复偏好：" + item.Key);
                    patched = string.Join("\n", lines);
                }
                if (!string.Equals(oldText, patched, StringComparison.Ordinal))
                    staged.Add(Tuple.Create(path, oldText, patched));
            }
            if (staged.Count == 0 && Fingerprint(Capture(escDirectory)) == Fingerprint(profile))
                return;
            Directory.CreateDirectory(Path.Combine(RuntimePaths.DataDirectory, "lobby-backups"));
            var changed = new List<Tuple<string, string, string>>();
            try
            {
                if (!AreLeagueProcessesAbsent())
                    throw new InvalidOperationException("游戏客户端已启动，已推迟配置恢复。");
                foreach (var file in staged)
                {
                    var backup = Path.Combine(RuntimePaths.DataDirectory, "lobby-backups",
                        Path.GetFileName(file.Item1) + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") +
                        "-" + Guid.NewGuid().ToString("N") + ".yaml");
                    File.WriteAllText(backup, file.Item2, new UTF8Encoding(false));
                    var tmp = file.Item1 + ".ggman-" + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllText(tmp, file.Item3, new UTF8Encoding(false));
                        File.Replace(tmp, file.Item1, null);
                        changed.Add(file);
                    }
                    finally { if (File.Exists(tmp)) File.Delete(tmp); }
                }
                EscSettingsBackup.Restore(profile.Esc, escDirectory);
                if (!string.Equals(Fingerprint(Capture(escDirectory)), Fingerprint(profile),
                    StringComparison.Ordinal))
                    throw new IOException("游戏配置写回校验失败。");
            }
            catch
            {
                foreach (var file in changed)
                {
                    try { File.WriteAllText(file.Item1, file.Item2, new UTF8Encoding(false)); }
                    catch { }
                }
                throw;
            }
        }

        internal static void ValidateForSmokeTest()
        {
            var root = Path.Combine(Path.GetTempPath(), "GGman-Lobby-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, "LCUAccountPreferences.yaml");
                File.WriteAllText(path, "lol-notifications:\n    data:\n        blockNonFriendGameInvites: true\n" +
                    "event-hub:\n    data:\n        playerSettingsDataMap:\n            private: true\n");
                var allowed = ReadAllowed(path, "LCUAccountPreferences.yaml");
                if (allowed.Count != 1 || !allowed[0].Value || allowed[0].Key != "blockNonFriendGameInvites")
                    throw new InvalidOperationException("Lobby field allowlist failed.");
            }
            finally { Directory.Delete(root, true); }
        }
    }
}