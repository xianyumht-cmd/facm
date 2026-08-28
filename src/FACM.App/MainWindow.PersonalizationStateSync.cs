namespace FACM.App;

public sealed partial class MainWindow
{
    internal void RefreshPersonalizationSurfaceFromRuntime()
    {
        if (_closed) return;
        SyncPersonalizationSurface();
    }
}
