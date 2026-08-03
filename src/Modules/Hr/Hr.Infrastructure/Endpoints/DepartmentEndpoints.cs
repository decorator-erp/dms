using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Hr.Contracts;
using Hr.Contracts.Departments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Hr.Infrastructure.Endpoints;

internal sealed class DepartmentEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/departments").WithTags("Departments");

        group.MapGet("/", async (
            int pageNumber, int pageSize, string? nameFilter,
            IQueryHandler<GetDepartmentsQuery, PagedResult<DepartmentResponse>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetDepartmentsQuery(pageNumber, pageSize, nameFilter), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsRead);

        group.MapGet("/{departmentId:guid}", async (
            Guid departmentId, IQueryHandler<GetDepartmentQuery, DepartmentResponse> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetDepartmentQuery(departmentId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsRead);

        group.MapGet("/{departmentId:guid}/members", async (
            Guid departmentId, IHrDirectory hrDirectory, CancellationToken ct) =>
            Results.Ok(await hrDirectory.DepartmentMembersAsync(departmentId, ct)))
            .RequirePermission(HrPermissions.DepartmentsViewUsers);

        group.MapPost("/", async (
            CreateDepartmentCommand command, ICommandHandler<CreateDepartmentCommand, Guid> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct);
            return result.Match(departmentId => Results.Ok(new { DepartmentId = departmentId }), r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsCreate);

        group.MapPut("/{departmentId:guid}", async (
            Guid departmentId, UpdateDepartmentRequest request,
            ICommandHandler<UpdateDepartmentCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new UpdateDepartmentCommand(departmentId, request.Name, request.Description), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsUpdate);

        group.MapDelete("/{departmentId:guid}", async (
            Guid departmentId, ICommandHandler<DeleteDepartmentCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new DeleteDepartmentCommand(departmentId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsDelete);

        group.MapPost("/{departmentId:guid}/head/{employeeId:guid}", async (
            Guid departmentId, Guid employeeId,
            ICommandHandler<AssignDepartmentHeadCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new AssignDepartmentHeadCommand(departmentId, employeeId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsAssignDepartmentHead);

        group.MapDelete("/{departmentId:guid}/head", async (
            Guid departmentId, ICommandHandler<UnassignDepartmentHeadCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new UnassignDepartmentHeadCommand(departmentId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsUnassignDepartmentHead);
    }

    private sealed record UpdateDepartmentRequest(string Name, string? Description);
}
