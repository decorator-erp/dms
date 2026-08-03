namespace BuildingBlocks;

/// <summary>
/// The authenticated caller, as seen by a module. Interface only — the HTTP-backed implementation
/// lives in <c>Api</c> (G0-08). Modules never see <c>HttpContext</c>.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
    bool IsAuthenticated { get; }
    bool HasPermission(string permission);
}
