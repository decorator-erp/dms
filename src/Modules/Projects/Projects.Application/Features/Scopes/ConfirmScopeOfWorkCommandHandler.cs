using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Projects.Application.Features.Projects;
using Projects.Contracts;
using Projects.Contracts.Scope;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Scopes;

internal sealed class ConfirmScopeOfWorkCommandValidator : AbstractValidator<ConfirmScopeOfWorkCommand>
{
    public ConfirmScopeOfWorkCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}

/// <summary>
/// Confirms the project's scope of work: creates only the picked stages, in item order, through the same
/// <see cref="StageInstantiator"/> a template instantiation uses (tasks, subtasks and teams copied from each
/// source stage, <c>Cost</c> = the item's fee, <c>StageId</c> = the source), records each item's created
/// stage, sets the project's estimated dates from the planned start, confirms the scope and enqueues
/// <see cref="ScopeOfWorkConfirmedIntegrationEvent"/>, all in one transaction.
///
/// <para><b>Serialization.</b> The scope row is locked (<c>FOR UPDATE</c>) before any rule is checked and
/// held until the transaction ends, so a second confirm waits and then reads Confirmed (409). The project
/// row is not locked: <c>StageInstantiator</c> is the only writer of <c>ProjectStage</c> rows, and every one
/// of its callers either creates the project in the same transaction or holds this lock. The transaction is
/// <c>DomainEventDispatchDecorator</c>'s; one is opened here only for an undecorated caller, as in CRM's
/// intake save, since a lock outside a transaction is released at once.</para>
///
/// <para><b>FreeStyle projects</b> (D-1, SoW F1) have no scope of work: every write and this confirm are 409
/// <c>projects.scope.WorkflowNotSupported</c>, checked first through
/// <c>ScopeOfWorkStore.EnsureEditableProjectAsync</c>.</para>
/// </summary>
internal sealed class ConfirmScopeOfWorkCommandHandler(
    ProjectsDbContext db, IHrDirectory hrDirectory, ICurrentUser currentUser)
    : ICommandHandler<ConfirmScopeOfWorkCommand, ConfirmScopeOfWorkResponse>
{
    public async Task<Result<ConfirmScopeOfWorkResponse>> HandleAsync(ConfirmScopeOfWorkCommand command, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await ConfirmAsync(command, ct);
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        Result<ConfirmScopeOfWorkResponse> result = await ConfirmAsync(command, ct);
        if (result.IsSuccess)
        {
            await transaction.CommitAsync(ct);
        }
        else
        {
            await transaction.RollbackAsync(ct);
        }

        return result;
    }

    private async Task<Result<ConfirmScopeOfWorkResponse>> ConfirmAsync(ConfirmScopeOfWorkCommand command, CancellationToken ct)
    {
        // D-1, D-2: an inactive project is not found and a FreeStyle one has no stage workflow to confirm into.
        Result editable = await ScopeOfWorkStore.EnsureEditableProjectAsync(db, command.ProjectId, ct);
        if (editable.IsFailure)
        {
            return Fail(editable.Error);
        }

        // xmin is selected explicitly: SELECT * omits system columns, and ScopeOfWork maps xmin as its token.
        ScopeOfWork? scope = await db.ScopesOfWork
            .FromSql($"SELECT *, xmin FROM projects.scopes_of_work WHERE \"ProjectId\" = {command.ProjectId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (scope is null)
        {
            return Fail(ScopeOfWorkErrors.Empty);
        }

        await db.Entry(scope).Collection(s => s.Items).LoadAsync(ct);

        Result confirmable = scope.EnsureConfirmable();
        if (confirmable.IsFailure)
        {
            return Fail(confirmable.Error);
        }

        Domain.Project? project = await db.Projects.FirstOrDefaultAsync(p => p.Id == command.ProjectId, ct);
        if (project is null || !project.IsActive)
        {
            return Fail(Domain.ProjectErrors.NotFound);
        }

        if (await db.ProjectStages.AnyAsync(s => s.ProjectId == project.Id, ct))
        {
            return Fail(ScopeOfWorkErrors.ProjectHasStages);
        }

        IReadOnlyList<ScopeOfWorkItem> items = scope.Items;
        var sourceIds = items.Select(i => i.SourceStageId).ToList();
        List<Guid> existingSources = await db.Stages.AsNoTracking().Where(s => sourceIds.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct);
        if (existingSources.Count != sourceIds.Count)
        {
            // D-3: name the items whose source is gone, so the caller knows which to remove.
            return Fail(ScopeOfWorkErrors.SourceStagesNotFound(items.Where(i => !existingSources.Contains(i.SourceStageId)).Select(i => i.Id)));
        }

        Guid? confirmedById = currentUser.UserId is null ? null : await hrDirectory.ResolveEmployeeIdAsync(currentUser.UserId, ct);
        if (confirmedById is null)
        {
            return Fail(Domain.ProjectErrors.AccessDenied);
        }

        Result<IReadOnlyList<Guid>> created = await new StageInstantiator(db, hrDirectory).InstantiateAsync(
            project, [.. items.Select(i => new StagePick(i.SourceStageId, i.Name, i.Description, i.Order, i.Fee))], ct);
        if (created.IsFailure)
        {
            return Fail(created.Error);
        }

        for (int i = 0; i < items.Count; i++)
        {
            Result recorded = scope.SetCreatedStage(items[i].Id, created.Value![i]);
            if (recorded.IsFailure)
            {
                return Fail(recorded.Error);
            }
        }

        if (scope.PlannedStartDate is { } plannedStart)
        {
            var start = plannedStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            project.SetEstimatedDates(start, start.AddDays(scope.DurationDays!.Value));
        }

        Result confirmed = scope.Confirm(confirmedById.Value, DateTime.UtcNow);
        if (confirmed.IsFailure)
        {
            return Fail(confirmed.Error);
        }

        db.Outbox().Enqueue(new ScopeOfWorkConfirmedIntegrationEvent(
            Guid.NewGuid(), scope.Id, scope.ProjectId, scope.Total, scope.DurationDays!.Value, DateTimeOffset.UtcNow));

        Result saved = await ScopeWrites.SaveAsync(db, scope, ct);
        return saved.IsFailure ? Fail(saved.Error) : new ConfirmScopeOfWorkResponse(created.Value!);
    }

    private static Result<ConfirmScopeOfWorkResponse> Fail(Error error) => Result.Failure<ConfirmScopeOfWorkResponse>(error);
}
