using System.Diagnostics;
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
    private readonly Action<LeagueHttpDiagnostic>? _diagnosticReporter;
    private readonly Func<LeagueGameflowSnapshot?>? _gameflowProvider;
    private readonly List<HttpClient> _retiredClients = [];
    private LeagueTransportSession? _clientSession;
    private HttpClient? _client;
    private int _inFlight;
    private int _maxInFlightObserved;
    private bool _disposed;

    public LeagueHttpGateway(
        ILeagueTransportSessionSource sessions,
        TimeSpan? requestTimeout = null,
        Func<HttpMessageHandler>? handlerFactory = null,
        Action<LeagueHttpDiagnostic>? diagnosticReporter = null,
        Func<LeagueGameflowSnapshot?>? gameflowProvider = null)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(2);
        if (_requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        _handlerFactory = handlerFactory ?? CreateDefaultHandler;
        _diagnosticReporter = diagnosticReporter;
        _gameflowProvider = gameflowProvider;
    }

    public async Task<byte[]?> TryGetBytesAsync(string resourceKey, CancellationToken cancellationToken)
    {
        var path = NormalizeRelativePath(resourceKey);
        var trace = BeginRequest("GET", path);
        var outcome = "unhandled-exception";
        var statusCode = (int?)null;
        var sessionInvalidated = false;
        var notFoundClassification = string.Empty;
        var gameflowPhase = string.Empty;
        var exceptionType = string.Empty;
        var hResult = string.Empty;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                outcome = "no-session";
                return null;
            }

            try
            {
                var client = GetOrCreateClient(session);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_requestTimeout);
                using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                statusCode = (int)response.StatusCode;
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    InvalidateSession(session, "unauthorized");
                    sessionInvalidated = true;
                }
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        gameflowPhase = TryReadGameflowPhase();
                        notFoundClassification = LeagueEndpointAvailabilityPolicy.Classify404(
                            path,
                            gameflowPhase,
                            LeagueConnectionState.Connected).ToString();
                        outcome = notFoundClassification == nameof(League404Classification.ExpectedUnavailable)
                            ? "expected-unavailable"
                            : "http-failure";
                        return null;
                    }
                    outcome = response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized => "http-unauthorized",
                        HttpStatusCode.Forbidden => "http-forbidden",
                        _ => "http-failure"
                    };
                    return null;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
                outcome = "success";
                return bytes;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                exceptionType = exception.GetType().Name;
                hResult = FormatHResult(exception);
                InvalidateSession(session, "timeout");
                sessionInvalidated = true;
                outcome = "timeout";
                return null;
            }
            catch (HttpRequestException exception)
            {
                exceptionType = exception.GetType().Name;
                hResult = FormatHResult(exception);
                InvalidateSession(session, "connection-refused");
                sessionInvalidated = true;
                outcome = "http-exception";
                return null;
            }
            catch (ObjectDisposedException exception)
            {
                exceptionType = exception.GetType().Name;
                hResult = FormatHResult(exception);
                outcome = "disposed";
                return null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "caller-cancelled";
            throw;
        }
        catch (Exception exception)
        {
            exceptionType = exception.GetType().Name;
            hResult = FormatHResult(exception);
            outcome = "unhandled-exception";
            throw;
        }
        finally
        {
            EndRequest(trace, statusCode, outcome, sessionInvalidated, notFoundClassification, gameflowPhase, exceptionType, hResult);
        }
    }

    public async Task<LeagueWriteResult?> ExecuteAsync(LeagueWriteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var target = LeagueWriteTargetPolicy.Resolve(command);
        var trace = BeginRequest(target.Method, target.Path);
        var outcome = "unhandled-exception";
        var statusCode = (int?)null;
        var sessionInvalidated = false;
        var notFoundClassification = string.Empty;
        var gameflowPhase = string.Empty;
        var exceptionType = string.Empty;
        var hResult = string.Empty;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                outcome = "no-session";
                return null;
            }

            try
            {
                var client = GetOrCreateClient(session);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_requestTimeout);
                using var request = new HttpRequestMessage(new HttpMethod(target.Method), target.Path);
                if (command.Json is not null)
                    request.Content = new StringContent(command.Json, Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                statusCode = (int)response.StatusCode;
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    InvalidateSession(session, "unauthorized");
                    sessionInvalidated = true;
                }

                var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    gameflowPhase = TryReadGameflowPhase();
                    notFoundClassification = LeagueEndpointAvailabilityPolicy.Classify404(
                        target.Path,
                        gameflowPhase,
                        LeagueConnectionState.Connected).ToString();
                }
                outcome = response.IsSuccessStatusCode ? "success" : response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "http-unauthorized",
                    HttpStatusCode.Forbidden => "http-forbidden",
                    HttpStatusCode.NotFound when notFoundClassification == nameof(League404Classification.ExpectedUnavailable) => "expected-unavailable",
                    _ => "http-failure"
                };
                return new LeagueWriteResult((int)response.StatusCode, body);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                exceptionType = exception.GetType().Name;
                hResult = FormatHResult(exception);
                InvalidateSession(session, "timeout");
                sessionInvalidated = true;
                outcome = "timeout";
                return null;
            }
            catch (HttpRequestException exception)
            {
                exceptionType = exception.GetType().Name;
                hResult = FormatHResult(exception);
                InvalidateSession(session, "connection-refused");
                sessionInvalidated = true;
                outcome = "http-exception";
                return null;
            }
            catch (ObjectDisposedException exception)
            {
                exceptionType = exception.GetType().Name;
                hResult = FormatHResult(exception);
                outcome = "disposed";
                return null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "caller-cancelled";
            throw;
        }
        catch (Exception exception)
        {
            exceptionType = exception.GetType().Name;
            hResult = FormatHResult(exception);
            outcome = "unhandled-exception";
            throw;
        }
        finally
        {
            EndRequest(trace, statusCode, outcome, sessionInvalidated, notFoundClassification, gameflowPhase, exceptionType, hResult);
        }
    }

    private RequestTrace BeginRequest(string method, string path)
    {
        var context = LeagueDiagnosticContext.Current;
        var trace = new RequestTrace(
            Guid.NewGuid().ToString("N"),
            context?.CorrelationId ?? LeagueDiagnosticContext.CreateCorrelationId(),
            context?.Source ?? "league",
            context?.Phase ?? "transport",
            method,
            LeagueEndpointRedactor.Redact(path),
            DateTimeOffset.UtcNow,
            Stopwatch.GetTimestamp(),
            BeginInFlight());
        ReportDiagnostic(trace, "started", trace.StartedUtc, 0, null, "started", false, trace.InFlightAtStart);
        return trace;
    }

    private Task<LeagueTransportSession?> GetSessionAsync(CancellationToken cancellationToken) =>
        _sessions is IAsyncLeagueTransportSessionSource asyncSource
            ? asyncSource.GetSessionAsync(cancellationToken: cancellationToken)
            : Task.FromResult(_sessions.GetSession());

    private void InvalidateSession(LeagueTransportSession session, string reason)
    {
        if (_sessions is IReasonedLeagueTransportSessionInvalidator reasoned)
            reasoned.Invalidate(session, reason);
        else
            _sessions.Invalidate(session);
    }

    private void EndRequest(
        RequestTrace trace,
        int? statusCode,
        string outcome,
        bool sessionInvalidated,
        string notFoundClassification,
        string gameflowPhase,
        string exceptionType,
        string hResult)
    {
        var finishedUtc = DateTimeOffset.UtcNow;
        var durationMs = Math.Max(0L, (long)Stopwatch.GetElapsedTime(trace.StartTimestamp).TotalMilliseconds);
        var inFlightAtEnd = Interlocked.Decrement(ref _inFlight);
        ReportDiagnostic(
            trace,
            "completed",
            finishedUtc,
            durationMs,
            statusCode,
            outcome,
            sessionInvalidated,
            inFlightAtEnd,
            notFoundClassification,
            gameflowPhase,
            exceptionType,
            hResult);
    }

    private int BeginInFlight()
    {
        var current = Interlocked.Increment(ref _inFlight);
        while (true)
        {
            var observed = Volatile.Read(ref _maxInFlightObserved);
            if (current <= observed || Interlocked.CompareExchange(ref _maxInFlightObserved, current, observed) == observed)
                return current;
        }
    }

    private void ReportDiagnostic(
        RequestTrace trace,
        string eventName,
        DateTimeOffset timestampUtc,
        long durationMs,
        int? statusCode,
        string outcome,
        bool sessionInvalidated,
        int inFlightAtEnd,
        string notFoundClassification = "",
        string gameflowPhase = "",
        string exceptionType = "",
        string hResult = "")
    {
        try
        {
            _diagnosticReporter?.Invoke(new LeagueHttpDiagnostic(
                trace.RequestId,
                trace.CorrelationId,
                trace.Source,
                trace.Phase,
                eventName,
                trace.Method,
                trace.Endpoint,
                trace.StartedUtc,
                timestampUtc,
                durationMs,
                statusCode,
                outcome,
             sessionInvalidated,
             trace.InFlightAtStart,
             inFlightAtEnd,
             Volatile.Read(ref _maxInFlightObserved),
             notFoundClassification,
             gameflowPhase,
             exceptionType,
             hResult,
             Environment.CurrentManagedThreadId));
        }
        catch
        {
            // Diagnostics must never change transport behavior.
        }
    }

    private sealed record RequestTrace(
        string RequestId,
        string CorrelationId,
        string Source,
        string Phase,
        string Method,
        string Endpoint,
        DateTimeOffset StartedUtc,
        long StartTimestamp,
        int InFlightAtStart);

    public int InFlightCount => Math.Max(0, Volatile.Read(ref _inFlight));

    public int MaxInFlightObserved => Math.Max(0, Volatile.Read(ref _maxInFlightObserved));

    private string TryReadGameflowPhase()
    {
        try { return _gameflowProvider?.Invoke()?.Phase ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string FormatHResult(Exception exception) =>
        "0x" + exception.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

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
