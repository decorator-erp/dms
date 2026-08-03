using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace BuildingBlocks.Web.Authorization;

public static class EndpointAuthorizationExtensions
{
    /// <summary>
    /// <c>.RequireAuthorization()</c> (parameterless) does two things worth knowing here: it requires an
    /// authenticated caller, AND its default policy is built by discovering any
    /// <c>IAuthorizationRequirementData</c> already attached to the endpoint's metadata — which is
    /// exactly what <see cref="HasPermissionAttribute"/> is. So the attribute has to be attached via
    /// <c>.WithMetadata(...)</c> BEFORE (or as part of the same chain as) the parameterless
    /// <c>.RequireAuthorization()</c> call for the permission check to actually take effect, not after.
    /// This extension exists so every module's endpoints get that ordering right by construction instead
    /// of by convention.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new HasPermissionAttribute(permission)).RequireAuthorization();
}
