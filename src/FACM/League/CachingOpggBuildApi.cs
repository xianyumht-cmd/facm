using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    /// <summary>
    /// Small raw-payload cache shared by the visible Advisor and Gate 4 auto executor. This avoids a
    /// second identical OP.GG request when the Advisor observation that produced a stable snapshot is
    /// immediately followed by automatic application of that same recommendation.
    /// </summary>
    internal sealed class CachingOpggBuildApi : IOpggBuildApi, IDisposable
    {
        private sealed class Entry
        {
            public DateTime CachedUtc { get; set; }
            public byte[] Bytes { get; set; }
        }

        private readonly object _sync = new object();
        private readonly IOpggBuildApi _inner;
        private readonly bool _ownsInner;
        private readonly TimeSpan _duration;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, Entry> _cache =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public CachingOpggBuildApi(IOpggBuildApi inner, bool ownsInner)
            : this(inner, ownsInner, LeagueBuildAdvisorDataService.BuildCacheDuration)
        {
        }

        internal CachingOpggBuildApi(IOpggBuildApi inner, bool ownsInner, TimeSpan duration)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _ownsInner = ownsInner;
            _duration = duration <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : duration;
        }

        public async Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(path)) return null;

            var cached = FindFresh(path, DateTime.UtcNow);
            if (cached != null) return cached;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                cached = FindFresh(path, DateTime.UtcNow);
                if (cached != null) return cached;

                var bytes = await _inner.TryGetBytesAsync(path, cancellationToken).ConfigureAwait(false);
                if (bytes == null || bytes.Length == 0) return bytes;
                lock (_sync)
                {
                    _cache[path] = new Entry
                    {
                        CachedUtc = DateTime.UtcNow,
                        Bytes = bytes
                    };
                }
                return bytes;
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_sync) _cache.Clear();
            _gate.Dispose();
            if (_ownsInner)
            {
                var disposable = _inner as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
        }

        internal int CachedEntryCount
        {
            get { lock (_sync) return _cache.Count; }
        }

        private byte[] FindFresh(string path, DateTime utcNow)
        {
            lock (_sync)
            {
                Entry entry;
                if (!_cache.TryGetValue(path, out entry) || entry == null || entry.Bytes == null)
                    return null;
                if (utcNow - entry.CachedUtc >= _duration)
                {
                    _cache.Remove(path);
                    return null;
                }
                return entry.Bytes;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CachingOpggBuildApi));
        }
    }
}
