using System;
using System.Collections.Generic;

namespace FACM.Services
{
    internal static class DiagnosticsUiTextKeys
    {
        public const string Export = "DiagnosticsExport";
        public const string ExportSuccessFormat = "DiagnosticsExportSuccessFormat";
        public const string ExportFailedFormat = "DiagnosticsExportFailedFormat";
    }

    internal static class DiagnosticsUiText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { DiagnosticsUiTextKeys.Export, "导出诊断包" },
            { DiagnosticsUiTextKeys.ExportSuccessFormat, "诊断包已生成并打开所在位置：\r\n{0}" },
            { DiagnosticsUiTextKeys.ExportFailedFormat, "生成诊断包失败：{0}" }
        };

        public static string Get(UiTextCatalog ui, string key)
        {
            string fallback;
            if (!Defaults.TryGetValue(key ?? string.Empty, out fallback)) fallback = string.Empty;
            return ui == null ? fallback : ui.Get(key, fallback);
        }

        internal static IReadOnlyDictionary<string, string> DefaultsForSmokeTest()
        {
            return Defaults;
        }
    }
}
