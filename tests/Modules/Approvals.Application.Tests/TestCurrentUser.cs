using BuildingBlocks;

namespace Approvals.Application.Tests;

/// <summary>
/// Registered Scoped (not the Singleton, fixed-permission shape Identity.Application.Tests uses) so each
/// test can set <see cref="UserId"/> and grant specific permissions BEFORE resolving a handler from the
/// same scope — needed here because <c>DecideApprovalCommandHandler</c>'s behavior depends on which user
/// is deciding and whether they hold <c>approvals.decide_any</c>, not just "some authenticated caller".
/// </summary>
internal sealed class TestCurrentUser : ICurrentUser
{
    private readonly HashSet<string> _permissions = [];

    public string? UserId { get; set; }
    public bool IsAuthenticated => UserId is not null;

    public void Grant(string permission) => _permissions.Add(permission);
    public bool HasPermission(string permission) => _permissions.Contains(permission);
}
