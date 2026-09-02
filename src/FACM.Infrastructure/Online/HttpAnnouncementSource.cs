using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using FACM.Core.Online;

namespace FACM.Infrastructure.Online;

public sealed class HttpAnnouncementSource : IAnnouncementSource, IDisposable
{
    public static readonly Uri ProductionAnnouncementUri = new(
        "https://raw.githubusercontent.com/xianyumht-cmd/facm/main/online/announcement.json",
        UriKind.Absolute);

    public const int DefaultMaxMetadataBytes = 128 * 1024;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    private readonly int _maxMetadataBytes;

    public HttpAnnouncementSource(
        HttpMessageHandler? handler = null,
        TimeSpan? timeout = null,
        int maxMetadataBytes = DefaultMaxMetadataBytes)
    {
        if (maxMetadataBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxMetadataBytes));
        _timeout = timeout ?? DefaultTimeout;
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        _maxMetadataBytes = maxMetadataBytes;

        handler ??= new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM-Windows/4.0");
        _client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
    }

    public async Task<AnnouncementSnapshot?> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, ProductionAnnouncementUri);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var length = response.Content.Headers.ContentLength;
        if (length.HasValue && length.Value > _maxMetadataBytes)
            throw new InvalidDataException("FACM announcement metadata response is too large.");

        using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(chunk.AsMemory(0, chunk.Length), timeout.Token).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > _maxMetadataBytes)
                throw new InvalidDataException("FACM announcement metadata response is too large.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
        }

        var dto = JsonSerializer.Deserialize<AnnouncementDto>(buffer.ToArray())
            ?? throw new InvalidDataException("FACM announcement metadata is empty.");
        return new AnnouncementSnapshot(
            dto.Enabled,
            SingleLine(dto.Id, 512),
            SingleLine(dto.Title, 1024),
            dto.Body?.Trim() ?? string.Empty,
            SingleLine(dto.Level, 64),
            dto.Popup,
            SingleLine(dto.UpdatedAt, 128),
            OnlineUriPolicy.NormalizeAbsoluteHttpsString(dto.LinkUrl));
    }

    private static string SingleLine(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    public void Dispose() => _client.Dispose();

    private sealed class AnnouncementDto
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; init; }
        [JsonPropertyName("id")]
        public string? Id { get; init; }
        [JsonPropertyName("title")]
        public string? Title { get; init; }
        [JsonPropertyName("body")]
        public string? Body { get; init; }
        [JsonPropertyName("level")]
        public string? Level { get; init; }
        [JsonPropertyName("popup")]
        public bool Popup { get; init; }
        [JsonPropertyName("updated_at")]
        public string? UpdatedAt { get; init; }
        [JsonPropertyName("link_url")]
        public string? LinkUrl { get; init; }
    }
}
