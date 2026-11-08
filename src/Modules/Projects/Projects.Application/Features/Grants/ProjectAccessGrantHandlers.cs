using BuildingBlocks;
using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Projects.Contracts;
using Projects.Contracts.Grants;
using Projects.Infrastructure.Persistence;
using Projects.Infrastructure.Services;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Grants;

internal sealed class CreateProjectAccessGrantCommandValidator : AbstractValidator<CreateProjectAccessGrantCommand>
{
    public CreateProjectAccessGrantCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Level).IsInEnum();
        RuleFor(x => x.Areas).Must(GrantRules.AreValidAreas).WithMessage("Choose at least one known area.");
        RuleFor(x => x.StageIds).NotNull();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(Domain.ProjectAccessGrant.ReasonMaxLength);
    }
}

internal sealed class UpdateProjectAccessGrantCommandValidator : AbstractValidator<UpdateProjectAccessGrantCommand>
{
    public UpdateProjectAccessGrantCommandValidator()
    {
        RuleFor(x => x.GrantId).NotEmpty();
        RuleFor(x => x.Level).IsInEnum();
        RuleFor(x => x.Areas).Must(GrantRules.AreValidAreas).WithMessage("Choose at least one known area.");
        RuleFor(x => x.StageIds).NotNull();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(Domain.ProjectAccessGrant.ReasonMaxLength);
    }
}

internal sealed class RevokeProjectAccessGrantCommandValidator : AbstractValidator<RevokeProjectAccessGrantCommand>
{
    public RevokeProjectAccessGrantCommandValidator()
    {
        RuleFor(x => x.GrantId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(Domain.ProjectAccessGrant.ReasonMaxLength);
    }
}

/// <summary>
/// P1-A: the checks every grant route shares, in the repo's order: the project must exist and the caller must reach
/// it (404), then may manage its grants (403), then the request is checked against the project (400/409).
/// </summary>
internal static class GrantRules
{
    private const int AllAreas = (int)ProjectAccessContext.AllAreas;

    public static bool AreValidAreas(ProjectAccessArea areas) => (int)areas is >= 1 and <= AllAreas;

    /// <summary>
    /// Who may manage a project's grants: a holder of <c>project.manage_access</c> (from the token; it is never
    /// implied), or a Manager <b>by membership</b> — never through a grant or <c>project.read_all</c> alone. Returns the
    /// caller's employee id and the highest level they may grant (a holder: Manager; a member: their own level).
    /// </summary>
    public static async Task<Result<Granter>> EnsureMayManageAsync(
        ProjectsDbContext db, ProjectAccessContext access, IProjectAccessGuard guard, ICurrentUser currentUser,
        Guid projectId, string userId, CancellationToken ct)
    {
        if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct)
            || (await guard.EnsureProjectAccessAsync(projectId, userId, ct)).IsFailure)
        {
            return Result.Failure<Granter>(Domain.ProjectErrors.NotFound);
        }

        Guid? employeeId = await access.ResolveEmployeeIdAsync(userId, ct);
        if (employeeId is null)
        {
            return Result.Failure<Granter>(Domain.ProjectAccessGrantErrors.NotAllowed);
        }

        if (currentUser.HasPermission(ProjectsPermissions.Projects.ManageAccess))
        {
            return new Granter(employeeId.Value, ProjectMemberAccess.Manager);
        }

        int? memberLevel = await access.MembershipLevels(employeeId.Value)
            .Where(l => l.ProjectId == projectId)
            .MaxAsync(l => (int?)l.Level, ct);

        return memberLevel == (int)ProjectMemberAccess.Manager
            ? new Granter(employeeId.Value, ProjectMemberAccess.Manager)
            : Result.Failure<Granter>(Domain.ProjectAccessGrantErrors.NotAllowed);
    }

    /// <summary>Every id must name a stage of the project. Duplicates are fine (they are collapsed).</summary>
    public static async Task<bool> StagesBelongAsync(
        ProjectsDbContext db, Guid projectId, IReadOnlyList<Guid> stageIds, CancellationToken ct)
    {
        Guid[] distinct = [.. stageIds.Distinct()];
        if (distinct.Length == 0)
        {
            return true;
        }

        int found = await db.ProjectStages.AsNoTracking()
            .CountAsync(s => s.ProjectId == projectId && distinct.Contains(s.Id), ct);
        return found == distinct.Length;
    }

    /// <summary>A null end is no end; otherwise it must be later than both the start and now.</summary>
    public static bool IsValidWindow(DateTime startsAtUtc, DateTime? expiresAtUtc, DateTime nowUtc) =>
        expiresAtUtc is not { } end || end > startsAtUtc && end > nowUtc;

    /// <summary>Any kind becomes UTC; an unspecified one is taken as UTC (never truncated to a date: a grant may end
    /// at any minute).</summary>
    public static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        { } v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
    };

    public static async Task<Result> SaveAsync(ProjectsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Domain.ProjectAccessGrantErrors.ConcurrentUpdate;
        }
    }

    /// <summary>The grantee hears about it (INotificationService, CLAUDE.md's one synchronous exception, so a dedupe
    /// key per grant and event). No login, no notification.</summary>
    public static async Task NotifyAsync(
        IHrDirectory hrDirectory, INotificationService notifications, ProjectsDbContext db, Domain.ProjectAccessGrant grant,
        NotificationType type, string eventKey, CancellationToken ct)
    {
        EmployeeSummary? grantee = await hrDirectory.GetAsync(grant.EmployeeId, ct);
        if (string.IsNullOrEmpty(grantee?.AppUserId))
        {
            return;
        }

        string projectName = await db.Projects.AsNoTracking()
            .Where(p => p.Id == grant.ProjectId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        (string title, string body) = type == NotificationType.ProjectAccessRevoked
            ? ($"Access removed — {projectName}", $"Your access to project '{projectName}' was revoked.")
            : ($"Access granted — {projectName}", grant.ExpiresAtUtc is { } end
                ? $"You have access to project '{projectName}' until {end:yyyy-MM-dd HH:mm} UTC."
                : $"You have access to project '{projectName}'.");

        await notifications.NotifyAsync(
            grantee.AppUserId, type, title, body, NotificationEntityType.Project, grant.ProjectId,
            dedupeKey: $"access-grant-{grant.Id}-{eventKey}", projectId: grant.ProjectId, ct: ct);
    }

    public static ProjectAccessGrantResponse ToResponse(Domain.ProjectAccessGrant g, string? employeeName, DateTime nowUtc) =>
        new(g.Id, g.ProjectId, g.EmployeeId, employeeName, (ProjectMemberAccess)g.Level, (ProjectAccessArea)g.Areas,
            [.. g.StageIds], g.StartsAtUtc, g.ExpiresAtUtc, g.Reason, g.GrantedById, g.GrantedAtUtc, g.RevokedAtUtc,
            g.RevokedById, g.RevokeReason, (ProjectAccessGrantStatus)g.StatusAt(nowUtc));

    internal sealed record Granter(Guid EmployeeId, ProjectMemberAccess MaxLevel);
}

/// <summary>P1-A: every grant of the project, all states, newest first; two statements plus one batched HR read for
/// the names.</summary>
internal sealed class GetProjectAccessGrantsQueryHandler(
    ProjectsDbContext db, ProjectAccessContext access, IProjectAccessGuard guard, ICurrentUser currentUser,
    IHrDirectory hrDirectory)
    : IQueryHandler<GetProjectAccessGrantsQuery, IReadOnlyList<ProjectAccessGrantResponse>>
{
    public async Task<Result<IReadOnlyList<ProjectAccessGrantResponse>>> HandleAsync(
        GetProjectAccessGrantsQuery query, CancellationToken ct = default)
    {
        Result<GrantRules.Granter> granter = await GrantRules.EnsureMayManageAsync(
            db, access, guard, currentUser, query.ProjectId, query.UserId, ct);
        if (granter.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ProjectAccessGrantResponse>>(granter.Error);
        }

        List<Domain.ProjectAccessGrant> grants = await db.ProjectAccessGrants.AsNoTracking()
            .Where(g => g.ProjectId == query.ProjectId)
            .OrderByDescending(g => g.GrantedAtUtc)
            .ThenBy(g => g.Id)
            .ToListAsync(ct);

        Dictionary<Guid, string> names = grants.Count == 0
            ? []
            : (await hrDirectory.GetProfilesAsync([.. grants.Select(g => g.EmployeeId).Distinct()], ct))
                .ToDictionary(p => p.Id, p => p.FullName);

        DateTime now = access.UtcNow;
        return Result.Success<IReadOnlyList<ProjectAccessGrantResponse>>(
            [.. grants.Select(g => GrantRules.ToResponse(g, names.GetValueOrDefault(g.EmployeeId), now))]);
    }
}

/// <summary>
/// P1-A. After the 404/403 of <see cref="GrantRules.EnsureMayManageAsync"/>: not to oneself (400 SelfGrant), an active
/// employee (404), at most the granter's level (403), at least one area, the project's own stages and a sane window
/// (400), not someone already on the project (409 AlreadyMember), and at most one current or upcoming grant per
/// (project, employee) (409 AlreadyGranted) — that last check runs under the project's write lock, so two racing
/// creates cannot both pass it.
/// </summary>
internal sealed class CreateProjectAccessGrantCommandHandler(
    ProjectsDbContext db, ProjectAccessContext access, IProjectAccessGuard guard, ICurrentUser currentUser,
    IHrDirectory hrDirectory, INotificationService notifications)
    : ICommandHandler<CreateProjectAccessGrantCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateProjectAccessGrantCommand command, CancellationToken ct = default)
    {
        Result<GrantRules.Granter> granter = await GrantRules.EnsureMayManageAsync(
            db, access, guard, currentUser, command.ProjectId, command.UserId, ct);
        if (granter.IsFailure)
        {
            return Result.Failure<Guid>(granter.Error);
        }

        if (command.EmployeeId == granter.Value!.EmployeeId)
        {
            return Result.Failure<Guid>(Domain.ProjectAccessGrantErrors.SelfGrant);
        }

        EmployeeSummary? employee = await hrDirectory.GetAsync(command.EmployeeId, ct);
        if (employee is not { IsActive: true })
        {
            return Result.Failure<Guid>(Domain.ProjectAccessGrantErrors.EmployeeNotFound);
        }

        if (command.Level > granter.Value!.MaxLevel)
        {
            return Result.Failure<Guid>(Domain.ProjectAccessGrantErrors.AboveOwnLevel);
        }

        if (!await GrantRules.StagesBelongAsync(db, command.ProjectId, command.StageIds, ct))
        {
            return Result.Failure<Guid>(Domain.ProjectAccessGrantErrors.StageNotInProject);
        }

        DateTime now = access.UtcNow;
        DateTime startsAt = GrantRules.ToUtc(command.StartsAtUtc) ?? now;
        DateTime? expiresAt = GrantRules.ToUtc(command.ExpiresAtUtc);
        if (!GrantRules.IsValidWindow(startsAt, expiresAt, now))
        {
            return Result.Failure<Guid>(Domain.ProjectAccessGrantErrors.InvalidWindow);
        }

        if (await ProjectAccessLevelReader.Levels(db, command.EmployeeId).AnyAsync(l => l.ProjectId == command.ProjectId, ct))
        {
            return Result.Failure<Guid>(Domain.ProjectAccessGrantErrors.AlreadyMember);
        }

        var grant = Domain.ProjectAccessGrant.Create(
            command.ProjectId, command.EmployeeId, (Domain.ProjectMemberAccess)command.Level,
            (Domain.ProjectAccessArea)command.Areas, command.StageIds, startsAt, expiresAt, command.Reason,
            granter.Value!.EmployeeId, now);

        Result<Guid> created = await ProjectWriteLock.RunAsync(db, command.ProjectId, async () =>
        {
            bool taken = await db.ProjectAccessGrants.AsNoTracking()
                .Where(g => g.ProjectId == command.ProjectId && g.EmployeeId == command.EmployeeId)
                .AnyAsync(Domain.ProjectAccessGrant.CurrentOrUpcomingAt(now), ct);
            if (taken)
            {
                return Result.Failure<Guid>(Domain.ProjectAccessGrantErrors.AlreadyGranted);
            }

            db.ProjectAccessGrants.Add(grant);
            Result saved = await GrantRules.SaveAsync(db, ct);
            return saved.IsFailure ? Result.Failure<Guid>(saved.Error) : grant.Id;
        }, ct);

        if (created.IsSuccess)
        {
            await GrantRules.NotifyAsync(hrDirectory, notifications, db, grant, NotificationType.ProjectAccessGranted, "granted", ct);
        }

        return created;
    }
}

/// <summary>P1-A: changes level, areas, stages, expiry and reason of an Upcoming or Active grant (Revoked or Expired:
/// 409 NotEditable). The same 404 → 403 → 400 checks as create.</summary>
internal sealed class UpdateProjectAccessGrantCommandHandler(
    ProjectsDbContext db, ProjectAccessContext access, IProjectAccessGuard guard, ICurrentUser currentUser)
    : ICommandHandler<UpdateProjectAccessGrantCommand>
{
    public async Task<Result> HandleAsync(UpdateProjectAccessGrantCommand command, CancellationToken ct = default)
    {
        Result<GrantRules.Granter> granter = await GrantRules.EnsureMayManageAsync(
            db, access, guard, currentUser, command.ProjectId, command.UserId, ct);
        if (granter.IsFailure)
        {
            return granter;
        }

        Domain.ProjectAccessGrant? grant = await db.ProjectAccessGrants
            .FirstOrDefaultAsync(g => g.Id == command.GrantId && g.ProjectId == command.ProjectId, ct);
        if (grant is null)
        {
            return Domain.ProjectAccessGrantErrors.NotFound;
        }

        if (command.Level > granter.Value!.MaxLevel)
        {
            return Domain.ProjectAccessGrantErrors.AboveOwnLevel;
        }

        if (!await GrantRules.StagesBelongAsync(db, command.ProjectId, command.StageIds, ct))
        {
            return Domain.ProjectAccessGrantErrors.StageNotInProject;
        }

        DateTime now = access.UtcNow;
        DateTime? expiresAt = GrantRules.ToUtc(command.ExpiresAtUtc);
        Domain.ProjectAccessGrantStatus status = grant.StatusAt(now);
        if (status is Domain.ProjectAccessGrantStatus.Revoked or Domain.ProjectAccessGrantStatus.Expired)
        {
            return Domain.ProjectAccessGrantErrors.NotEditable;
        }

        if (!GrantRules.IsValidWindow(grant.StartsAtUtc, expiresAt, now))
        {
            return Domain.ProjectAccessGrantErrors.InvalidWindow;
        }

        Result updated = grant.Update(
            (Domain.ProjectMemberAccess)command.Level, (Domain.ProjectAccessArea)command.Areas, command.StageIds,
            expiresAt, command.Reason, now);
        return updated.IsFailure ? updated : await GrantRules.SaveAsync(db, ct);
    }
}

/// <summary>P1-A: final, and effective on the grantee's next request (nothing from a grant is in their token).</summary>
internal sealed class RevokeProjectAccessGrantCommandHandler(
    ProjectsDbContext db, ProjectAccessContext access, IProjectAccessGuard guard, ICurrentUser currentUser,
    IHrDirectory hrDirectory, INotificationService notifications)
    : ICommandHandler<RevokeProjectAccessGrantCommand>
{
    public async Task<Result> HandleAsync(RevokeProjectAccessGrantCommand command, CancellationToken ct = default)
    {
        Result<GrantRules.Granter> granter = await GrantRules.EnsureMayManageAsync(
            db, access, guard, currentUser, command.ProjectId, command.UserId, ct);
        if (granter.IsFailure)
        {
            return granter;
        }

        Domain.ProjectAccessGrant? grant = await db.ProjectAccessGrants
            .FirstOrDefaultAsync(g => g.Id == command.GrantId && g.ProjectId == command.ProjectId, ct);
        if (grant is null)
        {
            return Domain.ProjectAccessGrantErrors.NotFound;
        }

        Result revoked = grant.Revoke(granter.Value!.EmployeeId, command.Reason, access.UtcNow);
        if (revoked.IsFailure)
        {
            return revoked;
        }

        Result saved = await GrantRules.SaveAsync(db, ct);
        if (saved.IsSuccess)
        {
            await GrantRules.NotifyAsync(hrDirectory, notifications, db, grant, NotificationType.ProjectAccessRevoked, "revoked", ct);
        }

        return saved;
    }
}

/// <summary>P1-A "Shared with me": the caller's Active and Upcoming grants with their projects' code and name, the
/// soonest-ending first (no end last). One statement.</summary>
internal sealed class GetMyAccessGrantsQueryHandler(ProjectsDbContext db, ProjectAccessContext access)
    : IQueryHandler<GetMyAccessGrantsQuery, IReadOnlyList<MyAccessGrantResponse>>
{
    public async Task<Result<IReadOnlyList<MyAccessGrantResponse>>> HandleAsync(
        GetMyAccessGrantsQuery query, CancellationToken ct = default)
    {
        Guid? employeeId = await access.ResolveEmployeeIdAsync(query.UserId, ct);
        if (employeeId is null)
        {
            return Result.Success<IReadOnlyList<MyAccessGrantResponse>>([]);
        }

        DateTime now = access.UtcNow;
        var rows = await db.ProjectAccessGrants.AsNoTracking()
            .Where(g => g.EmployeeId == employeeId.Value)
            .Where(Domain.ProjectAccessGrant.CurrentOrUpcomingAt(now))
            .Join(db.Projects, g => g.ProjectId, p => p.Id, (g, p) => new { Grant = g, p.Code, p.Name })
            .OrderBy(x => x.Grant.ExpiresAtUtc == null)
            .ThenBy(x => x.Grant.ExpiresAtUtc)
            .ThenBy(x => x.Grant.Id)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<MyAccessGrantResponse>>([.. rows.Select(x => new MyAccessGrantResponse(
            x.Grant.Id, x.Grant.ProjectId, x.Code, x.Name, (ProjectMemberAccess)x.Grant.Level,
            (ProjectAccessArea)x.Grant.Areas, [.. x.Grant.StageIds], x.Grant.StartsAtUtc, x.Grant.ExpiresAtUtc,
            x.Grant.Reason, (ProjectAccessGrantStatus)x.Grant.StatusAt(now)))]);
    }
}
