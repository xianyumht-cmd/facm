namespace FACM.Core.League;

public sealed record LeagueWorkbenchSection(string Id, string TitleTextKey, string DescriptionTextKey);

public static class LeagueWorkbenchCatalog
{
    public const string Match = "match";
    public const string Strategy = "strategy";
    public const string Automation = "automation";

    private static readonly IReadOnlyList<LeagueWorkbenchSection> Values =
    [
        new(Match, "LeagueWorkbenchMatch", "LeagueWorkbenchMatchDescription"),
        new(Strategy, "LeagueWorkbenchStrategy", "LeagueWorkbenchStrategyDescription"),
        new(Automation, "LeagueWorkbenchAutomation", "LeagueWorkbenchAutomationDescription")
    ];

    public static IReadOnlyList<LeagueWorkbenchSection> Sections => Values;

    public static LeagueWorkbenchSection Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Values.FirstOrDefault(section => string.Equals(section.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown League Workbench section.");
    }
}
