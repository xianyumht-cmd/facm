using System;
using System.Collections.Generic;

namespace FACM.Pets
{
    internal sealed class FlyingPetProfile
    {
        public string Id { get; set; }
        public float MinBaseSpeed { get; set; }
        public float MaxBaseSpeed { get; set; }
        public double MoveMinSeconds { get; set; }
        public double MoveMaxSeconds { get; set; }
        public double IdleChance { get; set; }
        public double IdleMinSeconds { get; set; }
        public double IdleMaxSeconds { get; set; }
        public float VelocityResponse { get; set; }
        public float HeadingResponse { get; set; }
        public float JitterXAmplitude { get; set; }
        public float JitterYAmplitude { get; set; }
        public float JitterXFrequency { get; set; }
        public float JitterYFrequency { get; set; }
    }

    internal static class FlyingPetProfiles
    {
        public const string GreenFly = "greenfly";
        public const string Bee = "bee";
        public const string Dragonfly = "dragonfly";
        public const string Butterfly = "butterfly";
        public const string Moth = "moth";

        private static readonly IReadOnlyDictionary<string, FlyingPetProfile> Profiles =
            new Dictionary<string, FlyingPetProfile>(StringComparer.OrdinalIgnoreCase)
            {
                [GreenFly] = new FlyingPetProfile
                {
                    Id = GreenFly,
                    // Exact FACM 3.1.3/PR #44 movement baseline. Do not tune these as part of art work.
                    MinBaseSpeed = 82f,
                    MaxBaseSpeed = 140f,
                    MoveMinSeconds = 0.55,
                    MoveMaxSeconds = 1.80,
                    IdleChance = 0.02,
                    IdleMinSeconds = 0.45,
                    IdleMaxSeconds = 1.60,
                    VelocityResponse = 7.5f,
                    HeadingResponse = 10.5f,
                    JitterXAmplitude = 10f,
                    JitterYAmplitude = 8f,
                    JitterXFrequency = 17f,
                    JitterYFrequency = 13f
                },
                [Bee] = new FlyingPetProfile
                {
                    Id = Bee,
                    // Bee: medium cruise, gentle acceleration and visibly longer hover pauses.
                    MinBaseSpeed = 48f,
                    MaxBaseSpeed = 82f,
                    MoveMinSeconds = 1.20,
                    MoveMaxSeconds = 2.80,
                    IdleChance = 0.18,
                    IdleMinSeconds = 0.35,
                    IdleMaxSeconds = 1.10,
                    VelocityResponse = 4.2f,
                    HeadingResponse = 5.8f,
                    JitterXAmplitude = 2.5f,
                    JitterYAmplitude = 4.0f,
                    JitterXFrequency = 7.2f,
                    JitterYFrequency = 6.1f
                },
                [Dragonfly] = new FlyingPetProfile
                {
                    Id = Dragonfly,
                    // Dragonfly: long high-speed dashes with almost no local wobble and brief stops.
                    MinBaseSpeed = 120f,
                    MaxBaseSpeed = 205f,
                    MoveMinSeconds = 2.20,
                    MoveMaxSeconds = 4.60,
                    IdleChance = 0.14,
                    IdleMinSeconds = 0.12,
                    IdleMaxSeconds = 0.40,
                    VelocityResponse = 12.0f,
                    HeadingResponse = 15.5f,
                    JitterXAmplitude = 0.5f,
                    JitterYAmplitude = 0.8f,
                    JitterXFrequency = 5.0f,
                    JitterYFrequency = 4.0f
                },
                [Butterfly] = new FlyingPetProfile
                {
                    Id = Butterfly,
                    // Butterfly: slow long arcs; strong low-frequency vertical drift makes the path float.
                    MinBaseSpeed = 18f,
                    MaxBaseSpeed = 38f,
                    MoveMinSeconds = 2.80,
                    MoveMaxSeconds = 5.60,
                    IdleChance = 0.04,
                    IdleMinSeconds = 0.50,
                    IdleMaxSeconds = 1.40,
                    VelocityResponse = 1.7f,
                    HeadingResponse = 2.4f,
                    JitterXAmplitude = 6.0f,
                    JitterYAmplitude = 14.0f,
                    JitterXFrequency = 2.6f,
                    JitterYFrequency = 2.1f
                },
                [Moth] = new FlyingPetProfile
                {
                    Id = Moth,
                    // Moth: short nervous hops. Equal X/Y frequency makes the existing sin/cos jitter
                    // trace a small local loop without adding another movement engine.
                    MinBaseSpeed = 36f,
                    MaxBaseSpeed = 68f,
                    MoveMinSeconds = 0.65,
                    MoveMaxSeconds = 1.55,
                    IdleChance = 0.04,
                    IdleMinSeconds = 0.18,
                    IdleMaxSeconds = 0.65,
                    VelocityResponse = 6.2f,
                    HeadingResponse = 8.6f,
                    JitterXAmplitude = 7.0f,
                    JitterYAmplitude = 7.0f,
                    JitterXFrequency = 4.8f,
                    JitterYFrequency = 4.8f
                }
            };

        public static FlyingPetProfile Get(AnimalPetDefinition pet)
        {
            if (pet == null || string.IsNullOrWhiteSpace(pet.FlyingProfileId)) return null;
            FlyingPetProfile profile;
            return Profiles.TryGetValue(pet.FlyingProfileId, out profile) ? profile : null;
        }

        public static FlyingPetProfile Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            FlyingPetProfile profile;
            return Profiles.TryGetValue(id, out profile) ? profile : null;
        }

        public static bool IsManaged(AnimalPetDefinition pet)
        {
            return Get(pet) != null;
        }
    }
}
