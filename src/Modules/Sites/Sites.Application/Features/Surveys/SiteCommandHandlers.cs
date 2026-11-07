using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;
using Sites.Contracts;
using Sites.Contracts.Surveys;
using Sites.Domain;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Surveys;

internal sealed class CreateSiteCommandValidator : AbstractValidator<CreateSiteCommand>
{
    public CreateSiteCommandValidator()
    {
        RuleFor(x => x.AppUserId).NotEmpty();
        RuleFor(x => x.CreatedById).NotEmpty();
        RuleFor(x => x.Request.ProjectId).NotEmpty();
        RuleFor(x => x.Request.Data).NotNull().SetValidator(new SiteUnitDataRequestValidator());
    }
}

/// <summary>
/// The one site command that is not <c>ISiteScoped</c> — there is no site yet to scope to, so it
/// authorizes itself.
/// </summary>
internal sealed class CreateSiteCommandHandler(
    SitesDbContext db, IProjectAccessGuard access, IProjectDirectory projects)
    : ICommandHandler<CreateSiteCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateSiteCommand command, CancellationToken ct = default)
    {
        Result scope = await access.EnsureProjectAccessAsync(command.Request.ProjectId, command.AppUserId, ct);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        // Defect B1's fix. The backend's only guard is the access check above -- and a caller holding
        // Projects.ReadAll short-circuits it WITHOUT touching the projects table, so a garbage project id
        // sailed through, Site.Create accepted it (it only rejects Guid.Empty), and the foreign key blew
        // up in SaveChanges as an unexplained 500. SiteErrors.ProjectNotFound existed for exactly this
        // and was referenced nowhere in the backend.
        ProjectSummary? project = await projects.GetProjectAsync(command.Request.ProjectId, ct);
        if (project is null)
        {
            return SiteErrors.ProjectNotFound;
        }

        // B-S-M7: a survey is new work; a deactivated, Completed or Cancelled project takes none.
        if (!project.IsActive || project.Status is ProjectStatus.Completed or ProjectStatus.Cancelled)
        {
            return SiteErrors.ProjectClosed;
        }

        Result<string> code = await NextCodeAsync(db, ct);
        if (code.IsFailure)
        {
            return code.Error;
        }

        Result<Site> site = Site.Create(
            command.Request.ProjectId, code.Value!, command.CreatedById, ToData(command.Request.Data));

        if (site.IsFailure)
        {
            return site.Error;
        }

        db.Sites.Add(site.Value!);

        Result saved = await SiteWrites.SaveAsync(db, ct);
        return saved.IsFailure ? saved.Error : site.Value!.Id;
    }

    /// <summary>
    /// Draws the next code from the sequence. Deliberately outside the EF model — the application reads
    /// it by name, which is why S1-04's schema-object test asserts the sequence exists at all.
    ///
    /// <para><c>nextval</c> is non-transactional, so a rollback burns the value and leaves a gap. That is
    /// correct: this is an identifier, not a count.</para>
    /// </summary>
    private static async Task<Result<string>> NextCodeAsync(SitesDbContext db, CancellationToken ct)
    {
        // The AS "Value" alias is load-bearing, not cosmetic: SqlQuery<T> wraps the text as a subquery
        // and projects a column literally named Value. Without it this fails at runtime with
        // 42703: column s.Value does not exist -- and only ever on an API-created site, since every read
        // path and every seeded row avoids this line entirely.
        long next = await db.Database
            .SqlQuery<long>($"""SELECT nextval('sites.site_code_seq') AS "Value" """)
            .SingleOrDefaultAsync(ct);

        return next <= 0 ? SiteErrors.CodeAllocationFailed : $"SITE-{next:D5}";
    }

    internal static SiteData ToData(SiteUnitDataRequest r) => new(
        r.Address, SiteEnumMapping.ToDomain(r.UnitType), SiteEnumMapping.ToDomain(r.UnitCondition),
        r.AreaSqm, r.FloorNo, r.UnitsPerFloor, SiteEnumMapping.ToDomain(r.BuildingKind),
        SiteEnumMapping.ToDomain(r.Position), SiteEnumMapping.ToDomain(r.ElevatorStatus),
        r.IsLicensed, r.IsRented, r.UtilityMetersNote, r.SurveyedOn, r.Notes);
}

/// <summary>
/// Mirrors <see cref="CreateSiteCommandValidator"/>. The backend's own comment records why: without it,
/// update accepted strings that create rejected and the column refused — a 501-character address came
/// back as a 500 from the write rather than a 400 naming the field.
/// </summary>
internal sealed class UpdateSiteCommandValidator : AbstractValidator<UpdateSiteCommand>
{
    public UpdateSiteCommandValidator()
    {
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.AppUserId).NotEmpty();
        RuleFor(x => x.Request.Data).NotNull().SetValidator(new SiteUnitDataRequestValidator());
    }
}

internal sealed class UpdateSiteCommandHandler(SitesDbContext db) : ICommandHandler<UpdateSiteCommand>
{
    public async Task<Result> HandleAsync(UpdateSiteCommand command, CancellationToken ct = default)
    {
        Site? site = await SiteWrites.LoadForWriteAsync(db, command.SiteId, ct);
        if (site is null)
        {
            return SiteErrors.NotFound;
        }

        Result updated = site.Update(CreateSiteCommandHandler.ToData(command.Request.Data));
        if (updated.IsFailure)
        {
            return updated;
        }

        return await SiteWrites.SaveAsync(db, ct);
    }
}

internal sealed class DeleteSiteCommandHandler(SitesDbContext db) : ICommandHandler<DeleteSiteCommand>
{
    public async Task<Result> HandleAsync(DeleteSiteCommand command, CancellationToken ct = default)
    {
        Site? site = await SiteWrites.LoadForWriteAsync(db, command.SiteId, ct);
        if (site is null)
        {
            return SiteErrors.NotFound;
        }

        // Defect B13's fix. The backend soft-deletes at any status, including Approved -- while
        // Site.Update refuses on Approved. Since the query filter then hides the row from every read AND
        // from SiteScopeDecorator's own resolution, an approved engineering record could be made to
        // vanish with no lifecycle check at all. The asymmetry with Update has no stated reason.
        if (!site.IsEditable)
        {
            return SiteErrors.SiteLocked;
        }

        site.Deactivate();

        return await SiteWrites.SaveAsync(db, ct);
    }
}

internal sealed class SubmitSiteCommandValidator : AbstractValidator<SubmitSiteCommand>
{
    public SubmitSiteCommandValidator()
    {
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.SubmittedById).NotEmpty();
        RuleFor(x => x.AppUserId).NotEmpty();
    }
}

internal sealed class SubmitSiteCommandHandler(SitesDbContext db, TimeProvider timeProvider)
    : ICommandHandler<SubmitSiteCommand>
{
    public async Task<Result> HandleAsync(SubmitSiteCommand command, CancellationToken ct = default)
    {
        Site? site = await SiteWrites.LoadForWriteAsync(db, command.SiteId, ct);
        if (site is null)
        {
            return SiteErrors.NotFound;
        }

        Result submitted = site.Submit(timeProvider.GetUtcNow().UtcDateTime);
        if (submitted.IsFailure)
        {
            return submitted;
        }

        // Enqueued in the SAME SaveChanges as the status change -- the outbox row and the write commit
        // together or not at all.
        db.Outbox().Enqueue(new SiteSubmittedIntegrationEvent(
            Guid.CreateVersion7(), site.Id, site.ProjectId, command.SubmittedById, timeProvider.GetUtcNow()));

        return await SiteWrites.SaveAsync(db, ct);
    }
}

/// <summary>
/// 1000 is the decision-note column's mapped width, so a longer note was a 500 from the write rather
/// than a 400 naming the field.
/// </summary>
internal sealed class DecideSiteCommandValidator : AbstractValidator<DecideSiteCommand>
{
    public DecideSiteCommandValidator()
    {
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.AppUserId).NotEmpty();
        RuleFor(x => x.DecidedById).NotEmpty();
        RuleFor(x => x.Request.Note).MaximumLength(1000);
    }
}

internal sealed class DecideSiteCommandHandler(
    SitesDbContext db, TimeProvider timeProvider, ICurrentUser currentUser)
    : ICommandHandler<DecideSiteCommand>
{
    public async Task<Result> HandleAsync(DecideSiteCommand command, CancellationToken ct = default)
    {
        Site? site = await SiteWrites.LoadForWriteAsync(db, command.SiteId, ct);
        if (site is null)
        {
            return SiteErrors.NotFound;
        }

        // B-S-L1 (C11): the creator may not approve their own survey -- that would make the lifecycle
        // decorative for anyone holding sites.approve. The one override is project.read_all, the same
        // claim IProjectAccessGuard treats as project-wide authority; the full-access admin and manager
        // roles carry it. Rejecting one's own survey is not self-approval and stays allowed.
        if (command.Request.Approved
            && site.Status == Domain.SiteStatus.Submitted
            && command.DecidedById == site.CreatedById
            && !currentUser.HasPermission(ProjectsPermissions.Projects.ReadAll))
        {
            return SiteErrors.SelfApprovalNotAllowed;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        Result decided = site.Decide(
            command.Request.Approved, command.DecidedById, now.UtcDateTime, command.Request.Note);

        if (decided.IsFailure)
        {
            return decided;
        }

        // One event carrying the decision rather than two event types -- every plausible subscriber
        // cares that a survey was decided and branches on the outcome.
        db.Outbox().Enqueue(new SiteApprovedIntegrationEvent(
            Guid.CreateVersion7(), site.Id, site.ProjectId,
            command.Request.Approved, command.DecidedById, now, site.DecisionNote));

        return await SiteWrites.SaveAsync(db, ct);
    }
}

internal sealed class ReopenSiteCommandHandler(SitesDbContext db) : ICommandHandler<ReopenSiteCommand>
{
    public async Task<Result> HandleAsync(ReopenSiteCommand command, CancellationToken ct = default)
    {
        Site? site = await SiteWrites.LoadForWriteAsync(db, command.SiteId, ct);
        if (site is null)
        {
            return SiteErrors.NotFound;
        }

        Result reopened = site.ReopenForRework();
        if (reopened.IsFailure)
        {
            return reopened;
        }

        return await SiteWrites.SaveAsync(db, ct);
    }
}
