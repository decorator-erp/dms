using Identity.Application.DTOs;
using Identity.Domain;

namespace Identity.Application;

internal static class ResponseMappingExtensions
{
    public static PermissionResponse ToResponse(this ApplicationRoleClaim claim) =>
        new(claim.ClaimType!, claim.ClaimValue!, claim.Group, claim.DisplayName, claim.Description);

    public static RoleResponse ToResponse(this ApplicationRole role) =>
        new(role.Id, role.Name!, role.Description, role.IsActive);
}
