namespace FACM.Core.Cleanup;

public enum CleanupRuleKind
{
    ProgramFilesDirectory,
    ProgramDataDirectory,
    ContainerChild,
    ExtraDirectory,
    LogFile
}

public enum CleanupTargetKind
{
    File,
    Directory
}

public sealed record CleanupTarget(
    string Path,
    string Group,
    CleanupRuleKind Rule,
    CleanupTargetKind Kind,
    long EstimatedBytes,
    int FileCount,
    int DirectoryCount,
    bool Blocked,
    string Detail);

public sealed record CleanupPlanSummary(
    int TargetCount,
    int FileCount,
    int DirectoryCount,
    long EstimatedBytes,
    int BlockedCount);

public sealed record CleanupPlan(string GameRoot, IReadOnlyList<CleanupTarget> Targets)
{
    public IReadOnlyList<CleanupTarget> DeletableTargets => Targets.Where(static target => !target.Blocked).ToArray();
    public IReadOnlyList<CleanupTarget> BlockedTargets => Targets.Where(static target => target.Blocked).ToArray();
    public long EstimatedBytes => DeletableTargets.Sum(static target => target.EstimatedBytes);
    public int FileCount => DeletableTargets.Sum(static target => target.FileCount);
    public int DirectoryCount => DeletableTargets.Sum(static target => target.DirectoryCount);
    public int BlockedCount => BlockedTargets.Count;
    public CleanupPlanSummary Summary => new(
        DeletableTargets.Count,
        FileCount,
        DirectoryCount,
        EstimatedBytes,
        BlockedCount);
}

public sealed record CleanupResult(int DeletedFiles, int DeletedDirectories, IReadOnlyList<string> Failures);

public sealed record CleanupProgress(string Stage, int Completed, int Total, string CurrentTarget)
{
    public int CompletedTargets => Completed;
    public int TotalTargets => Total;
}

public interface ICleanupPlanner
{
    Task<CleanupPlan> CreatePlanAsync(string selectedPath, CancellationToken cancellationToken);
}

public interface ICleanupExecutor
{
    Task<CleanupResult> ExecuteAsync(
        CleanupPlan plan,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class CleanupApplicationService
{
    private readonly ICleanupPlanner _planner;
    private readonly ICleanupExecutor _executor;

    public CleanupApplicationService(ICleanupPlanner planner, ICleanupExecutor executor)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public Task<CleanupPlan> PreviewAsync(string selectedPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);
        return _planner.CreatePlanAsync(selectedPath, cancellationToken);
    }

    public Task<CleanupResult> ExecuteConfirmedAsync(
        CleanupPlan plan,
        bool confirmed,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!confirmed) throw new InvalidOperationException("Cleanup execution requires explicit confirmation.");
        if (plan.DeletableTargets.Count == 0)
            return Task.FromResult(new CleanupResult(0, 0, Array.Empty<string>()));
        return _executor.ExecuteAsync(plan, progress, cancellationToken);
    }
}
