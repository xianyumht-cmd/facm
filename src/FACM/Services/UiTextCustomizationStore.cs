using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace FACM.Services
{
    internal sealed class UiTextProfile
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, string> Text { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Replace { get; set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }

    internal static class UiTextCustomizationStore
    {
        private const int MaximumPayload = 180 * 1024;
        private const int MaximumTextLength = 1800;
        private const int MaximumReplaceRules = 80;
        private static readonly Regex Placeholder = new Regex(@"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})",
            RegexOptions.Compiled);

        internal static UiTextProfile Capture()
        {
            var catalog = UiTextCatalog.Load();
            var profile = new UiTextProfile();
            foreach (var entry in UiTextCatalog.DefaultEntries)
            {
                var current = catalog.GetConfiguredValue(entry.Key);
                if (!string.Equals(current, entry.Value, StringComparison.Ordinal))
                    profile.Text[entry.Key] = current;
            }
            foreach (var entry in catalog.ReplacementEntries)
                profile.Replace[entry.Key] = entry.Value;
            return profile;
        }

        internal static string Serialize(UiTextProfile value)
        {
            Validate(value);
            var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumPayload };
            var json = serializer.Serialize(value);
            if (Encoding.UTF8.GetByteCount(json) > MaximumPayload)
                throw new InvalidDataException("文字自定义内容超过云端大小限制。");
            return json;
        }

        internal static UiTextProfile Deserialize(object value)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumPayload };
            var profile = serializer.ConvertToType<UiTextProfile>(value);
            Validate(profile);
            return profile;
        }

        internal static void Validate(UiTextProfile profile)
        {
            if (profile == null || profile.SchemaVersion != 1 || profile.Text == null || profile.Replace == null)
                throw new InvalidDataException("文字自定义数据格式不受支持。");
            var defaults = UiTextCatalog.DefaultEntries.ToDictionary(item => item.Key, item => item.Value,
                StringComparer.OrdinalIgnoreCase);
            if (profile.Text.Count > defaults.Count || profile.Replace.Count > MaximumReplaceRules)
                throw new InvalidDataException("文字配置条目过多。");
            foreach (var pair in profile.Text)
            {
                string original;
                if (pair.Key == null || !defaults.TryGetValue(pair.Key, out original))
                    throw new InvalidDataException("未知文字配置项。");
                ValidateText(pair.Value, original);
            }
            foreach (var pair in profile.Replace)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 200 || pair.Value == null ||
                    pair.Value.Length > MaximumTextLength)
                    throw new InvalidDataException("替换规则不合法。");
                EnsureSafeCharacters(pair.Key);
                EnsureSafeCharacters(pair.Value);
            }
        }

        internal static void ValidateText(string value, string original)
        {
            if (value == null || value.Length > MaximumTextLength)
                throw new InvalidDataException("文字必须为非空内容，且不能超过 1800 字符。");
            EnsureSafeCharacters(value);
            var expected = Placeholder.Matches(original ?? string.Empty).Cast<Match>()
                .Select(match => match.Value).OrderBy(part => part).ToArray();
            var actual = Placeholder.Matches(value).Cast<Match>()
                .Select(match => match.Value).OrderBy(part => part).ToArray();
            if (!expected.SequenceEqual(actual))
                throw new InvalidDataException("请保留原文中的 {0}、{1} 等动态占位符。");
        }

        private static void EnsureSafeCharacters(string value)
        {
            if (value.IndexOf('\0') >= 0 || value.Any(c =>
                char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
                throw new InvalidDataException("文字中包含不支持的控制字符。");
        }

        internal static void Apply(UiTextProfile profile)
        {
            Validate(profile);
            UiTextCatalog.Load();
            ApplyFile(profile, UiTextCatalog.ConfigPath,
                Path.Combine(RuntimePaths.DataDirectory, "ui-text-backups"));
        }

        private static void ApplyFile(UiTextProfile profile, string path, string archive)
        {
            var defaults = UiTextCatalog.DefaultEntries.ToDictionary(x => x.Key, x => x.Value,
                StringComparer.OrdinalIgnoreCase);
            var textValues = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in profile.Text) textValues[pair.Key] = pair.Value;

            var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
            var updated = new List<string>(lines.Count + 40);
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var section = string.Empty;
            foreach (var source in lines)
            {
                var line = source ?? string.Empty;
                var trimmed = line.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) &&
                    trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    updated.Add(line);
                    continue;
                }
                var index = UiTextCatalog.FindUnescapedEquals(line);
                if (index > 0 && section.Equals("Text", StringComparison.OrdinalIgnoreCase))
                {
                    var key = UiTextCatalog.Unescape(line.Substring(0, index).Trim());
                    string value;
                    if (textValues.TryGetValue(key, out value))
                    {
                        present.Add(key);
                        updated.Add(line.Substring(0, index + 1) + UiTextCatalog.Escape(value));
                        continue;
                    }
                }
                if (index > 0 && section.Equals("Replace", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("#", StringComparison.Ordinal) &&
                    !trimmed.StartsWith(";", StringComparison.Ordinal))
                    continue;
                updated.Add(line);
            }

            updated.Add("");
            updated.Add("[Text]");
            foreach (var entry in textValues.Where(x => !present.Contains(x.Key)))
                updated.Add(entry.Key + "=" + UiTextCatalog.Escape(entry.Value));
            updated.Add("");
            updated.Add("[Replace]");
            foreach (var entry in profile.Replace.OrderBy(x => x.Key, StringComparer.Ordinal))
                updated.Add(UiTextCatalog.Escape(entry.Key) + "=" + UiTextCatalog.Escape(entry.Value));

            var text = string.Join(Environment.NewLine, updated) + Environment.NewLine;
            if (Encoding.UTF8.GetByteCount(text) > 512 * 1024)
                throw new InvalidDataException("文字配置文件超过本地大小限制。");

            RuntimePaths.Initialize();
            Directory.CreateDirectory(archive);
            var backup = Path.Combine(archive, "ui-text-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".ini");
            File.Copy(path, backup, false);

            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                File.Replace(temp, path, null);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        internal static void ValidateForSmokeTest()
        {
            var defaults = UiTextCatalog.DefaultEntries;
            if (defaults.Count < 100) throw new InvalidOperationException("UI text catalog unexpectedly small.");
            var entry = defaults.FirstOrDefault(x => x.Value.Contains("{0}"));
            if (string.IsNullOrEmpty(entry.Key))
                throw new InvalidOperationException("No interpolated UI text fixture.");
            ValidateText(entry.Value, entry.Value);
            try
            {
                ValidateText(entry.Value.Replace("{0}", "value"), entry.Value);
                throw new InvalidOperationException("Placeholder validation accepted a missing variable.");
            }
            catch (InvalidDataException) { }
            var plain = defaults.FirstOrDefault(x => !x.Value.Contains("{0}") &&
                !x.Value.Contains("{1}"));
            if (string.IsNullOrEmpty(plain.Key))
                throw new InvalidOperationException("No ordinary UI text fixture.");
            ValidateText(string.Empty, plain.Value);
            var temporary = Path.Combine(Path.GetTempPath(), "ggman-text-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                var path = Path.Combine(temporary, "ui-text.ini");
                var custom = "# keep this comment\n[Text]\n" +
                    entry.Key + "=" + UiTextCatalog.Escape(entry.Value) +
                    "\n\n[Replace]\nGGman=Older Brand\n[Extra]\nUserValue=remain\n";
                File.WriteAllText(path, custom, new UTF8Encoding(false));
                var fixture = new UiTextProfile();
                fixture.Text[entry.Key] = entry.Value;
                fixture.Replace["GGman"] = "个人工作台";
                var backups = Path.Combine(temporary, "backups");
                ApplyFile(fixture, path, backups);
                var saved = File.ReadAllText(path, Encoding.UTF8);
                if (!saved.Contains("# keep this comment") ||
                    !saved.Contains("[Extra]") || !saved.Contains("UserValue=remain") ||
                    !saved.Contains("GGman=个人工作台") ||
                    saved.Contains("GGman=Older Brand") ||
                    Directory.GetFiles(backups, "*.ini").Length != 1)
                    throw new InvalidOperationException("UI wording apply lost legacy lines or backup.");
            }
            finally { try { Directory.Delete(temporary, true); } catch { } }

            var sample = new UiTextProfile();
            sample.Text[entry.Key] = entry.Value;
            sample.Replace["GGman"] = "个人工具";
            var json = Serialize(sample);
            var restored = Deserialize(new JavaScriptSerializer().DeserializeObject(json));
            if (restored.Text.Count != 1 || restored.Replace.Count != 1)
                throw new InvalidOperationException("UI text profile serialization drifted.");
        }
    }
}
