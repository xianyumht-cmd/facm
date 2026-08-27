namespace FACM.League
{
    internal static class LeagueGameRepairUiText
    {
        public const string Title = "游戏修复";
        public const string Hint = "游戏运行期间遇到客户端窗口、大厅或结算异常时使用。";
        public const string WindowGroup = "客户端窗口";
        public const string LobbyGroup = "大厅 / 客户端";
        public const string FixNow = "立即修复窗口";
        public const string FixNowHint = "按当前显示器、窗口状态与合理尺寸原生修复客户端窗口。";
        public const string FixAuto = "自动修复窗口";
        public const string FixAutoDisable = "关闭自动修复";
        public const string FixAutoHint = "监听客户端窗口变化；仅在检测到异常后处理，不常驻轮询。";
        public const string SkipSettlement = "跳过卡结算";
        public const string SkipSettlementHint = "通过 FACM 当前客户端连接跳过卡住、持续转圈的结算页面。";
        public const string RestartUx = "重启客户端界面";
        public const string RestartUxHint = "重新加载 LeagueClient UX；不结束正在进行的游戏进程。";
        public const string ExitGame = "一键结束游戏";
        public const string ExitGameHint = "结束当前英雄联盟游戏进程；与“跳过卡结算”是两个不同功能。";
        public const string Ready = "准备就绪 · 游戏修复已由 FACM 原生接管";
        public const string ExitSuccess = "已结束当前游戏进程";
        public const string ExitNoTarget = "当前没有检测到正在运行的游戏进程";
        public const string ExitFailed = "结束游戏进程失败，请查看日志";
        public const string ActionFailed = "执行失败，请查看日志";
        public const string Facm = "FACM";
    }
}
