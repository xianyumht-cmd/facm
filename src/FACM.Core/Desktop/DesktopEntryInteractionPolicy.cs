namespace FACM.Core.Desktop;

public enum DesktopEntryKind
{
    FloatingLauncher,
    FlyingPet,
    VPet
}

public enum DesktopEntryGesture
{
    LeftClick,
    RightClick
}

public enum DesktopEntryAction
{
    ToggleCompactLauncher,
    ShowTrayContextMenu
}

public static class DesktopEntryInteractionPolicy
{
    public static DesktopEntryAction Resolve(DesktopEntryGesture gesture) => gesture switch
    {
        DesktopEntryGesture.LeftClick => DesktopEntryAction.ToggleCompactLauncher,
        DesktopEntryGesture.RightClick => DesktopEntryAction.ShowTrayContextMenu,
        _ => throw new ArgumentOutOfRangeException(nameof(gesture), gesture, null)
    };
}
