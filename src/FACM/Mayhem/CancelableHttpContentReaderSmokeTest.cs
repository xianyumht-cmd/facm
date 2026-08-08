using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.Mayhem
{
    internal static class CancelableHttpContentReaderSmokeTest
    {
        public static int Run()
        {
            try
            {
                VerifyNormalRead();
                VerifyStalledBodyCanBeCancelled();
                VerifySizeLimit();
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 10;
            }
        }

        private static void VerifyNormalRead()
        {
            using (var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("FACM-body-ok")))
            {
                var text = CancelableHttpContentReader
                    .ReadStringAsync(content, CancellationToken.None, 1024)
                    .GetAwaiter()
                    .GetResult();
                if (!string.Equals(text, "FACM-body-ok", StringComparison.Ordinal))
                    throw new InvalidOperationException("Cancelable HTTP reader changed normal response content.");
            }
        }

        private static void VerifyStalledBodyCanBeCancelled()
        {
            using (var stream = new StalledAfterFirstReadStream())
            using (var content = new StreamContent(stream))
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.CancelAfter(180);
                var stopwatch = Stopwatch.StartNew();
                var cancelled = false;
                try
                {
                    CancelableHttpContentReader
                        .ReadBytesAsync(content, cancellation.Token, 1024)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                stopwatch.Stop();

                if (!cancelled)
                    throw new InvalidOperationException("Stalled response body was not cancelled.");
                if (stopwatch.Elapsed > TimeSpan.FromSeconds(2))
                    throw new TimeoutException("Stalled response body cancellation took too long: " + stopwatch.Elapsed + ".");
            }
        }

        private static void VerifySizeLimit()
        {
            using (var content = new ByteArrayContent(new byte[2048]))
            {
                content.Headers.ContentLength = 2048;
                var rejected = false;
                try
                {
                    CancelableHttpContentReader
                        .ReadBytesAsync(content, CancellationToken.None, 1024)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }
                if (!rejected)
                    throw new InvalidOperationException("HTTP response size limit was not enforced.");
            }
        }

        private sealed class StalledAfterFirstReadStream : Stream
        {
            private readonly TaskCompletionSource<int> _blocked = new TaskCompletionSource<int>();
            private bool _firstRead = true;
            private bool _disposed;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(StalledAfterFirstReadStream));
                if (_firstRead)
                {
                    _firstRead = false;
                    buffer[offset] = 0x46;
                    return Task.FromResult(1);
                }

                // Deliberately ignore the token. The production reader must still be able to break a
                // body read like this by disposing the stream from its cancellation registration.
                return _blocked.Task;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_disposed)
                {
                    _disposed = true;
                    _blocked.TrySetException(new ObjectDisposedException(nameof(StalledAfterFirstReadStream)));
                }
                base.Dispose(disposing);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }
    }
}
