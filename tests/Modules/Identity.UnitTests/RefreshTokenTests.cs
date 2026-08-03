using Identity.Domain;
using Xunit;

namespace Identity.UnitTests;

public class RefreshTokenTests
{
    [Fact]
    public void Issue_BeforeExpiry_IsActive()
    {
        var now = DateTime.UtcNow;
        var token = RefreshToken.Issue("t1", now, TimeSpan.FromDays(14));

        Assert.False(token.IsExpired);
        Assert.True(token.IsActive);
    }

    [Fact]
    public void IsExpired_AfterExpiresOn_IsTrue()
    {
        var now = DateTime.UtcNow;
        var token = RefreshToken.Issue("t1", now, TimeSpan.FromSeconds(-1));

        Assert.True(token.IsExpired);
        Assert.False(token.IsActive);
    }

    [Fact]
    public void Revoke_MakesTokenInactive()
    {
        var now = DateTime.UtcNow;
        var token = RefreshToken.Issue("t1", now, TimeSpan.FromDays(14));

        token.Revoke(now);

        Assert.False(token.IsActive);
        Assert.NotNull(token.RevokedOn);
    }

    [Fact]
    public void Revoke_AlreadyRevoked_Throws()
    {
        var now = DateTime.UtcNow;
        var token = RefreshToken.Issue("t1", now, TimeSpan.FromDays(14));
        token.Revoke(now);

        Assert.Throws<InvalidOperationException>(() => token.Revoke(now));
    }
}
