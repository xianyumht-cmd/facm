namespace FACM.App;

public sealed partial class FloatingWindow
{
    internal void SetDesktopEntryVisible(bool visible)
    {
        if (_closed) return;
        if (visible)
        {
            AppWindow.Show();
            return;
        }
        AppWindow.Hide();
    }

    internal Task ResetDesktopEntryPositionAsync()
    {
        if (_closed) return Task.CompletedTask;
        _ = ApplyPlacement(null);
        return Task.CompletedTask;
    }
}
