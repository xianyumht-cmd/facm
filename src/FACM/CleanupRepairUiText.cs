namespace FACM
{
    internal static class CleanupRepairUiText
    {
        public const string WindowTitle = "FACM · 清理与修复";
        public const string WindowHint = "环境处理与完整修复流程";
        public const string GameDirectory = "游戏目录";
        public const string DirectoryRecognized = "● 已识别：{0}";
        public const string DirectoryMissing = "当前目录：未选择";
        public const string DirectoryPrompt = "请选择英雄联盟游戏目录";
        public const string DirectoryMarkerPrompt = "请选择包含 LeagueClient.exe 的“英雄联盟”文件夹";
        public const string DirectoryExample = @"比如选择到：E:\WeGameApps\英雄联盟";
        public const string SelectDirectory = "选择目录";
        public const string ManageDirectory = "管理目录";
        public const string AutoDetect = "自动识别";
        public const string DriverRepair = "驱动修复";
        public const string DriverHint = "启动驱动修复工具；与环境清理先后不限，建议两项都执行一次。";
        public const string DriverNotRun = "○ 未执行";
        public const string DriverStarted = "● 已启动";
        public const string EnvironmentCleanup = "环境清理";
        public const string CleanupHint = "先预览精确路径，确认后再执行环境清理。";
        public const string CleanupNotRun = "○ 未执行";
        public const string CleanupDone = "● 已完成";
        public const string CleanupPartial = "● 已执行 · 有失败项";
        public const string CurrentStatus = "当前状态";
        public const string DirectoryOk = "● 游戏目录正常";
        public const string DirectoryNeed = "○ 游戏目录未设置";
        public const string DriverNeed = "○ 驱动修复未执行";
        public const string DriverOk = "● 驱动修复已启动";
        public const string CleanupNeed = "○ 环境清理未完成";
        public const string CleanupOk = "● 环境清理已完成";
        public const string NextDirectory = "下一步：先选择英雄联盟游戏目录";
        public const string NextBoth = "下一步：驱动修复和环境清理都执行一次";
        public const string NextDriver = "下一步：再执行一次驱动修复";
        public const string NextCleanup = "下一步：执行环境清理";
        public const string NextFinal = "下一步：重启电脑 → WEGAME → 英雄联盟 → 修复游戏";
        public const string FlowTitle = "推荐流程";
        public const string Flow = "驱动修复 / 环境清理（先后不限）  →  重启电脑  →  WEGAME  →  英雄联盟  →  修复游戏";
        public const string FolderDialog = "请选择英雄联盟游戏目录。请选择包含 LeagueClient.exe 的“英雄联盟”文件夹，比如选择到 E:\\WeGameApps\\英雄联盟。";
        public const string DetectSuccess = "已识别英雄联盟目录。";
        public const string DetectFailed = "暂未自动识别到英雄联盟目录，请手动选择。";
        public const string InvalidDirectory = "这个目录不是有效的英雄联盟游戏目录。请选到包含 LeagueClient.exe 的“英雄联盟”文件夹。";
        public const string RelatedProcesses = "检测到相关进程仍在运行：\r\n\r\n{0}\r\n\r\n请先退出英雄联盟、WeGame 等相关程序后再清理。";
        public const string NeedAdmin = "环境清理需要管理员权限。FACM 将以管理员身份重新启动并继续清理。";
        public const string ElevationFailed = "未能启动管理员模式，请手动右键 FACM.exe，选择“以管理员身份运行”。";
        public const string NoTargets = "当前没有检测到需要清理的目标。";
        public const string CleanupComplete = "环境清理完成。\r\n\r\n如果还没执行驱动修复，请再执行一次。\r\n两项都完成后：重启电脑 → WEGAME → 英雄联盟 → 修复游戏。";
        public const string CleanupWithFailures = "环境清理已执行，但有 {0} 个项目处理失败。详情已写入日志。\r\n\r\n处理完失败项后，再按流程重启电脑并使用 WEGAME 修复游戏。";
        public const string OperationFailed = "操作失败：{0}";
        public const string Facm = "FACM";
    }
}
