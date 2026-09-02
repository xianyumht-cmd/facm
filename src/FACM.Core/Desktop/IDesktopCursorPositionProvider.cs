namespace FACM.Core.Desktop;

/// <summary>
/// Minimal cursor boundary consumed by a desktop surface. Native cursor access stays in the
/// platform adapter; WinUI shells only depend on this Core contract.
/// </summary>
public interface IDesktopCursorPositionProvider
{
    bool TryGetCursorPosition(out DesktopPoint position);
}
