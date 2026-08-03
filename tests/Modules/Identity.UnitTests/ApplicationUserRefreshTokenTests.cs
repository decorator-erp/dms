using Identity.Domain;
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
        var user = CreateUser();
        var now = DateTime.UtcNow;
        user.IssueRefreshToken("old-token", now, TimeSpan.FromDays(14));

        var result = user.RotateRefreshToken("old-token", "new-token", now, TimeSpan.FromDays(14));

        Assert.True(result.IsSuccess);
        Assert.Equal("new-token", result.Value!.Token);
        Assert.Equal(2, user.RefreshTokens.Count);
        Assert.False(user.RefreshTokens.Single(t => t.Token == "old-token").IsActive);
        Assert.True(user.RefreshTokens.Single(t => t.Token == "new-token").IsActive);
    }

    [Fact]
    public void RotateRefreshToken_WithUnknownToken_Fails()
    {
        var user = CreateUser();
        var now = DateTime.UtcNow;
        user.IssueRefreshToken("old-token", now, TimeSpan.FromDays(14));

        var result = user.RotateRefreshToken("never-issued", "new-token", now, TimeSpan.FromDays(14));

        Assert.True(result.IsFailure);
    }

    /// <summary>The reuse case G1-05's integration test also exercises: once a refresh token has been
    /// used to rotate (or has otherwise been revoked), presenting it again must not mint another
    /// token — otherwise a stolen-then-rotated token stays usable forever.</summary>
    [Fact]
    public void RotateRefreshToken_WithAlreadyRotatedToken_RejectsReuse()
    {
        var user = CreateUser();
        var now = DateTime.UtcNow;
        user.IssueRefreshToken("token-1", now, TimeSpan.FromDays(14));
        user.RotateRefreshToken("token-1", "token-2", now, TimeSpan.FromDays(14));

        var reuseAttempt = user.RotateRefreshToken("token-1", "token-3", now, TimeSpan.FromDays(14));

        Assert.True(reuseAttempt.IsFailure);
        Assert.DoesNotContain(user.RefreshTokens, t => t.Token == "token-3");
    }

    [Fact]
    public void RevokeAllRefreshTokens_DeactivatesEveryActiveToken()
    {
        var user = CreateUser();
        var now = DateTime.UtcNow;
        user.IssueRefreshToken("token-1", now, TimeSpan.FromDays(14));
        user.IssueRefreshToken("token-2", now, TimeSpan.FromDays(14));

        user.RevokeAllRefreshTokens(now);

        Assert.All(user.RefreshTokens, t => Assert.False(t.IsActive));
    }
}
