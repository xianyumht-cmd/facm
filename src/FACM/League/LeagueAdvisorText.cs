using FACM.Services;

namespace FACM.League
{
    internal static class LeagueAdvisorText
    {
        public static string Get(UiTextCatalog ui, string key)
        {
            string fallback;
            if (LeagueAutoApplyUiTextKeys.TryGetDefault(key, out fallback) ||
                LeagueItemSetUiTextKeys.TryGetDefault(key, out fallback) ||
                LeagueBuildApplyUiTextKeys.TryGetDefault(key, out fallback))
                return ui == null ? fallback : ui.Get(key, fallback);
            return ui == null ? string.Empty : ui.Get(key);
        }
    }
}
