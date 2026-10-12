using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.Mayhem
{
    internal static class CancelableHttpContentReader
    {
        internal const int DefaultTextLimitBytes = 12 * 1024 * 1024;
        internal const int DefaultImageLimitBytes = 20 * 1024 * 1024;

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
                cancellationToken.ThrowIfCancellationRequested();

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

        private static bool IsCancellationSideEffect(Exception exception)
        {
            return exception is OperationCanceledException ||
                   exception is ObjectDisposedException ||
                   exception is IOException ||
                   exception is HttpRequestException;
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
