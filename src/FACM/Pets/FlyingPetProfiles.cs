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
                    MinBaseSpeed = 52f,
                    MaxBaseSpeed = 88f,
                    MoveMinSeconds = 1.05,
                    MoveMaxSeconds = 2.60,
                    IdleChance = 0.12,
                    IdleMinSeconds = 0.25,
                    IdleMaxSeconds = 0.90,
                    VelocityResponse = 4.8f,
                    HeadingResponse = 7.0f,
                    JitterXAmplitude = 3.0f,
                    JitterYAmplitude = 4.5f,
                    JitterXFrequency = 8.0f,
                    JitterYFrequency = 6.2f
                },
                [Dragonfly] = new FlyingPetProfile
                {
                    Id = Dragonfly,
                    MinBaseSpeed = 104f,
                    MaxBaseSpeed = 176f,
                    MoveMinSeconds = 1.60,
                    MoveMaxSeconds = 3.60,
                    IdleChance = 0.08,
                    IdleMinSeconds = 0.18,
                    IdleMaxSeconds = 0.55,
                    VelocityResponse = 9.2f,
                    HeadingResponse = 12.0f,
                    JitterXAmplitude = 1.5f,
                    JitterYAmplitude = 1.8f,
                    JitterXFrequency = 7.0f,
                    JitterYFrequency = 6.0f
                },
                [Butterfly] = new FlyingPetProfile
                {
                    Id = Butterfly,
                    MinBaseSpeed = 24f,
                    MaxBaseSpeed = 48f,
                    MoveMinSeconds = 2.30,
                    MoveMaxSeconds = 4.80,
                    IdleChance = 0.08,
                    IdleMinSeconds = 0.45,
                    IdleMaxSeconds = 1.35,
                    VelocityResponse = 2.2f,
                    HeadingResponse = 3.4f,
                    JitterXAmplitude = 7.0f,
                    JitterYAmplitude = 10.0f,
                    JitterXFrequency = 4.4f,
                    JitterYFrequency = 3.7f
                },
                [Moth] = new FlyingPetProfile
                {
                    Id = Moth,
                    MinBaseSpeed = 42f,
                    MaxBaseSpeed = 78f,
                    MoveMinSeconds = 0.85,
                    MoveMaxSeconds = 2.25,
                    IdleChance = 0.06,
                    IdleMinSeconds = 0.25,
                    IdleMaxSeconds = 1.05,
                    VelocityResponse = 5.6f,
                    HeadingResponse = 6.5f,
                    JitterXAmplitude = 5.0f,
                    JitterYAmplitude = 5.5f,
                    JitterXFrequency = 7.6f,
                    JitterYFrequency = 6.4f
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
