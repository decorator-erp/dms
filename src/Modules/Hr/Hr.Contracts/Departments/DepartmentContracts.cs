using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Hr.Contracts.Departments;

/// <summary>See the remarks on <c>Hr.Contracts.Employees.EmployeeContracts</c> — same reason these
/// command/query wrapper types and DTOs live here rather than in <c>Hr.Application</c>.</summary>
public sealed record DepartmentResponse(Guid Id, string Name, string? Description, Guid? DepartmentHeadId, bool IsActive);

public sealed record CreateDepartmentCommand(string Name, string? Description = null) : ICommand<Guid>;

public sealed record UpdateDepartmentCommand(Guid DepartmentId, string Name, string? Description) : ICommand;

public sealed record DeleteDepartmentCommand(Guid DepartmentId) : ICommand;

public sealed record AssignDepartmentHeadCommand(Guid DepartmentId, Guid EmployeeId) : ICommand;

public sealed record UnassignDepartmentHeadCommand(Guid DepartmentId) : ICommand;

public sealed record GetDepartmentQuery(Guid DepartmentId) : IQuery<DepartmentResponse>;

public sealed record GetDepartmentsQuery(int PageNumber, int PageSize, string? NameFilter = null)
    : IQuery<PagedResult<DepartmentResponse>>;

/// <summary>Moves an employee from whichever department they're currently in (if any) to
/// <paramref name="NewDepartmentId"/> — ported from the backend's
/// <c>DepartmentDomainService.MoveEmployeeAsync</c>.</summary>
public sealed record MoveEmployeeToDepartmentCommand(Guid EmployeeId, Guid NewDepartmentId) : ICommand;

public sealed record RemoveEmployeeFromDepartmentCommand(Guid EmployeeId, Guid DepartmentId) : ICommand;
