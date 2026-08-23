using FACM.Services;

namespace FACM.League
{
    internal static class LeaguePresenceText
    {
        public static string Get(UiTextCatalog ui, string key)
        {
            return (ui ?? UiTextCatalog.Load()).Get(key);
        }
    }
}
