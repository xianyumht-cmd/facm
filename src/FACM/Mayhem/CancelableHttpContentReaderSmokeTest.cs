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
                VerifyStalledBodyCanBeCancelled(false);
                VerifyStalledBodyCanBeCancelled(true);
                VerifyUncancelledInvalidOperationIsNotHidden();
                VerifySizeLimit();
                VerifyOptionalSourceDeadline();
                VerifyEarlyReturnCleanup();
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 10;
            }
        }

        private static void VerifyOptionalSourceDeadline()
        {
            using (var deadline = new CancellationTokenSource())
            using (var caller = new CancellationTokenSource())
            {
                deadline.Cancel();
                var completed = CancelableHttpContentReader.AwaitOptionalSourceAsync(
                    Task.FromResult("usable"), caller.Token);
                var slow = CancelableHttpContentReader.AwaitOptionalSourceAsync(
                    Task.FromCanceled<string>(deadline.Token), caller.Token);
                var results = Task.WhenAll(completed, slow).GetAwaiter().GetResult();
                if (results[0] != "usable" || results[1] != null)
                    throw new InvalidOperationException("An optional source deadline discarded usable data.");

                caller.Cancel();
                var cancelled = false;
                try
                {
                    CancelableHttpContentReader.AwaitOptionalSourceAsync(
                        Task.FromResult("usable"), caller.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) { cancelled = true; }
                if (!cancelled)
                    throw new InvalidOperationException("Optional source recovery ignored caller cancellation.");
            }
        }

        private static void VerifyEarlyReturnCleanup()
        {
            using (var scope = new CancellationTokenSource())
            {
                var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (scope.Token.Register(() => pending.TrySetCanceled()))
                {
                    var success = Task.FromResult("complete");
                    CancelableHttpContentReader.CancelAndObserveSourcesAsync(scope,
                        new Task[] { success, pending.Task }).GetAwaiter().GetResult();
                    if (!pending.Task.IsCanceled || success.Result != "complete" || !scope.IsCancellationRequested)
                        throw new InvalidOperationException("Source scope leaked an unfinished request on early return.");
                }
            }
            if (!CancelableHttpContentReader.IsCancellationSideEffect(
                    new System.Net.Http.HttpRequestException("fixture", new ObjectDisposedException("fixture"))) ||
                !CancelableHttpContentReader.IsCancellationSideEffect(new System.Net.WebException("fixture")) ||
                CancelableHttpContentReader.IsCancellationSideEffect(new FormatException("fixture")))
                throw new InvalidOperationException("Response transport cancellation classification regressed.");
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

        private static void VerifyStalledBodyCanBeCancelled(bool plainInvalidOperation)
        {
            using (var stream = new StalledAfterFirstReadStream(plainInvalidOperation))
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
                catch (OperationCanceledException exception)
                {
                    cancelled = exception.CancellationToken == cancellation.Token;
                }
                stopwatch.Stop();

                if (!cancelled)
                    throw new InvalidOperationException("Stalled response body was not cancelled.");
                if (stopwatch.Elapsed > TimeSpan.FromSeconds(2))
                    throw new TimeoutException("Stalled response body cancellation took too long: " + stopwatch.Elapsed + ".");
            }
        }

        private static void VerifyUncancelledInvalidOperationIsNotHidden()
        {
            var failure = new InvalidOperationException("fixture: stream failed before cancellation");
            if (CancelableHttpContentReader.IsCancellationSideEffect(failure) ||
                !CancelableHttpContentReader.IsCancellationSideEffect(failure, true))
                throw new InvalidOperationException("Plain invalid operation classification escaped the body reader.");
            using (var stream = new StalledAfterFirstReadStream(true, failure))
            using (var content = new StreamContent(stream))
            {
                var observed = false;
                try
                {
                    CancelableHttpContentReader.ReadBytesAsync(content, CancellationToken.None, 1024)
                        .GetAwaiter().GetResult();
                }
                catch (InvalidOperationException exception) { observed = ReferenceEquals(exception, failure); }
                if (!observed)
                    throw new InvalidOperationException("Uncancelled stream failure was not preserved.");
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
            private readonly bool _plainInvalidOperation;
            private readonly Exception _initialFailure;

            internal StalledAfterFirstReadStream(bool plainInvalidOperation, Exception initialFailure = null)
            {
                _plainInvalidOperation = plainInvalidOperation;
                _initialFailure = initialFailure;
            }

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
                    if (_initialFailure != null) return Task.FromException<int>(_initialFailure);
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
                    _blocked.TrySetException(_plainInvalidOperation
                        ? new InvalidOperationException("fixture: response stream closed")
                        : new ObjectDisposedException(nameof(StalledAfterFirstReadStream)));
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
