namespace Identity.Infrastructure.Authentication;

/// <summary>
/// Aug 4.1: the bounds on <c>identity.refresh_tokens</c>. Every user load brings the user's whole token
/// collection back (an owned type), so it has to stay small: a cap on live sessions per account, and
/// <c>RefreshTokenPruneJob</c> deleting rows that can no longer be used.
/// </summary>
internal sealed class RefreshTokenOptions
{
    public const string SectionName = "Identity:RefreshTokens";

    /// <summary>Live sessions per account. Signing in beyond it ends the oldest session's refresh.</summary>
    public int MaxActivePerUser { get; init; } = 10;

    /// <summary>How long an expired row is kept before it is pruned.</summary>
    public int PruneGraceDays { get; init; } = 1;

    /// <summary>How long a revoked row is kept. Inside the window, presenting a rotated-away token
    /// revokes the whole family (K5's reuse detection); after it, the row is gone and the token is just
    /// unknown, still refused but without the family revoke.</summary>
    public int RevokedRetentionHours { get; init; } = 24;

    public int PruneBatchSize { get; init; } = 1000;

    public int PruneMaxBatchesPerSweep { get; init; } = 10;
}
