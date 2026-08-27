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
using SharedKernel;

namespace Hr.Infrastructure.Endpoints;

internal sealed class DepartmentEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/departments").WithTags("Departments");

        // Paging is optional. Declared non-nullable, these bound as REQUIRED query parameters, so a
        // caller that named no page got a BadHttpRequestException from model binding - a 400 raised
        // before the handler, and before the validator that supposedly owns the range, ever ran.
        //
        // The default size is 200 here and 50 on /api/employees. That divergence is deliberate:
        // departments is a lookup dimension backing a picker, and a picker silently truncated at 50
        // with nothing to signal that more exist is a trap for the next caller. 200 is the validator's
        // own ceiling - "everything you will allow". Employees is a genuine browse list, where one
        // page is what the screen actually wants.
        group.MapGet("/", async (
            int? pageNumber, int? pageSize, string? nameFilter,
            IQueryHandler<GetDepartmentsQuery, PagedResult<DepartmentResponse>> handler, CancellationToken ct) =>
        {
            Result<PagedResult<DepartmentResponse>> result = await handler.HandleAsync(
                new GetDepartmentsQuery(pageNumber ?? 1, pageSize ?? 200, nameFilter), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsRead);

        group.MapGet("/{departmentId:guid}", async (
            Guid departmentId, IQueryHandler<GetDepartmentQuery, DepartmentResponse> handler, CancellationToken ct) =>
        {
            Result<DepartmentResponse> result = await handler.HandleAsync(new GetDepartmentQuery(departmentId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsRead);

        // A bare Guid[] cannot render a member row. DepartmentMembersAsync answers with HR's own
        // employee ids; GetProfilesAsync turns the whole set into names and emails in ONE batched
        // Identity call - the same two-call shape the employees LIST and DETAIL routes use, and never a
        // lookup per id. GetProfilesAsync already short-circuits an empty id list, so a memberless
        // department costs one query here and no cross-module hop at all.
        group.MapGet("/{departmentId:guid}/members", async (
            Guid departmentId, IHrDirectory hrDirectory, CancellationToken ct) =>
        {
            IReadOnlyList<Guid> memberIds = await hrDirectory.DepartmentMembersAsync(departmentId, ct);
            IReadOnlyList<EmployeeProfile> members = await hrDirectory.GetProfilesAsync(memberIds, ct);

            return Results.Ok(members);
        }).RequirePermission(HrPermissions.DepartmentsViewUsers);

        group.MapPost("/", async (
            CreateDepartmentCommand command, ICommandHandler<CreateDepartmentCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(command, ct);
            return result.Match(departmentId => Results.Ok(new { DepartmentId = departmentId }), r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsCreate);

        group.MapPut("/{departmentId:guid}", async (
            Guid departmentId, UpdateDepartmentRequest request,
            ICommandHandler<UpdateDepartmentCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new UpdateDepartmentCommand(departmentId, request.Name, request.Description), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsUpdate);

        group.MapDelete("/{departmentId:guid}", async (
            Guid departmentId, ICommandHandler<DeleteDepartmentCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteDepartmentCommand(departmentId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsDelete);

        group.MapPost("/{departmentId:guid}/head/{employeeId:guid}", async (
            Guid departmentId, Guid employeeId,
            ICommandHandler<AssignDepartmentHeadCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new AssignDepartmentHeadCommand(departmentId, employeeId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsAssignDepartmentHead);

        group.MapDelete("/{departmentId:guid}/head", async (
            Guid departmentId, ICommandHandler<UnassignDepartmentHeadCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UnassignDepartmentHeadCommand(departmentId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsUnassignDepartmentHead);
    }

    private sealed record UpdateDepartmentRequest(string Name, string? Description);
}
