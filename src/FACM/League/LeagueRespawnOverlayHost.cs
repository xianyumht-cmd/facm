using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueRespawnOverlayVisibility
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        internal static bool IsGameProcess(string processName)
        {
            return string.Equals(processName, "League of Legends", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsGameForeground()
        {
            try
            {
                uint processId;
                var window = GetForegroundWindow();
                if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out processId) == 0 || processId == 0)
                    return false;
                using (var process = Process.GetProcessById((int)processId))
                    return IsGameProcess(process.ProcessName);
            }
            catch { return false; }
        }
    }

    /// <summary>Optional UI-thread consumer of the existing Gameflow owner.</summary>
    internal sealed class LeagueRespawnOverlayHost : IDisposable
    {
        private readonly ILeagueClientApi _client;
        private readonly Func<LeagueClientSession> _sessionProvider;
        private readonly object _sync = new object();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private Control _owner;
        private System.Windows.Forms.Timer _timer;
        private LeagueRespawnOverlayForm _form;
        private LeagueRespawnTimerReader _reader;
        private LeagueRespawnTimerSample _sample;
        private LeagueClientSession _session;
        private string _account;
        private DateTime _nextIdentityReadUtc;
        private bool _enabled;
        private bool _inProgress;
        private bool _refreshing;
        private bool _disposed;
        private int _generation;

        internal LeagueRespawnOverlayHost(ILeagueClientApi client, Func<LeagueClientSession> sessionProvider)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
            Application.Idle += AttachWhenReady;
            Application.ApplicationExit += HandleApplicationExit;
        }

        internal static bool ShouldRun(bool enabled, LeagueDashboardPhaseState state)
        {
            return enabled && state != null && state.Connected &&
                string.Equals(state.Phase, "InProgress", StringComparison.OrdinalIgnoreCase);
        }

        internal void Configure(bool enabled, LeagueDashboardPhaseState current)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _enabled = enabled;
                SetInProgressLocked(ShouldRun(_enabled, current));
            }
            DispatchUiState();
        }

        internal void Observe(LeagueDashboardPhaseState state)
        {
            lock (_sync)
            {
                if (_disposed) return;
                SetInProgressLocked(ShouldRun(_enabled, state));
            }
            DispatchUiState();
        }

        private void SetInProgressLocked(bool desired)
        {
            if (_inProgress == desired) return;
            _inProgress = desired;
            _generation++;
        }

        private void AttachWhenReady(object sender, EventArgs args)
        {
            lock (_sync) { if (_disposed || _owner != null) return; }
            var owner = Application.OpenForms.OfType<MainForm>().FirstOrDefault(form => !form.IsDisposed);
            if (owner == null || !owner.IsHandleCreated) return;
            lock (_sync)
            {
                if (_disposed || _owner != null) return;
                _owner = owner;
            }
            Application.Idle -= AttachWhenReady;
            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += async delegate { await RefreshAsync(); };
            ApplyUiState();
        }

        private void DispatchUiState()
        {
            Control owner;
            lock (_sync) { owner = _owner; }
            if (owner == null || owner.IsDisposed || !owner.IsHandleCreated) return;
            try { owner.BeginInvoke(new Action(ApplyUiState)); }
            catch (InvalidOperationException) { }
        }

        private void ApplyUiState()
        {
            if (_timer == null) return;
            bool active;
            lock (_sync) { active = !_disposed && _inProgress; }
            if (active)
            {
                if (!_timer.Enabled) _timer.Start();
                return;
            }
            _timer.Stop();
            _form?.Render(null);
            _reader?.Dispose();
            _reader = null;
            _account = null;
            _sample = null;
            _session = null;
            _nextIdentityReadUtc = DateTime.MinValue;
        }

        private async Task RefreshAsync()
        {
            if (_refreshing || _lifetime.IsCancellationRequested) return;
            int generation;
            lock (_sync)
            {
                if (_disposed || !_inProgress) return;
                generation = _generation;
            }
            _refreshing = true;
            try
            {
                if (!LeagueRespawnOverlayVisibility.IsGameForeground())
                {
                    HideOverlay();
                    return;
                }
                var session = _sessionProvider();
                if (session == null) { HideOverlay(); return; }
                if (_session == null || !_session.Matches(session))
                {
                    _session = session;
                    _account = null;
                    _sample = null;
                    _nextIdentityReadUtc = DateTime.MinValue;
                    HideOverlay();
                }

                if (DateTime.UtcNow >= _nextIdentityReadUtc)
                {
                    _nextIdentityReadUtc = DateTime.UtcNow.AddSeconds(3);
                    var summoner = await _client.TryGetBytesAsync(
                        LeagueDashboardDetailsService.SummonerPath, _lifetime.Token).ConfigureAwait(true);
                    if (!IsCurrent(generation, session)) return;
                    var account = LeagueRespawnTimerProjection.ReadCurrentAccount(summoner);
                    if (!string.Equals(account, _account, StringComparison.OrdinalIgnoreCase))
                    {
                        _sample = null;
                        _account = account;
                        HideOverlay();
                    }
                    if (!string.IsNullOrEmpty(account)) _nextIdentityReadUtc = DateTime.UtcNow.AddSeconds(30);
                }
                if (string.IsNullOrEmpty(_account) || !IsCurrent(generation, session)) return;
                if (_reader == null) _reader = new LeagueRespawnTimerReader();
                var bytes = await _reader.ReadAsync(_lifetime.Token).ConfigureAwait(true);
                if (!IsCurrent(generation, session)) return;
                _sample = LeagueRespawnTimerProjection.Project(bytes, _account, _sample);
                if (_sample == null || !_sample.IsDead || !LeagueRespawnOverlayVisibility.IsGameForeground())
                {
                    HideOverlay();
                    return;
                }
                if (_form == null || _form.IsDisposed)
                    _form = new LeagueRespawnOverlayForm(UiTextRuntime.Text(UiTextKeys.LeagueDashboardRespawnCountdown));
                _form.Render(_sample);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception exception)
            {
                AppLog.Info("League respawn overlay read skipped: " + exception.GetType().Name);
                HideOverlay();
            }
            finally { _refreshing = false; }
        }

        private bool IsCurrent(int generation, LeagueClientSession session)
        {
            lock (_sync)
            {
                return !_disposed && _inProgress && _generation == generation &&
                    session.Matches(_sessionProvider());
            }
        }

        private void HideOverlay() { _form?.Render(null); }

        private void HandleApplicationExit(object sender, EventArgs args) { Dispose(); }

        public void Dispose()
        {
            Control owner;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _generation++;
                owner = _owner;
            }
            _lifetime.Cancel();
            Application.Idle -= AttachWhenReady;
            Application.ApplicationExit -= HandleApplicationExit;
            Action release = () =>
            {
                _timer?.Stop();
                _timer?.Dispose();
                _timer = null;
                _form?.Dispose();
                _form = null;
                _reader?.Dispose();
                _reader = null;
            };
            if (owner != null && owner.InvokeRequired && owner.IsHandleCreated && !owner.IsDisposed)
            {
                try { owner.BeginInvoke(release); }
                catch (InvalidOperationException) { _reader?.Dispose(); }
            }
            else release();
            _lifetime.Dispose();
        }
    }
}
