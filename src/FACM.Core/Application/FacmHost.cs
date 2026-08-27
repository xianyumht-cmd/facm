using System.Diagnostics;

namespace FACM.Core.Application;

public sealed record FacmModuleTiming(
    string Id,
    long DurationMilliseconds,
    bool Succeeded,
    string ErrorMessage);

public sealed record FacmHostReport(
    IReadOnlyList<string> InitializationOrder,
    IReadOnlyList<FacmModuleTiming> Timings,
    long TotalDurationMilliseconds,
    string SlowestModuleId,
    long SlowestModuleDurationMilliseconds)
{
    public static FacmHostReport Empty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<FacmModuleTiming>(),
        0,
        string.Empty,
        0);
}

public interface IFacmHostLog
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception exception);
}

public sealed class NullFacmHostLog : IFacmHostLog
{
    public static NullFacmHostLog Instance { get; } = new();

    private NullFacmHostLog()
    {
    }

    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception exception) { }
}

public sealed class FacmHost : IDisposable
{
    private readonly Dictionary<string, IFacmModule> _modules = new(StringComparer.Ordinal);
    private readonly List<string> _registrationOrder = [];
    private readonly List<IFacmModule> _initializedModules = [];
    private readonly List<FacmModuleTiming> _timings = [];
    private readonly IFacmHostLog _log;
    private bool _initialized;
    private bool _disposed;

    public FacmHost(IFacmHostLog? log = null)
    {
        _log = log ?? NullFacmHostLog.Instance;
    }

    public FacmHostReport Report { get; private set; } = FacmHostReport.Empty;

    public void Register(IFacmModule module)
    {
        ThrowIfDisposed();
        if (_initialized) throw new InvalidOperationException("FACM host is already initialized.");
        ArgumentNullException.ThrowIfNull(module);
        if (string.IsNullOrWhiteSpace(module.Id))
            throw new ArgumentException("FACM module ID cannot be empty.", nameof(module));
        if (_modules.ContainsKey(module.Id))
            throw new InvalidOperationException("Duplicate FACM module ID: " + module.Id);

        _modules.Add(module.Id, module);
        _registrationOrder.Add(module.Id);
    }

    public void Initialize()
    {
        ThrowIfDisposed();
        if (_initialized) throw new InvalidOperationException("FACM host is already initialized.");

        IReadOnlyList<string> order;
        try
        {
            order = ResolveInitializationOrder();
        }
        catch (Exception exception)
        {
            _log.Error("FACM module graph validation failed", exception);
            throw;
        }

        var total = Stopwatch.StartNew();
        _timings.Clear();
        _initializedModules.Clear();

        try
        {
            foreach (var id in order)
            {
                var module = _modules[id];
                var moduleTimer = Stopwatch.StartNew();
                try
                {
                    module.Initialize();
                    moduleTimer.Stop();
                    _timings.Add(new FacmModuleTiming(id, moduleTimer.ElapsedMilliseconds, true, string.Empty));
                    _initializedModules.Add(module);
                }
                catch (Exception exception)
                {
                    moduleTimer.Stop();
                    _timings.Add(new FacmModuleTiming(id, moduleTimer.ElapsedMilliseconds, false, exception.Message));
                    _log.Error("FACM module initialization failed: " + id, exception);
                    DisposeFailedModule(module);
                    throw new InvalidOperationException("Failed to initialize FACM module: " + id, exception);
                }
            }

            total.Stop();
            _initialized = true;
            UpdateReport(order, total.ElapsedMilliseconds);
            LogInitializationReport();
        }
        catch
        {
            total.Stop();
            DisposeInitializedModules();
            UpdateReport(order, total.ElapsedMilliseconds);
            LogFailureReport();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeInitializedModules();
        _initialized = false;
        _disposed = true;
    }

    private IReadOnlyList<string> ResolveInitializationOrder()
    {
        foreach (var module in _modules.Values)
        {
            var dependencies = module.Dependencies ?? Array.Empty<string>();
            foreach (var dependencyId in dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependencyId))
                    throw new InvalidOperationException("Module " + module.Id + " contains an empty dependency ID.");
                if (!_modules.ContainsKey(dependencyId))
                    throw new InvalidOperationException("Module " + module.Id + " depends on missing module: " + dependencyId);
            }
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<string>();
        var stack = new List<string>();
        foreach (var id in _registrationOrder) Visit(id, state, result, stack);
        return result;
    }

    private void Visit(
        string id,
        IDictionary<string, int> state,
        ICollection<string> result,
        IList<string> stack)
    {
        if (state.TryGetValue(id, out var value))
        {
            if (value == 2) return;
            if (value == 1)
            {
                var cycleStart = stack.IndexOf(id);
                var cycle = cycleStart >= 0
                    ? stack.Skip(cycleStart).Concat([id])
                    : stack.Concat([id]);
                throw new InvalidOperationException(
                    "Circular FACM module dependency detected: " + string.Join(" -> ", cycle));
            }
        }

        state[id] = 1;
        stack.Add(id);
        var dependencies = _modules[id].Dependencies ?? Array.Empty<string>();
        foreach (var dependencyId in dependencies) Visit(dependencyId, state, result, stack);
        stack.RemoveAt(stack.Count - 1);
        state[id] = 2;
        result.Add(id);
    }

    private void DisposeFailedModule(IFacmModule module)
    {
        try
        {
            module.Dispose();
        }
        catch (Exception exception)
        {
            _log.Error("FACM failed module dispose also failed: " + module.Id, exception);
        }
    }

    private void DisposeInitializedModules()
    {
        for (var index = _initializedModules.Count - 1; index >= 0; index--)
        {
            var module = _initializedModules[index];
            try
            {
                module.Dispose();
            }
            catch (Exception exception)
            {
                _log.Error("FACM module dispose failed: " + module.Id, exception);
            }
        }

        _initializedModules.Clear();
    }

    private void UpdateReport(IReadOnlyList<string> order, long totalDurationMilliseconds)
    {
        var slowest = _timings.OrderByDescending(item => item.DurationMilliseconds).FirstOrDefault();
        Report = new FacmHostReport(
            order.ToArray(),
            _timings.ToArray(),
            totalDurationMilliseconds,
            slowest?.Id ?? string.Empty,
            slowest?.DurationMilliseconds ?? 0);
    }

    private void LogInitializationReport()
    {
        _log.Info("FACM host initialized: " + Report.TotalDurationMilliseconds + "ms");
        _log.Info("FACM module initialization order: " + string.Join(" -> ", Report.InitializationOrder));
        foreach (var timing in Report.Timings)
            _log.Info("FACM module initialized: " + timing.Id + "; duration=" + timing.DurationMilliseconds + "ms");
        LogSlowestModule();
    }

    private void LogFailureReport()
    {
        _log.Warning(
            "FACM host initialization aborted: total=" + Report.TotalDurationMilliseconds +
            "ms; plannedOrder=" + string.Join(" -> ", Report.InitializationOrder));
        foreach (var timing in Report.Timings)
        {
            _log.Warning(
                "FACM module initialization attempt: " + timing.Id +
                "; duration=" + timing.DurationMilliseconds +
                "ms; succeeded=" + timing.Succeeded +
                (timing.Succeeded ? string.Empty : "; error=" + timing.ErrorMessage));
        }
        LogSlowestModule();
    }

    private void LogSlowestModule()
    {
        if (!string.IsNullOrWhiteSpace(Report.SlowestModuleId))
            _log.Info("FACM slowest module: " + Report.SlowestModuleId + " (" + Report.SlowestModuleDurationMilliseconds + "ms)");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
