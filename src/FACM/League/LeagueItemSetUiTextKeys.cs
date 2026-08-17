using System;
using System.Collections.Generic;

namespace FACM.League
{
    internal static class LeagueItemSetUiTextKeys
    {
        public const string Menu = "League.ItemSet.Menu";
        public const string WindowTitle = "League.ItemSet.WindowTitle";
        public const string Title = "League.ItemSet.Title";
        public const string Hint = "League.ItemSet.Hint";
        public const string Context = "League.ItemSet.Context";
        public const string Preview = "League.ItemSet.Preview";
        public const string Refresh = "League.ItemSet.Refresh";
        public const string Write = "League.ItemSet.Write";
        public const string Waiting = "League.ItemSet.Waiting";
        public const string Ready = "League.ItemSet.Ready";
        public const string Preparing = "League.ItemSet.Preparing";
        public const string NoItems = "League.ItemSet.NoItems";
        public const string ConfirmTitle = "League.ItemSet.ConfirmTitle";
        public const string ConfirmFormat = "League.ItemSet.ConfirmFormat";
        public const string ContextChanged = "League.ItemSet.ContextChanged";
        public const string SucceededFormat = "League.ItemSet.SucceededFormat";
        public const string CleanupWarningFormat = "League.ItemSet.CleanupWarningFormat";
        public const string FailedFormat = "League.ItemSet.FailedFormat";
        public const string InstallLayoutUnavailable = "League.ItemSet.InstallLayoutUnavailable";
        public const string WriteFailed = "League.ItemSet.WriteFailed";
        public const string ChampSelectOnly = "League.ItemSet.ChampSelectOnly";

        internal static readonly string[] All =
        {
            Menu,
            WindowTitle,
            Title,
            Hint,
            Context,
            Preview,
            Refresh,
            Write,
            Waiting,
            Ready,
            Preparing,
            NoItems,
            ConfirmTitle,
            ConfirmFormat,
            ContextChanged,
            SucceededFormat,
            CleanupWarningFormat,
            FailedFormat,
            InstallLayoutUnavailable,
            WriteFailed,
            ChampSelectOnly
        };

        private static readonly Dictionary<string, string> Defaults =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Menu, "OP.GG 推荐装备集" },
                { WindowTitle, "FACM · OP.GG 推荐装备集" },
                { Title, "OP.GG Item Set" },
                { Hint, "仅在英雄选择阶段，由你确认后把 OP.GG 推荐装备写入客户端 Recommended；只管理 FACM 自己的文件。" },
                { Context, "当前上下文" },
                { Preview, "推荐装备预览" },
                { Refresh, "刷新推荐" },
                { Write, "预览并写入" },
                { Waiting, "先进入英雄选择，并等待 OP.GG 推荐就绪。" },
                { Ready, "推荐已就绪；点击后仍需确认，不会自动写文件。" },
                { Preparing, "正在准备当前英雄的 OP.GG 装备集..." },
                { NoItems, "当前 OP.GG 数据没有可写入的推荐装备。" },
                { ConfirmTitle, "确认写入 OP.GG 装备集" },
                { ConfirmFormat, "将写入：{0}\r\n\r\n装备组：{1} 组 / {2} 个条目\r\n{3}\r\n\r\n只有点击“是”后才写磁盘；FACM 只清理 facm1- 前缀文件，不会删除其它推荐文件。" },
                { ContextChanged, "确认期间英雄、队列或阶段已经变化；已安全取消，没有写入。" },
                { SucceededFormat, "装备集已写入并读回验证：{0}；清理旧 FACM 文件 {1} 个。" },
                { CleanupWarningFormat, "装备集已写入并读回验证：{0}；部分旧 FACM 文件清理失败，用户/第三方文件未触碰。" },
                { FailedFormat, "装备集没有完成写入。{0}" },
                { InstallLayoutUnavailable, "无法安全确认英雄联盟安装目录布局；没有写任何文件。" },
                { WriteFailed, "磁盘写入或读回验证失败；没有清理其它推荐文件。" },
                { ChampSelectOnly, "当前已离开英雄选择；没有执行装备集写入。" }
            };

        public static bool TryGetDefault(string key, out string value)
        {
            return Defaults.TryGetValue(key ?? string.Empty, out value);
        }
    }
}
