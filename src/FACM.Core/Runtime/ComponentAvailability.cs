namespace FACM.Core.Runtime;

public static class FacmComponentIds
{
    public const string CoreWinX64 = "facm-core-win-x64";
    public const string PetHostWinX64 = "facm-pet-pethost-win-x64";
    public const string FlyingHostWinX64 = "facm-pet-flying-win-x64";

    public static string ForPet(FACM.Core.Personalization.FacmPetDefinition pet) =>
        pet.Runtime switch
        {
            FACM.Core.Personalization.FacmPetRuntimeKind.VPetCore => PetHostWinX64,
            FACM.Core.Personalization.FacmPetRuntimeKind.FlyingSprite => FlyingHostWinX64,
            _ => string.Empty
        };
}

public interface IComponentAvailability
{
    bool IsAvailable(string componentId);
}

public sealed class AlwaysAvailableComponentAvailability : IComponentAvailability
{
    public static AlwaysAvailableComponentAvailability Instance { get; } = new();

    private AlwaysAvailableComponentAvailability()
    {
    }

    public bool IsAvailable(string componentId) => !string.IsNullOrWhiteSpace(componentId);
}
