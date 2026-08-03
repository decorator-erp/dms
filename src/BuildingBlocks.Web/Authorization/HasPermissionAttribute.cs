using Microsoft.AspNetCore.Authorization;

namespace BuildingBlocks.Web.Authorization;

/// <summary>
/// Moved here from <c>Api/Authorization</c> at G1-07 — module endpoints (<c>Identity.Infrastructure</c>,
/// and later <c>Hr.Infrastructure</c>) need to apply this via
/// <c>.WithMetadata(new HasPermissionAttribute(...))</c>, and a module's <c>*.Infrastructure</c> project
/// cannot reference <c>Api</c> (the composition root depends on modules, never the reverse). Ported
/// originally from
/// <c>../backend/Infrastructure/Services/Authentication/Filters/HasPermissionAttribute.cs</c>. Multiple
/// instances on the same endpoint are combined with AND semantics by ASP.NET's authorization
/// middleware (every requirement must succeed).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission)
    : Attribute, IAuthorizationRequirement, IAuthorizationRequirementData
{
    public string Permission { get; } = permission;

    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        yield return this;
    }
}
