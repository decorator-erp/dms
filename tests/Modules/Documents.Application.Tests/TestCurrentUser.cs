using BuildingBlocks;

namespace Documents.Application.Tests;

/// <summary>Scoped, mutable stand-in for the HTTP-backed <c>ICurrentUser</c> — same shape
/// <c>Projects.Application.Tests.TestCurrentUser</c> uses. Documents' own handlers don't check
/// permissions themselves (no scope decorator exists for this module, unlike Projects), but the shared
/// DI graph still needs an <c>ICurrentUser</c> registered for Identity/Projects' own handlers.</summary>
internal sealed class TestCurrentUser : ICurrentUser
{
    private readonly HashSet<string> _permissions = [];

    public string? UserId { get; set; }
    public bool IsAuthenticated => UserId is not null;

    public void Grant(string permission) => _permissions.Add(permission);
    public bool HasPermission(string permission) => _permissions.Contains(permission);
}
