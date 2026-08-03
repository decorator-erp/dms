using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Hr.Contracts.Employees;

/// <summary>
/// Command/query wrapper types and DTOs for HR's employee features, living here rather than in
/// <c>Hr.Application</c> for the identical reason Identity's own command/DTO types moved to
/// <c>Identity.Contracts</c> at G1-07: <c>Hr.Infrastructure</c>'s endpoints (G2-06) need to name these
/// exact types, and <c>Hr.Application</c> already has a real <c>ProjectReference</c> to
/// <c>Hr.Infrastructure</c> (mirroring Identity's G1-04 decision) — the reverse reference would be
/// circular. <c>ContractType</c> is deliberately represented as a <c>string</c> here (the enum name,
/// e.g. <c>"FullTime"</c>), not the actual <c>Hr.Domain.ContractType</c> enum — <c>Hr.Contracts</c> may
/// only reference <c>SharedKernel</c>/<c>BuildingBlocks</c> (arch rule 5's own scope for this project),
/// and <c>Hr.Domain</c> may only reference <c>SharedKernel</c> (the reference matrix), so neither side
/// can depend on the other to share one enum type. The Application handler converts.
/// </summary>
public sealed record EmployeeResponse(
    Guid Id,
    string AppUserId,
    string JobTitle,
    DateOnly HireDate,
    DateOnly? EndDate,
    string? Notes,
    string ContractType,
    string? PhoneNumber,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    bool IsActive);

public sealed record CreateEmployeeCommand(
    string AppUserId,
    string JobTitle,
    DateOnly HireDate,
    string ContractType,
    string? PhoneNumber = null,
    string? EmergencyContactName = null,
    string? EmergencyContactPhone = null,
    string? Notes = null) : ICommand<Guid>;

public sealed record UpdateEmployeeCommand(
    Guid EmployeeId,
    string? JobTitle = null,
    string? ContractType = null,
    string? PhoneNumber = null,
    string? EmergencyContactName = null,
    string? EmergencyContactPhone = null,
    string? Notes = null,
    DateOnly? EndDate = null) : ICommand;

public sealed record DeactivateEmployeeCommand(Guid EmployeeId) : ICommand;

public sealed record ActivateEmployeeCommand(Guid EmployeeId) : ICommand;

public sealed record GetEmployeeQuery(Guid EmployeeId) : IQuery<EmployeeResponse>;

public sealed record GetEmployeesQuery(int PageNumber, int PageSize, string? JobTitleFilter = null)
    : IQuery<PagedResult<EmployeeResponse>>;

public sealed record AssignEmployeeToDepartmentCommand(Guid EmployeeId, Guid DepartmentId) : ICommand;
