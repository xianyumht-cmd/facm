namespace FACM.Core.Repair;

public sealed record RepairToolLaunchResult(
    bool Started,
    string State,
    string Message,
    int? ProcessId = null);

public interface IRepairToolService
{
    string DriverCleanupExpectedSha256 { get; }

    RepairToolLaunchResult LaunchDriverCleanup();
}
