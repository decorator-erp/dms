namespace Identity.Domain;

/// <summary>One override as the resolver needs it: its code, its direction, and what decides whether it
/// is live.</summary>
internal readonly record struct OverrideState(string Permission, bool IsGranted, bool IsActive, DateTime? ExpiresAt);

/// <summary>
/// Phase 5: a user's effective permissions from their roles' codes and their overrides. Roles are
/// additive (the union of every role's codes). Then every live grant adds its code and every live deny
/// removes its code, in that order, so a deny wins over a grant for the same code. The unique
/// (UserId, Permission) index means that pair can't exist today, but the rule no longer rests on the index
/// alone: before, the overrides were applied in row order, and a grant read after a deny would have won.
/// A dead override (inactive, or expired at <paramref name="utcNow"/>) counts for nothing either way.
/// </summary>
internal static class PermissionResolution
{
    public static HashSet<string> Resolve(
        IEnumerable<string> roleCodes, IEnumerable<OverrideState> overrides, DateTime utcNow)
    {
        var effective = new HashSet<string>(roleCodes);
        List<OverrideState> live = [.. overrides.Where(o => UserPermissionOverride.IsLive(o.IsActive, o.ExpiresAt, utcNow))];

        foreach (OverrideState grant in live.Where(o => o.IsGranted))
        {
            effective.Add(grant.Permission);
        }

        foreach (OverrideState deny in live.Where(o => !o.IsGranted))
        {
            effective.Remove(deny.Permission);
        }

        return effective;
    }
}
