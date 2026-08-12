using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FACM.Services;

namespace FACM.Application
{
    internal sealed class FacmModuleTiming
    {
        public FacmModuleTiming(string id, long durationMilliseconds, bool succeeded, string errorMessage)
        {
            Id = id;
            DurationMilliseconds = durationMilliseconds;
            Succeeded = succeeded;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public string Id { get; private set; }

        public long DurationMilliseconds { get; private set; }

        public bool Succeeded { get; private set; }

        public string ErrorMessage { get; private set; }
    }

    internal sealed class FacmHostReport
    {
        public FacmHostReport(
            IReadOnlyList<string> initializationOrder,
            IReadOnlyList<FacmModuleTiming> timings,
            long totalDurationMilliseconds,
            string slowestModuleId,
            long slowestModuleDurationMilliseconds)
        {
            InitializationOrder = initializationOrder ?? Array.Empty<string>();
            Timings = timings ?? Array.Empty<FacmModuleTiming>();
            TotalDurationMilliseconds = totalDurationMilliseconds;
            SlowestModuleId = slowestModuleId ?? string.Empty;
            SlowestModuleDurationMilliseconds = slowestModuleDurationMilliseconds;
        }

        public IReadOnlyList<string> InitializationOrder { get; private set; }

        public IReadOnlyList<FacmModuleTiming> Timings { get; private set; }

        public long TotalDurationMilliseconds { get; private set; }

        public string SlowestModuleId { get; private set; }

        public long SlowestModuleDurationMilliseconds { get; private set; }
    }

    internal sealed class FacmHost : IDisposable
    {
        private readonly Dictionary<string, IFacmModule> _modules =
            new Dictionary<string, IFacmModule>(StringComparer.Ordinal);
        private readonly List<string> _registrationOrder = new List<string>();
        private readonly List<IFacmModule> _initializedModules = new List<IFacmModule>();
        private readonly List<FacmModuleTiming> _timings = new List<FacmModuleTiming>();
        private bool _initialized;
        private bool _disposed;
        private FacmHostReport _report = new FacmHostReport(
            Array.Empty<string>(),
            Array.Empty<FacmModuleTiming>(),
            0,
            string.Empty,
            0);

        public FacmHostReport Report
        {
            get { return _report; }
        }

        public void Register(IFacmModule module)
        {
            ThrowIfDisposed();
            if (_initialized) throw new InvalidOperationException("FACM host is already initialized.");
            if (module == null) throw new ArgumentNullException(nameof(module));
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

            var order = ResolveInitializationOrder();
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
                        AppLog.Error("FACM module initialization failed: " + id, exception);
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
                        throw new InvalidOperationException(
                            "Module " + module.Id + " depends on missing module: " + dependencyId);
                }
            }

            var state = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = new List<string>();
            var stack = new List<string>();

            foreach (var id in _registrationOrder)
                Visit(id, state, result, stack);

            return result;
        }

        private void Visit(
            string id,
            IDictionary<string, int> state,
            ICollection<string> result,
            IList<string> stack)
        {
            int value;
            if (state.TryGetValue(id, out value))
            {
                if (value == 2) return;
                if (value == 1)
                {
                    var cycleStart = stack.IndexOf(id);
                    var cycle = cycleStart >= 0
                        ? stack.Skip(cycleStart).Concat(new[] { id })
                        : stack.Concat(new[] { id });
                    throw new InvalidOperationException(
                        "Circular FACM module dependency detected: " + string.Join(" -> ", cycle));
                }
            }

            state[id] = 1;
            stack.Add(id);

            var dependencies = _modules[id].Dependencies ?? Array.Empty<string>();
            foreach (var dependencyId in dependencies)
                Visit(dependencyId, state, result, stack);

            stack.RemoveAt(stack.Count - 1);
            state[id] = 2;
            result.Add(id);
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
                    AppLog.Error("FACM module dispose failed: " + module.Id, exception);
                }
            }

            _initializedModules.Clear();
        }

        private void UpdateReport(IReadOnlyList<string> order, long totalDurationMilliseconds)
        {
            var successful = _timings.Where(item => item.Succeeded).OrderByDescending(item => item.DurationMilliseconds).FirstOrDefault();
            _report = new FacmHostReport(
                order.ToArray(),
                _timings.ToArray(),
                totalDurationMilliseconds,
                successful == null ? string.Empty : successful.Id,
                successful == null ? 0 : successful.DurationMilliseconds);
        }

        private void LogInitializationReport()
        {
            AppLog.Info("FACM host initialized: " + _report.TotalDurationMilliseconds + "ms");
            AppLog.Info("FACM module initialization order: " + string.Join(" -> ", _report.InitializationOrder));
            foreach (var timing in _report.Timings)
            {
                AppLog.Info(
                    "FACM module initialized: " + timing.Id + "; duration=" + timing.DurationMilliseconds + "ms");
            }

            if (!string.IsNullOrWhiteSpace(_report.SlowestModuleId))
            {
                AppLog.Info(
                    "FACM slowest module: " + _report.SlowestModuleId +
                    " (" + _report.SlowestModuleDurationMilliseconds + "ms)");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FacmHost));
        }
    }
}
