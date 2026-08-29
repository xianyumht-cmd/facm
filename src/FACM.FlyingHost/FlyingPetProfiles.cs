namespace FACM.PetHost;

internal sealed record FlyingPetProfile(
    string Id,
    double MinBaseSpeed,
    double MaxBaseSpeed,
    double MoveMinSeconds,
    double MoveMaxSeconds,
    double IdleChance,
    double IdleMinSeconds,
    double IdleMaxSeconds,
    double VelocityResponse,
    double HeadingResponse,
    double JitterXAmplitude,
    double JitterYAmplitude,
    double JitterXFrequency,
    double JitterYFrequency,
    double SpeedMultiplier,
    double VisualScale);

internal static class FlyingPetProfiles
{
    // These values are intentionally frozen from FACM 3.5.15 FlyingPetProfiles / AnimalPetCatalog.
    // 4.0 changes the host/runtime technology, not the movement personality users already know.
    private static readonly IReadOnlyDictionary<string, FlyingPetProfile> Profiles =
        new Dictionary<string, FlyingPetProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["greenfly"] = new(
                "greenfly", 82, 140, 0.55, 1.80, 0.02, 0.45, 1.60,
                7.5, 10.5, 10, 8, 17, 13, 1.36, 0.56),
            ["bee"] = new(
                "bee", 48, 82, 1.20, 2.80, 0.18, 0.35, 1.10,
                4.2, 5.8, 2.5, 4.0, 7.2, 6.1, 1.00, 0.62),
            ["real-bee"] = new(
                "real-bee", 48, 82, 1.20, 2.80, 0.18, 0.35, 1.10,
                4.2, 5.8, 2.5, 4.0, 7.2, 6.1, 1.00, 0.55),
            ["dragonfly"] = new(
                "dragonfly", 120, 205, 2.20, 4.60, 0.14, 0.12, 0.40,
                12.0, 15.5, 0.5, 0.8, 5.0, 4.0, 1.00, 0.72),
            ["butterfly"] = new(
                "butterfly", 18, 38, 2.80, 5.60, 0.04, 0.50, 1.40,
                1.7, 2.4, 6.0, 14.0, 2.6, 2.1, 1.00, 0.74),
            ["moth"] = new(
                "moth", 36, 68, 0.65, 1.55, 0.04, 0.18, 0.65,
                6.2, 8.6, 7.0, 7.0, 4.8, 4.8, 1.00, 0.68)
        };

    public static IReadOnlyCollection<string> Ids => Profiles.Keys.ToArray();

    public static FlyingPetProfile Get(string? id) =>
        id is not null && Profiles.TryGetValue(id, out var profile)
            ? profile
            : Profiles["greenfly"];

    public static bool Contains(string? id) => id is not null && Profiles.ContainsKey(id);
}
