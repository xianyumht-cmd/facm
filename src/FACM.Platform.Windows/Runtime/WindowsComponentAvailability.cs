using FACM.Core.Runtime;

namespace FACM.Platform.Windows.Runtime;

/// <summary>
/// Small, read-only component probe used by optional desktop-pet runtimes.
/// It deliberately does not manage installation, downloads, or activation.
/// </summary>
public sealed class WindowsComponentAvailability : IComponentAvailability
{
    private readonly IReadOnlyDictionary<string, Func<bool>> _probes;

    public WindowsComponentAvailability(IReadOnlyDictionary<string, Func<bool>> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);
        _probes = probes;
    }

    public bool IsAvailable(string componentId) =>
        !string.IsNullOrWhiteSpace(componentId) &&
        (!_probes.TryGetValue(componentId, out var probe) || SafeProbe(probe));

    private static bool SafeProbe(Func<bool> probe)
    {
        try { return probe(); }
        catch { return false; }
    }
}
