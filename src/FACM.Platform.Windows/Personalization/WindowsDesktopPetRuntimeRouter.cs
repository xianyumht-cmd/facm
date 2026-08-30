using FACM.Core.Personalization;

namespace FACM.Platform.Windows.Personalization;

public sealed class WindowsDesktopPetRuntimeRouter : IDesktopPetRuntime, IDisposable
{
    private readonly object _stateSync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly WindowsFlyingPetRuntime _flying;
    private readonly WindowsVPetRuntime _vpet;
    private DesktopPetRuntimeState _current = new(false, false, string.Empty, "launcher-only");
    private FacmPetRuntimeKind? _activeKind;
    private bool _disposed;

    public WindowsDesktopPetRuntimeRouter(WindowsFlyingPetRuntime flying, WindowsVPetRuntime vpet)
    {
        _flying = flying ?? throw new ArgumentNullException(nameof(flying));
        _vpet = vpet ?? throw new ArgumentNullException(nameof(vpet));
        _flying.StateChanged += OnFlyingStateChanged;
        _vpet.StateChanged += OnVPetStateChanged;
    }

    public DesktopPetRuntimeState Current
    {
        get
        {
            lock (_stateSync) return _current;
        }
    }

    public event EventHandler<DesktopPetRuntimeState>? StateChanged;

    public async Task<DesktopPetModeResult> ApplyAsync(
        bool enabled,
        FacmPetDefinition pet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pet);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!enabled)
            {
                SetActiveKind(null);
                _ = await _flying.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
                _ = await _vpet.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
                Publish(new DesktopPetRuntimeState(false, false, string.Empty, "launcher-restored"));
                return new DesktopPetModeResult(true, false, "launcher-restored");
            }

            if (pet.Runtime is not (FacmPetRuntimeKind.FlyingSprite or FacmPetRuntimeKind.VPetCore))
            {
                SetActiveKind(null);
                _ = await _flying.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
                _ = await _vpet.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
                var detail = "runtime-unsupported:" + pet.Runtime;
                Publish(new DesktopPetRuntimeState(false, false, string.Empty, detail));
                return new DesktopPetModeResult(false, false, detail);
            }

            var targetKind = pet.Runtime;
            SetActiveKind(null);
            if (targetKind == FacmPetRuntimeKind.FlyingSprite)
                _ = await _vpet.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
            else
                _ = await _flying.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);

            SetActiveKind(targetKind);
            var target = targetKind == FacmPetRuntimeKind.FlyingSprite
                ? (IDesktopPetRuntime)_flying
                : _vpet;
            try
            {
                var result = await target.ApplyAsync(true, pet, cancellationToken).ConfigureAwait(false);
                Publish(target.Current);
                if (!result.Success) SetActiveKind(null);
                return result;
            }
            catch
            {
                // A cancellation or unexpected target failure must not leave the router claiming that
                // a host is still active. The underlying runtime retains its own fail-soft cleanup.
                SetActiveKind(null);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetPositionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (GetActiveKind() == FacmPetRuntimeKind.VPetCore)
                await _vpet.ResetPositionAsync(cancellationToken).ConfigureAwait(false);
            else
                await _flying.ResetPositionAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnFlyingStateChanged(object? sender, DesktopPetRuntimeState state)
    {
        if (GetActiveKind() == FacmPetRuntimeKind.FlyingSprite) Publish(state);
    }

    private void OnVPetStateChanged(object? sender, DesktopPetRuntimeState state)
    {
        if (GetActiveKind() == FacmPetRuntimeKind.VPetCore) Publish(state);
    }

    private FacmPetRuntimeKind? GetActiveKind()
    {
        lock (_stateSync) return _activeKind;
    }

    private void SetActiveKind(FacmPetRuntimeKind? kind)
    {
        lock (_stateSync) _activeKind = kind;
    }

    private void Publish(DesktopPetRuntimeState state)
    {
        lock (_stateSync) _current = state;
        try { StateChanged?.Invoke(this, state); } catch { }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SetActiveKind(null);
        _flying.StateChanged -= OnFlyingStateChanged;
        _vpet.StateChanged -= OnVPetStateChanged;
        _flying.Dispose();
        _vpet.Dispose();
        StateChanged = null;
    }
}
