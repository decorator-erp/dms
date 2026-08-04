using BuildingBlocks;

namespace Projects.Application.Tests;

/// <summary>Scoped, mutable stand-in for the HTTP-backed <c>ICurrentUser</c> — same shape
/// <c>Approvals.Application.Tests.TestCurrentUser</c> uses, for the identical reason: tests need to set
/// <see cref="UserId"/> and grant specific permissions before resolving a handler from the same
/// scope.</summary>
internal sealed class TestCurrentUser : ICurrentUser
{
    private readonly HashSet<string> _permissions = [];

    public string? UserId { get; set; }
    public bool IsAuthenticated => UserId is not null;

    public void Grant(string permission) => _permissions.Add(permission);
    public bool HasPermission(string permission) => _permissions.Contains(permission);
}
