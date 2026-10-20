using Identity.Domain;
using SharedKernel;
using Xunit;

namespace Identity.UnitTests;

public class ApplicationUserRefreshTokenTests
{
    private static ApplicationUser CreateUser() =>
        ApplicationUser.Create(
            Guid.NewGuid().ToString(), "jdoe", "jdoe@example.com", "Jane", "Doe", UserType.Employee);

    [Fact]
    public void RotateRefreshToken_WithValidPresentedToken_RevokesOldAndIssuesNew()
    {
        ApplicationUser user = CreateUser();
        DateTime now = DateTime.UtcNow;
        user.IssueRefreshToken("old-token", now, TimeSpan.FromDays(14));

        Result<RefreshToken> result = user.RotateRefreshToken("old-token", "new-token", now, TimeSpan.FromDays(14));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Matches("new-token"));
        Assert.Equal(2, user.RefreshTokens.Count);
        Assert.False(user.RefreshTokens.Single(t => t.Matches("old-token")).IsActive);
        Assert.True(user.RefreshTokens.Single(t => t.Matches("new-token")).IsActive);
    }

    [Fact]
    public void RotateRefreshToken_WithUnknownToken_Fails()
    {
        ApplicationUser user = CreateUser();
        DateTime now = DateTime.UtcNow;
        user.IssueRefreshToken("old-token", now, TimeSpan.FromDays(14));

        Result<RefreshToken> result = user.RotateRefreshToken("never-issued", "new-token", now, TimeSpan.FromDays(14));

        Assert.True(result.IsFailure);
    }

    /// <summary>The reuse case G1-05's integration test also exercises: once a refresh token has been
    /// used to rotate (or has otherwise been revoked), presenting it again must not mint another
    /// token — otherwise a stolen-then-rotated token stays usable forever.</summary>
    [Fact]
    public void RotateRefreshToken_WithAlreadyRotatedToken_RejectsReuse()
    {
        ApplicationUser user = CreateUser();
        DateTime now = DateTime.UtcNow;
        user.IssueRefreshToken("token-1", now, TimeSpan.FromDays(14));
        user.RotateRefreshToken("token-1", "token-2", now, TimeSpan.FromDays(14));

        Result<RefreshToken> reuseAttempt = user.RotateRefreshToken("token-1", "token-3", now, TimeSpan.FromDays(14));

        Assert.True(reuseAttempt.IsFailure);
        Assert.DoesNotContain(user.RefreshTokens, t => t.Matches("token-3"));
    }

    /// <summary>K5: reuse of a rotated token is treated as theft. The whole family is revoked, so the
    /// token the thief (or the victim) rotated to stops working too.</summary>
    [Fact]
    public void RotateRefreshToken_WithAlreadyRotatedToken_RevokesTheWholeFamily()
    {
        ApplicationUser user = CreateUser();
        DateTime now = DateTime.UtcNow;
        user.IssueRefreshToken("token-1", now, TimeSpan.FromDays(14));
        user.RotateRefreshToken("token-1", "token-2", now, TimeSpan.FromDays(14));

        Result<RefreshToken> reuseAttempt = user.RotateRefreshToken("token-1", "token-3", now, TimeSpan.FromDays(14));

        Assert.Equal(AuthErrors.RefreshTokenReused, reuseAttempt.Error);
        Assert.False(user.RefreshTokens.Single(t => t.Matches("token-2")).IsActive);
        Assert.All(user.RefreshTokens, t => Assert.False(t.IsActive));
    }

    /// <summary>K5: an unknown token is just invalid, not a reuse. Nothing is revoked.</summary>
    [Fact]
    public void RotateRefreshToken_WithUnknownToken_RevokesNothing()
    {
        ApplicationUser user = CreateUser();
        DateTime now = DateTime.UtcNow;
        user.IssueRefreshToken("token-1", now, TimeSpan.FromDays(14));

        Result<RefreshToken> attempt = user.RotateRefreshToken("never-issued", "token-2", now, TimeSpan.FromDays(14));

        Assert.Equal(AuthErrors.InvalidRefreshToken, attempt.Error);
        Assert.True(user.RefreshTokens.Single().IsActive);
    }

    /// <summary>K5: what is kept is the SHA-256 hex of the value, never the value.</summary>
    [Fact]
    public void IssueRefreshToken_StoresOnlyTheHash()
    {
        ApplicationUser user = CreateUser();

        RefreshToken issued = user.IssueRefreshToken("plain-value", DateTime.UtcNow, TimeSpan.FromDays(14));

        Assert.NotEqual("plain-value", issued.TokenHash);
        Assert.Equal(64, issued.TokenHash.Length);
        Assert.Equal(RefreshToken.Hash("plain-value"), issued.TokenHash);
    }

    [Fact]
    public void RevokeAllRefreshTokens_DeactivatesEveryActiveToken()
    {
        ApplicationUser user = CreateUser();
        DateTime now = DateTime.UtcNow;
        user.IssueRefreshToken("token-1", now, TimeSpan.FromDays(14));
        user.IssueRefreshToken("token-2", now, TimeSpan.FromDays(14));

        user.RevokeAllRefreshTokens(now);

        Assert.All(user.RefreshTokens, t => Assert.False(t.IsActive));
    }
}
