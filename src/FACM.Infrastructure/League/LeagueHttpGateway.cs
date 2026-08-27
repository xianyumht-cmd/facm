using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FACM.Core.League;

namespace FACM.Infrastructure.League;

/// <summary>
/// Authenticated localhost LCU transport. One gateway may serve both read and write contracts,
/// and both sides consume the exact same ILeagueTransportSessionSource.
/// </summary>
public sealed class LeagueHttpGateway : ILeagueReadGateway, ILeagueWriteGateway, IDisposable
{
    private readonly object _sync = new();
    private readonly ILeagueTransportSessionSource _sessions;
    private readonly Func<HttpMessageHandler> _handlerFactory;
    private readonly TimeSpan _requestTimeout;
    private readonly List<HttpClient> _retiredClients = [];
    private LeagueTransportSession? _clientSession;
    private HttpClient? _client;
    private bool _disposed;

    public LeagueHttpGateway(
        ILeagueTransportSessionSource sessions,
        TimeSpan? requestTimeout = null,
        Func<HttpMessageHandler>? handlerFactory = null)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(2);
        if (_requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        _handlerFactory = handlerFactory ?? CreateDefaultHandler;
    }

    public async Task<byte[]?> TryGetBytesAsync(string resourceKey, CancellationToken cancellationToken)
    {
        var path = NormalizeRelativePath(resourceKey);
        cancellationToken.ThrowIfCancellationRequested();
        var session = _sessions.GetSession();
        if (session is null) return null;

        try
        {
            var client = GetOrCreateClient(session);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);
            using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) _sessions.Invalidate(session);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _sessions.Invalidate(session);
            return null;
        }
        catch (HttpRequestException)
        {
            _sessions.Invalidate(session);
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    public async Task<LeagueWriteResult?> ExecuteAsync(LeagueWriteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var target = LeagueWriteTargetPolicy.Resolve(command);
        cancellationToken.ThrowIfCancellationRequested();
        var session = _sessions.GetSession();
        if (session is null) return null;

        try
        {
            var client = GetOrCreateClient(session);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);
            using var request = new HttpRequestMessage(new HttpMethod(target.Method), target.Path)
            {
                Content = new StringContent(command.Json ?? string.Empty, Encoding.UTF8, "application/json")
            };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) _sessions.Invalidate(session);
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            return new LeagueWriteResult((int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _sessions.Invalidate(session);
            return null;
        }
        catch (HttpRequestException)
        {
            _sessions.Invalidate(session);
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private HttpClient GetOrCreateClient(LeagueTransportSession session)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_client is not null && _clientSession is not null && _clientSession.Matches(session)) return _client;

            if (!session.BaseUri.IsLoopback || session.BaseUri.Scheme is not ("https" or "http"))
                throw new InvalidOperationException("LCU credentials may only be sent to loopback HTTP(S).");

            if (_client is not null) _retiredClients.Add(_client);
            var client = new HttpClient(_handlerFactory(), disposeHandler: true)
            {
                BaseAddress = session.BaseUri,
                Timeout = Timeout.InfiniteTimeSpan
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", session.CreateBasicAuthorizationParameter());
            _clientSession = session;
            _client = client;
            return client;
        }
    }

    private static HttpMessageHandler CreateDefaultHandler() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };

    private static string NormalizeRelativePath(string? path)
    {
        var value = (path ?? string.Empty).Trim();
        if (value.Length == 0) throw new ArgumentException("LCU resource path is required.", nameof(path));
        if (Uri.TryCreate(value, UriKind.Absolute, out _)) throw new ArgumentException("Absolute LCU URLs are not allowed.", nameof(path));
        return value.StartsWith('/') ? value : "/" + value;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _client?.Dispose();
            _client = null;
            _clientSession = null;
            foreach (var retired in _retiredClients) retired.Dispose();
            _retiredClients.Clear();
        }
    }
}
