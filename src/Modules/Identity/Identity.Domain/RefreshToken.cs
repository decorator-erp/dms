namespace Identity.Domain;

internal sealed class RefreshToken
{
    public string Token { get; private set; } = null!;
    public DateTime CreatedOn { get; private set; }
    public DateTime ExpiresOn { get; private set; }
    public DateTime? RevokedOn { get; private set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresOn;
    public bool IsActive => RevokedOn is null && !IsExpired;

    private RefreshToken() { }
    public static RefreshToken Issue(string token, DateTime utcNow, TimeSpan lifetime) => new()
    {
        Token = token,
        CreatedOn = utcNow,
        ExpiresOn = utcNow.Add(lifetime)
    };

    public void Revoke(DateTime utcNow)
    {
        if (RevokedOn is not null)
        {
            throw new InvalidOperationException("Refresh token is already revoked.");
        }

        RevokedOn = utcNow;
    }
}
