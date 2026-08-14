using System;
using System.Collections.Generic;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueAdvisorText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { UiTextKeys.LeagueAdvisorMenu, "OP.GG 对局助手" },
            { UiTextKeys.LeagueAdvisorWindowTitle, "FACM · OP.GG 对局助手" },
            { UiTextKeys.LeagueAdvisorTitle, "OP.GG Build Advisor" },
            { UiTextKeys.LeagueAdvisorHint, "自动识别当前英雄并显示 OP.GG Global 构筑建议；Gate 1 严格只读。" },
            { UiTextKeys.LeagueAdvisorContext, "当前上下文" },
            { UiTextKeys.LeagueAdvisorStats, "英雄数据" },
            { UiTextKeys.LeagueAdvisorSource, "数据来源" },
            { UiTextKeys.LeagueAdvisorVersion, "数据版本" },
            { UiTextKeys.LeagueAdvisorCategory, "项目" },
            { UiTextKeys.LeagueAdvisorRecommendation, "推荐" },
            { UiTextKeys.LeagueAdvisorEvidence, "样本" },
            { UiTextKeys.LeagueAdvisorRunes, "符文" },
            { UiTextKeys.LeagueAdvisorStarterItems, "出门装" },
            { UiTextKeys.LeagueAdvisorBoots, "鞋子" },
            { UiTextKeys.LeagueAdvisorCoreItems, "核心装备" },
            { UiTextKeys.LeagueAdvisorSkills, "技能加点" },
            { UiTextKeys.LeagueAdvisorCounters, "克制关系" },
            { UiTextKeys.LeagueAdvisorWaitingChampion, "等待你在选人阶段选择或预选英雄..." },
            { UiTextKeys.LeagueAdvisorWaitingChampSelect, "进入英雄选择后会自动切换当前英雄的推荐。" },
            { UiTextKeys.LeagueAdvisorUnsupportedMode, "当前模式暂未映射到 OP.GG 构筑数据。" },
            { UiTextKeys.LeagueAdvisorOpggUnavailable, "OP.GG 暂时不可用；客户端主链不受影响。" },
            { UiTextKeys.LeagueAdvisorInGameCache, "游戏中只读显示已缓存推荐，不发送新的 OP.GG 请求。" },
            { UiTextKeys.LeagueAdvisorInGameNoCache, "游戏中禁止新增 OP.GG 请求；本局暂无已缓存推荐。" },
            { UiTextKeys.LeagueAdvisorTimeout, "读取超时，已安全停止本轮请求。" },
            { UiTextKeys.LeagueAdvisorReady, "推荐已就绪" },
            { UiTextKeys.LeagueAdvisorReadOnly, "只读模式 · 不修改符文、技能、装备集或客户端设置" }
        };

        public static string Get(UiTextCatalog ui, string key)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            string fallback;
            if (!Defaults.TryGetValue(key ?? string.Empty, out fallback)) fallback = string.Empty;
            return ui.Get(key, fallback);
        }
    }
}
