namespace FACM.Core.Maintenance;

public sealed record LogOpenResult(bool Started, string Path, string Reason);

public interface ILogFileOpener
{
    Task<LogOpenResult> OpenAsync(CancellationToken cancellationToken = default);
}

public enum SingleInstanceDisposition
{
    Primary,
    ExistingSignaled,
    ExistingUnresponsive
}

public interface ISingleInstanceGate : IDisposable
{
    SingleInstanceDisposition EnterNormal(Action onActivated, TimeSpan signalTimeout);
}
