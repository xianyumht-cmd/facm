using System.Net;
using System.Text;
using FACM.Core.Online;
using FACM.Core.Settings;
using FACM.Infrastructure.Online;

internal static class MaintenanceSmoke
{
    public static async Task RunAsync()
    {
        await ValidateRecoveryLoadDoesNotSaveAsync();
        await ValidateExplicitToggleRepairsPrimaryAsync();
        await ValidateManualCheckIgnoresAutoToggleAsync();
        await ValidateAnnouncementHttpsPolicyAsync();
    }

    private static async Task ValidateRecoveryLoadDoesNotSaveAsync()
    {
        var repository = new FakeSettingsRepository
        {
            Origin = SettingsLoadOrigin.RecoveredLastKnownGood,
            Document = Settings2Document.CreateDefault()
        };
        repository.Document.Online.AutoUpdateEnabled = false;
        repository.Document.Appearance.ThemeId = "obsidian-gold";
        var service = CreateService(repository, new FakeUpdateSource(), new FakeAnnouncementSource());

        var preferences = await service.LoadPreferencesAsync();

        Require(!preferences.AutoUpdateEnabled, "Recovered AutoUpdate value was not loaded.");
        Require(preferences.LoadedFromRecovery, "Recovery origin was not surfaced.");
        Require(repository.SaveCalls == 0, "Loading recovered maintenance settings must not overwrite primary settings.");
    }

    private static async Task ValidateExplicitToggleRepairsPrimaryAsync()
    {
        var repository = new FakeSettingsRepository
        {
            Origin = SettingsLoadOrigin.RecoveryDefaults,
            Document = Settings2Document.CreateDefault()
        };
        repository.Document.Appearance.ThemeId = "obsidian-gold";
        repository.Document.Environment.GamePath = @"C:\Games\League";
        var service = CreateService(repository, new FakeUpdateSource(), new FakeAnnouncementSource());

        var preferences = await service.SetAutoUpdateEnabledAsync(false);

        Require(repository.SaveCalls == 1, "Explicit auto-update toggle must persist exactly once.");
        Require(!repository.Document.Online.AutoUpdateEnabled, "Explicit toggle was not persisted.");
        Require(repository.Document.Appearance.ThemeId == "obsidian-gold", "Maintenance save overwrote another Settings2 section.");
        Require(repository.Document.Environment.GamePath == @"C:\Games\League", "Maintenance save overwrote environment settings.");
        Require(!preferences.LoadedFromRecovery, "Explicit user save should rebuild a normal primary settings state.");
    }

    private static async Task ValidateManualCheckIgnoresAutoToggleAsync()
    {
        var repository = new FakeSettingsRepository { Document = Settings2Document.CreateDefault() };
        repository.Document.Online.AutoUpdateEnabled = false;
        var updates = new FakeUpdateSource
        {
            Manifest = new UpdateManifestSnapshot(
                true,
                "4.1.0",
                "4.0.0",
                false,
                "https://github.com/xianyumht-cmd/facm/releases/download/v4.1.0/FACM.App.exe",
                new string('a', 64),
                "notes",
                "2026-08-28")
        };
        var service = CreateService(repository, updates, new FakeAnnouncementSource());

        var result = await service.CheckNowAsync();

        Require(updates.Calls == 1, "Manual update check must call the manifest source even when auto-update is disabled.");
        Require(result.Decision.UpdateAvailable && result.Decision.LatestVersion == new Version(4, 1, 0),
            "Manual update decision did not surface the available release.");
    }

    private static async Task ValidateAnnouncementHttpsPolicyAsync()
    {
        Require(OnlineUriPolicy.NormalizeAbsoluteHttps("https://example.com/details") is not null,
            "Absolute HTTPS announcement detail must be allowed.");
        Require(OnlineUriPolicy.NormalizeAbsoluteHttps("http://example.com/details") is null,
            "HTTP announcement detail must be rejected.");
        Require(OnlineUriPolicy.NormalizeAbsoluteHttps("https://localhost/details") is null,
            "Loopback announcement detail must be rejected.");
        Require(OnlineUriPolicy.NormalizeAbsoluteHttps("/relative") is null,
            "Relative announcement detail must be rejected.");

        const string json = "{\"enabled\":true,\"id\":\"notice-1\",\"title\":\"Title\",\"body\":\"Body\",\"level\":\"info\",\"popup\":true,\"updated_at\":\"2026-08-28\",\"link_url\":\"http://unsafe.example/details\"}";
        using var source = new HttpAnnouncementSource(new StaticJsonHandler(json));
        var announcement = await source.GetAsync();
        Require(announcement is not null && announcement.Id == "notice-1", "Announcement source did not parse the fixed-origin payload.");
        Require(announcement!.LinkUrl.Length == 0 && announcement.DetailUri is null,
            "Invalid announcement detail link must be removed without discarding the announcement body.");
    }

    private static MaintenanceApplicationService CreateService(
        ISettings2Repository settings,
        IUpdateManifestSource updates,
        IAnnouncementSource announcements) =>
        new(settings, updates, announcements, new Version(4, 0, 0));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeSettingsRepository : ISettings2Repository
    {
        public Settings2Document Document { get; set; } = Settings2Document.CreateDefault();
        public SettingsLoadOrigin Origin { get; set; } = SettingsLoadOrigin.ExistingV2;
        public int SaveCalls { get; private set; }

        public Task<Settings2LoadResult> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Settings2LoadResult(Document, Origin));
        }

        public Task SaveAsync(Settings2Document settings, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Settings2Validator.ThrowIfInvalid(settings);
            SaveCalls++;
            Document = settings;
            Origin = SettingsLoadOrigin.ExistingV2;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUpdateSource : IUpdateManifestSource
    {
        public UpdateManifestSnapshot? Manifest { get; set; }
        public int Calls { get; private set; }

        public Task<UpdateManifestSnapshot?> GetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Manifest);
        }
    }

    private sealed class FakeAnnouncementSource : IAnnouncementSource
    {
        public Task<AnnouncementSnapshot?> GetAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AnnouncementSnapshot?>(null);
        }
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(request.RequestUri == HttpAnnouncementSource.ProductionAnnouncementUri,
                "Announcement adapter must use the fixed GitHub raw production origin.");
            var bytes = Encoding.UTF8.GetBytes(json);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
            response.Content.Headers.ContentLength = bytes.Length;
            return Task.FromResult(response);
        }
    }
}
