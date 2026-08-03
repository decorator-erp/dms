namespace Identity.Application.DTOs;

internal sealed record PermissionResponse(
    string ClaimType, string ClaimValue, string Group, string DisplayName, string? Description);
