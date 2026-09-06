using FACM.Services;

namespace FACM.Online
{
    /// <summary>
    /// Stable update-center copy that predates the role-scoped UI text registry. Keep these phrases
    /// out of visual components so UI refactors do not silently change action semantics; legacy
    /// ui-text.ini replacement rules are still applied through UiTextRuntime.Translate().
    /// </summary>
    internal static class OnlineCenterUiText
    {
        public static string UpdateNow { get { return T("立即更新"); } }
        public static string ViewDetails { get { return T("查看详情"); } }
        public static string Checking { get { return T("检查中"); } }
        public static string FetchFailed { get { return T("获取失败"); } }
        public static string ForceRequired { get { return T("必须更新"); } }
        public static string UpdateAvailable { get { return T("发现更新"); } }
        public static string UpToDate { get { return T("已是最新"); } }
        public static string Processing { get { return T("处理中"); } }

        private static string T(string value)
        {
            return UiTextRuntime.Translate(value);
        }
    }
}
