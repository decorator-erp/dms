using BuildingBlocks.Messaging;
using Finance.Contracts.BudgetLines;
using Finance.Domain;
using Finance.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Finance.Application.Features.BudgetLines;

internal sealed class CreateBudgetLineCommandValidator : AbstractValidator<CreateBudgetLineCommand>
{
    public CreateBudgetLineCommandValidator()
    {
        RuleFor(x => x.Request.ProjectId).NotEmpty();
        RuleFor(x => x.Request.CostCategoryId).NotEmpty();
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.AllocatedAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.Code).MaximumLength(32);
        RuleFor(x => x.Request.Description).MaximumLength(512);

        RuleFor(x => x.Request.ProjectStageId)
            .NotEmpty()
            .When(x => x.Request.ProjectTaskId.HasValue)
            .WithMessage("ProjectStageId is required when ProjectTaskId is supplied.");
    }
}

/// <summary>
/// <para><b>The task-scope check is a genuine cross-module read and cannot be anything else.</b> "This
/// task belongs to that stage and that project" is a fact about <c>projects</c> tables; the foreign keys
/// added at F1-05 prove each id EXISTS but say nothing about how the three relate to each other. The
/// backend asks its own <c>ProjectTaskRepository.GetScopeAsync</c>; here it is
/// <c>IProjectDirectory.GetTaskAsync</c>, which returns the same two fields.</para>
///
/// <para><b>DIVERGENCE: the project is validated too.</b> The backend checks the task scope but never
/// the project on its own, so a budget line naming an unknown project reaches the database and fails as
/// a foreign-key violation — a 500 where a 404 belongs. This is the same defect Sites recorded as B1 and
/// fixed the same way. When a task IS supplied the scope check already proves the project, so the extra
/// lookup only runs on project- and stage-scoped lines.</para>
/// </summary>
internal sealed class CreateBudgetLineCommandHandler(FinanceDbContext db, IProjectDirectory projects)
    : ICommandHandler<CreateBudgetLineCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateBudgetLineCommand command, CancellationToken ct = default)
    {
        CreateBudgetLineRequest r = command.Request;

        if (!await db.CostCategories.AnyAsync(c => c.Id == r.CostCategoryId && c.IsActive, ct))
        {
            return BudgetLineErrors.CostCategoryNotFound;
        }

        if (r.ProjectTaskId is Guid taskId)
        {
            TaskSummary? task = await projects.GetTaskAsync(taskId, ct);

            if (task is null || task.ProjectId != r.ProjectId || task.ProjectStageId != r.ProjectStageId)
            {
                return BudgetLineErrors.TaskStageMismatch;
            }
        }
        else if (await projects.GetProjectAsync(r.ProjectId, ct) is null)
        {
            return BudgetLineErrors.ProjectNotFound;
        }

        Result<BudgetLine> line = BudgetLine.Create(
            r.Name, r.AllocatedAmount, r.ProjectId, r.CostCategoryId,
            r.ProjectStageId, r.ProjectTaskId, r.Code, r.Description);

        if (line.IsFailure)
        {
            return line.Error;
        }

        db.BudgetLines.Add(line.Value!);
        await db.SaveChangesAsync(ct);

        return line.Value!.Id;
    }
}

internal sealed class UpdateBudgetLineAllocationCommandValidator
    : AbstractValidator<UpdateBudgetLineAllocationCommand>
{
    public UpdateBudgetLineAllocationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.AllocatedAmount).GreaterThanOrEqualTo(0);
    }
}

/// <summary>
/// DIVERGENCE: no explicit transaction. The backend wraps this in a <c>RepeatableRead</c> transaction,
/// which buys nothing here — <c>UpdateAllocation</c> sets an ABSOLUTE value rather than applying a
/// delta, so two concurrent callers produce the same outcome (last writer wins) with or without the
/// isolation level. The transactions that matter in this module are the ones around read-then-derive
/// sequences: BOQ version allocation, and the ledger postings at F1-07.
///
/// <para><see cref="BudgetLine.AllocatedAmount"/> is the ORIGINAL estimate only. It deliberately does
/// not touch <c>AdjustmentAllocation</c>, so a variation already applied to this line survives an
/// allocation correction — which is why <c>EffectiveAllocation</c> is the sum of the two rather than one
/// mutable number.</para>
/// </summary>
internal sealed class UpdateBudgetLineAllocationCommandHandler(FinanceDbContext db)
    : ICommandHandler<UpdateBudgetLineAllocationCommand>
{
    public async Task<Result> HandleAsync(
        UpdateBudgetLineAllocationCommand command, CancellationToken ct = default)
    {
        BudgetLine? line = await db.BudgetLines.FirstOrDefaultAsync(b => b.Id == command.Id && b.IsActive, ct);
        if (line is null)
        {
            return BudgetLineErrors.NotFound;
        }

        Result updated = line.UpdateAllocation(command.AllocatedAmount);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>
/// The repair hatch: re-derives <c>CommittedAmount</c> and <c>ActualAmount</c> from a full re-sum of the
/// ledger instead of trusting the running deltas the postings maintain.
///
/// <para><b>The arithmetic is the posting rules read backwards, and the subtraction is the part that
/// looks wrong until you trace it.</b> A <c>Commitment</c> raises Committed. An <c>ActualCost</c> raises
/// Actual AND consumes the matching commitment, lowering Committed by the same amount. So re-deriving
/// from scratch gives <c>Committed = ΣCommitments − ΣActualCosts</c> and <c>Actual = ΣActualCosts</c>.
/// Dropping the subtraction is the single most plausible way to port this wrong, and it would restate
/// every spent commitment as still outstanding.</para>
///
/// <para><b>Both sums are SIGNED by direction</b> (<c>Out</c> positive, <c>In</c> negative) rather than
/// filtered. That is what makes reversals cancel: a reversal is a mirror row with the opposite direction
/// and the same magnitude, so the pair sums to zero without this query needing to know which rows are
/// reversals or which have been reversed.</para>
///
/// <para><b>An inconsistency in the backend, resolved rather than copied.</b>
/// <c>BudgetLineRepository.SumLedgerAggregatesAsync</c> does not filter <c>IsActive</c> on ledger
/// entries, while <c>ProjectTaskRepository.GetBudgetSnapshotAsync</c> — summing the same table for the
/// same purpose — does. The filter is kept here for consistency with the BOQ path below, and the choice
/// is provably unobservable either way: <see cref="LedgerEntry"/> exposes no deactivation method, so no
/// ledger row in this port can have <c>IsActive == false</c>. Corrections are reversal rows.</para>
/// </summary>
internal sealed class RecalculateBudgetLineCommandHandler(FinanceDbContext db)
    : ICommandHandler<RecalculateBudgetLineCommand>
{
    public async Task<Result> HandleAsync(RecalculateBudgetLineCommand command, CancellationToken ct = default)
    {
        BudgetLine? line = await db.BudgetLines.FirstOrDefaultAsync(b => b.Id == command.Id && b.IsActive, ct);
        if (line is null)
        {
            return BudgetLineErrors.NotFound;
        }

        IQueryable<LedgerEntry> entries = db.LedgerEntries.Where(e => e.BudgetLineId == command.Id && e.IsActive);

        decimal commitments = await entries
            .Where(e => e.Type == LedgerEntryType.Commitment)
            .SumAsync(e => (decimal?)(e.Direction == LedgerDirection.Out ? e.Amount : -e.Amount), ct) ?? 0m;

        decimal actualCosts = await entries
            .Where(e => e.Type == LedgerEntryType.ActualCost)
            .SumAsync(e => (decimal?)(e.Direction == LedgerDirection.Out ? e.Amount : -e.Amount), ct) ?? 0m;

        line.ResetAggregates(committedAmount: commitments - actualCosts, actualAmount: actualCosts);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class GetBudgetLineByIdQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetBudgetLineByIdQuery, BudgetLineResponse>
{
    public async Task<Result<BudgetLineResponse>> HandleAsync(
        GetBudgetLineByIdQuery query, CancellationToken ct = default)
    {
        // EffectiveAllocation and AvailableAmount are projected here rather than read off the entity,
        // because the entity properties are [NotMapped] and the projection must run in SQL. The two
        // expressions repeat BudgetLine's own definitions; BudgetLineTests pins them on the entity side
        // and BudgetLineProjectionTests pins that these agree.
        BudgetLineResponse? response = await db.BudgetLines
            .Where(b => b.Id == query.Id && b.IsActive)
            .Select(b => new BudgetLineResponse(
                b.Id,
                b.Code ?? string.Empty,
                b.ProjectId,
                b.CostCategoryId ?? Guid.Empty,
                b.ProjectStageId,
                b.ProjectTaskId,
                b.Name,
                b.Description,
                b.AllocatedAmount,
                b.AdjustmentAllocation,
                b.AllocatedAmount + b.AdjustmentAllocation,
                b.CommittedAmount,
                b.ActualAmount,
                b.AllocatedAmount + b.AdjustmentAllocation - b.CommittedAmount - b.ActualAmount,
                b.CreatedAtUtc,
                b.UpdatedAtUtc,
                db.CostCategories.Where(c => c.Id == b.CostCategoryId).Select(c => c.Name).FirstOrDefault()))
            .FirstOrDefaultAsync(ct);

        return response is null ? BudgetLineErrors.NotFound : response;
    }
}

internal sealed class GetBudgetLinesByProjectQueryValidator : AbstractValidator<GetBudgetLinesByProjectQuery>
{
    public GetBudgetLinesByProjectQueryValidator() => RuleFor(x => x.ProjectId).NotEmpty();
}

internal sealed class GetBudgetLinesByProjectQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetBudgetLinesByProjectQuery, IReadOnlyList<BudgetLineListItemResponse>>
{
    public async Task<Result<IReadOnlyList<BudgetLineListItemResponse>>> HandleAsync(
        GetBudgetLinesByProjectQuery query, CancellationToken ct = default)
    {
        IQueryable<BudgetLine> q = db.BudgetLines.Where(b => b.ProjectId == query.ProjectId && b.IsActive);

        if (query.CostCategoryId.HasValue)
        {
            q = q.Where(b => b.CostCategoryId == query.CostCategoryId.Value);
        }

        if (query.ProjectStageId.HasValue)
        {
            q = q.Where(b => b.ProjectStageId == query.ProjectStageId.Value);
        }

        if (query.ProjectTaskId.HasValue)
        {
            q = q.Where(b => b.ProjectTaskId == query.ProjectTaskId.Value);
        }

        List<BudgetLineListItemResponse> items = await q
            .OrderBy(b => b.ProjectStageId)
            .ThenBy(b => b.ProjectTaskId)
            .ThenBy(b => b.Name)
            .Select(b => new BudgetLineListItemResponse(
                b.Id,
                b.Code ?? string.Empty,
                b.ProjectId,
                b.CostCategoryId,
                b.ProjectStageId,
                b.ProjectTaskId,
                b.Name,
                b.AllocatedAmount,
                b.AdjustmentAllocation,
                b.AllocatedAmount + b.AdjustmentAllocation,
                b.CommittedAmount,
                b.ActualAmount,
                b.AllocatedAmount + b.AdjustmentAllocation - b.CommittedAmount - b.ActualAmount,
                db.CostCategories.Where(c => c.Id == b.CostCategoryId).Select(c => c.Name).FirstOrDefault()))
            .ToListAsync(ct);

        return items;
    }
}
