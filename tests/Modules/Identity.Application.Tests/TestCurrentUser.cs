using BuildingBlocks;

namespace Identity.Application.Tests;

/// <summary>Fixed-identity stand-in for the HTTP-backed <c>ICurrentUser</c> (that implementation lives
/// in <c>Api</c>, out of reach for a handler-level test) — enough for commands that just need
/// "some authenticated caller id" to attribute a permission-override grant/deny to.
///
/// <para><see cref="HasPermission"/> answers from the permission set it was built with, the way
/// <c>HttpCurrentUser</c> answers from the token's <c>permissions</c> claims. It used to return
/// <c>true</c> for everything, which would have let a handler-level authorization check pass without
/// ever being exercised. The fixture's shared caller holds nothing, so a test that needs a permission
/// must say which one.</para></summary>
internal sealed class TestCurrentUser(string userId, params string[] permissions) : ICurrentUser
{
    private readonly HashSet<string> _permissions = [.. permissions];

    public string? UserId => userId;
    public bool IsAuthenticated => true;
    public bool HasPermission(string permission) => _permissions.Contains(permission);
}
