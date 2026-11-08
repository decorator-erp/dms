namespace BuildingBlocks;

/// <summary>
/// P1-A: permission codes a user holds without them being in their token, read live on every request. Projects
/// implements it for project access grants (each active grant implies the route codes of its areas and level), so
/// a grant works on the next request after it is made and stops on the next request after it is revoked or
/// expires, with no re-login. Nothing an implementation returns is ever written into a JWT.
///
/// <para>Several implementations may be registered; <see cref="ImpliedPermissions"/> unions them.</para>
/// </summary>
public interface IImpliedPermissionSource
{
    Task<IReadOnlySet<string>> GetImpliedPermissionsAsync(string userId, CancellationToken ct);
}

/// <summary>
/// P1-A: the per-request (scoped) view of <see cref="IImpliedPermissionSource"/>. The route permission handlers
/// (<c>BuildingBlocks.Web.Authorization</c>) ask it only when the token lacks the code, and the sources are read at
/// most once per request.
///
/// <para><b><see cref="RouteAuthorizedByImpliedPermission"/></b> records that this request's route claim came
/// from a grant, not from the token. An implied code is user-wide, but a grant is not: it covers one project's
/// areas. So when the route was opened only by an implied code, the project scope checks count <i>grants only</i>
/// for the rest of the request, ignoring membership and <c>project.read_all</c>. Otherwise a grant on one project's
/// documents would let its holder read the documents of every other project they are a member of, though their
/// role never gave them <c>documents.read</c>.</para>
/// </summary>
public sealed class ImpliedPermissions(IEnumerable<IImpliedPermissionSource> sources)
{
    private readonly IImpliedPermissionSource[] _sources = [.. sources];
    private string? _loadedFor;
    private HashSet<string> _codes = [];

    /// <summary>True once a route requirement on this request was met by an implied code rather than a token
    /// claim.</summary>
    public bool RouteAuthorizedByImpliedPermission { get; private set; }

    /// <summary>True when <paramref name="userId"/> holds one of <paramref name="permissions"/> through an
    /// implied source; it then marks the request (<see cref="RouteAuthorizedByImpliedPermission"/>).</summary>
    public async Task<bool> TryAuthorizeAsync(string userId, IEnumerable<string> permissions, CancellationToken ct)
    {
        IReadOnlySet<string> codes = await GetAsync(userId, ct);
        if (!permissions.Any(codes.Contains))
        {
            return false;
        }

        RouteAuthorizedByImpliedPermission = true;
        return true;
    }

    /// <summary>The union of every source for <paramref name="userId"/>, read once per request.</summary>
    public async Task<IReadOnlySet<string>> GetAsync(string userId, CancellationToken ct)
    {
        if (_loadedFor == userId)
        {
            return _codes;
        }

        HashSet<string> codes = new(StringComparer.Ordinal);
        foreach (IImpliedPermissionSource source in _sources)
        {
            codes.UnionWith(await source.GetImpliedPermissionsAsync(userId, ct));
        }

        _codes = codes;
        _loadedFor = userId;
        return _codes;
    }
}
