using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace FACM.Services
{
    internal sealed class UiTextCatalog
    {
        private static readonly string LegacyConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FACM",
            "ui-text.ini");

        private static readonly KeyValuePair<string, string>[] DefaultText =
        {
            Pair("AppName", "FACM"),
            Pair("ControlCenter", "控制中心"),
            Pair("Cleanup", "清理环境"),
            Pair("ToolGroup", "快捷工具"),
            Pair("ToolA", "工具 A"),
            Pair("Mode1", "模式 1"),
            Pair("Mode2", "模式 2"),
            Pair("Mode3", "模式 3"),
            Pair("Mode4", "模式 4"),
            Pair("CheckUpdate", "检查更新"),
            Pair("OpenLog", "操作日志"),
            Pair("About", "程序信息"),
            Pair("EditText", "界面文字"),
            Pair("Exit", "退出程序"),
            Pair("PanelTheme", "面板主题"),
            Pair("ThemeSettings", "主题设置"),
            Pair("DesktopPet", "桌面宠物"),
            Pair("PetReset", "宠物复位"),
            Pair("RestoreFloatingBall", "恢复默认悬浮球"),
            Pair("MayhemRanking", "海斗排行榜"),
            Pair("WorkDirectory", "工作目录"),
            Pair("AutoDetect", "自动识别"),
            Pair("SelectDirectory", "选择目录"),
            Pair("RulesConfigured", "规则已配置"),
            Pair("WaitingConfiguration", "等待配置"),
            Pair("CleanupHint", "先预览路径，再确认执行"),
            Pair("StartCleanup", "开始清理"),
            Pair("UpdateAndAnnouncements", "更新与公告"),
            Pair("AutoCheckAtStartup", "启动时自动检查"),
            Pair("Ready", "准备就绪"),
            Pair("Administrator", "管理员"),
            Pair("StandardMode", "标准模式"),
            Pair("Close", "关闭"),
            Pair("ApplyPet", "应用桌宠"),
            Pair("PetSource", "来源"),
            Pair("Open", "打开")
        };

        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _replacements =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private UiTextCatalog()
        {
            foreach (var entry in DefaultText) _values[entry.Key] = entry.Value;
        }

        public string AppName { get { return Get("AppName", "FACM"); } }
        public string ControlCenter { get { return Get("ControlCenter", "控制中心"); } }
        public string Cleanup { get { return Get("Cleanup", "清理环境"); } }
        public string ToolGroup { get { return Get("ToolGroup", "快捷工具"); } }
        public string ToolA { get { return Get("ToolA", "工具 A"); } }
        public string Mode1 { get { return Get("Mode1", "模式 1"); } }
        public string Mode2 { get { return Get("Mode2", "模式 2"); } }
        public string Mode3 { get { return Get("Mode3", "模式 3"); } }
        public string Mode4 { get { return Get("Mode4", "模式 4"); } }
        public string CheckUpdate { get { return Get("CheckUpdate", "检查更新"); } }
        public string OpenLog { get { return Get("OpenLog", "操作日志"); } }
        internal string About { get { return Get("About", "程序信息"); } }
        public string EditText { get { return Get("EditText", "界面文字"); } }
        public string Exit { get { return Get("Exit", "退出程序"); } }

        public static string ConfigPath
        {
            get { return RuntimePaths.UiTextPath; }
        }

        public static UiTextCatalog Load()
        {
            EnsureTemplate();
            var result = new UiTextCatalog();
            try
            {
                var section = string.Empty;
                foreach (var sourceLine in File.ReadAllLines(RuntimePaths.UiTextPath, Encoding.UTF8))
                {
                    var line = sourceLine ?? string.Empty;
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith(";", StringComparison.Ordinal)) continue;
                    if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal) && trimmed.Length > 2)
                    {
                        section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                        continue;
                    }

                    var separator = FindUnescapedEquals(line);
                    if (separator <= 0) continue;
                    var key = Unescape(line.Substring(0, separator).Trim());
                    var value = Unescape(line.Substring(separator + 1).Trim());
                    if (key.Length == 0) continue;

                    if (section.Equals("Replace", StringComparison.OrdinalIgnoreCase))
                        result._replacements[key] = value;
                    else
                        result._values[key] = value;
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to load UI text configuration", exception);
            }
            return result;
        }

        public static void OpenConfig()
        {
            EnsureTemplate();
            Process.Start(new ProcessStartInfo
            {
                FileName = RuntimePaths.UiTextPath,
                UseShellExecute = true
            });
        }

        public string Get(string key, string fallback)
        {
            string value;
            return !string.IsNullOrEmpty(key) && _values.TryGetValue(key, out value) ? value : fallback;
        }

        public string Translate(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            var translated = text;

            foreach (var rule in OrderedReplacementRules())
            {
                if (rule.Key.Length == 0 || string.Equals(rule.Key, rule.Value, StringComparison.Ordinal)) continue;
                translated = translated.Replace(rule.Key, rule.Value);
            }

            foreach (var entry in OrderedNamedRules())
            {
                if (entry.Key.Length == 0 || string.Equals(entry.Key, entry.Value, StringComparison.Ordinal)) continue;
                translated = translated.Replace(entry.Key, entry.Value);
            }

            return translated;
        }

        public string Canonicalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            var canonical = text;

            foreach (var entry in OrderedNamedReverseRules())
            {
                if (entry.Key.Length == 0 || string.Equals(entry.Key, entry.Value, StringComparison.Ordinal)) continue;
                canonical = canonical.Replace(entry.Key, entry.Value);
            }

            foreach (var rule in OrderedReplacementReverseRules())
            {
                if (rule.Key.Length == 0 || string.Equals(rule.Key, rule.Value, StringComparison.Ordinal)) continue;
                canonical = canonical.Replace(rule.Key, rule.Value);
            }

            return canonical;
        }

        public string ModeName(int mode)
        {
            switch (mode)
            {
                case 1: return Mode1;
                case 2: return Mode2;
                case 3: return Mode3;
                case 4: return Mode4;
                default: return Translate("模式 " + mode);
            }
        }

        private IEnumerable<KeyValuePair<string, string>> OrderedNamedRules()
        {
            return DefaultText
                .Select(entry => Pair(entry.Value, Get(entry.Key, entry.Value)))
                .OrderByDescending(entry => entry.Key.Length);
        }

        private IEnumerable<KeyValuePair<string, string>> OrderedNamedReverseRules()
        {
            return DefaultText
                .Select(entry => Pair(Get(entry.Key, entry.Value), entry.Value))
                .Where(entry => entry.Key.Length > 0)
                .OrderByDescending(entry => entry.Key.Length);
        }

        private IEnumerable<KeyValuePair<string, string>> OrderedReplacementRules()
        {
            return _replacements.OrderByDescending(entry => entry.Key.Length);
        }

        private IEnumerable<KeyValuePair<string, string>> OrderedReplacementReverseRules()
        {
            return _replacements
                .Where(entry => !string.IsNullOrEmpty(entry.Value))
                .Select(entry => Pair(entry.Value, entry.Key))
                .OrderByDescending(entry => entry.Key.Length);
        }

        private static void EnsureTemplate()
        {
            try
            {
                RuntimePaths.Initialize();
                if (!File.Exists(RuntimePaths.UiTextPath))
                {
                    if (File.Exists(LegacyConfigPath))
                        File.Copy(LegacyConfigPath, RuntimePaths.UiTextPath, false);
                    else
                        File.WriteAllLines(RuntimePaths.UiTextPath, BuildTemplate(), new UTF8Encoding(false));
                }

                EnsureMissingKeysAndSections(RuntimePaths.UiTextPath);
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to create UI text configuration", exception);
            }
        }

        private static string[] BuildTemplate()
        {
            var lines = new List<string>
            {
                "# FACM 界面文字配置",
                "# 修改后保存即可，程序运行时会自动重新读取，不需要重新编译。",
                "# [Text] 是常用文字；把等号右侧改成你想显示的内容。",
                "# [Replace] 是全局兜底：左边写当前原文，右边写新文字，可替换整句或关键词。",
                "# 需要换行时写 \\n；需要显示反斜杠写 \\\\。",
                string.Empty,
                "[Text]"
            };
            foreach (var entry in DefaultText) lines.Add(entry.Key + "=" + Escape(entry.Value));
            lines.Add(string.Empty);
            lines.Add("[Replace]");
            lines.Add("# 示例（去掉前面的 # 即生效）：");
            lines.Add("# FACM=我的程序");
            lines.Add("# VPet Core=高精度桌宠");
            lines.Add("# 面向开发者=自定义文字");
            return lines.ToArray();
        }

        private static void EnsureMissingKeysAndSections(string path)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasReplace = false;
            var section = string.Empty;

            foreach (var sourceLine in lines)
            {
                var trimmed = (sourceLine ?? string.Empty).Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal) && trimmed.Length > 2)
                {
                    section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    if (section.Equals("Replace", StringComparison.OrdinalIgnoreCase)) hasReplace = true;
                    continue;
                }
                if (section.Equals("Replace", StringComparison.OrdinalIgnoreCase)) continue;
                var separator = FindUnescapedEquals(sourceLine ?? string.Empty);
                if (separator <= 0) continue;
                var key = Unescape(sourceLine.Substring(0, separator).Trim());
                if (key.Length > 0) known.Add(key);
            }

            var missing = DefaultText.Where(entry => !known.Contains(entry.Key)).ToList();
            if (missing.Count == 0 && hasReplace) return;

            lines.Add(string.Empty);
            if (missing.Count > 0)
            {
                lines.Add("# 自动补充的新版本可配置文字；已有值不会被覆盖。 ");
                lines.Add("[Text]");
                foreach (var entry in missing) lines.Add(entry.Key + "=" + Escape(entry.Value));
            }
            if (!hasReplace)
            {
                lines.Add(string.Empty);
                lines.Add("[Replace]");
                lines.Add("# 任意没有单独键的界面文字都可写在这里：原文=新文");
                lines.Add("# FACM=我的程序");
            }
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        private static int FindUnescapedEquals(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] != '=') continue;
                var slashCount = 0;
                for (var scan = index - 1; scan >= 0 && value[scan] == '\\'; scan--) slashCount++;
                if (slashCount % 2 == 0) return index;
            }
            return -1;
        }

        private static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (current != '\\' || index + 1 >= value.Length)
                {
                    builder.Append(current);
                    continue;
                }

                var next = value[++index];
                switch (next)
                {
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case '=': builder.Append('='); break;
                    case '\\': builder.Append('\\'); break;
                    default:
                        builder.Append('\\');
                        builder.Append(next);
                        break;
                }
            }
            return builder.ToString();
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("=", "\\=");
        }

        private static KeyValuePair<string, string> Pair(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }
    }
}
