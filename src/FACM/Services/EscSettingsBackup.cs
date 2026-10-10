using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace FACM.Services
{
    internal sealed class EscSettingsFile
    {
        public string Name { get; set; }
        public string Data { get; set; }
        public string Sha256 { get; set; }
    }

    internal sealed class EscSettingsBundle
    {
        public int SchemaVersion { get; set; } = 1;
        public string CreatedAtUtc { get; set; } = string.Empty;
        public List<EscSettingsFile> Files { get; set; } = new List<EscSettingsFile>();
    }

    internal static class EscSettingsBackup
    {
        internal const int MaximumFileBytes = 128 * 1024;
        internal const int MaximumTotalBytes = 192 * 1024;
        private static readonly string[] AllowedFiles =
        {
            "PersistedSettings.json", "game.cfg", "input.ini"
        };
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
        {
            MaxJsonLength = 512 * 1024
        };

        internal static string FindConfigDirectory(string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(selectedPath)) throw new InvalidOperationException("请先选择英雄联盟安装目录。");
            var full = Path.GetFullPath(selectedPath.Trim().Trim('"'));
            var config = string.Equals(Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)), "Config", StringComparison.OrdinalIgnoreCase)
                ? full
                : Path.Combine(full, "Config");
            if (!Directory.Exists(config)) throw new DirectoryNotFoundException("所选目录下未找到英雄联盟 Config 文件夹。");
            if ((File.GetAttributes(config) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("不支持通过目录链接访问游戏配置。");
            return config;
        }

        internal static EscSettingsBundle Capture(string configDirectory)
        {
            var directory = FindConfigDirectory(configDirectory);
            var bundle = new EscSettingsBundle { CreatedAtUtc = DateTimeOffset.UtcNow.ToString("o") };
            var totalBytes = 0;
            foreach (var name in AllowedFiles)
            {
                var path = Path.Combine(directory, name);
                if (!File.Exists(path)) continue;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("配置文件包含链接，已拒绝读取。");
                var length = new FileInfo(path).Length;
                if (length <= 0 || length > MaximumFileBytes)
                    throw new InvalidDataException("配置文件大小不符合要求：" + name);
                totalBytes = checked(totalBytes + (int)length);
                if (totalBytes > MaximumTotalBytes)
                    throw new InvalidDataException("游戏配置超过备份大小限制。");
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length != length) throw new IOException("读取配置时文件发生了变化，请关闭游戏后重试。");
                bundle.Files.Add(new EscSettingsFile
                {
                    Name = name,
                    Data = Convert.ToBase64String(bytes),
                    Sha256 = Hash(bytes)
                });
            }
            if (bundle.Files.Count == 0) throw new FileNotFoundException("没有找到可备份的 ESC 游戏配置。");
            Validate(bundle);
            return bundle;
        }

        internal static string SaveLocal(EscSettingsBundle bundle, string label)
        {
            Validate(bundle);
            var directory = Path.Combine(RuntimePaths.DataDirectory, "esc-backups");
            Directory.CreateDirectory(directory);
            var name = label + "-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json";
            var path = Path.Combine(directory, name);
            var temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, Serializer.Serialize(bundle), new UTF8Encoding(false));
                File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return path;
        }

        internal static EscSettingsBundle ReadLocal(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
                new FileInfo(path).Length > 512 * 1024)
                throw new InvalidDataException("备份文件不存在或超过大小限制。");
            var bundle = Serializer.Deserialize<EscSettingsBundle>(File.ReadAllText(path, Encoding.UTF8));
            Validate(bundle);
            return bundle;
        }

        internal static string Serialize(EscSettingsBundle bundle)
        {
            Validate(bundle);
            var json = Serializer.Serialize(bundle);
            if (json.Length > 400000) throw new InvalidDataException("云端备份数据超过大小限制。");
            return json;
        }

        internal static EscSettingsBundle Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 400000)
                throw new InvalidDataException("云端配置不存在或超过大小限制。");
            var bundle = Serializer.Deserialize<EscSettingsBundle>(json);
            Validate(bundle);
            return bundle;
        }

        internal static void Validate(EscSettingsBundle bundle)
        {
            if (bundle == null || bundle.SchemaVersion != 1 || bundle.Files == null ||
                bundle.Files.Count < 1 || bundle.Files.Count > AllowedFiles.Length)
                throw new InvalidDataException("游戏配置备份的格式不正确。");
            var total = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in bundle.Files)
            {
                if (file == null || !AllowedFiles.Contains(file.Name, StringComparer.OrdinalIgnoreCase) ||
                    !seen.Add(file.Name) || string.IsNullOrWhiteSpace(file.Data))
                    throw new InvalidDataException("游戏配置包含未知或重复文件。");
                if (file.Data.Length > MaximumFileBytes * 4 / 3 + 16)
                    throw new InvalidDataException("游戏配置文件过大。");
                byte[] bytes;
                try { bytes = Convert.FromBase64String(file.Data); }
                catch (FormatException) { throw new InvalidDataException("备份内容损坏。"); }
                if (bytes.Length == 0 || bytes.Length > MaximumFileBytes ||
                    !string.Equals(Hash(bytes), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("游戏配置校验失败：" + file.Name);
                total = checked(total + bytes.Length);
                if (total > MaximumTotalBytes) throw new InvalidDataException("备份内容超过大小限制。");
            }
        }

        internal static string Restore(EscSettingsBundle bundle, string selectedPath)
        {
            Validate(bundle);
            var directory = FindConfigDirectory(selectedPath);
            EscSettingsBundle before = null;
            var previous = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in AllowedFiles)
            {
                var path = Path.Combine(directory, name);
                if (!File.Exists(path)) continue;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("配置文件包含链接，已拒绝恢复。");
                previous[name] = File.ReadAllBytes(path);
            }
            if (previous.Count > 0)
            {
                before = Capture(directory);
                SaveLocal(before, "before-restore");
            }

            var written = new List<string>();
            try
            {
                foreach (var file in bundle.Files)
                {
                    var path = Path.Combine(directory, file.Name);
                    if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("目标配置包含链接，已拒绝覆盖。");
                    var temp = path + ".ggman-" + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllBytes(temp, Convert.FromBase64String(file.Data));
                        written.Add(file.Name);
                        if (File.Exists(path))
                            File.Replace(temp, path, null);
                        else
                            File.Move(temp, path);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
            }
            catch
            {
                foreach (var name in written)
                {
                    try
                    {
                        var path = Path.Combine(directory, name);
                        byte[] old;
                        if (previous.TryGetValue(name, out old)) File.WriteAllBytes(path, old);
                        else if (File.Exists(path)) File.Delete(path);
                    }
                    catch { }
                }
                throw;
            }
            return before == null ? string.Empty : "已在 data\\esc-backups 中保留恢复前备份。";
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
        }

        internal static void ValidateForSmokeTest()
        {
            var root = Path.Combine(Path.GetTempPath(), "GGman-EscBackup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Config"));
            try
            {
                var file = Path.Combine(root, "Config", "input.ini");
                File.WriteAllText(file, "[GameEvents]\nA=1\n");
                var snapshot = Capture(root);
                var serialized = Serialize(snapshot);
                var parsed = Deserialize(serialized);
                File.WriteAllText(file, "[GameEvents]\nA=2\n");
                Restore(parsed, root);
                if (File.ReadAllText(file).IndexOf("A=1", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("ESC settings restore failed.");
                parsed.Files[0].Name = "../input.ini";
                try { Validate(parsed); throw new InvalidOperationException("ESC file allowlist failed."); }
                catch (InvalidDataException) { }
                parsed = Deserialize(serialized);
                parsed.Files[0].Sha256 = "BAD";
                try { Validate(parsed); throw new InvalidOperationException("ESC hash validation failed."); }
                catch (InvalidDataException) { }
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }
    }
}
