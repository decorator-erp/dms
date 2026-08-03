using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Identity.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/ApplicationUser.cs</c>. Refresh-token
/// issue/rotate/revoke — inline in <c>AuthService</c> in the backend — moved onto the aggregate; see
/// <see cref="RefreshToken"/>.</summary>
internal sealed class ApplicationUser : IdentityUser<string>, IAuditable
{
    private readonly List<RefreshToken> _refreshTokens = [];

    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public UserType UserType { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }

    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    private ApplicationUser()
    {
        // EF materialization.
    }

    public static ApplicationUser Create(
        string id,
        string userName,
        string email,
        string firstName,
        string lastName,
        UserType userType,
        string? phoneNumber = null,
        string? securityStamp = null,
        string? concurrencyStamp = null) =>
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
            IsActive = true,
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

    /// <summary>Revokes the presented token and issues a new one — atomic from the caller's
    /// perspective. Fails if the presented token is unknown or already inactive (expired or already
    /// revoked), which is what stops a revoked refresh token from being reused to mint another.</summary>
    public Result<RefreshToken> RotateRefreshToken(string presentedToken, string newToken, DateTime utcNow, TimeSpan lifetime)
    {
        var existing = _refreshTokens.SingleOrDefault(t => t.Token == presentedToken);
        if (existing is null || !existing.IsActive)
            return AuthErrors.InvalidRefreshToken;

        existing.Revoke(utcNow);
        var issued = RefreshToken.Issue(newToken, utcNow, lifetime);
        _refreshTokens.Add(issued);
        return issued;
    }

    public Result RevokeRefreshToken(string token, DateTime utcNow)
    {
        var existing = _refreshTokens.SingleOrDefault(t => t.Token == token);
        if (existing is null || !existing.IsActive)
            return AuthErrors.InvalidRefreshToken;

        existing.Revoke(utcNow);
        return Result.Success();
    }

    public void RevokeAllRefreshTokens(DateTime utcNow)
    {
        foreach (var token in _refreshTokens.Where(t => t.IsActive))
            token.Revoke(utcNow);
    }
}
