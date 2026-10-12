using System;

namespace FACM.League
{
    internal static class LeagueItemSetUiTextSmokeTest
    {
        public static void Validate()
        {
            if (LeagueItemSetUiTextKeys.All == null || LeagueItemSetUiTextKeys.All.Length != 21)
                throw new InvalidOperationException("Gate 3 scoped UI text key inventory changed without updating its smoke contract.");

            foreach (var key in LeagueItemSetUiTextKeys.All)
            {
                string value;
                if (string.IsNullOrWhiteSpace(key) ||
                    !LeagueItemSetUiTextKeys.TryGetDefault(key, out value) ||
                    string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException("Gate 3 scoped UI text is missing a non-empty default: " + key);
                }
            }
        }
    }
}
