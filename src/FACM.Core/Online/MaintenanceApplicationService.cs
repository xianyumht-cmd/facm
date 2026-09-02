using FACM.Core.Settings;

namespace FACM.Core.Online;

public sealed record MaintenancePreferences(
    bool AutoUpdateEnabled,
    string LastAnnouncementId,
    SettingsLoadOrigin Origin)
{
    public bool LoadedFromRecovery =>
        Origin is SettingsLoadOrigin.RecoveredLastKnownGood or SettingsLoadOrigin.RecoveryDefaults;
}

public sealed record MaintenanceCheckResult(
    UpdateManifestSnapshot? Manifest,
    UpdateDecision Decision);

public sealed class MaintenanceApplicationService
{
    private readonly ISettings2Repository _settings;
    private readonly IUpdateManifestSource _updates;
    private readonly IAnnouncementSource _announcements;
    private readonly Version _currentVersion;

    public MaintenanceApplicationService(
        ISettings2Repository settings,
        IUpdateManifestSource updates,
        IAnnouncementSource announcements,
        Version currentVersion)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _announcements = announcements ?? throw new ArgumentNullException(nameof(announcements));
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
    }

    public Version CurrentVersion => _currentVersion;

    public async Task<MaintenancePreferences> LoadPreferencesAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        return new MaintenancePreferences(
            loaded.Settings.Online.AutoUpdateEnabled,
            loaded.Settings.Online.LastAnnouncementId,
            loaded.Origin);
    }

    // This is an explicit user intent. Even when settings were recovered from LKG/defaults, saving here
    // is allowed: the user is deliberately choosing to rebuild the primary settings document.
    public async Task<MaintenancePreferences> SetAutoUpdateEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var updated = await _settings.UpdateAsync(
            settings => settings.Online.AutoUpdateEnabled = enabled,
            allowRecoveryRebuild: true,
            cancellationToken).ConfigureAwait(false);
        return new MaintenancePreferences(
            updated.Settings.Online.AutoUpdateEnabled,
            updated.Settings.Online.LastAnnouncementId,
            updated.Origin);
    }

    // Manual check intentionally ignores AutoUpdateEnabled. That flag only gates automatic startup checks.
    public async Task<MaintenanceCheckResult> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        UpdateManifestSnapshot? manifest;
        try
        {
            manifest = await _updates.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            manifest = null;
        }
        return new MaintenanceCheckResult(manifest, UpdateDecisionService.Evaluate(_currentVersion, manifest));
    }

    public async Task<AnnouncementSnapshot?> GetAnnouncementAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var announcement = await _announcements.GetAsync(cancellationToken).ConfigureAwait(false);
            return announcement is null
                ? null
                : announcement with { LinkUrl = OnlineUriPolicy.NormalizeAbsoluteHttpsString(announcement.LinkUrl) };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<MaintenancePreferences> MarkAnnouncementSeenAsync(
        string announcementId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(announcementId);
        var id = announcementId.Trim();
        if (id.Length > 512 || id.Contains('\r') || id.Contains('\n'))
            throw new ArgumentException("Announcement id must be a single line of at most 512 characters.", nameof(announcementId));

        var updated = await _settings.UpdateAsync(
            settings => settings.Online.LastAnnouncementId = id,
            allowRecoveryRebuild: true,
            cancellationToken).ConfigureAwait(false);
        return new MaintenancePreferences(
            updated.Settings.Online.AutoUpdateEnabled,
            updated.Settings.Online.LastAnnouncementId,
            updated.Origin);
    }
}
