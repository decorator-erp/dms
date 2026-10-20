using BuildingBlocks;
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
/// The LIST route doesn't serve what its CQRS query returns. <c>GetEmployeesQuery</c> answers with
/// <see cref="EmployeeResponse"/>, which never carries a name — names and emails live in Identity, not
/// HR. So the route enriches the page's ids through <see cref="IHrDirectory.GetProfilesAsync"/>, one
/// batched call, after the query has run, and puts the profiles back in the page's order. The DETAIL and
/// <c>/me</c> routes used to do the same composition here; since Aug 3.10 it is
/// <c>GetEmployeeDetailQuery</c> and <c>GetMyEmployeeDetailQuery</c>.
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
            int? pageNumber, int? pageSize, string? jobTitleFilter, bool? includeInactive,
            IQueryHandler<GetEmployeesQuery, PagedResult<EmployeeResponse>> listHandler,
            IHrDirectory hrDirectory,
            CancellationToken ct) =>
        {
            Result<PagedResult<EmployeeResponse>> pageResult = await listHandler.HandleAsync(
                new GetEmployeesQuery(pageNumber ?? 1, pageSize ?? 50, jobTitleFilter, includeInactive ?? false), ct);
            if (pageResult.IsFailure)
            {
                return pageResult.ToProblem();
            }

            PagedResult<EmployeeResponse> page = pageResult.Value!;
            IReadOnlyList<EmployeeProfile> profiles = await hrDirectory.GetProfilesAsync([.. page.Items.Select(e => e.Id)], ct);

            // Aug 3.4: GetProfilesAsync answers in no particular order; the page's order is the query's.
            var byId = profiles.ToDictionary(p => p.Id);
            EmployeeProfile[] ordered = [.. page.Items.Where(e => byId.ContainsKey(e.Id)).Select(e => byId[e.Id])];

            return Results.Ok(new PagedResult<EmployeeProfile>(ordered, page.PageNumber, page.PageSize, page.TotalCount));
        }).RequirePermission(HrPermissions.EmployeesRead);

        group.MapGet("/{employeeId:guid}", async (
            Guid employeeId, IQueryHandler<GetEmployeeDetailQuery, EmployeeDetailResponse> handler, CancellationToken ct) =>
        {
            Result<EmployeeDetailResponse> result = await handler.HandleAsync(new GetEmployeeDetailQuery(employeeId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(HrPermissions.EmployeesRead);

        // Safe without reordering ahead of the route above: "/{employeeId:guid}" carries a :guid route
        // constraint, so the literal segment "me" can never match it.
        //
        // 404, not 401, once the caller resolves: a client user legitimately has no employee record, and
        // that is a normal outcome for this route, not a caller error the same way a missing token is.
        group.MapGet("/me", async (
            IQueryHandler<GetMyEmployeeDetailQuery, EmployeeDetailResponse> handler, CancellationToken ct) =>
        {
            Result<EmployeeDetailResponse> result = await handler.HandleAsync(new GetMyEmployeeDetailQuery(), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(HrPermissions.EmployeesViewOwn);

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
                request.EmergencyContactPhone, request.Notes, request.EndDate, request.FirstName, request.LastName), ct);
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

    // No EmergencyContactName: the column was dropped by migration 20260827091145_UpdateEmployeeEntity,
    // and the field is gone from Employee, EmployeeResponse and UpdateEmployeeCommand alike. Declaring
    // it here without ever reading it would let a client send it and have it silently discarded.
    // N7: a full replacement. JobTitle and ContractType are required (the validator says so); a missing
    // or null optional field clears it. FirstName/LastName are the login's name, both or neither, and
    // omitted means unchanged.
    private sealed record UpdateEmployeeRequest(
        string JobTitle, string ContractType, string? PhoneNumber,
        string? EmergencyContactPhone, string? Notes, DateOnly? EndDate,
        string? FirstName = null, string? LastName = null);
}
