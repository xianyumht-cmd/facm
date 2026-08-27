using FACM.Core.Runtime;

namespace FACM.Platform.Windows.Runtime;

public sealed class WindowsExecutablePathProvider : IExecutablePathProvider
{
    public string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable.");
    public string BaseDirectory => AppContext.BaseDirectory;
}
