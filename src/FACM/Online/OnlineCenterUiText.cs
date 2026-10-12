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
        public static string CheckWindowTitle { get { return T("GGman 检查更新"); } }
        public static string RequiredWindowTitle { get { return T("GGman 必须更新"); } }
        public static string PromptTitle { get { return T("GGman 更新"); } }
        public static string ReleaseNotesTitle { get { return T("版本说明"); } }
        public static string ReleaseNotesUnavailable { get { return T("检查更新后显示新版本说明。"); } }
        public static string ReleaseNotesMissing { get { return T("此版本未提供更新说明。"); } }
        public static string FetchUnavailable { get { return T("暂时无法获取更新信息，请检查网络后重试。"); } }
        public static string NotYetVerified { get { return T("尚未确认最新版本，可点击检查更新重试。"); } }
        public static string InstallFailed { get { return T("更新未完成，请检查网络或系统权限后重试。"); } }
        public static string Cancelled { get { return T("更新已取消。"); } }

        private static string T(string value)
        {
            return UiTextRuntime.Translate(value);
        }
    }
}
