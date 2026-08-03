using BuildingBlocks;

namespace Identity.Application.Tests;

/// <summary>Fixed-identity stand-in for the HTTP-backed <c>ICurrentUser</c> (that implementation lives
/// in <c>Api</c>, out of reach for a handler-level test) — enough for commands that just need
/// "some authenticated caller id" to attribute a permission-override grant/deny to.</summary>
internal sealed class TestCurrentUser(string userId) : ICurrentUser
{
    public string? UserId => userId;
    public bool IsAuthenticated => true;
    public bool HasPermission(string permission) => true;
}
