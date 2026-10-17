using BuildingBlocks.Messaging;
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

/// <summary>Moving/removing an employee-department membership — split from
/// <see cref="DepartmentEndpoints"/> and <see cref="EmployeeEndpoints"/> (which owns the initial
/// assignment) because both of these act on the membership itself, not on either side of it alone.</summary>
internal sealed class EmployeeDepartmentEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/employees/{employeeId:guid}/department").WithTags("EmployeeDepartments");

        group.MapPost("/{newDepartmentId:guid}/move", async (
            Guid employeeId, Guid newDepartmentId,
            ICommandHandler<MoveEmployeeToDepartmentCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new MoveEmployeeToDepartmentCommand(employeeId, newDepartmentId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsMoveUser);

        group.MapDelete("/{departmentId:guid}", async (
            Guid employeeId, Guid departmentId,
            ICommandHandler<RemoveEmployeeFromDepartmentCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RemoveEmployeeFromDepartmentCommand(employeeId, departmentId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(HrPermissions.DepartmentsRemoveFromUser);
    }
}
