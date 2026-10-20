using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Hr.Contracts.Employees;

public sealed record EmployeeResponse(
    Guid Id,
    string AppUserId,
    string JobTitle,
    DateOnly HireDate,
    DateOnly? EndDate,
    string? Notes,
    string ContractType,
    string? PhoneNumber,
    string? EmergencyContactPhone,
    bool IsActive);

/// <summary>
/// What the employee-detail and profile screens read: every field of <see cref="EmployeeResponse"/>,
/// plus the name and email that live in Identity and the department membership that lives in HR's own
/// join table, and (N7) whether the login is active. Built by <c>GetEmployeeDetailQuery</c> (Aug 3.10)
/// from one HR read, one batched <see cref="IHrDirectory.GetProfilesAsync"/> call and the login's state.
///
/// <para><b>Deliberately not <see cref="EmployeeSummary"/>, and deliberately not a widening of it.</b>
/// <c>EmployeeSummary</c> is the cross-module lookup type; it carries no name on purpose, because the
/// authorization and assignment paths that read it never need one, and it has nine consumers across
/// four other modules. A detail screen asks a different question than an authorization check, so it
/// gets its own answer rather than making every one of those callers pay for a name.</para>
///
/// <para><see cref="PrimaryDepartmentId"/> is here because <see cref="EmployeeSummary"/> carried it and
/// this route used to return that type — dropping it would have been a silent regression on a field
/// the screen shows. <see cref="IHrDirectory.GetProfilesAsync"/> already resolves it, so it is free.</para>
/// </summary>
public sealed record EmployeeDetailResponse(
    Guid Id,
    string AppUserId,
    string JobTitle,
    DateOnly HireDate,
    DateOnly? EndDate,
    string? Notes,
    string ContractType,
    string? PhoneNumber,
    string? EmergencyContactPhone,
    bool IsActive,
    string FullName,
    string? Email,
    Guid? PrimaryDepartmentId,
    bool LoginActive);

public sealed record CreateEmployeeCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string JobTitle,
    DateOnly HireDate,
    string ContractType,
    string? PhoneNumber = null,
    string? EmergencyContactPhone = null,
    string? Notes = null) : ICommand<Guid>;

/// <summary>N7: a full replacement. <see cref="JobTitle"/> and <see cref="ContractType"/> are required;
/// a null optional field clears it. <see cref="FirstName"/> and <see cref="LastName"/> are the login's
/// name, both or neither; omitted, the name is left as it is.</summary>
public sealed record UpdateEmployeeCommand(
    Guid EmployeeId,
    string JobTitle,
    string ContractType,
    string? PhoneNumber,
    string? EmergencyContactPhone,
    string? Notes,
    DateOnly? EndDate,
    string? FirstName = null,
    string? LastName = null) : ICommand;

public sealed record DeactivateEmployeeCommand(Guid EmployeeId) : ICommand;

public sealed record ActivateEmployeeCommand(Guid EmployeeId) : ICommand;

public sealed record GetEmployeeQuery(Guid EmployeeId) : IQuery<EmployeeResponse>;

/// <summary>Aug 3.10: the detail route's read (it used to be composed in the endpoint).</summary>
public sealed record GetEmployeeDetailQuery(Guid EmployeeId) : IQuery<EmployeeDetailResponse>;

/// <summary>Aug 3.10: the caller's own detail, for <c>GET /api/employees/me</c>.</summary>
public sealed record GetMyEmployeeDetailQuery : IQuery<EmployeeDetailResponse>;

/// <summary>K3: deactivated employees are left out unless <paramref name="IncludeInactive"/>.</summary>
public sealed record GetEmployeesQuery(int PageNumber, int PageSize, string? JobTitleFilter = null, bool IncludeInactive = false)
    : IQuery<PagedResult<EmployeeResponse>>;

/// <summary>Aug 4.3: the page <c>GET /api/employees</c> serves, names included, from one read of
/// <c>hr.employees</c> and one batched Identity call. Same filters and order as
/// <see cref="GetEmployeesQuery"/>.</summary>
public sealed record GetEmployeeProfilesQuery(
    int PageNumber, int PageSize, string? JobTitleFilter = null, bool IncludeInactive = false)
    : IQuery<PagedResult<EmployeeProfile>>;

public sealed record AssignEmployeeToDepartmentCommand(Guid EmployeeId, Guid DepartmentId) : ICommand;
