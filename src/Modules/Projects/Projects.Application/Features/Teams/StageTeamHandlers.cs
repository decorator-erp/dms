using BuildingBlocks.Messaging;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts.Teams;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Teams;

/// <summary><see cref="Domain.StageTeam"/> carries no <c>ProjectId</c> of its own — only
/// <c>ProjectStageId</c> — so every handler here re-derives project ownership by joining through
/// <c>ProjectStages</c>, same as <c>ProjectAccessGuard</c>'s own membership query (G5-05).</summary>
internal static class StageTeamAccess
{
    internal static Task<bool> BelongsToProjectAsync(ProjectsDbContext db, Guid projectStageId, Guid projectId, CancellationToken ct) =>
        db.ProjectStages.AsNoTracking().AnyAsync(s => s.Id == projectStageId && s.ProjectId == projectId, ct);
}

internal sealed class AddStageTeamMemberCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : ICommandHandler<AddStageTeamMemberCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(AddStageTeamMemberCommand command, CancellationToken ct = default)
    {
        StageTeam? team = await db.StageTeams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == command.StageTeamId, ct);
        if (team is null || !await StageTeamAccess.BelongsToProjectAsync(db, team.ProjectStageId, command.ProjectId, ct))
        {
            return Result.Failure<Guid>(Domain.TeamErrors.NotFound);
        }

        EmployeeSummary? employee = await hrDirectory.GetAsync(command.EmployeeId, ct);
        if (employee is null || !employee.IsActive)
        {
            return Result.Failure<Guid>(Domain.ProjectErrors.AccessDenied);
        }

        Result<StageTeamMember> result = team.AddMember(command.EmployeeId, ProjectsEnumMapping.ToDomain(command.Role));
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await db.SaveChangesAsync(ct);
        return result.Value!.Id;
    }
}

internal sealed class ChangeStageTeamMemberRoleCommandHandler(ProjectsDbContext db) : ICommandHandler<ChangeStageTeamMemberRoleCommand>
{
    public async Task<Result> HandleAsync(ChangeStageTeamMemberRoleCommand command, CancellationToken ct = default)
    {
        StageTeamMember? member = await db.StageTeamMembers.FindAsync([command.StageTeamMemberId], ct);
        if (member is null)
        {
            return Domain.TeamErrors.MemberNotFound;
        }

        StageTeam? team = await db.StageTeams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == member.StageTeamId, ct);
        if (team is null || !await StageTeamAccess.BelongsToProjectAsync(db, team.ProjectStageId, command.ProjectId, ct))
        {
            return Domain.TeamErrors.NotFound;
        }

        member.ChangeRole(ProjectsEnumMapping.ToDomain(command.Role));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class ChangeStageTeamHeadCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : ICommandHandler<ChangeStageTeamHeadCommand>
{
    public async Task<Result> HandleAsync(ChangeStageTeamHeadCommand command, CancellationToken ct = default)
    {
        StageTeam? team = await db.StageTeams.FirstOrDefaultAsync(t => t.Id == command.StageTeamId, ct);
        if (team is null || !await StageTeamAccess.BelongsToProjectAsync(db, team.ProjectStageId, command.ProjectId, ct))
        {
            return Domain.TeamErrors.NotFound;
        }

        EmployeeSummary? employee = await hrDirectory.GetAsync(command.HeadId, ct);
        if (employee is null || !employee.IsActive)
        {
            return Domain.ProjectErrors.AccessDenied;
        }

        team.ChangeHead(command.HeadId);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class GetStageTeamQueryHandler(ProjectsDbContext db) : IQueryHandler<GetStageTeamQuery, StageTeamResponse>
{
    public async Task<Result<StageTeamResponse>> HandleAsync(GetStageTeamQuery query, CancellationToken ct = default)
    {
        StageTeamResponse? response = await db.StageTeams.AsNoTracking()
            .Where(t => t.Id == query.StageTeamId)
            .Select(t => new StageTeamResponse(t.Id, t.ProjectStageId, t.DepartmentId, t.HeadId))
            .FirstOrDefaultAsync(ct);

        if (response is null)
        {
            return Result.Failure<StageTeamResponse>(Domain.TeamErrors.NotFound);
        }

        return await StageTeamAccess.BelongsToProjectAsync(db, response.ProjectStageId, query.ProjectId, ct)
            ? response
            : Result.Failure<StageTeamResponse>(Domain.TeamErrors.NotFound);
    }
}

internal sealed class GetStageTeamMembersQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetStageTeamMembersQuery, IReadOnlyList<StageTeamMemberResponse>>
{
    public async Task<Result<IReadOnlyList<StageTeamMemberResponse>>> HandleAsync(
        GetStageTeamMembersQuery query, CancellationToken ct = default)
    {
        StageTeam? team = await db.StageTeams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == query.StageTeamId, ct);
        if (team is null || !await StageTeamAccess.BelongsToProjectAsync(db, team.ProjectStageId, query.ProjectId, ct))
        {
            return Result.Failure<IReadOnlyList<StageTeamMemberResponse>>(Domain.TeamErrors.NotFound);
        }

        List<StageTeamMemberResponse> members = await db.StageTeamMembers.AsNoTracking()
            .Where(m => m.StageTeamId == query.StageTeamId)
            .Select(m => new StageTeamMemberResponse(m.Id, m.StageTeamId, m.EmployeeId, ProjectsEnumMapping.ToContracts(m.Role)))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<StageTeamMemberResponse>>(members);
    }
}
