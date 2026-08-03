namespace Identity.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/RefreshToken.cs</c>, but no longer inert — the backend's
/// version was a plain data bag with expiry/rotation logic living entirely in <c>AuthService</c>
/// (Infrastructure). Moved here since it is a domain invariant (a token is active iff not revoked and
/// not expired; a token, once revoked, cannot be revoked again), not an application concern, and G1-02
/// explicitly asks for unit-testable expiry/rotation behaviour.
/// </summary>
internal sealed class RefreshToken
{
    public string Token { get; private set; } = null!;
    public DateTime CreatedOn { get; private set; }
    public DateTime ExpiresOn { get; private set; }
    public DateTime? RevokedOn { get; private set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresOn;
    public bool IsActive => RevokedOn is null && !IsExpired;

    private RefreshToken()
    {
        // EF materialization.
    }

    private RefreshToken(string token, DateTime createdOn, DateTime expiresOn)
    {
        Token = token;
        CreatedOn = createdOn;
        ExpiresOn = expiresOn;
    }

    public static RefreshToken Issue(string token, DateTime utcNow, TimeSpan lifetime) =>
        new(token, utcNow, utcNow.Add(lifetime));

    public void Revoke(DateTime utcNow)
    {
        if (RevokedOn is not null)
            throw new InvalidOperationException("Refresh token is already revoked.");

        RevokedOn = utcNow;
    }
}
