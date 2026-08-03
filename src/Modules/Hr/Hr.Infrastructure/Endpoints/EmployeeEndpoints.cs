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

namespace Hr.Infrastructure.Endpoints;

/// <summary>
/// The LIST endpoint returns <see cref="EmployeeProfile"/> (names — what the screen shows); the LOOKUP
/// endpoint returns <see cref="EmployeeSummary"/> (HR-only fields — what another module, or a
/// low-privilege caller, needs). <c>GetEmployeesQuery</c>'s own response type
/// (<see cref="EmployeeResponse"/>) never carries a name — this endpoint enriches the page's ids through
/// <see cref="IHrDirectory.GetProfilesAsync"/> after paging, not by changing what the CQRS query itself
/// returns.
/// </summary>
internal sealed class EmployeeEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/employees").WithTags("Employees");

        group.MapGet("/", async (
            int pageNumber, int pageSize, string? jobTitleFilter,
            IQueryHandler<GetEmployeesQuery, PagedResult<EmployeeResponse>> listHandler,
            IHrDirectory hrDirectory,
            CancellationToken ct) =>
        {
            var pageResult = await listHandler.HandleAsync(new GetEmployeesQuery(pageNumber, pageSize, jobTitleFilter), ct);
            if (pageResult.IsFailure)
                return pageResult.ToProblem();

            var page = pageResult.Value!;
            var profiles = await hrDirectory.GetProfilesAsync([.. page.Items.Select(e => e.Id)], ct);

            return Results.Ok(new PagedResult<EmployeeProfile>(profiles, page.PageNumber, page.PageSize, page.TotalCount));
        }).RequirePermission(HrPermissions.EmployeesRead);

        group.MapGet("/{employeeId:guid}", async (
            Guid employeeId, IHrDirectory hrDirectory, CancellationToken ct) =>
        {
            var summary = await hrDirectory.GetAsync(employeeId, ct);
            return summary is null ? Results.NotFound() : Results.Ok(summary);
        }).RequirePermission(HrPermissions.EmployeesRead);

        group.MapPost("/", async (
            CreateEmployeeCommand command, ICommandHandler<CreateEmployeeCommand, Guid> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct);
            return result.Match(employeeId => Results.Ok(new { EmployeeId = employeeId }), r => r.ToProblem());
        }).RequirePermission(HrPermissions.EmployeesCreate);

        group.MapPut("/{employeeId:guid}", async (
            Guid employeeId, UpdateEmployeeRequest request, ICommandHandler<UpdateEmployeeCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new UpdateEmployeeCommand(
                employeeId, request.JobTitle, request.ContractType, request.PhoneNumber,
                request.EmergencyContactName, request.EmergencyContactPhone, request.Notes, request.EndDate), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.EmployeesUpdate);

        group.MapPost("/{employeeId:guid}/deactivate", async (
            Guid employeeId, ICommandHandler<DeactivateEmployeeCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new DeactivateEmployeeCommand(employeeId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.EmployeesSetActive);

        group.MapPost("/{employeeId:guid}/activate", async (
            Guid employeeId, ICommandHandler<ActivateEmployeeCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ActivateEmployeeCommand(employeeId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.EmployeesSetActive);

        group.MapPost("/{employeeId:guid}/department/{departmentId:guid}", async (
            Guid employeeId, Guid departmentId,
            ICommandHandler<AssignEmployeeToDepartmentCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new AssignEmployeeToDepartmentCommand(employeeId, departmentId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsAssignToUser);
    }

    private sealed record UpdateEmployeeRequest(
        string? JobTitle, string? ContractType, string? PhoneNumber,
        string? EmergencyContactName, string? EmergencyContactPhone, string? Notes, DateOnly? EndDate);
}
