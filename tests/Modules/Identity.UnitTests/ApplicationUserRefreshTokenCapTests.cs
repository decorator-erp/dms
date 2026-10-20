using Identity.Domain;
using SharedKernel;
using Xunit;

namespace Identity.UnitTests;

/// <summary>Aug 4.1 (P3): the cap bounds an account's live sessions, by count of rows rather than
/// by statements.</summary>
public class ApplicationUserRefreshTokenCapTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    private static ApplicationUser CreateUser() =>
        ApplicationUser.Create(Guid.NewGuid().ToString(), "cap", "cap@example.com", "Cap", "User", UserType.Employee);

    [Fact]
    public void Fifteen_logins_leave_ten_active_and_revoke_the_five_oldest()
    {
        ApplicationUser user = CreateUser();
        DateTime start = DateTime.UtcNow;
        for (int i = 0; i < 15; i++)
        {
            user.IssueRefreshToken($"t{i}", start.AddMinutes(i), Lifetime, maxActive: 10);
        }

        Assert.Equal(10, user.RefreshTokens.Count(t => t.IsActive));
        Assert.All(user.RefreshTokens.Where(t => t.CreatedOn < start.AddMinutes(5)), t => Assert.NotNull(t.RevokedOn));
        Assert.All(user.RefreshTokens.Where(t => t.CreatedOn >= start.AddMinutes(5)), t => Assert.True(t.IsActive));
    }

    [Fact]
    public void Rotation_keeps_the_active_count()
    {
        ApplicationUser user = CreateUser();
        DateTime now = DateTime.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            user.IssueRefreshToken($"t{i}", now.AddMinutes(i), Lifetime, maxActive: 10);
        }

        Result<RefreshToken> rotated = user.RotateRefreshToken("t9", "t10", now.AddMinutes(20), Lifetime);

        Assert.True(rotated.IsSuccess);
        Assert.Equal(10, user.RefreshTokens.Count(t => t.IsActive));
    }

    /// <summary>A session the cap ended is revoked, so presenting its token again is reuse, and the
    /// family goes, as for any rotated-away token inside the retention window.</summary>
    [Fact]
    public void A_token_the_cap_revoked_is_reuse_when_presented()
    {
        ApplicationUser user = CreateUser();
        DateTime now = DateTime.UtcNow;
        for (int i = 0; i < 11; i++)
        {
            user.IssueRefreshToken($"t{i}", now.AddMinutes(i), Lifetime, maxActive: 10);
        }

        Result<RefreshToken> attempt = user.RotateRefreshToken("t0", "t-new", now.AddMinutes(30), Lifetime);

        Assert.Equal(AuthErrors.RefreshTokenReused, attempt.Error);
        Assert.Equal(0, user.RefreshTokens.Count(t => t.IsActive));
    }

    [Fact]
    public void Without_a_cap_nothing_is_revoked()
    {
        ApplicationUser user = CreateUser();
        for (int i = 0; i < 12; i++)
        {
            user.IssueRefreshToken($"t{i}", DateTime.UtcNow, Lifetime);
        }

        Assert.Equal(12, user.RefreshTokens.Count(t => t.IsActive));
    }
}
