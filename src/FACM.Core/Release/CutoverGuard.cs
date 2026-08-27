namespace FACM.Core.Release;

public static class ProductionCutoverScopes
{
    public const string Facm4ProductionCutover = "FACM4ProductionCutover";
}

public sealed record ProductionCutoverAuthorization(
    bool Granted,
    string Scope,
    string CandidateHeadSha,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public enum CutoverDecisionCode
{
    Allowed,
    ReleaseEvidenceBlocked,
    AuthorizationMissing,
    AuthorizationNotGranted,
    AuthorizationScopeMismatch,
    AuthorizationCandidateMismatch,
    AuthorizationIssuedInFuture,
    AuthorizationExpired,
    AuthorizationStale,
    AuthorizationWindowTooLong
}

public sealed record CutoverDecision(
    bool Allowed,
    CutoverDecisionCode Code,
    IReadOnlyList<string> BlockingEvidenceIds);

public static class CutoverDecisionService
{
    public static readonly TimeSpan MaximumAuthorizationAge = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MaximumAuthorizationWindow = TimeSpan.FromMinutes(30);

    public static CutoverDecision Evaluate(
        ReleaseEvidenceDocument evidence,
        ProductionCutoverAuthorization? authorization,
        DateTimeOffset nowUtc)
    {
        var release = ReleaseEvidenceEvaluator.Evaluate(evidence);
        if (!release.ReleaseReady)
            return Denied(CutoverDecisionCode.ReleaseEvidenceBlocked, release.BlockingIds);

        if (authorization is null)
            return Denied(CutoverDecisionCode.AuthorizationMissing);
        if (!authorization.Granted)
            return Denied(CutoverDecisionCode.AuthorizationNotGranted);
        if (!string.Equals(authorization.Scope, ProductionCutoverScopes.Facm4ProductionCutover, StringComparison.Ordinal))
            return Denied(CutoverDecisionCode.AuthorizationScopeMismatch);
        if (!string.Equals(authorization.CandidateHeadSha, evidence.Candidate.HeadSha, StringComparison.OrdinalIgnoreCase))
            return Denied(CutoverDecisionCode.AuthorizationCandidateMismatch);
        if (authorization.IssuedAtUtc > nowUtc)
            return Denied(CutoverDecisionCode.AuthorizationIssuedInFuture);
        if (authorization.ExpiresAtUtc <= nowUtc)
            return Denied(CutoverDecisionCode.AuthorizationExpired);
        if (nowUtc - authorization.IssuedAtUtc > MaximumAuthorizationAge)
            return Denied(CutoverDecisionCode.AuthorizationStale);
        if (authorization.ExpiresAtUtc <= authorization.IssuedAtUtc ||
            authorization.ExpiresAtUtc - authorization.IssuedAtUtc > MaximumAuthorizationWindow)
            return Denied(CutoverDecisionCode.AuthorizationWindowTooLong);

        return new CutoverDecision(true, CutoverDecisionCode.Allowed, Array.Empty<string>());
    }

    private static CutoverDecision Denied(
        CutoverDecisionCode code,
        IReadOnlyList<string>? blockingEvidenceIds = null) =>
        new(false, code, blockingEvidenceIds ?? Array.Empty<string>());
}
