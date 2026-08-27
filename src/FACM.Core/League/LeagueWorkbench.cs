using FACM.Core.Text;

namespace FACM.Core.League;

public sealed record LeagueWorkbenchSection(string Id, string TitleTextKey, string DescriptionTextKey);

public static class LeagueWorkbenchCatalog
{
    public const string Match = "match";
    public const string Strategy = "strategy";
    public const string Automation = "automation";

    private static readonly IReadOnlyList<LeagueWorkbenchSection> Values =
    [
        new(Match, UiTextKeys.LeagueWorkbenchMatch, UiTextKeys.LeagueWorkbenchMatchDescription),
        new(Strategy, UiTextKeys.LeagueWorkbenchStrategy, UiTextKeys.LeagueWorkbenchStrategyDescription),
        new(Automation, UiTextKeys.LeagueWorkbenchAutomation, UiTextKeys.LeagueWorkbenchAutomationDescription)
    ];

    public static IReadOnlyList<LeagueWorkbenchSection> Sections => Values;

    public static LeagueWorkbenchSection Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Values.FirstOrDefault(section => string.Equals(section.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown League Workbench section.");
    }
}
