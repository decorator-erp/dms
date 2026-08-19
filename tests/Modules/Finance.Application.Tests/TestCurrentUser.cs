using BuildingBlocks;

namespace Finance.Application.Tests;

/// <summary>Scoped, mutable stand-in for the HTTP-backed <c>ICurrentUser</c> — same shape
/// <c>Sites.Application.Tests.TestCurrentUser</c> uses.</summary>
internal sealed class TestCurrentUser : ICurrentUser
{
    private readonly HashSet<string> _permissions = [];

    public string? UserId { get; set; }
    public bool IsAuthenticated => UserId is not null;

    public void Grant(string permission) => _permissions.Add(permission);
    public bool HasPermission(string permission) => _permissions.Contains(permission);
}
