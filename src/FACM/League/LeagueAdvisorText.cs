using System;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueAdvisorText
    {
        public static string Get(UiTextCatalog ui, string key)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            return ui.Get(key);
        }
    }
}
