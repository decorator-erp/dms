using BuildingBlocks.Messaging;
using Finance.Application.Common;
using Finance.Contracts.Financial;
using Finance.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Finance.Application.Features.Financial;

// The task-scope half: four queries. Every one of them resolves the task through BOTH routes -- named
// directly, or reached through a budget line the task owns -- because a posting against a task's budget
// line is the more common shape and the direct column alone would miss it.

internal sealed class GetTaskFinancialMovementsQueryValidator
    : AbstractValidator<GetTaskFinancialMovementsQuery>
{
    public GetTaskFinancialMovementsQueryValidator()
    {
        RuleFor(x => x.ProjectTaskId).NotEmpty();

        RuleFor(x => x.FromDate!.Value)
            .LessThanOrEqualTo(x => x.ToDate!.Value)
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);
    }
}

internal sealed class GetTaskFinancialMovementsQueryHandler(
    FinanceDbContext db, IProjectDirectory projects)
    : IQueryHandler<GetTaskFinancialMovementsQuery, IReadOnlyList<LedgerMovementResponse>>
{
    public async Task<Result<IReadOnlyList<LedgerMovementResponse>>> HandleAsync(
        GetTaskFinancialMovementsQuery query, CancellationToken ct = default)
    {
        var scoped = new LedgerScopes(db, projects).ForTask(query.ProjectTaskId);

        if (query.FromDate is { } from)
            scoped = scoped.Where(e => e.OccurredOn >= from);

        if (query.ToDate is { } to)
            scoped = scoped.Where(e => e.OccurredOn <= to);

        if (query.Type is { } type)
        {
            var value = FinanceEnumMapping.ToDomain(type);
            scoped = scoped.Where(e => e.Type == value);
        }

        var ordered = scoped
            .OrderByDescending(e => e.OccurredOn)
            .ThenByDescending(e => e.CreatedAtUtc);

        return await LedgerMovementProjection.Project(db, ordered).ToListAsync(ct);
    }
}

internal sealed class GetTaskInvoiceLinesQueryValidator : AbstractValidator<GetTaskInvoiceLinesQuery>
{
    public GetTaskInvoiceLinesQueryValidator() => RuleFor(x => x.ProjectTaskId).NotEmpty();
}

/// <summary>Every invoice line charged to the task, whether the line names the task directly or names a
/// budget line the task owns. Cancelled invoices are included, matching the backend: the question is
/// what was billed against this task, and a cancelled invoice is part of that history.</summary>
internal sealed class GetTaskInvoiceLinesQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetTaskInvoiceLinesQuery, IReadOnlyList<TaskInvoiceLineResponse>>
{
    public async Task<Result<IReadOnlyList<TaskInvoiceLineResponse>>> HandleAsync(
        GetTaskInvoiceLinesQuery query, CancellationToken ct = default)
    {
        var taskId = query.ProjectTaskId;

        var budgetLineIds = db.BudgetLines
            .Where(b => b.ProjectTaskId == taskId && b.IsActive)
            .Select(b => b.Id);

        var items = await db.InvoiceLines
            .AsNoTracking()
            .Where(l => l.IsActive
                        && (l.ProjectTaskId == taskId
                            || (l.BudgetLineId != null && budgetLineIds.Contains(l.BudgetLineId.Value))))
            .Join(
                db.Invoices.Where(i => i.IsActive),
                l => l.InvoiceId,
                i => i.Id,
                (l, i) => new { Line = l, Invoice = i })
            .OrderByDescending(x => x.Invoice.IssuedOn)
            .ThenByDescending(x => x.Invoice.CreatedAtUtc)
            .Select(x => new TaskInvoiceLineResponse(
                x.Invoice.Id,
                x.Invoice.Number,
                FinanceEnumMapping.ToContracts(x.Invoice.Type),
                FinanceEnumMapping.ToContracts(x.Invoice.Status),
                x.Invoice.IssuedOn,
                x.Line.Id,
                x.Line.Description,
                x.Line.Quantity,
                x.Line.Unit,
                x.Line.UnitPrice,
                x.Line.LineTotal,
                x.Line.BudgetLineId))
            .ToListAsync(ct);

        return items;
    }
}

internal sealed class GetTaskVariationsQueryValidator : AbstractValidator<GetTaskVariationsQuery>
{
    public GetTaskVariationsQueryValidator() => RuleFor(x => x.ProjectTaskId).NotEmpty();
}

internal sealed class GetTaskVariationsQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetTaskVariationsQuery, IReadOnlyList<VariationListItemResponse>>
{
    public async Task<Result<IReadOnlyList<VariationListItemResponse>>> HandleAsync(
        GetTaskVariationsQuery query, CancellationToken ct = default)
    {
        var q = db.Variations.AsNoTracking()
            .Where(v => v.IsActive && v.ProjectTaskId == query.ProjectTaskId);

        if (query.Status is { } status)
        {
            var value = FinanceEnumMapping.ToDomain(status);
            q = q.Where(v => v.Status == value);
        }

        var items = await q
            .OrderByDescending(v => v.CreatedAtUtc)
            .Select(FinancialProjections.VariationRow())
            .ToListAsync(ct);

        return items;
    }
}

internal sealed class GetTaskBoqLinesQueryValidator : AbstractValidator<GetTaskBoqLinesQuery>
{
    public GetTaskBoqLinesQueryValidator()
    {
        RuleFor(x => x.ProjectTaskId).NotEmpty();
        RuleFor(x => x.SnapshotVersion!.Value).GreaterThan(0).When(x => x.SnapshotVersion.HasValue);
    }
}

/// <summary>
/// The task's lines from the project's BOQ snapshots.
///
/// <para><b>The default is the LATEST snapshot of each type, not every snapshot.</b> Snapshots are
/// immutable and versioned, so returning all of them would show the same task repeated once per
/// generation — the caller wants the current position under each snapshot type (StageClose,
/// ProjectFinal, Manual), and naming a version overrides that to return exactly that one.</para>
///
/// <para>An unknown task returns an empty list rather than NotFound, matching the backend: this is a
/// drilldown panel, and a task with no BOQ lines and a task that does not exist look the same to it.</para>
/// </summary>
internal sealed class GetTaskBoqLinesQueryHandler(FinanceDbContext db, IProjectDirectory projects)
    : IQueryHandler<GetTaskBoqLinesQuery, IReadOnlyList<TaskBoqLineResponse>>
{
    public async Task<Result<IReadOnlyList<TaskBoqLineResponse>>> HandleAsync(
        GetTaskBoqLinesQuery query, CancellationToken ct = default)
    {
        var task = await projects.GetTaskAsync(query.ProjectTaskId, ct);
        if (task is null)
            return Array.Empty<TaskBoqLineResponse>();

        var snapshotQuery = db.BoqSnapshots.AsNoTracking()
            .Where(s => s.ProjectId == task.ProjectId && s.IsActive);

        if (query.SnapshotVersion is { } version)
            snapshotQuery = snapshotQuery.Where(s => s.Version == version);

        var snapshots = await snapshotQuery
            .Select(s => new { s.Id, s.Version, s.Type, s.GeneratedAt })
            .ToListAsync(ct);

        if (query.SnapshotVersion is null)
        {
            snapshots = [.. snapshots
                .GroupBy(s => s.Type)
                .Select(g => g.OrderByDescending(s => s.Version).First())];
        }

        if (snapshots.Count == 0)
            return Array.Empty<TaskBoqLineResponse>();

        var byId = snapshots.ToDictionary(s => s.Id);
        var ids = byId.Keys.ToArray();

        var lines = await db.BoqSnapshotLines
            .AsNoTracking()
            .Where(l => l.IsActive && l.ProjectTaskId == query.ProjectTaskId && ids.Contains(l.BoqSnapshotId))
            .Select(l => new
            {
                l.BoqSnapshotId,
                l.Id,
                l.Description,
                l.Quantity,
                l.Unit,
                l.BudgetedAmount,
                l.ActualAmount,
                l.DifferenceAmount,
                l.VariationId,
                l.InvoiceLineId,
            })
            .ToListAsync(ct);

        List<TaskBoqLineResponse> result = [.. lines
            .Select(l =>
            {
                var snapshot = byId[l.BoqSnapshotId];

                return new TaskBoqLineResponse(
                    snapshot.Id,
                    snapshot.Version,
                    snapshot.GeneratedAt,
                    FinanceEnumMapping.ToContracts(snapshot.Type),
                    l.Id,
                    l.Description,
                    l.Quantity,
                    l.Unit,
                    l.BudgetedAmount,
                    l.ActualAmount,
                    l.DifferenceAmount,
                    l.VariationId,
                    l.InvoiceLineId);
            })
            .OrderByDescending(l => l.SnapshotVersion)
            .ThenBy(l => l.Description, StringComparer.Ordinal)];

        return result;
    }
}
