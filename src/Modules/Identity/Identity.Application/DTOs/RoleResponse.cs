namespace Identity.Application.DTOs;

internal sealed record RoleResponse(string Id, string Name, string? Description, bool IsActive);
