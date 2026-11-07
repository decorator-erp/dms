using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Application.Features.Projects;
using Projects.Contracts.Teams;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Teams;

internal sealed class CreateStageTeamCommandValidator : AbstractValidator<CreateStageTeamCommand>
{
    public CreateStageTeamCommandValidator()
    {
        RuleFor(x => x.ProjectStageId).NotEmpty();
        RuleFor(x => x)
            .Must(x => x.DepartmentId is null != x.HeadId is null)
            .WithErrorCode("projects.teams.HeadOrDepartmentRequired")
            .WithMessage("Give either a department or a head, not both.");
    }
}

/// <summary>E7: the stage must be the project's (404). A department team takes the department's head from HR
/// (409 <c>teams.MissingHead</c> when it has none, as instantiation does) and is unique per stage (409); a
/// standalone team needs an active head (404).</summary>
internal sealed class CreateStageTeamCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : ICommandHandler<CreateStageTeamCommand, Guid>
{
    public Task<Result<Guid>> HandleAsync(CreateStageTeamCommand command, CancellationToken ct = default) =>
        ProjectWriteLock.RunAsync(db, command.ProjectId, () => CreateAsync(command, ct), ct);

    private async Task<Result<Guid>> CreateAsync(CreateStageTeamCommand command, CancellationToken ct)
    {
        // NP3: no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return Result.Failure<Guid>(live.Error);
        }

        if (!await StageTeamAccess.BelongsToProjectAsync(db, command.ProjectStageId, command.ProjectId, ct))
        {
            return Result.Failure<Guid>(Domain.StageErrors.NotFound);
        }

        Domain.StageTeam team;
        if (command.DepartmentId is { } departmentId)
        {
            if (await db.StageTeams.AnyAsync(t => t.ProjectStageId == command.ProjectStageId && t.DepartmentId == departmentId, ct))
            {
                return Result.Failure<Guid>(Domain.TeamErrors.AlreadyExists);
            }

            IReadOnlyList<DepartmentHeadSummary> heads = await hrDirectory.GetDepartmentHeadsAsync([departmentId], ct);
            Guid? headId = heads.Count == 0 ? null : heads[0].HeadEmployeeId;
            if (headId is null)
            {
                return Result.Failure<Guid>(Domain.TeamErrors.MissingHead);
            }

            team = Domain.StageTeam.CreateProjectDepartmentTeam(command.ProjectStageId, departmentId, headId.Value);
        }
        else
        {
            EmployeeSummary? head = await hrDirectory.GetAsync(command.HeadId!.Value, ct);
            if (head is not { IsActive: true })
            {
                return Result.Failure<Guid>(Domain.TeamErrors.HeadNotFound);
            }

            team = Domain.StageTeam.CreateProjectStandaloneTeam(command.ProjectStageId, head.Id);
        }

        db.StageTeams.Add(team);
        await db.SaveChangesAsync(ct);
        return team.Id;
    }
}

/// <summary>E7: the member must sit on a team of one of this project's stages (404 otherwise).</summary>
internal sealed class RemoveStageTeamMemberCommandHandler(ProjectsDbContext db) : ICommandHandler<RemoveStageTeamMemberCommand>
{
    public async Task<Result> HandleAsync(RemoveStageTeamMemberCommand command, CancellationToken ct = default)
    {
        // NP3: no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return live;
        }

        Domain.StageTeamMember? member = await db.StageTeamMembers
            .Where(m => m.Id == command.StageTeamMemberId)
            .Join(db.StageTeams, m => m.StageTeamId, t => t.Id, (m, t) => new { m, t })
            .Join(db.ProjectStages, x => x.t.ProjectStageId, s => s.Id, (x, s) => new { x.m, s.ProjectId })
            .Where(x => x.ProjectId == command.ProjectId)
            .Select(x => x.m)
            .FirstOrDefaultAsync(ct);
        if (member is null)
        {
            return Domain.TeamErrors.MemberNotFound;
        }

        db.StageTeamMembers.Remove(member);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // FINDING-E-7: a racing removal (or a stage delete cascading the team) got there first.
            return Domain.TeamErrors.MemberNotFound;
        }

        return Result.Success();
    }
}

/// <summary>E7: two statements whatever the team count — the teams with stage name and member count in one
/// query, then one batched HR read for the heads' names.</summary>
internal sealed class GetProjectTeamsQueryHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : IQueryHandler<GetProjectTeamsQuery, IReadOnlyList<ProjectTeamListItemResponse>>
{
    public async Task<Result<IReadOnlyList<ProjectTeamListItemResponse>>> HandleAsync(GetProjectTeamsQuery query, CancellationToken ct = default)
    {
        IQueryable<Domain.ProjectStage> stages = db.ProjectStages.AsNoTracking().Where(s => s.ProjectId == query.ProjectId);
        if (query.StageId is { } stageId)
        {
            stages = stages.Where(s => s.Id == stageId);
        }

        var rows = await db.StageTeams.AsNoTracking()
            .Join(stages, t => t.ProjectStageId, s => s.Id, (t, s) => new
            {
                t.Id,
                t.ProjectStageId,
                StageName = s.Name,
                StageOrder = s.Order,
                t.DepartmentId,
                t.HeadId,
                MemberCount = db.StageTeamMembers.Count(m => m.StageTeamId == t.Id),
            })
            .OrderBy(x => x.StageOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        Dictionary<Guid, string> names = rows.Count == 0
            ? []
            : (await hrDirectory.GetProfilesAsync([.. rows.Select(r => r.HeadId).Distinct()], ct)).ToDictionary(p => p.Id, p => p.FullName);

        List<ProjectTeamListItemResponse> response = [.. rows.Select(r => new ProjectTeamListItemResponse(
            r.Id, r.ProjectStageId, r.StageName, r.StageOrder, r.DepartmentId, r.HeadId, names.GetValueOrDefault(r.HeadId), r.MemberCount))];
        return Result.Success<IReadOnlyList<ProjectTeamListItemResponse>>(response);
    }
}
