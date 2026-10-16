namespace DAP.Core.Targets;

public enum TargetResolutionStatus
{
    Resolved,
    NotFound,
    Ambiguous
}

public sealed record TargetResolution<TTarget>(
    TargetResolutionStatus Status,
    TTarget? Target,
    int CandidateCount)
{
    public static TargetResolution<TTarget> Resolved(TTarget target) =>
        new(TargetResolutionStatus.Resolved, target, 1);

    public static TargetResolution<TTarget> NotFound() =>
        new(TargetResolutionStatus.NotFound, default, 0);

    public static TargetResolution<TTarget> Ambiguous(int candidateCount)
    {
        if (candidateCount < 2)
            throw new ArgumentOutOfRangeException(nameof(candidateCount), "Ambiguous resolution requires at least two candidates.");

        return new(TargetResolutionStatus.Ambiguous, default, candidateCount);
    }
}
