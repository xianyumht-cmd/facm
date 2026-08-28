namespace FACM.Core.Personalization;

public interface IFacmThemeRuntime
{
    string CurrentThemeId { get; }
    bool CustomPaletteActive { get; }
    void Apply(FacmThemeDefinition theme);
}

public sealed record DesktopPetModeResult(bool Success, bool PetVisible, string Detail);

public interface IDesktopPetRuntime
{
    bool IsPetVisible { get; }
    string ActivePetId { get; }
    Task<DesktopPetModeResult> ApplyAsync(bool enabled, FacmPetDefinition pet, CancellationToken cancellationToken = default);
    Task ResetPositionAsync(CancellationToken cancellationToken = default);
}
