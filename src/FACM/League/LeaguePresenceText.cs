using System;
using System.Collections.Generic;
using FACM.Services;

namespace FACM.League
{
    internal static class LeaguePresenceText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeaguePresenceUiTextKeys.Menu, "在线状态" },
            { LeaguePresenceUiTextKeys.WindowTitle, "GGman · 在线状态" },
            { LeaguePresenceUiTextKeys.Title, "好友展示状态" },
            { LeaguePresenceUiTextKeys.Hint, "修改好友列表里看到的状态、聊天签名和聊天卡片展示段位；每次操作只写一次，不在后台反复抢写。" },
            { LeaguePresenceUiTextKeys.Current, "当前" },
            { LeaguePresenceUiTextKeys.Refresh, "刷新" },
            { LeaguePresenceUiTextKeys.Online, "在线" },
            { LeaguePresenceUiTextKeys.Away, "离开" },
            { LeaguePresenceUiTextKeys.DoNotDisturb, "勿扰" },
            { LeaguePresenceUiTextKeys.Mobile, "手机在线" },
            { LeaguePresenceUiTextKeys.Offline, "隐身" },
            { LeaguePresenceUiTextKeys.InGame, "显示为游戏中" },
            { LeaguePresenceUiTextKeys.Signature, "聊天签名" },
            { LeaguePresenceUiTextKeys.SignatureHint, "保存到当前客户端好友展示签名；会保留在线状态和其它 Presence 字段，并读回确认。留空可清除。" },
            { LeaguePresenceUiTextKeys.SignaturePlaceholder, "输入聊天签名" },
            { LeaguePresenceUiTextKeys.SignatureSave, "保存" },
            { LeaguePresenceUiTextKeys.SignatureSaved, "聊天签名已读回确认" },
            { LeaguePresenceUiTextKeys.SignatureOverridden, "客户端已恢复其它签名；GGman 没有继续强制覆盖" },
            { LeaguePresenceUiTextKeys.DisplayedRank, "展示段位" },
            { LeaguePresenceUiTextKeys.DisplayedRankHint, "只修改好友聊天卡片中的展示字段，不改变服务器真实排位、胜点或战绩。保存后会读回确认。" },
            { LeaguePresenceUiTextKeys.RankedQueue, "队列" },
            { LeaguePresenceUiTextKeys.RankedTier, "段位" },
            { LeaguePresenceUiTextKeys.RankedDivision, "分段" },
            { LeaguePresenceUiTextKeys.RankedSave, "应用" },
            { LeaguePresenceUiTextKeys.RankedSaved, "展示段位已读回确认" },
            { LeaguePresenceUiTextKeys.RankedOverridden, "客户端已恢复其它展示段位；GGman 没有继续强制覆盖" },
            { LeaguePresenceUiTextKeys.RankedInvalid, "展示段位参数无效，未向客户端写入" },
            { LeaguePresenceUiTextKeys.RankedQueueSolo, "单双排" },
            { LeaguePresenceUiTextKeys.RankedQueueFlex, "灵活组排" },
            { LeaguePresenceUiTextKeys.RankedQueueArena, "斗魂竞技场" },
            { LeaguePresenceUiTextKeys.RankedQueueTft, "云顶之弈" },
            { LeaguePresenceUiTextKeys.RankedQueueTftTurbo, "云顶极速" },
            { LeaguePresenceUiTextKeys.RankedQueueTftDoubleUp, "云顶双人" },
            { LeaguePresenceUiTextKeys.RankedQueueFlex3v3, "3v3灵活" },
            { LeaguePresenceUiTextKeys.RankedTierIron, "黑铁" },
            { LeaguePresenceUiTextKeys.RankedTierBronze, "青铜" },
            { LeaguePresenceUiTextKeys.RankedTierSilver, "白银" },
            { LeaguePresenceUiTextKeys.RankedTierGold, "黄金" },
            { LeaguePresenceUiTextKeys.RankedTierPlatinum, "铂金" },
            { LeaguePresenceUiTextKeys.RankedTierEmerald, "翡翠" },
            { LeaguePresenceUiTextKeys.RankedTierDiamond, "钻石" },
            { LeaguePresenceUiTextKeys.RankedTierMaster, "大师" },
            { LeaguePresenceUiTextKeys.RankedTierGrandmaster, "宗师" },
            { LeaguePresenceUiTextKeys.RankedTierChallenger, "王者" },
            { LeaguePresenceUiTextKeys.Waiting, "正在读取客户端状态..." },
            { LeaguePresenceUiTextKeys.Applied, "状态已读回确认" },
            { LeaguePresenceUiTextKeys.Overridden, "客户端已恢复实际状态；GGman 没有继续强制覆盖" },
            { LeaguePresenceUiTextKeys.Unavailable, "未读取到英雄联盟客户端在线状态" },
            { LeaguePresenceUiTextKeys.WriteFailed, "客户端拒绝或未完成这次状态修改" },
            { LeaguePresenceUiTextKeys.Footer, "这些项目仅影响客户端展示。GGman 会读回验证，但不会使用代理、拦截或后台循环去强制伪装；展示段位不会改变真实排位数据。" },
            { LeaguePresenceUiTextKeys.CurrentFormat, "{0}" }
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
