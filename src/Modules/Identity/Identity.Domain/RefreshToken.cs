using System.Security.Cryptography;
using System.Text;

namespace Identity.Domain;

/// <summary>
/// One refresh token of a user. K5: only <see cref="TokenHash"/>, the SHA-256 of the value, is stored.
/// The value itself is handed to the caller once and never persisted, so a read of
/// <c>identity.refresh_tokens</c> (a backup, a replica, a leaked dump) yields nothing that can be
/// replayed. Refresh tokens are 64 random bytes, so an unsalted fast hash is enough; there is no
/// dictionary to defend against.
/// </summary>
internal sealed class RefreshToken
{
    public string TokenHash { get; private set; } = null!;
    public DateTime CreatedOn { get; private set; }
    public DateTime ExpiresOn { get; private set; }
    public DateTime? RevokedOn { get; private set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresOn;
    public bool IsActive => RevokedOn is null && !IsExpired;

    private RefreshToken() { }

    /// <param name="token">The plain value given to the caller. Only its hash is kept.</param>
    public static RefreshToken Issue(string token, DateTime utcNow, TimeSpan lifetime) => new()
    {
        TokenHash = Hash(token),
        CreatedOn = utcNow,
        ExpiresOn = utcNow.Add(lifetime)
    };

    /// <summary>Lower-case hex SHA-256 of the UTF-8 token value.</summary>
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public bool Matches(string token) => TokenHash == Hash(token);

    public void Revoke(DateTime utcNow)
    {
        if (RevokedOn is not null)
        {
            throw new InvalidOperationException("Refresh token is already revoked.");
        }

        RevokedOn = utcNow;
    }
}
