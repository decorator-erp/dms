using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Identity.Domain;

internal sealed class ApplicationUser : IdentityUser<string>, IAuditable
{
    private readonly List<RefreshToken> _refreshTokens = [];
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public UserType UserType { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }

    // add a profilePic prop
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    private ApplicationUser() { }

    /// <param name="isActive">
    /// Defaults to <c>true</c> for the seeded admin, which is the ADR's one documented exemption: it has
    /// no employee record and therefore no integration event that would ever activate it. Every other
    /// caller provisions through <c>IUserProvisioning</c> and passes <c>false</c> — a login only becomes
    /// usable once its business record has committed. Last parameter so the exemption is the default
    /// nobody types, and the deliberate choice is the one that shows up at the call site.
    /// </param>
    public static ApplicationUser Create(
        string id,
        string userName,
        string email,
        string firstName,
        string lastName,
        UserType userType,
        string? phoneNumber = null,
        string? securityStamp = null,
        string? concurrencyStamp = null,
        bool isActive = true) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            PhoneNumber = phoneNumber,
            PhoneNumberConfirmed = phoneNumber is not null,
            FirstName = firstName,
            LastName = lastName,
            UserType = userType,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
            SecurityStamp = securityStamp ?? Guid.NewGuid().ToString(),
            ConcurrencyStamp = concurrencyStamp ?? Guid.NewGuid().ToString(),
        };

    public void UpdateLastLogin() => LastLoginAt = DateTime.UtcNow;

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    public RefreshToken IssueRefreshToken(string token, DateTime utcNow, TimeSpan lifetime)
    {
        var issued = RefreshToken.Issue(token, utcNow, lifetime);
        _refreshTokens.Add(issued);
        return issued;
    }

    /// <summary>
    /// Rotation: the presented token is revoked and a new one issued. K5 adds reuse detection. A token
    /// that was <b>already revoked</b> (rotated away, or revoked) being presented again means two parties
    /// hold the same token family, one of them a thief. Every refresh token of the account is revoked,
    /// and the result is <see cref="AuthErrors.RefreshTokenReused"/>. The caller must persist that
    /// revocation outside the failing command's transaction (see <c>RefreshTokenCommandHandler</c>). An
    /// unknown or merely expired token is just <see cref="AuthErrors.InvalidRefreshToken"/>.
    /// </summary>
    public Result<RefreshToken> RotateRefreshToken(string presentedToken, string newToken, DateTime utcNow, TimeSpan lifetime)
    {
        RefreshToken? existing = _refreshTokens.SingleOrDefault(t => t.Matches(presentedToken));
        if (existing is { RevokedOn: not null })
        {
            RevokeAllRefreshTokens(utcNow);
            return AuthErrors.RefreshTokenReused;
        }

        if (existing is null || !existing.IsActive)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        existing.Revoke(utcNow);
        var issued = RefreshToken.Issue(newToken, utcNow, lifetime);
        _refreshTokens.Add(issued);
        return issued;
    }

    public Result RevokeRefreshToken(string token, DateTime utcNow)
    {
        RefreshToken? existing = _refreshTokens.SingleOrDefault(t => t.RevokedOn == null && t.Matches(token));
        if (existing is null || !existing.IsActive)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        existing.Revoke(utcNow);
        return Result.Success();
    }

    public void RevokeAllRefreshTokens(DateTime utcNow)
    {
        foreach (RefreshToken? token in _refreshTokens.Where(t => t.IsActive))
        {
            token.Revoke(utcNow);
        }
    }
}
