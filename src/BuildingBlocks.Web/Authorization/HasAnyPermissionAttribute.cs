using Microsoft.AspNetCore.Authorization;

namespace BuildingBlocks.Web.Authorization;

/// <summary>
/// N6: a route open to callers who hold <b>any one</b> of several permissions (the permission catalog
/// is for whoever manages roles <i>or</i> overrides). Kept separate from
/// <see cref="HasPermissionAttribute"/>, whose single-permission meaning (and AND semantics across
/// several instances) stays exactly as it was. Apply it with
/// <see cref="EndpointAuthorizationExtensions.RequireAnyPermission{TBuilder}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class HasAnyPermissionAttribute : Attribute, IAuthorizationRequirement, IAuthorizationRequirementData
{
    public HasAnyPermissionAttribute(params string[] permissions)
    {
        if (permissions.Length < 2)
        {
            throw new ArgumentException(
                "An any-of requirement names at least two permissions; use HasPermissionAttribute for one.",
                nameof(permissions));
        }

        Permissions = permissions;
    }

    public IReadOnlyList<string> Permissions { get; }

    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        yield return this;
    }
}
