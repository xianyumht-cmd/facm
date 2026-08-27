using System.Text.Json;
using FACM.Core.Release;

internal static class Gate13Smoke
{
    public static async Task RunAsync()
    {
        var repositoryEvidence = await ReadRepositoryEvidenceAsync();
        var now = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);
        var matchingAuthorization = FreshAuthorization(repositoryEvidence.Candidate.HeadSha, now);

        var currentDecision = CutoverDecisionService.Evaluate(repositoryEvidence, null, now);
        Equal(CutoverDecisionCode.ReleaseEvidenceBlocked, currentDecision.Code, "current matrix must block cutover");
        True(!currentDecision.Allowed, "current matrix cutover must be denied");
        True(currentDecision.BlockingEvidenceIds.Count > 0, "current blockers must be surfaced");

        var blockedEvenWithAuth = CutoverDecisionService.Evaluate(repositoryEvidence, matchingAuthorization, now);
        Equal(CutoverDecisionCode.ReleaseEvidenceBlocked, blockedEvenWithAuth.Code, "authorization cannot override blocked evidence");

        var ready = CreateReadyEvidence(repositoryEvidence.Candidate);
        Equal(CutoverDecisionCode.AuthorizationMissing, CutoverDecisionService.Evaluate(ready, null, now).Code, "missing authorization");
        Equal(CutoverDecisionCode.AuthorizationNotGranted, CutoverDecisionService.Evaluate(ready, matchingAuthorization with { Granted = false }, now).Code, "not granted");
        Equal(CutoverDecisionCode.AuthorizationScopeMismatch, CutoverDecisionService.Evaluate(ready, matchingAuthorization with { Scope = "OtherScope" }, now).Code, "scope mismatch");
        Equal(CutoverDecisionCode.AuthorizationCandidateMismatch, CutoverDecisionService.Evaluate(ready, matchingAuthorization with { CandidateHeadSha = new string('f', 40) }, now).Code, "candidate mismatch");
        Equal(CutoverDecisionCode.AuthorizationIssuedInFuture, CutoverDecisionService.Evaluate(ready, matchingAuthorization with { IssuedAtUtc = now.AddMinutes(1), ExpiresAtUtc = now.AddMinutes(10) }, now).Code, "future authorization");
        Equal(CutoverDecisionCode.AuthorizationExpired, CutoverDecisionService.Evaluate(ready, matchingAuthorization with { IssuedAtUtc = now.AddMinutes(-10), ExpiresAtUtc = now }, now).Code, "expired authorization");
        Equal(CutoverDecisionCode.AuthorizationStale, CutoverDecisionService.Evaluate(ready, matchingAuthorization with { IssuedAtUtc = now.AddMinutes(-31), ExpiresAtUtc = now.AddMinutes(1) }, now).Code, "stale authorization");
        Equal(CutoverDecisionCode.AuthorizationWindowTooLong, CutoverDecisionService.Evaluate(ready, matchingAuthorization with { IssuedAtUtc = now.AddMinutes(-1), ExpiresAtUtc = now.AddMinutes(30) }, now).Code, "authorization window too long");

        var allowed = CutoverDecisionService.Evaluate(ready, matchingAuthorization, now);
        True(allowed.Allowed, "all-pass evidence + fresh matching authorization should allow cutover");
        Equal(CutoverDecisionCode.Allowed, allowed.Code, "allowed decision code");
    }

    private static ProductionCutoverAuthorization FreshAuthorization(string candidateHeadSha, DateTimeOffset now) =>
        new(
            true,
            ProductionCutoverScopes.Facm4ProductionCutover,
            candidateHeadSha,
            now.AddMinutes(-5),
            now.AddMinutes(10));

    private static ReleaseEvidenceDocument CreateReadyEvidence(ReleaseCandidateIdentity candidate) => new()
    {
        Candidate = new ReleaseCandidateIdentity
        {
            HeadSha = candidate.HeadSha,
            ArtifactId = candidate.ArtifactId,
            ArtifactDigest = candidate.ArtifactDigest,
            ArtifactSizeBytes = candidate.ArtifactSizeBytes
        },
        Items =
        [
            new ReleaseEvidenceItem
            {
                Id = "synthetic.release-ready",
                Category = "smoke",
                RequiredForRelease = true,
                Status = ReleaseEvidenceStatus.Passed,
                Evidence = "synthetic all-pass evidence for cutover decision testing"
            }
        ]
    };

    private static async Task<ReleaseEvidenceDocument> ReadRepositoryEvidenceAsync()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "evidence", "facm4-release-evidence.json");
        if (!File.Exists(path)) throw new InvalidOperationException("Gate 13 release evidence matrix is missing.");
        return JsonSerializer.Deserialize<ReleaseEvidenceDocument>(
                   await File.ReadAllTextAsync(path),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException("Gate 13 release evidence matrix deserialized to null.");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }
}
