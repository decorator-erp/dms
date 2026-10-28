using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts.Scope;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Scopes;

internal sealed class GetScopeOfWorkQueryValidator : AbstractValidator<GetScopeOfWorkQuery>
{
    public GetScopeOfWorkQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}

internal sealed class UpdateScopeOfWorkHeaderCommandValidator : AbstractValidator<UpdateScopeOfWorkHeaderCommand>
{
    public UpdateScopeOfWorkHeaderCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.DurationDays).InclusiveBetween(1, 3650).When(x => x.DurationDays.HasValue);
        // Bounded so PlannedStartDate + DurationDays (up to 3650) always stays a valid date.
        RuleFor(x => x.PlannedStartDate)
            .InclusiveBetween(new DateOnly(2000, 1, 1), new DateOnly(2100, 12, 31))
            .When(x => x.PlannedStartDate.HasValue);
        RuleFor(x => x.AdjustmentAmount).InclusiveBetween(-1_000_000_000_000m, 1_000_000_000_000m);
        RuleFor(x => x.AdjustmentNote).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

internal sealed class AddScopeOfWorkItemsCommandValidator : AbstractValidator<AddScopeOfWorkItemsCommand>
{
    public AddScopeOfWorkItemsCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.SourceStageIds).NotEmpty()
            .Must(ids => ids.Count <= ScopeOfWork.MaxItems).WithMessage($"At most {ScopeOfWork.MaxItems} stages at once.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Each stage may be named once.");
        RuleForEach(x => x.SourceStageIds).NotEmpty();
    }
}

internal sealed class AddScopeOfWorkTemplateCommandValidator : AbstractValidator<AddScopeOfWorkTemplateCommand>
{
    public AddScopeOfWorkTemplateCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.StageTemplateId).NotEmpty();
    }
}

internal sealed class UpdateScopeOfWorkItemCommandValidator : AbstractValidator<UpdateScopeOfWorkItemCommand>
{
    public UpdateScopeOfWorkItemCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        // ProjectStage.Name's length: confirm copies this name into the stage it creates.
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Fee).InclusiveBetween(0m, 1_000_000_000_000m);
    }
}

internal sealed class RemoveScopeOfWorkItemCommandValidator : AbstractValidator<RemoveScopeOfWorkItemCommand>
{
    public RemoveScopeOfWorkItemCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
    }
}

internal sealed class ReorderScopeOfWorkItemsCommandValidator : AbstractValidator<ReorderScopeOfWorkItemsCommand>
{
    public ReorderScopeOfWorkItemsCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ItemIds).NotEmpty()
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Each item may be named once.");
        RuleForEach(x => x.ItemIds).NotEmpty();
    }
}

/// <summary>Loading and mapping shared by every scope-of-work handler.</summary>
internal static class ScopeOfWorkStore
{
    /// <summary>D-1, D-2: a scope of work is kept only for an active, stage-workflow project. An inactive or
    /// missing project is <see cref="Domain.ProjectErrors.NotFound"/>, as the confirm already treated it; a
    /// FreeStyle one is 409 <see cref="ScopeOfWorkErrors.WorkflowNotSupported"/>. Every write and the confirm
    /// check this first.</summary>
    public static async Task<Result> EnsureEditableProjectAsync(ProjectsDbContext db, Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.IsActive, p.WorkflowType })
            .FirstOrDefaultAsync(ct);
        if (project is null || !project.IsActive)
        {
            return Domain.ProjectErrors.NotFound;
        }

        return project.WorkflowType == WorkflowType.FreeStyle ? ScopeOfWorkErrors.WorkflowNotSupported : Result.Success();
    }

    public static Task<ScopeOfWork?> FindAsync(ProjectsDbContext db, Guid projectId, CancellationToken ct) =>
        db.ScopesOfWork.Include(s => s.Items).FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);

    /// <summary>The project's scope, or a new Draft staged for insert on the first write. A missing project is
    /// <see cref="Domain.ProjectErrors.NotFound"/>: the scope decorator lets a project-wide reader through
    /// without checking the row exists, and the insert would otherwise hit the foreign key (a 500).</summary>
    public static async Task<Result<ScopeOfWork>> FindOrCreateAsync(ProjectsDbContext db, Guid projectId, CancellationToken ct)
    {
        if (await FindAsync(db, projectId, ct) is { } existing)
        {
            return existing;
        }

        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            return Result.Failure<ScopeOfWork>(Domain.ProjectErrors.NotFound);
        }

        var scope = ScopeOfWork.Create(projectId);
        db.ScopesOfWork.Add(scope);
        return scope;
    }

    public static async Task<ScopeOfWorkResponse> ToResponseAsync(
        ProjectsDbContext db, ScopeOfWork scope, CancellationToken ct)
    {
        IReadOnlyList<ScopeOfWorkItem> items = scope.Items;
        Dictionary<Guid, string> templateNames = await TemplateNamesAsync(db, [.. items.Select(i => i.SourceStageId)], ct);
        Error? blocker = await ConfirmBlockerAsync(db, scope.ProjectId, scope, templateNames, ct);
        return new ScopeOfWorkResponse(
            scope.Id, scope.ProjectId, ProjectsEnumMapping.ToContracts(scope.Status), scope.DurationDays,
            scope.PlannedStartDate, scope.EstimatedEnd, scope.Subtotal, scope.AdjustmentAmount, scope.AdjustmentNote,
            scope.Total, scope.Notes, scope.ConfirmedAtUtc, scope.ConfirmedById,
            [.. items.Select(i => ToResponse(i, templateNames))], blocker is null, blocker?.Code);
    }

    /// <summary>D-1: the first rule <c>ConfirmScopeOfWorkCommandHandler</c> would fail on right now, in its own
    /// order — the workflow, the scope's own rules (<see cref="ScopeOfWork.EnsureConfirmable"/>), existing
    /// project stages, then a deleted source stage — or null when the confirm would go through. A missing
    /// scope reads as <see cref="ScopeOfWorkErrors.Empty"/>.</summary>
    public static async Task<Error?> ConfirmBlockerAsync(
        ProjectsDbContext db, Guid projectId, ScopeOfWork? scope, Dictionary<Guid, string> templateNames, CancellationToken ct)
    {
        Result editable = await EnsureEditableProjectAsync(db, projectId, ct);
        if (editable.IsFailure)
        {
            return editable.Error;
        }

        if (scope is null)
        {
            return ScopeOfWorkErrors.Empty;
        }

        Result confirmable = scope.EnsureConfirmable();
        if (confirmable.IsFailure)
        {
            return confirmable.Error;
        }

        if (await db.ProjectStages.AsNoTracking().AnyAsync(s => s.ProjectId == projectId, ct))
        {
            return ScopeOfWorkErrors.ProjectHasStages;
        }

        return scope.Items.Any(i => !templateNames.ContainsKey(i.SourceStageId)) ? ScopeOfWorkErrors.SourceStageNotFound : null;
    }

    public static async Task<IReadOnlyList<ScopeOfWorkItemResponse>> ToResponsesAsync(
        ProjectsDbContext db, IReadOnlyList<ScopeOfWorkItem> items, CancellationToken ct)
    {
        Dictionary<Guid, string> templateNames = await TemplateNamesAsync(db, [.. items.Select(i => i.SourceStageId)], ct);
        return [.. items.Select(i => ToResponse(i, templateNames))];
    }

    /// <summary>Adds <paramref name="picks"/> to the scope and stages the new rows as inserts.</summary>
    public static Result<IReadOnlyList<ScopeOfWorkItem>> AddItems(
        ProjectsDbContext db, ScopeOfWork scope, IReadOnlyList<ScopeItemPick> picks)
    {
        Result<IReadOnlyList<ScopeOfWorkItem>> added = scope.AddItems(picks);
        if (added.IsSuccess)
        {
            // Explicit: the ids are client-generated, so say these are new rather than rely on discovery.
            db.ScopeOfWorkItems.AddRange(added.Value!);
        }

        return added;
    }

    /// <summary>Template stage id → its template's name, in one query.</summary>
    private static async Task<Dictionary<Guid, string>> TemplateNamesAsync(
        ProjectsDbContext db, List<Guid> stageIds, CancellationToken ct) =>
        stageIds.Count == 0
            ? []
            : await (
                    from s in db.Stages.AsNoTracking()
                    join t in db.StageTemplates.AsNoTracking() on s.StageTemplateId equals t.Id
                    where stageIds.Contains(s.Id)
                    select new { s.Id, t.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    private static ScopeOfWorkItemResponse ToResponse(ScopeOfWorkItem i, Dictionary<Guid, string> templateNames) =>
        new(i.Id, i.SourceStageId, templateNames.GetValueOrDefault(i.SourceStageId), i.Name, i.Description, i.Order,
            i.Fee, i.CreatedStageId, SourceMissing: !templateNames.ContainsKey(i.SourceStageId));
}

/// <summary>Never writes: with no scope saved yet, it returns an empty Draft.</summary>
internal sealed class GetScopeOfWorkQueryHandler(ProjectsDbContext db) : IQueryHandler<GetScopeOfWorkQuery, ScopeOfWorkResponse>
{
    public async Task<Result<ScopeOfWorkResponse>> HandleAsync(GetScopeOfWorkQuery query, CancellationToken ct = default)
    {
        // D-2: an inactive project is not found, like on the confirm. A FreeStyle one stays readable; its
        // BlockingReason says why it can't be confirmed (D-1).
        if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == query.ProjectId && p.IsActive, ct))
        {
            return Result.Failure<ScopeOfWorkResponse>(Domain.ProjectErrors.NotFound);
        }

        ScopeOfWork? scope = await db.ScopesOfWork.AsNoTracking().Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.ProjectId == query.ProjectId, ct);
        if (scope is not null)
        {
            return await ScopeOfWorkStore.ToResponseAsync(db, scope, ct);
        }

        Error? blocker = await ScopeOfWorkStore.ConfirmBlockerAsync(db, query.ProjectId, null, [], ct);
        return new ScopeOfWorkResponse(
            null, query.ProjectId, Contracts.ScopeOfWorkStatus.Draft, null, null, null, 0m, 0m, null, 0m, null, null, null, [],
            CanConfirm: false, BlockingReason: blocker?.Code);
    }
}

internal sealed class UpdateScopeOfWorkHeaderCommandHandler(ProjectsDbContext db) : ICommandHandler<UpdateScopeOfWorkHeaderCommand>
{
    public async Task<Result> HandleAsync(UpdateScopeOfWorkHeaderCommand command, CancellationToken ct = default)
    {
        Result editable = await ScopeOfWorkStore.EnsureEditableProjectAsync(db, command.ProjectId, ct);
        if (editable.IsFailure)
        {
            return editable;
        }

        Result<ScopeOfWork> scope = await ScopeOfWorkStore.FindOrCreateAsync(db, command.ProjectId, ct);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        Result updated = scope.Value!.UpdateHeader(
            command.DurationDays, command.PlannedStartDate, command.AdjustmentAmount, command.AdjustmentNote, command.Notes);
        return updated.IsFailure ? updated : await ScopeWrites.SaveAsync(db, scope.Value!, ct);
    }
}

internal sealed class AddScopeOfWorkItemsCommandHandler(ProjectsDbContext db)
    : ICommandHandler<AddScopeOfWorkItemsCommand, IReadOnlyList<ScopeOfWorkItemResponse>>
{
    public async Task<Result<IReadOnlyList<ScopeOfWorkItemResponse>>> HandleAsync(
        AddScopeOfWorkItemsCommand command, CancellationToken ct = default)
    {
        Result editable = await ScopeOfWorkStore.EnsureEditableProjectAsync(db, command.ProjectId, ct);
        if (editable.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(editable.Error);
        }

        Dictionary<Guid, Stage> stages = await db.Stages.AsNoTracking()
            .Where(s => command.SourceStageIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        if (stages.Count != command.SourceStageIds.Count)
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(
                ScopeOfWorkErrors.SourceStagesNotFound(command.SourceStageIds.Where(id => !stages.ContainsKey(id))));
        }

        Result<ScopeOfWork> scope = await ScopeOfWorkStore.FindOrCreateAsync(db, command.ProjectId, ct);
        if (scope.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(scope.Error);
        }

        Result<IReadOnlyList<ScopeOfWorkItem>> added = ScopeOfWorkStore.AddItems(
            db, scope.Value!, [.. command.SourceStageIds.Select(id => Pick(stages[id]))]);
        if (added.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(added.Error);
        }

        Result saved = await ScopeWrites.SaveAsync(db, scope.Value!, ct);
        return saved.IsFailure
            ? Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(saved.Error)
            : Result.Success(await ScopeOfWorkStore.ToResponsesAsync(db, added.Value!, ct));
    }

    internal static ScopeItemPick Pick(Stage stage) => new(stage.Id, stage.Name, stage.Description, stage.DefaultFee ?? 0m);
}

internal sealed class AddScopeOfWorkTemplateCommandHandler(ProjectsDbContext db)
    : ICommandHandler<AddScopeOfWorkTemplateCommand, IReadOnlyList<ScopeOfWorkItemResponse>>
{
    public async Task<Result<IReadOnlyList<ScopeOfWorkItemResponse>>> HandleAsync(
        AddScopeOfWorkTemplateCommand command, CancellationToken ct = default)
    {
        Result editable = await ScopeOfWorkStore.EnsureEditableProjectAsync(db, command.ProjectId, ct);
        if (editable.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(editable.Error);
        }

        if (!await db.StageTemplates.AsNoTracking().AnyAsync(t => t.Id == command.StageTemplateId, ct))
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(Domain.StageTemplateErrors.NotFound);
        }

        ScopeOfWork? existing = await ScopeOfWorkStore.FindAsync(db, command.ProjectId, ct);
        HashSet<Guid> picked = existing?.Items.Select(i => i.SourceStageId).ToHashSet() ?? [];
        List<ScopeItemPick> picks = await db.Stages.AsNoTracking()
            .Where(s => s.StageTemplateId == command.StageTemplateId)
            .OrderBy(s => s.Order)
            .Select(s => new ScopeItemPick(s.Id, s.Name, s.Description, s.DefaultFee ?? 0m))
            .ToListAsync(ct);
        picks = [.. picks.Where(p => !picked.Contains(p.SourceStageId))];

        // Nothing to add is a no-op: no Draft is created and the scope isn't saved, so its xmin doesn't move and
        // nobody else's open edit becomes a false ConcurrentUpdate. A confirmed scope still says so.
        if (picks.Count == 0)
        {
            if (existing?.Status == ScopeOfWorkStatus.Confirmed)
            {
                return Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(ScopeOfWorkErrors.Confirmed);
            }

            if (existing is null && !await db.Projects.AsNoTracking().AnyAsync(p => p.Id == command.ProjectId, ct))
            {
                return Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(Domain.ProjectErrors.NotFound);
            }

            return Result.Success<IReadOnlyList<ScopeOfWorkItemResponse>>([]);
        }

        Result<ScopeOfWork> scope = existing ?? await ScopeOfWorkStore.FindOrCreateAsync(db, command.ProjectId, ct);
        if (scope.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(scope.Error);
        }

        Result<IReadOnlyList<ScopeOfWorkItem>> added = ScopeOfWorkStore.AddItems(db, scope.Value!, picks);
        if (added.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(added.Error);
        }

        Result saved = await ScopeWrites.SaveAsync(db, scope.Value!, ct);
        return saved.IsFailure
            ? Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(saved.Error)
            : Result.Success(await ScopeOfWorkStore.ToResponsesAsync(db, added.Value!, ct));
    }
}

internal sealed class UpdateScopeOfWorkItemCommandHandler(ProjectsDbContext db) : ICommandHandler<UpdateScopeOfWorkItemCommand>
{
    public async Task<Result> HandleAsync(UpdateScopeOfWorkItemCommand command, CancellationToken ct = default)
    {
        Result editable = await ScopeOfWorkStore.EnsureEditableProjectAsync(db, command.ProjectId, ct);
        if (editable.IsFailure)
        {
            return editable;
        }

        if (await ScopeOfWorkStore.FindAsync(db, command.ProjectId, ct) is not { } scope)
        {
            return ScopeOfWorkErrors.ItemNotFound;
        }

        Result updated = scope.UpdateItem(command.ItemId, command.Name.Trim(), command.Description, command.Fee);
        return updated.IsFailure ? updated : await ScopeWrites.SaveAsync(db, scope, ct);
    }
}

internal sealed class RemoveScopeOfWorkItemCommandHandler(ProjectsDbContext db) : ICommandHandler<RemoveScopeOfWorkItemCommand>
{
    public async Task<Result> HandleAsync(RemoveScopeOfWorkItemCommand command, CancellationToken ct = default)
    {
        Result editable = await ScopeOfWorkStore.EnsureEditableProjectAsync(db, command.ProjectId, ct);
        if (editable.IsFailure)
        {
            return editable;
        }

        if (await ScopeOfWorkStore.FindAsync(db, command.ProjectId, ct) is not { } scope)
        {
            return ScopeOfWorkErrors.ItemNotFound;
        }

        Result removed = scope.RemoveItem(command.ItemId);
        return removed.IsFailure ? removed : await ScopeWrites.SaveAsync(db, scope, ct);
    }
}

internal sealed class ReorderScopeOfWorkItemsCommandHandler(ProjectsDbContext db) : ICommandHandler<ReorderScopeOfWorkItemsCommand>
{
    public async Task<Result> HandleAsync(ReorderScopeOfWorkItemsCommand command, CancellationToken ct = default)
    {
        Result editable = await ScopeOfWorkStore.EnsureEditableProjectAsync(db, command.ProjectId, ct);
        if (editable.IsFailure)
        {
            return editable;
        }

        if (await ScopeOfWorkStore.FindAsync(db, command.ProjectId, ct) is not { } scope)
        {
            return ScopeOfWorkErrors.InvalidOrder;
        }

        Result reordered = scope.Reorder(command.ItemIds);
        return reordered.IsFailure ? reordered : await ScopeWrites.SaveAsync(db, scope, ct);
    }
}
