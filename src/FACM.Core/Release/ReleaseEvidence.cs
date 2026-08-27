using System.Text.Json.Serialization;

namespace FACM.Core.Release;

[JsonConverter(typeof(JsonStringEnumConverter<ReleaseEvidenceStatus>))]
public enum ReleaseEvidenceStatus
{
    Passed,
    Blocked,
    NotRun,
    Failed
}

public sealed class ReleaseEvidenceDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public ReleaseCandidateIdentity Candidate { get; set; } = new();
    public List<ReleaseEvidenceItem> Items { get; set; } = [];
}

public sealed class ReleaseCandidateIdentity
{
    public string HeadSha { get; set; } = string.Empty;
    public long? ArtifactId { get; set; }
    public string ArtifactDigest { get; set; } = string.Empty;
    public long? ArtifactSizeBytes { get; set; }
}

public sealed class ReleaseEvidenceItem
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool RequiredForRelease { get; set; }
    public ReleaseEvidenceStatus Status { get; set; }
    public string Evidence { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed record ReleaseEvidenceSummary(
    bool ReleaseReady,
    int RequiredCount,
    int PassedRequiredCount,
    IReadOnlyList<string> BlockingIds,
    IReadOnlyDictionary<ReleaseEvidenceStatus, int> StatusCounts);

public static class ReleaseEvidenceEvaluator
{
    public static ReleaseEvidenceSummary Evaluate(ReleaseEvidenceDocument document)
    {
        Validate(document);

        var required = document.Items.Where(item => item.RequiredForRelease).ToArray();
        var blocking = required
            .Where(item => item.Status != ReleaseEvidenceStatus.Passed)
            .Select(item => item.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var counts = Enum.GetValues<ReleaseEvidenceStatus>()
            .ToDictionary(status => status, status => document.Items.Count(item => item.Status == status));

        return new ReleaseEvidenceSummary(
            blocking.Length == 0,
            required.Length,
            required.Count(item => item.Status == ReleaseEvidenceStatus.Passed),
            blocking,
            counts);
    }

    public static void Validate(ReleaseEvidenceDocument? document)
    {
        if (document is null) throw new InvalidDataException("Release evidence document is null.");
        if (document.SchemaVersion != ReleaseEvidenceDocument.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported release evidence schema: {document.SchemaVersion}.");
        if (document.Candidate is null) throw new InvalidDataException("Candidate identity is missing.");
        ValidateCandidate(document.Candidate);
        if (document.Items is null || document.Items.Count == 0)
            throw new InvalidDataException("Release evidence matrix is empty.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in document.Items)
        {
            if (item is null) throw new InvalidDataException("Release evidence item is null.");
            if (string.IsNullOrWhiteSpace(item.Id)) throw new InvalidDataException("Evidence id is required.");
            if (!ids.Add(item.Id)) throw new InvalidDataException("Duplicate evidence id: " + item.Id);
            if (string.IsNullOrWhiteSpace(item.Category)) throw new InvalidDataException("Evidence category is required: " + item.Id);
            if (item.Status == ReleaseEvidenceStatus.Passed && string.IsNullOrWhiteSpace(item.Evidence))
                throw new InvalidDataException("Passed evidence must cite proof: " + item.Id);
            if (item.RequiredForRelease && item.Status != ReleaseEvidenceStatus.Passed && string.IsNullOrWhiteSpace(item.Notes))
                throw new InvalidDataException("Required blocker must explain why it is not passed: " + item.Id);
        }
    }

    private static void ValidateCandidate(ReleaseCandidateIdentity candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.HeadSha) || candidate.HeadSha.Length != 40 ||
            candidate.HeadSha.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Candidate head SHA must be a full 40-character Git SHA.");
        if (candidate.ArtifactId is null or <= 0)
            throw new InvalidDataException("Candidate artifact id must be positive.");
        if (candidate.ArtifactSizeBytes is null or <= 0)
            throw new InvalidDataException("Candidate artifact size must be positive.");
        if (string.IsNullOrWhiteSpace(candidate.ArtifactDigest) ||
            !candidate.ArtifactDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            candidate.ArtifactDigest.Length != 71 ||
            candidate.ArtifactDigest[7..].Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Candidate artifact digest must be a SHA-256 digest.");
    }
}
