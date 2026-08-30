using System.Diagnostics;
using System.Collections;
using System.Runtime.InteropServices;
using FACM.Core.League;

namespace FACM.Platform.Windows.League;

public interface ILeagueSessionDiscovery
{
    LeagueTransportSession? TryDiscover();
}

public interface ILeagueSessionDiscoveryResultProvider
{
    LeagueSessionDiscoveryResult Discover();
}

public sealed record LeagueProcessSnapshot(
    int ProcessId,
    string? ExecutablePath,
    string? CommandLine);

public interface ILeagueProcessSnapshotProvider
{
    IReadOnlyList<LeagueProcessSnapshot> GetProcesses(string processName);
}

/// <summary>
/// Windows process snapshot provider used only by the bounded discovery worker. The native process
/// query avoids a package/runtime dependency while retaining the 3.5 fallback's process command-line
/// behavior. No command line is returned to diagnostics.
/// </summary>
public sealed class WindowsLeagueProcessSnapshotProvider : ILeagueProcessSnapshotProvider
{
    public IReadOnlyList<LeagueProcessSnapshot> GetProcesses(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return Array.Empty<LeagueProcessSnapshot>();

        var result = new List<LeagueProcessSnapshot>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                var executablePath = TryReadExecutablePath(process);
                var commandLine = TryReadCommandLine(process.Id);
                result.Add(new LeagueProcessSnapshot(process.Id, executablePath, commandLine));
            }
            catch
            {
                // League can exit or deny process access while the snapshot is being collected.
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    private static string? TryReadExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadCommandLine(int processId)
    {
        if (processId <= 0) return null;

        var handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, processId);
        if (handle == IntPtr.Zero) return TryReadWmiCommandLine(processId);

        try
        {
            var status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, IntPtr.Zero, 0, out var length);
            if (length <= 0) return TryReadWmiCommandLine(processId);

            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, length, out _);
                if (status != 0) return TryReadWmiCommandLine(processId);

                var commandLine = Marshal.PtrToStructure<UnicodeString>(buffer);
                if (commandLine.Length == 0 || commandLine.Buffer == IntPtr.Zero)
                    return TryReadWmiCommandLine(processId);

                var bytes = new byte[commandLine.Length];
                if (!ReadProcessMemory(handle, commandLine.Buffer, bytes, bytes.Length, out var read) ||
                    read.ToInt64() < bytes.Length)
                    return TryReadWmiCommandLine(processId);
                return System.Text.Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return TryReadWmiCommandLine(processId);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string? TryReadWmiCommandLine(int processId)
    {
        object? locator = null;
        object? service = null;
        object? processes = null;
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType is null) return null;
            locator = Activator.CreateInstance(locatorType);
            if (locator is null) return null;

            dynamic dynamicLocator = locator;
            service = dynamicLocator.ConnectServer(".");
            if (service is null) return null;
            dynamic dynamicService = service;
            processes = dynamicService.ExecQuery(
                "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + processId);
            if (processes is not IEnumerable enumerable) return null;

            foreach (var item in enumerable)
            {
                try
                {
                    if (item is null) continue;
                    dynamic dynamicItem = item;
                    var commandLine = (string?)dynamicItem.CommandLine;
                    if (!string.IsNullOrWhiteSpace(commandLine)) return commandLine;
                }
                finally
                {
                    ReleaseComObject(item);
                }
            }
        }
        catch
        {
            // WMI is a fallback only; an unavailable provider is represented as no command line.
        }
        finally
        {
            ReleaseComObject(processes);
            ReleaseComObject(service);
            ReleaseComObject(locator);
        }

        return null;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null || !Marshal.IsComObject(value)) return;
        try { Marshal.FinalReleaseComObject(value); } catch { }
    }

    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const int ProcessCommandLineInformation = 60;

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr address,
        [Out] byte[] buffer,
        int size,
        out IntPtr bytesRead);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr process,
        int informationClass,
        IntPtr information,
        int informationLength,
        out int returnLength);
}

public sealed record LeagueSessionDiscoveryResult(
    LeagueTransportSession? Session,
    string Source,
    int? ProcessId,
    int? Port,
    string Outcome);

public sealed class ProcessLockfileLeagueSessionDiscovery : ILeagueSessionDiscovery, ILeagueSessionDiscoveryResultProvider
{
    private static readonly string[] ProcessNames = ["LeagueClientUx", "LeagueClient"];
    private readonly ILeagueProcessSnapshotProvider _processes;

    public ProcessLockfileLeagueSessionDiscovery(ILeagueProcessSnapshotProvider? processes = null)
    {
        _processes = processes ?? new WindowsLeagueProcessSnapshotProvider();
    }

    public LeagueTransportSession? TryDiscover() => Discover().Session;

    public LeagueSessionDiscoveryResult Discover()
    {
        var sawProcess = false;
        var sawUxProcess = false;
        var sawCommandLine = false;
        var sawInvalidCommandLine = false;
        var sawEmptyLockfile = false;
        var sawMalformedLockfile = false;
        var sawStaleLockfile = false;
        var sawFailure = false;

        foreach (var processName in ProcessNames)
        {
            IReadOnlyList<LeagueProcessSnapshot> snapshots;
            try
            {
                snapshots = _processes.GetProcesses(processName);
            }
            catch
            {
                sawFailure = true;
                continue;
            }

            foreach (var process in snapshots)
            {
                sawProcess = true;
                var isUx = string.Equals(processName, "LeagueClientUx", StringComparison.OrdinalIgnoreCase);
                sawUxProcess |= isUx;

                var executableDirectory = string.IsNullOrWhiteSpace(process.ExecutablePath)
                    ? null
                    : Path.GetDirectoryName(process.ExecutablePath);
                var lockfile = string.IsNullOrWhiteSpace(executableDirectory)
                    ? null
                    : Path.Combine(executableDirectory, "lockfile");

                if (!string.IsNullOrWhiteSpace(lockfile) && File.Exists(lockfile))
                {
                    try
                    {
                        var content = File.ReadAllText(lockfile);
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            sawEmptyLockfile = true;
                        }
                        else if (LeagueTransportSessionParser.TryParseLockfile(
                                     content,
                                     process.ProcessId,
                                     out var lockfileSession))
                        {
                            if (lockfileSession!.Descriptor.ProcessId > 0 &&
                                lockfileSession.Descriptor.ProcessId != process.ProcessId)
                            {
                                sawStaleLockfile = true;
                            }
                            else
                            {
                                return CreateResult(lockfileSession, "lockfile", "lockfile-success");
                            }
                        }
                        else
                        {
                            sawMalformedLockfile = true;
                        }
                    }
                    catch
                    {
                        sawFailure = true;
                    }
                }

                // 3.5 only used the command line fallback for the UX process. Keep that
                // precedence and auth contract: the parser consumes the token but telemetry never does.
                if (!isUx) continue;
                if (string.IsNullOrWhiteSpace(process.CommandLine)) continue;

                sawCommandLine = true;
                if (LeagueTransportSessionParser.TryParseCommandLine(
                        process.CommandLine,
                        process.ProcessId,
                        out var commandLineSession))
                {
                    return CreateResult(commandLineSession!, "process-command-line", "process-fallback-success");
                }

                sawInvalidCommandLine = true;
            }
        }

        var outcome = !sawProcess
            ? "process-not-found"
            : sawUxProcess && !sawCommandLine && !sawEmptyLockfile && !sawMalformedLockfile && !sawStaleLockfile
                ? "command-line-unavailable"
                : sawInvalidCommandLine || sawMalformedLockfile || sawStaleLockfile
                    ? "failed"
                    : sawEmptyLockfile
                        ? "lockfile-empty"
                        : sawFailure
                            ? "failed"
                            : "process-not-found";

        return new LeagueSessionDiscoveryResult(null, "none", null, null, outcome);
    }

    private static LeagueSessionDiscoveryResult CreateResult(
        LeagueTransportSession session,
        string source,
        string outcome) =>
        new(
            session,
            source,
            session.Descriptor.ProcessId > 0 ? session.Descriptor.ProcessId : null,
            session.Descriptor.Port,
            outcome);
}

/// <summary>
/// The single FACM 4.0 League discovery/auth/session owner. Read and write gateways must share
/// one instance of this source; feature modules must never create their own discovery loops.
/// </summary>
public sealed class WindowsLeagueTransportSessionSource :
    ILeagueTransportSessionSource,
    IAsyncLeagueTransportSessionSource,
    IReasonedLeagueTransportSessionInvalidator,
    ILeagueSessionAccessor,
    IDisposable
{
    private readonly object _sync = new();
    private readonly ILeagueSessionDiscovery _discovery;
    private readonly TimeSpan _negativeCacheDuration;
    private readonly TimeSpan _discoveryTimeout;
    private readonly Action<LeagueSessionDiscoveryDiagnostic>? _diagnosticReporter;
    private LeagueTransportSession? _session;
    private DateTime _negativeCacheUntilUtc = DateTime.MinValue;
    private LeagueConnectionState _state = LeagueConnectionState.NotRunning;
    private DiscoveryFlight? _inFlight;
    private bool _disposed;

    public WindowsLeagueTransportSessionSource(
        ILeagueSessionDiscovery? discovery = null,
        TimeSpan? retryInterval = null,
        TimeSpan? discoveryTimeout = null,
        Action<LeagueSessionDiscoveryDiagnostic>? diagnosticReporter = null)
    {
        _discovery = discovery ?? new ProcessLockfileLeagueSessionDiscovery();
        _negativeCacheDuration = retryInterval ?? TimeSpan.FromSeconds(1);
        _discoveryTimeout = discoveryTimeout ?? TimeSpan.FromSeconds(2);
        if (_negativeCacheDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryInterval));
        if (_discoveryTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(discoveryTimeout));
        _diagnosticReporter = diagnosticReporter;
    }

    public LeagueConnectionState State
    {
        get { lock (_sync) return _state; }
    }

    public LeagueSessionDescriptor? Current
    {
        get { lock (_sync) return _session?.Descriptor; }
    }

    public LeagueTransportSession? GetSession(bool forceRefresh = false) =>
        GetSessionAsync(forceRefresh).GetAwaiter().GetResult();

    public async Task<LeagueTransportSession?> GetSessionAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DiscoveryRequest? request = null;
        LeagueSessionDiscoveryDiagnostic? immediateDiagnostic = null;
        LeagueTransportSession? cachedSession = null;
        var caller = GetCaller();
        var now = DateTime.UtcNow;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!forceRefresh && _session is not null)
            {
                _state = LeagueConnectionState.Connected;
                cachedSession = _session;
                immediateDiagnostic = CreateDiagnostic(
                    Guid.NewGuid().ToString("N"),
                    "cache-hit",
                    _session.Descriptor.Source,
                    _session.Descriptor.ProcessId,
                    _session.Descriptor.Port,
                    0,
                    "positive-cache",
                    cacheHit: true,
                    negativeCacheHit: false,
                    joinedExistingDiscovery: false,
                    caller: caller,
                    reason: null);
            }
            else if (!forceRefresh && now < _negativeCacheUntilUtc)
            {
                immediateDiagnostic = CreateDiagnostic(
                    Guid.NewGuid().ToString("N"),
                    "cache-hit",
                    "none",
                    null,
                    null,
                    0,
                    "negative-cache",
                    cacheHit: false,
                    negativeCacheHit: true,
                    joinedExistingDiscovery: false,
                    caller: caller,
                    reason: null);
            }
            else
            {
                var joined = _inFlight is not null;
                var flight = _inFlight;
                if (flight is null)
                {
                    flight = new DiscoveryFlight(Guid.NewGuid().ToString("N"));
                    _inFlight = flight;
                    _state = LeagueConnectionState.Connecting;
                    _ = ExecuteDiscoveryAsync(flight);
                }

                request = new DiscoveryRequest(flight, joined, caller, Stopwatch.GetTimestamp());
            }
        }

        if (immediateDiagnostic is not null)
            ReportDiagnostic(immediateDiagnostic);
        if (request is null) return cachedSession;

        ReportDiagnostic(CreateDiagnostic(
            request.Flight.DiscoveryId,
            "discovery-start",
            "lockfile-first",
            null,
            null,
            0,
            "started",
            cacheHit: false,
            negativeCacheHit: false,
            joinedExistingDiscovery: request.JoinedExistingDiscovery,
            caller: request.Caller,
            reason: null));

        try
        {
            var result = await request.Flight.Completion.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            ReportDiagnostic(CreateDiagnostic(
                request.Flight.DiscoveryId,
                "discovery-finish",
                result.Source,
                result.ProcessId,
                result.Port,
                ElapsedMilliseconds(request.StartTimestamp),
                result.Outcome,
                cacheHit: false,
                negativeCacheHit: false,
                joinedExistingDiscovery: request.JoinedExistingDiscovery,
                caller: request.Caller,
                reason: null));
            return result.Session;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ReportDiagnostic(CreateDiagnostic(
                request.Flight.DiscoveryId,
                "discovery-finish",
                "none",
                null,
                null,
                ElapsedMilliseconds(request.StartTimestamp),
                "cancelled",
                cacheHit: false,
                negativeCacheHit: false,
                joinedExistingDiscovery: request.JoinedExistingDiscovery,
                caller: request.Caller,
                reason: null));
            throw;
        }
    }

    public void Invalidate(LeagueTransportSession expected) => Invalidate(expected, "explicit");

    public void Invalidate(LeagueTransportSession expected, string reason)
    {
        ArgumentNullException.ThrowIfNull(expected);
        reason = string.IsNullOrWhiteSpace(reason) ? "explicit" : reason.Trim();
        LeagueSessionDiscoveryDiagnostic? diagnostic = null;

        lock (_sync)
        {
            if (_session is null || !_session.Matches(expected)) return;
            var current = _session;
            _session = null;
            _negativeCacheUntilUtc = DateTime.MinValue;
            _state = LeagueConnectionState.Unavailable;
            diagnostic = CreateDiagnostic(
                Guid.NewGuid().ToString("N"),
                "invalidate",
                current.Descriptor.Source,
                current.Descriptor.ProcessId,
                current.Descriptor.Port,
                0,
                "invalidated",
                cacheHit: false,
                negativeCacheHit: false,
                joinedExistingDiscovery: false,
                caller: GetCaller(),
                reason: reason);
        }

        if (diagnostic is not null) ReportDiagnostic(diagnostic);
    }

    private async Task ExecuteDiscoveryAsync(DiscoveryFlight flight)
    {
        LeagueSessionDiscoveryResult result;
        try
        {
            var work = Task.Run(DiscoverDetailed, CancellationToken.None);
            result = await work.WaitAsync(_discoveryTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            result = new LeagueSessionDiscoveryResult(null, "none", null, null, "timeout");
        }
        catch (OperationCanceledException)
        {
            result = new LeagueSessionDiscoveryResult(null, "none", null, null, "cancelled");
        }
        catch
        {
            result = new LeagueSessionDiscoveryResult(null, "none", null, null, "failed");
        }

        LeagueSessionDiscoveryDiagnostic? pidChangedDiagnostic = null;
        lock (_sync)
        {
            if (result.Session is not null)
            {
                if (_session is not null && !_session.Matches(result.Session))
                {
                    var previous = _session;
                    pidChangedDiagnostic = CreateDiagnostic(
                        Guid.NewGuid().ToString("N"),
                        "invalidate",
                        previous.Descriptor.Source,
                        previous.Descriptor.ProcessId,
                        previous.Descriptor.Port,
                        0,
                        "invalidated",
                        cacheHit: false,
                        negativeCacheHit: false,
                        joinedExistingDiscovery: false,
                        caller: "session-owner",
                        reason: "pid-changed");
                }

                _session = result.Session;
                _negativeCacheUntilUtc = DateTime.MinValue;
                _state = LeagueConnectionState.Connected;
            }
            else
            {
                _session = null;
                _negativeCacheUntilUtc = DateTime.UtcNow + _negativeCacheDuration;
                _state = result.Outcome is "timeout" or "failed" or "cancelled"
                    ? LeagueConnectionState.Unavailable
                    : LeagueConnectionState.NotRunning;
            }

            if (ReferenceEquals(_inFlight, flight)) _inFlight = null;
            flight.Completion.TrySetResult(result);
        }

        if (pidChangedDiagnostic is not null) ReportDiagnostic(pidChangedDiagnostic);
    }

    private LeagueSessionDiscoveryResult DiscoverDetailed() =>
        _discovery is ILeagueSessionDiscoveryResultProvider detailed
            ? detailed.Discover()
            : CreateFallbackResult(_discovery.TryDiscover());

    private static LeagueSessionDiscoveryResult CreateFallbackResult(LeagueTransportSession? session) =>
        session is null
            ? new LeagueSessionDiscoveryResult(null, "none", null, null, "process-not-found")
            : new LeagueSessionDiscoveryResult(
                session,
                session.Descriptor.Source,
                session.Descriptor.ProcessId > 0 ? session.Descriptor.ProcessId : null,
                session.Descriptor.Port,
                "success");

    private string GetCaller()
    {
        var context = LeagueDiagnosticContext.Current;
        if (context is null) return "league";
        return string.IsNullOrWhiteSpace(context.Phase)
            ? context.Source
            : context.Source + "/" + context.Phase;
    }

    private LeagueSessionDiscoveryDiagnostic CreateDiagnostic(
        string discoveryId,
        string @event,
        string source,
        int? processId,
        int? port,
        long durationMs,
        string outcome,
        bool cacheHit,
        bool negativeCacheHit,
        bool joinedExistingDiscovery,
        string caller,
        string? reason) =>
        new(
            discoveryId,
            @event,
            source,
            processId,
            port,
            durationMs,
            outcome,
            cacheHit,
            negativeCacheHit,
            joinedExistingDiscovery,
            Environment.CurrentManagedThreadId,
            caller,
            reason);

    private void ReportDiagnostic(LeagueSessionDiscoveryDiagnostic diagnostic)
    {
        try { _diagnosticReporter?.Invoke(diagnostic); }
        catch { /* diagnostics never change session behavior */ }
    }

    private static long ElapsedMilliseconds(long timestamp) =>
        Math.Max(0L, (long)Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _session = null;
            _state = LeagueConnectionState.Unavailable;
        }
    }

    private sealed class DiscoveryFlight(string discoveryId)
    {
        public string DiscoveryId { get; } = discoveryId;
        public TaskCompletionSource<LeagueSessionDiscoveryResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record DiscoveryRequest(
        DiscoveryFlight Flight,
        bool JoinedExistingDiscovery,
        string Caller,
        long StartTimestamp);
}
