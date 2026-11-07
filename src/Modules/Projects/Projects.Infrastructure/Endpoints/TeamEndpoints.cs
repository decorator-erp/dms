using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Projects.Contracts;
using Projects.Contracts.Teams;
using SharedKernel;

namespace Projects.Infrastructure.Endpoints;

internal sealed class TeamEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/projects/{projectId:guid}/teams").WithTags("StageTeams");

        // E7: the project's team list (a read: stages.read), a team made by hand and member removal
        // (stage_teams.manage).
        group.MapGet("/", async (
            Guid projectId, Guid? stageId, ICurrentUser currentUser,
            IQueryHandler<GetProjectTeamsQuery, IReadOnlyList<ProjectTeamListItemResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<ProjectTeamListItemResponse>> result =
                await handler.HandleAsync(new GetProjectTeamsQuery(projectId, currentUser.UserId!, stageId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Read);

        group.MapPost("/", async (
            Guid projectId, CreateTeamRequest request, ICurrentUser currentUser,
            ICommandHandler<CreateStageTeamCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateStageTeamCommand(
                projectId, currentUser.UserId!, request.ProjectStageId, request.DepartmentId, request.HeadId), ct);
            return result.Match(stageTeamId => Results.Ok(new { StageTeamId = stageTeamId }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTeams.Manage);

        group.MapDelete("/members/{stageTeamMemberId:guid}", async (
            Guid projectId, Guid stageTeamMemberId, ICurrentUser currentUser,
            ICommandHandler<RemoveStageTeamMemberCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RemoveStageTeamMemberCommand(projectId, stageTeamMemberId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTeams.Manage);

        group.MapGet("/{stageTeamId:guid}", async (
            Guid projectId, Guid stageTeamId, ICurrentUser currentUser,
            IQueryHandler<GetStageTeamQuery, StageTeamResponse> handler, CancellationToken ct) =>
        {
            Result<StageTeamResponse> result = await handler.HandleAsync(new GetStageTeamQuery(projectId, stageTeamId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTeams.Manage);

        group.MapGet("/{stageTeamId:guid}/members", async (
            Guid projectId, Guid stageTeamId, ICurrentUser currentUser,
            IQueryHandler<GetStageTeamMembersQuery, IReadOnlyList<StageTeamMemberResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<StageTeamMemberResponse>> result = await handler.HandleAsync(new GetStageTeamMembersQuery(projectId, stageTeamId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTeams.Manage);

        group.MapPost("/{stageTeamId:guid}/members", async (
            Guid projectId, Guid stageTeamId, AddMemberRequest request, ICurrentUser currentUser,
            ICommandHandler<AddStageTeamMemberCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new AddStageTeamMemberCommand(
                projectId, stageTeamId, currentUser.UserId!, request.EmployeeId, request.Role), ct);
            return result.Match(memberId => Results.Ok(new { StageTeamMemberId = memberId }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTeams.Manage);

        group.MapPut("/members/{stageTeamMemberId:guid}/role", async (
            Guid projectId, Guid stageTeamMemberId, ChangeRoleRequest request, ICurrentUser currentUser,
            ICommandHandler<ChangeStageTeamMemberRoleCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new ChangeStageTeamMemberRoleCommand(projectId, stageTeamMemberId, currentUser.UserId!, request.Role), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTeams.Manage);

        group.MapPut("/{stageTeamId:guid}/head", async (
            Guid projectId, Guid stageTeamId, ChangeHeadRequest request, ICurrentUser currentUser,
            ICommandHandler<ChangeStageTeamHeadCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new ChangeStageTeamHeadCommand(projectId, stageTeamId, currentUser.UserId!, request.HeadId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTeams.Manage);
    }

    private sealed record AddMemberRequest(Guid EmployeeId, StageTeamMemberRole Role);
    private sealed record ChangeRoleRequest(StageTeamMemberRole Role);
    private sealed record ChangeHeadRequest(Guid HeadId);

    private sealed record CreateTeamRequest(Guid ProjectStageId, Guid? DepartmentId, Guid? HeadId);
}
