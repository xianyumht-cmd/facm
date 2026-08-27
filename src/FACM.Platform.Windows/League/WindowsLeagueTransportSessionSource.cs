using System.Diagnostics;
using FACM.Core.League;

namespace FACM.Platform.Windows.League;

public interface ILeagueSessionDiscovery
{
    LeagueTransportSession? TryDiscover();
}

public sealed class ProcessLockfileLeagueSessionDiscovery : ILeagueSessionDiscovery
{
    private static readonly string[] ProcessNames = ["LeagueClientUx", "LeagueClient"];

    public LeagueTransportSession? TryDiscover()
    {
        foreach (var processName in ProcessNames)
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        var executable = process.MainModule?.FileName;
                        var directory = string.IsNullOrWhiteSpace(executable) ? null : Path.GetDirectoryName(executable);
                        var lockfile = string.IsNullOrWhiteSpace(directory) ? null : Path.Combine(directory, "lockfile");
                        if (string.IsNullOrWhiteSpace(lockfile) || !File.Exists(lockfile)) continue;
                        if (LeagueTransportSessionParser.TryParseLockfile(File.ReadAllText(lockfile), out var session)) return session;
                    }
                    catch
                    {
                        // League can exit or deny module access while discovery is running.
                    }
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        return null;
    }
}

/// <summary>
/// The single FACM 4.0 League discovery/auth/session owner. Read and write gateways must share
/// one instance of this source; feature modules must never create their own discovery loops.
/// </summary>
public sealed class WindowsLeagueTransportSessionSource : ILeagueTransportSessionSource, ILeagueSessionAccessor
{
    private readonly object _sync = new();
    private readonly ILeagueSessionDiscovery _discovery;
    private readonly TimeSpan _retryInterval;
    private LeagueTransportSession? _session;
    private DateTime _lastDiscoveryAttemptUtc = DateTime.MinValue;
    private LeagueConnectionState _state = LeagueConnectionState.NotRunning;

    public WindowsLeagueTransportSessionSource(ILeagueSessionDiscovery? discovery = null, TimeSpan? retryInterval = null)
    {
        _discovery = discovery ?? new ProcessLockfileLeagueSessionDiscovery();
        _retryInterval = retryInterval ?? TimeSpan.FromMilliseconds(750);
    }

    public LeagueConnectionState State
    {
        get { lock (_sync) return _state; }
    }

    public LeagueSessionDescriptor? Current
    {
        get { lock (_sync) return _session?.Descriptor; }
    }

    public LeagueTransportSession? GetSession(bool forceRefresh = false)
    {
        lock (_sync)
        {
            if (!forceRefresh && _session is not null)
            {
                _state = LeagueConnectionState.Connected;
                return _session;
            }

            var now = DateTime.UtcNow;
            if (!forceRefresh && now - _lastDiscoveryAttemptUtc < _retryInterval) return null;
            _lastDiscoveryAttemptUtc = now;
            _state = LeagueConnectionState.Connecting;

            try
            {
                _session = _discovery.TryDiscover();
                _state = _session is null ? LeagueConnectionState.NotRunning : LeagueConnectionState.Connected;
                return _session;
            }
            catch
            {
                _session = null;
                _state = LeagueConnectionState.Unavailable;
                return null;
            }
        }
    }

    public void Invalidate(LeagueTransportSession expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        lock (_sync)
        {
            if (_session is null || !_session.Matches(expected)) return;
            _session = null;
            _state = LeagueConnectionState.Unavailable;
            _lastDiscoveryAttemptUtc = DateTime.UtcNow;
        }
    }
}
