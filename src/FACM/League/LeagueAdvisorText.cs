using System;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueAdvisorText
    {
        public static string Get(UiTextCatalog ui, string key)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            string fallback;
            if (LeagueBuildApplyUiTextKeys.TryGetDefault(key, out fallback))
                return ui.Get(key, fallback);
            if (LeagueAutoApplyUiTextKeys.TryGetDefault(key, out fallback))
                return ui.Get(key, fallback);
            if (LeagueItemSetUiTextKeys.TryGetDefault(key, out fallback))
                return ui.Get(key, fallback);
            return ui.Get(key);
        }
    }
}
