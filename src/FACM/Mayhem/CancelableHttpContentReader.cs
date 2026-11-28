using System;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Collections.Generic;
using FACM.Services;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.Mayhem
{
    internal static class CancelableHttpContentReader
    {
        internal const int DefaultTextLimitBytes = 12 * 1024 * 1024;
        internal const int DefaultImageLimitBytes = 20 * 1024 * 1024;

        // An optional source timing out must not discard other completed sources.
        // Only the original caller's cancellation aborts the whole operation.
        internal static async Task<T> AwaitOptionalSourceAsync<T>(Task<T> source,
            CancellationToken callerToken) where T : class
        {
            try
            {
                var result = await source.ConfigureAwait(false);
                callerToken.ThrowIfCancellationRequested();
                return result;
            }
            catch (OperationCanceledException)
            {
                callerToken.ThrowIfCancellationRequested();
                return null;
            }
        }

        public static async Task<string> ReadStringAsync(
            HttpContent content,
            CancellationToken cancellationToken,
            int maxBytes = DefaultTextLimitBytes)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            var bytes = await ReadBytesAsync(content, cancellationToken, maxBytes).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0) return string.Empty;

            Encoding encoding = Encoding.UTF8;
            try
            {
                var charset = content.Headers.ContentType == null ? null : content.Headers.ContentType.CharSet;
                if (!string.IsNullOrWhiteSpace(charset))
                    encoding = Encoding.GetEncoding(charset.Trim().Trim('"'));
            }
            catch
            {
                encoding = Encoding.UTF8;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return encoding.GetString(bytes);
        }

        public static async Task<byte[]> ReadBytesAsync(
            HttpContent content,
            CancellationToken cancellationToken,
            int maxBytes = DefaultImageLimitBytes)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            cancellationToken.ThrowIfCancellationRequested();

            var contentLength = content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > maxBytes)
                throw new InvalidDataException("HTTP 响应正文超过允许大小：" + contentLength.Value + " bytes。");

            Stream input = null;
            try
            {
                input = await content.ReadAsStreamAsync().ConfigureAwait(false);
                using (input)
                using (var registration = cancellationToken.Register(SafeDispose, input))
                using (var output = new MemoryStream(contentLength.HasValue && contentLength.Value > 0 && contentLength.Value <= maxBytes
                    ? (int)contentLength.Value
                    : 0))
                {
                    var buffer = new byte[81920];
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int read;
                        try
                        {
                            read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            if (cancellationToken.IsCancellationRequested && IsCancellationSideEffect(exception))
                                throw new OperationCanceledException("HTTP response body read was cancelled.", exception, cancellationToken);
                            throw;
                        }

                        if (read <= 0) break;
                        if (output.Length + read > maxBytes)
                            throw new InvalidDataException("HTTP 响应正文超过允许大小：" + maxBytes + " bytes。");
                        output.Write(buffer, 0, read);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    return output.ToArray();
                }
            }
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested && IsCancellationSideEffect(exception))
                    throw new OperationCanceledException("HTTP response body read was cancelled.", exception, cancellationToken);
                throw;
            }
        }

        internal static bool IsCancellationSideEffect(Exception exception)
        {
            for (var cause = exception; cause != null; cause = cause.InnerException)
            {
                if (cause is OperationCanceledException || cause is ObjectDisposedException ||
                    cause is IOException || cause is HttpRequestException || cause is WebException)
                    return true;
            }
            return false;
        }

        internal static async Task CancelAndObserveSourcesAsync(CancellationTokenSource scope, IEnumerable<Task> sources)
        {
            // Keep the parent cancellation source alive until every started child has settled.
            try { scope.Cancel(); }
            catch (AggregateException exception)
            {
                AppLog.Info("Mayhem source cancellation callback failed: " + exception.GetType().Name);
            }
            try { await Task.WhenAll(sources).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Mayhem source cleanup observed: " + exception.GetType().Name);
            }
        }

        private static void SafeDispose(object state)
        {
            try
            {
                var stream = state as Stream;
                if (stream != null) stream.Dispose();
            }
            catch
            {
            }
        }
    }
}
