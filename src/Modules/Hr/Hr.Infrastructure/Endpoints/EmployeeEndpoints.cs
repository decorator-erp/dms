using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Hr.Infrastructure.Endpoints;

/// <summary>
/// Neither the LIST nor the DETAIL route serves what its CQRS query returns. <c>GetEmployeesQuery</c>
/// and <c>GetEmployeeQuery</c> both answer with <see cref="EmployeeResponse"/>, which never carries a
/// name — names and emails live in Identity, not HR. So both routes enrich HR's own ids through
/// <see cref="IHrDirectory.GetProfilesAsync"/>, one batched call, after the query has run: the LIST
/// route for the page's ids, the DETAIL route for a single one. Neither changes what the query itself
/// returns.
///
/// <para><b><see cref="EmployeeSummary"/> is deliberately not what any of these routes serve.</b> It is
/// the cross-module lookup type — no name, by design, because the authorization and assignment paths
/// that read it never need one — and <see cref="IHrDirectory.GetAsync"/> keeps returning it unchanged
/// for its nine consumers in four other modules. Serving it from the DETAIL route was the bug: a
/// profile screen normalised seventeen fields out of five and rendered blanks with no error.</para>
/// </summary>
internal sealed class EmployeeEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/employees").WithTags("Employees");

        // Paging is optional, for the reason spelled out on /api/departments - which also explains why
        // that default is 200 and this one is 50.
        group.MapGet("/", async (
            int? pageNumber, int? pageSize, string? jobTitleFilter,
            IQueryHandler<GetEmployeesQuery, PagedResult<EmployeeResponse>> listHandler,
            IHrDirectory hrDirectory,
            CancellationToken ct) =>
        {
            Result<PagedResult<EmployeeResponse>> pageResult = await listHandler.HandleAsync(
                new GetEmployeesQuery(pageNumber ?? 1, pageSize ?? 50, jobTitleFilter), ct);
            if (pageResult.IsFailure)
            {
                return pageResult.ToProblem();
            }

            PagedResult<EmployeeResponse> page = pageResult.Value!;
            IReadOnlyList<EmployeeProfile> profiles = await hrDirectory.GetProfilesAsync([.. page.Items.Select(e => e.Id)], ct);

            return Results.Ok(new PagedResult<EmployeeProfile>(profiles, page.PageNumber, page.PageSize, page.TotalCount));
        }).RequirePermission(HrPermissions.EmployeesRead);

        group.MapGet("/{employeeId:guid}", (
            Guid employeeId,
            IQueryHandler<GetEmployeeQuery, EmployeeResponse> handler,
            IHrDirectory hrDirectory,
            CancellationToken ct) => DetailAsync(employeeId, handler, hrDirectory, ct))
            .RequirePermission(HrPermissions.EmployeesRead);

        // 201 + Location, matching the clients route. Still EmployeesCreate: hiring now creates the
        // login as well, but that is the same act by the same person, not a second privilege.
        group.MapPost("/", async (
            CreateEmployeeCommand command, ICommandHandler<CreateEmployeeCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(command, ct);
            return result.Match(
                employeeId => Results.Created($"/api/employees/{employeeId}", new { EmployeeId = employeeId }),
                r => r.ToProblem());
        }).RequirePermission(HrPermissions.EmployeesCreate);

        group.MapPut("/{employeeId:guid}", async (
            Guid employeeId, UpdateEmployeeRequest request, ICommandHandler<UpdateEmployeeCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateEmployeeCommand(
                employeeId, request.JobTitle, request.ContractType, request.PhoneNumber,
                request.EmergencyContactPhone, request.Notes, request.EndDate), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.EmployeesUpdate);

        group.MapPost("/{employeeId:guid}/deactivate", async (
            Guid employeeId, ICommandHandler<DeactivateEmployeeCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeactivateEmployeeCommand(employeeId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.EmployeesSetActive);

        group.MapPost("/{employeeId:guid}/activate", async (
            Guid employeeId, ICommandHandler<ActivateEmployeeCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new ActivateEmployeeCommand(employeeId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.EmployeesSetActive);

        group.MapPost("/{employeeId:guid}/department/{departmentId:guid}", async (
            Guid employeeId, Guid departmentId,
            ICommandHandler<AssignEmployeeToDepartmentCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new AssignEmployeeToDepartmentCommand(employeeId, departmentId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsAssignToUser);
    }

    /// <summary>
    /// The DETAIL composition, shared with <c>/me</c> so the two routes cannot drift into answering the
    /// same question differently: one <c>GetEmployeeQuery</c>, then one batched
    /// <see cref="IHrDirectory.GetProfilesAsync"/> to attach the name, email and department that
    /// <see cref="EmployeeResponse"/> does not carry. Two calls total, never a loop.
    ///
    /// <para>The 404 comes from the query handler's own <c>EmployeeErrors.NotFound</c> through
    /// <c>ToProblem</c>, so a missing employee now answers with the same ProblemDetails body every other
    /// HR route uses, rather than the bare empty 404 this route used to return.</para>
    /// </summary>
    private static async Task<IResult> DetailAsync(
        Guid employeeId,
        IQueryHandler<GetEmployeeQuery, EmployeeResponse> handler,
        IHrDirectory hrDirectory,
        CancellationToken ct)
    {
        Result<EmployeeResponse> result = await handler.HandleAsync(new GetEmployeeQuery(employeeId), ct);
        if (result.IsFailure)
        {
            return result.ToProblem();
        }

        EmployeeResponse employee = result.Value!;
        IReadOnlyList<EmployeeProfile> profiles = await hrDirectory.GetProfilesAsync([employeeId], ct);
        EmployeeProfile? profile = profiles.Count > 0 ? profiles[0] : null;

        return Results.Ok(new EmployeeDetailResponse(
            employee.Id, employee.AppUserId, employee.JobTitle, employee.HireDate, employee.EndDate,
            employee.Notes, employee.ContractType, employee.PhoneNumber, employee.EmergencyContactPhone,
            employee.IsActive, profile?.FullName ?? string.Empty, profile?.Email,
            profile?.PrimaryDepartmentId));
    }

    private sealed record UpdateEmployeeRequest(
        string? JobTitle, string? ContractType, string? PhoneNumber,
        string? EmergencyContactName, string? EmergencyContactPhone, string? Notes, DateOnly? EndDate);
}
