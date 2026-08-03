using Microsoft.AspNetCore.Authorization;

namespace Api.Authorization;

/// <summary>
/// Ported from <c>../backend/Infrastructure/Services/Authentication/Filters/HasPermissionAttribute.cs</c>.
/// Applied via <c>.WithMetadata(new HasPermissionAttribute(...))</c> on minimal-API endpoints. Multiple
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
