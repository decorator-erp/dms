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
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
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
        return new ScopeOfWorkResponse(
            scope.Id, scope.ProjectId, ProjectsEnumMapping.ToContracts(scope.Status), scope.DurationDays,
            scope.PlannedStartDate, scope.EstimatedEnd, scope.Subtotal, scope.AdjustmentAmount, scope.AdjustmentNote,
            scope.Total, scope.Notes, scope.ConfirmedAtUtc, scope.ConfirmedById,
            [.. items.Select(i => ToResponse(i, templateNames))]);
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
            i.Fee, i.CreatedStageId);
}

/// <summary>Never writes: with no scope saved yet, it returns an empty Draft.</summary>
internal sealed class GetScopeOfWorkQueryHandler(ProjectsDbContext db) : IQueryHandler<GetScopeOfWorkQuery, ScopeOfWorkResponse>
{
    public async Task<Result<ScopeOfWorkResponse>> HandleAsync(GetScopeOfWorkQuery query, CancellationToken ct = default)
    {
        ScopeOfWork? scope = await db.ScopesOfWork.AsNoTracking().Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.ProjectId == query.ProjectId, ct);
        if (scope is not null)
        {
            return await ScopeOfWorkStore.ToResponseAsync(db, scope, ct);
        }

        if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == query.ProjectId, ct))
        {
            return Result.Failure<ScopeOfWorkResponse>(Domain.ProjectErrors.NotFound);
        }

        return new ScopeOfWorkResponse(
            null, query.ProjectId, Contracts.ScopeOfWorkStatus.Draft, null, null, null, 0m, 0m, null, 0m, null, null, null, []);
    }
}

internal sealed class UpdateScopeOfWorkHeaderCommandHandler(ProjectsDbContext db) : ICommandHandler<UpdateScopeOfWorkHeaderCommand>
{
    public async Task<Result> HandleAsync(UpdateScopeOfWorkHeaderCommand command, CancellationToken ct = default)
    {
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
        Dictionary<Guid, Stage> stages = await db.Stages.AsNoTracking()
            .Where(s => command.SourceStageIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        if (stages.Count != command.SourceStageIds.Count)
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(ScopeOfWorkErrors.SourceStageNotFound);
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
        if (!await db.StageTemplates.AsNoTracking().AnyAsync(t => t.Id == command.StageTemplateId, ct))
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(Domain.StageTemplateErrors.NotFound);
        }

        Result<ScopeOfWork> scope = await ScopeOfWorkStore.FindOrCreateAsync(db, command.ProjectId, ct);
        if (scope.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ScopeOfWorkItemResponse>>(scope.Error);
        }

        var picked = scope.Value!.Items.Select(i => i.SourceStageId).ToHashSet();
        List<Stage> stages = await db.Stages.AsNoTracking()
            .Where(s => s.StageTemplateId == command.StageTemplateId)
            .OrderBy(s => s.Order)
            .ToListAsync(ct);

        Result<IReadOnlyList<ScopeOfWorkItem>> added = ScopeOfWorkStore.AddItems(
            db, scope.Value!, [.. stages.Where(s => !picked.Contains(s.Id)).Select(AddScopeOfWorkItemsCommandHandler.Pick)]);
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
        if (await ScopeOfWorkStore.FindAsync(db, command.ProjectId, ct) is not { } scope)
        {
            return ScopeOfWorkErrors.InvalidOrder;
        }

        Result reordered = scope.Reorder(command.ItemIds);
        return reordered.IsFailure ? reordered : await ScopeWrites.SaveAsync(db, scope, ct);
    }
}
