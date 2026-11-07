using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts.Members;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Members;

internal sealed class AddProjectMemberCommandValidator : AbstractValidator<AddProjectMemberCommand>
{
    public AddProjectMemberCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Access).IsInEnum();
    }
}

internal sealed class ChangeProjectMemberAccessCommandValidator : AbstractValidator<ChangeProjectMemberAccessCommand>
{
    public ChangeProjectMemberAccessCommandValidator()
    {
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.Access).IsInEnum();
    }
}

internal static class ProjectMembers
{
    /// <summary>The project exists and is active (an inactive project takes no new members).</summary>
    public static Task<bool> ProjectIsLiveAsync(ProjectsDbContext db, Guid projectId, CancellationToken ct) =>
        db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId && p.IsActive, ct);
}

/// <summary>E1 (D1). Runs behind the project scope decorator (404 for a caller outside the project). The
/// employee must exist and be active in HR (<see cref="IHrDirectory.GetAsync"/>), else 404. A duplicate —
/// whether seen up front or lost to a racing insert on the unique (project, employee) index — is 409.</summary>
internal sealed class AddProjectMemberCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : ICommandHandler<AddProjectMemberCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(AddProjectMemberCommand command, CancellationToken ct = default)
    {
        // FINDING-E-6: a read-all caller passes the scope decorator without the project existing.
        if (!await ProjectMembers.ProjectIsLiveAsync(db, command.ProjectId, ct))
        {
            return Result.Failure<Guid>(Domain.ProjectErrors.NotFound);
        }

        Guid? addedById = await hrDirectory.ResolveEmployeeIdAsync(command.UserId, ct);
        if (addedById is null)
        {
            return Result.Failure<Guid>(Domain.ProjectErrors.AccessDenied);
        }

        EmployeeSummary? employee = await hrDirectory.GetAsync(command.EmployeeId, ct);
        if (employee is not { IsActive: true })
        {
            return Result.Failure<Guid>(Domain.ProjectMemberErrors.EmployeeNotFound);
        }

        bool exists = await db.ProjectMembers.AsNoTracking()
            .AnyAsync(m => m.ProjectId == command.ProjectId && m.EmployeeId == command.EmployeeId, ct);
        if (exists)
        {
            return Result.Failure<Guid>(Domain.ProjectMemberErrors.AlreadyMember);
        }

        var member = Domain.ProjectMember.Create(
            command.ProjectId, command.EmployeeId, ProjectsEnumMapping.ToDomain(command.Access), addedById.Value);
        db.ProjectMembers.Add(member);

        Result saved = await ProjectMemberWrites.SaveAsync(db, ct);
        return saved.IsFailure ? Result.Failure<Guid>(saved.Error) : member.Id;
    }
}

internal sealed class ChangeProjectMemberAccessCommandHandler(ProjectsDbContext db)
    : ICommandHandler<ChangeProjectMemberAccessCommand>
{
    public async Task<Result> HandleAsync(ChangeProjectMemberAccessCommand command, CancellationToken ct = default)
    {
        Domain.ProjectMember? member = await db.ProjectMembers
            .FirstOrDefaultAsync(m => m.Id == command.MemberId && m.ProjectId == command.ProjectId, ct);
        if (member is null)
        {
            return Domain.ProjectMemberErrors.NotFound;
        }

        member.ChangeAccess(ProjectsEnumMapping.ToDomain(command.Access));
        return await ProjectMemberWrites.SaveAsync(db, ct);
    }
}

/// <summary>The member is matched on (member id, project id), so an id from another project is 404 —
/// the scope decorator has already checked the caller against <i>this</i> project only.</summary>
internal sealed class RemoveProjectMemberCommandHandler(ProjectsDbContext db)
    : ICommandHandler<RemoveProjectMemberCommand>
{
    public async Task<Result> HandleAsync(RemoveProjectMemberCommand command, CancellationToken ct = default)
    {
        Domain.ProjectMember? member = await db.ProjectMembers
            .FirstOrDefaultAsync(m => m.Id == command.MemberId && m.ProjectId == command.ProjectId, ct);
        if (member is null)
        {
            return Domain.ProjectMemberErrors.NotFound;
        }

        db.ProjectMembers.Remove(member);
        return await ProjectMemberWrites.SaveAsync(db, ct);
    }
}

/// <summary>Two statements whatever the member count: the rows, then one batched HR profile read for the
/// names.</summary>
internal sealed class GetProjectMembersQueryHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : IQueryHandler<GetProjectMembersQuery, IReadOnlyList<ProjectMemberResponse>>
{
    public async Task<Result<IReadOnlyList<ProjectMemberResponse>>> HandleAsync(
        GetProjectMembersQuery query, CancellationToken ct = default)
    {
        if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == query.ProjectId, ct))
        {
            return Result.Failure<IReadOnlyList<ProjectMemberResponse>>(Domain.ProjectErrors.NotFound);
        }

        List<Domain.ProjectMember> members = await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == query.ProjectId)
            .OrderBy(m => m.AddedAtUtc)
            .ToListAsync(ct);

        Dictionary<Guid, string> names = members.Count == 0
            ? []
            : (await hrDirectory.GetProfilesAsync([.. members.Select(m => m.EmployeeId).Distinct()], ct))
                .ToDictionary(p => p.Id, p => p.FullName);

        List<ProjectMemberResponse> response = [.. members.Select(m => new ProjectMemberResponse(
            m.Id, m.ProjectId, m.EmployeeId, ProjectsEnumMapping.ToContracts(m.Access), m.CanManage, m.CanExecuteTasks,
            names.GetValueOrDefault(m.EmployeeId), m.AddedById, m.AddedAtUtc))];

        return Result.Success<IReadOnlyList<ProjectMemberResponse>>(response);
    }
}
