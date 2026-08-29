using FACM.Core.Personalization;

namespace FACM.Platform.Windows.Personalization;

public sealed class WindowsDesktopPetRuntimeRouter : IDesktopPetRuntime, IDisposable
{
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

    public DesktopPetRuntimeState Current => _current;

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
                _activeKind = null;
                _ = await _flying.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
                _ = await _vpet.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
                Publish(new DesktopPetRuntimeState(false, false, string.Empty, "launcher-restored"));
                return new DesktopPetModeResult(true, false, "launcher-restored");
            }

            if (pet.Runtime is not (FacmPetRuntimeKind.FlyingSprite or FacmPetRuntimeKind.VPetCore))
            {
                _activeKind = null;
                _ = await _flying.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
                _ = await _vpet.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
                var detail = "runtime-unsupported:" + pet.Runtime;
                Publish(new DesktopPetRuntimeState(false, false, string.Empty, detail));
                return new DesktopPetModeResult(false, false, detail);
            }

            var targetKind = pet.Runtime;
            _activeKind = null;
            if (targetKind == FacmPetRuntimeKind.FlyingSprite)
                _ = await _vpet.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
            else
                _ = await _flying.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);

            _activeKind = targetKind;
            var target = targetKind == FacmPetRuntimeKind.FlyingSprite
                ? (IDesktopPetRuntime)_flying
                : _vpet;
            var result = await target.ApplyAsync(true, pet, cancellationToken).ConfigureAwait(false);
            Publish(target.Current);
            if (!result.Success) _activeKind = null;
            return result;
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
            if (_activeKind == FacmPetRuntimeKind.VPetCore)
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
        if (_activeKind == FacmPetRuntimeKind.FlyingSprite) Publish(state);
    }

    private void OnVPetStateChanged(object? sender, DesktopPetRuntimeState state)
    {
        if (_activeKind == FacmPetRuntimeKind.VPetCore) Publish(state);
    }

    private void Publish(DesktopPetRuntimeState state)
    {
        _current = state;
        try { StateChanged?.Invoke(this, state); } catch { }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flying.StateChanged -= OnFlyingStateChanged;
        _vpet.StateChanged -= OnVPetStateChanged;
        _flying.Dispose();
        _vpet.Dispose();
        StateChanged = null;
    }
}
