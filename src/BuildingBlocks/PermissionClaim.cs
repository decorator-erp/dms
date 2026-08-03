namespace BuildingBlocks;

/// <summary>
/// The JWT claim type permissions are carried under. Shared between the token issuer (Identity's
/// <c>JwtProvider</c>, G1-04) and the token validator (<c>Api</c>'s <c>HttpCurrentUser</c> and
/// permission authorization handler, G0-08) — both need the exact same string, and this is the one
/// place it's spelled out, rather than a literal duplicated in two modules that could drift.
/// </summary>
public static class PermissionClaim
{
    public const string Type = "permissions";
}
