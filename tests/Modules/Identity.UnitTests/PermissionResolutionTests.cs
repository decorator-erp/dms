using Identity.Domain;
using Xunit;

namespace Identity.UnitTests;

/// <summary>
/// Phase 5 (5.2): deny-wins and the live rule, without the unique index in the way. The index means a
/// grant and a deny for one code can't both be stored, so only a test at this level can show the rule
/// itself decides.
/// </summary>
public class PermissionResolutionTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static OverrideState Live(string code, bool isGranted) => new(code, isGranted, IsActive: true, ExpiresAt: null);

    [Fact]
    public void Roles_are_a_union_and_a_live_grant_adds()
    {
        HashSet<string> result = PermissionResolution.Resolve(["a", "b", "a"], [Live("c", isGranted: true)], Now);

        Assert.Equal(["a", "b", "c"], result.Order());
    }

    [Fact]
    public void A_grant_and_a_deny_for_one_code_leave_it_out()
    {
        HashSet<string> result = PermissionResolution.Resolve(["a"], [Live("x", isGranted: true), Live("x", isGranted: false)], Now);

        Assert.DoesNotContain("x", result);
    }

    /// <summary>The order the old loop got wrong: a grant read after the deny re-added the code.</summary>
    [Fact]
    public void A_deny_before_a_grant_still_leaves_it_out()
    {
        HashSet<string> result = PermissionResolution.Resolve(["x"], [Live("x", isGranted: false), Live("x", isGranted: true)], Now);

        Assert.DoesNotContain("x", result);
    }

    [Fact]
    public void An_inactive_deny_is_ignored()
    {
        HashSet<string> result = PermissionResolution.Resolve(["x"], [new OverrideState("x", false, IsActive: false, null)], Now);

        Assert.Contains("x", result);
    }

    [Fact]
    public void An_inactive_grant_is_ignored()
    {
        HashSet<string> result = PermissionResolution.Resolve([], [new OverrideState("x", true, IsActive: false, null)], Now);

        Assert.DoesNotContain("x", result);
    }

    [Fact]
    public void An_expired_override_is_ignored_either_way()
    {
        HashSet<string> result = PermissionResolution.Resolve(
            ["kept"],
            [new OverrideState("kept", false, true, Now.AddSeconds(-1)), new OverrideState("added", true, true, Now.AddSeconds(-1))],
            Now);

        Assert.Equal(["kept"], result);
    }

    /// <summary>The query form and the in-memory form of "live" are two spellings of one rule. This pins
    /// them to the same answer on every edge, so neither can drift.</summary>
    [Theory]
    [InlineData(true, null)]
    [InlineData(false, null)]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    [InlineData(false, -1)]
    public void The_query_predicate_and_the_in_memory_rule_agree(bool isActive, int? expiresInSeconds)
    {
        DateTime? expiresAt = expiresInSeconds is null ? null : Now.AddSeconds(expiresInSeconds.Value);
        var row = UserPermissionOverride.Grant("u", "p", "g", null, expiresAt);
        if (!isActive)
        {
            // No domain method deactivates an override yet (revoke is a hard delete); the flag lives on
            // Entity, so set it the way EF would when reading such a row.
            typeof(SharedKernel.Entity).GetProperty(nameof(SharedKernel.Entity.IsActive))!.SetValue(row, false);
        }

        bool viaQuery = UserPermissionOverride.LiveAt(Now).Compile()(row);

        Assert.Equal(UserPermissionOverride.IsLive(isActive, expiresAt, Now), viaQuery);
        Assert.Equal(viaQuery, row.IsLiveAt(Now));
    }
}
