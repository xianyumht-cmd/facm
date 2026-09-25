using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.Online;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class CloudSyncModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[]
        {
            SettingsModule.ModuleId
        };

        private readonly SettingsModule _settingsModule;
        private CloudIdentityStore _store;
        private CloudIdentityState _identity;
        private CloudBaseClient _client;
        private CancellationTokenSource _cancellation;
        private Task _syncTask;

        public const string ModuleId = "cloud-sync";

        public CloudSyncModule(SettingsModule settingsModule)
        {
            _settingsModule = settingsModule ?? throw new ArgumentNullException(nameof(settingsModule));
        }

        public string Id
        {
            get { return ModuleId; }
        }

        public IReadOnlyList<string> Dependencies
        {
            get { return ModuleDependencies; }
        }

        internal bool IsReady
        {
            get { return _identity != null && _client != null; }
        }

        internal Task RecordAccountAsync(PersonalStatsAccountRecord account, CancellationToken cancellationToken)
        {
            if (_identity == null || _client == null)
                throw new InvalidOperationException("Cloud sync is not initialized.");
            return _client.RecordAccountAsync(_identity.DeviceId, account, cancellationToken);
        }

        internal Task SetRankingOptInAsync(bool enabled, CancellationToken cancellationToken)
        {
            if (_identity == null || _client == null)
                throw new InvalidOperationException("Cloud sync is not initialized.");
            return _client.SetRankingOptInAsync(_identity.DeviceId, enabled, cancellationToken);
        }

        internal Task<CloudPersonalRanking> GetPersonalRankingAsync(CancellationToken cancellationToken)
        {
            if (_identity == null || _client == null)
                throw new InvalidOperationException("Cloud sync is not initialized.");
            return _client.GetPersonalRankingAsync(_identity.DeviceId, cancellationToken);
        }

        internal string DeviceId
        {
            get { return _identity == null ? string.Empty : _identity.DeviceId ?? string.Empty; }
        }

        public void Initialize()
        {
            try
            {
                _store = CloudIdentityStore.CreateDefault();
                _identity = _store.LoadOrCreate();
                _client = new CloudBaseClient();
                Application.Idle += StartRemoteSync;
            }
            catch (Exception exception)
            {
                AppLog.Info("Cloud sync disabled for this run: " + exception.GetType().Name);
                DisposeClient();
            }
        }

        private void StartRemoteSync(object sender, EventArgs eventArgs)
        {
            Application.Idle -= StartRemoteSync;
            if (_store == null || _identity == null || _client == null) return;

            _cancellation = new CancellationTokenSource();
            _syncTask = SynchronizeOnceAsync(_cancellation.Token);
        }

        private async Task SynchronizeOnceAsync(CancellationToken cancellationToken)
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                var row = await _client.SyncDeviceAsync(
                    _identity.DeviceId,
                    version == null ? "0.0.0.0" : version.ToString(),
                    Environment.OSVersion.VersionString,
                    cancellationToken).ConfigureAwait(false);

                var subject = _client.CurrentSubject;
                if (row != null && !string.IsNullOrWhiteSpace(subject))
                {
                    _store.SaveCloudUserId(_identity.DeviceId, subject);
                    _identity.CloudUserId = subject;
                }

                AppLog.Info("Cloud device sync succeeded.");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                AppLog.Info("Cloud device sync skipped: " + exception.GetType().Name);
                return;
            }

            try
            {
                await SynchronizeSettingsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Info("Cloud settings sync skipped: " + exception.GetType().Name);
            }
        }

        internal async Task SynchronizeSettingsAsync(CancellationToken cancellationToken)
        {
            if (_identity == null || _client == null || _settingsModule.Settings == null)
                throw new InvalidOperationException("Cloud sync is not initialized.");

            var settings = _settingsModule.Settings;
            var local = CloudSettingsSnapshot.Capture(settings);
            var remote = await _client.GetSettingsAsync(_identity.DeviceId, cancellationToken).ConfigureAwait(false);
            if (remote == null || remote.Settings == null)
            {
                await _client.SetSettingsAsync(_identity.DeviceId, local, cancellationToken).ConfigureAwait(false);
                AppLog.Info("Cloud settings initialized from local preferences.");
                return;
            }

            var localWriteTime = CloudSettingsSnapshot.GetLocalSettingsWriteTimeUtc();
            var remoteWriteTime = remote.UpdatedAtUtc.UtcDateTime;
            if (_settingsModule.WasSettingsCreatedThisRun || remoteWriteTime > localWriteTime)
            {
                remote.Settings.ApplyTo(settings);
                settings.Save();
                AppLog.Info("Cloud settings restored to local preferences.");
                return;
            }

            var localHash = CloudSettingsSnapshot.CreateHash(local);
            var remoteHash = CloudSettingsSnapshot.CreateHash(remote.Settings);
            if (!string.Equals(localHash, remoteHash, StringComparison.Ordinal))
            {
                await _client.SetSettingsAsync(_identity.DeviceId, local, cancellationToken).ConfigureAwait(false);
                AppLog.Info("Cloud settings updated from local preferences.");
            }
        }

        public void Dispose()
        {
            Application.Idle -= StartRemoteSync;

            var cancellation = _cancellation;
            _cancellation = null;
            if (cancellation != null)
            {
                try { cancellation.Cancel(); }
                catch { }
            }

            var task = _syncTask;
            _syncTask = null;
            if (task != null && !task.IsCompleted)
            {
                try { task.Wait(500); }
                catch { }
            }

            if (cancellation != null) cancellation.Dispose();
            DisposeClient();
            _store = null;
            _identity = null;
        }

        private void DisposeClient()
        {
            var client = _client;
            _client = null;
            if (client != null) client.Dispose();
        }
    }
}
