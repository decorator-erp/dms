using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Finance.Application.Common;
using Finance.Contracts.Financial;
using Finance.Domain;
using Finance.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Finance.Application.Features.Financial;

// ═══════════════════════════════════════════════════════════════════════════════
// The project-scope half of the financial read slice: seven queries.
//
// These are Finance HANDLERS behind Projects ROUTES. In the backend they live under
// Application/Features/Projects/, which is why an inventory counting only the seven finance noun
// folders found 39 feature files instead of 51 (F1-01's correction). They read finance tables
// exclusively; the only Projects call is for names and scope, through IProjectDirectory.
// ═══════════════════════════════════════════════════════════════════════════════

internal sealed class GetProjectFinancialSummaryQueryValidator
    : AbstractValidator<GetProjectFinancialSummaryQuery>
{
    public GetProjectFinancialSummaryQueryValidator() => RuleFor(x => x.ProjectId).NotEmpty();
}

internal sealed class GetProjectFinancialSummaryQueryHandler(
    FinanceDbContext db, IProjectDirectory projects)
    : IQueryHandler<GetProjectFinancialSummaryQuery, ProjectFinancialSummaryResponse>
{
    public async Task<Result<ProjectFinancialSummaryResponse>> HandleAsync(
        GetProjectFinancialSummaryQuery query, CancellationToken ct = default)
    {
        if (await projects.GetProjectAsync(query.ProjectId, ct) is null)
            return FinanceDashboardErrors.ProjectNotFound;

        var scopes = new LedgerScopes(db, projects);

        var totals = await FinancialBlocks.BuildProjectBlockAsync(db, scopes, query.ProjectId, ct);
        var stages = await FinancialBlocks.BuildStageBudgetRowsAsync(db, projects, query.ProjectId, ct);

        return new ProjectFinancialSummaryResponse(totals, stages);
    }
}

internal sealed class GetProjectFinancialMovementsQueryValidator
    : AbstractValidator<GetProjectFinancialMovementsQuery>
{
    public GetProjectFinancialMovementsQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 200);

        RuleFor(x => x.FromDate!.Value)
            .LessThanOrEqualTo(x => x.ToDate!.Value)
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);
    }
}

/// <summary>The project ledger, filtered eight ways. <c>Count</c> runs on the scoped query and the
/// projection on the page, so the correlated budget-line-name subquery in
/// <see cref="LedgerMovementProjection"/> runs once per returned row rather than once per matching
/// row.</summary>
internal sealed class GetProjectFinancialMovementsQueryHandler(
    FinanceDbContext db, IProjectDirectory projects)
    : IQueryHandler<GetProjectFinancialMovementsQuery, PagedResult<LedgerMovementResponse>>
{
    public async Task<Result<PagedResult<LedgerMovementResponse>>> HandleAsync(
        GetProjectFinancialMovementsQuery query, CancellationToken ct = default)
    {
        var scopes = new LedgerScopes(db, projects);
        var scoped = scopes.ForProject(query.ProjectId);

        if (query.FromDate is { } from)
            scoped = scoped.Where(e => e.OccurredOn >= from);

        if (query.ToDate is { } to)
            scoped = scoped.Where(e => e.OccurredOn <= to);

        if (query.Type is { } type)
        {
            var value = FinanceEnumMapping.ToDomain(type);
            scoped = scoped.Where(e => e.Type == value);
        }

        if (query.Direction is { } direction)
        {
            var value = FinanceEnumMapping.ToDomain(direction);
            scoped = scoped.Where(e => e.Direction == value);
        }

        if (query.ProjectStageId is { } stageId)
            scoped = scoped.Where(e => e.ProjectStageId == stageId);

        if (query.ProjectTaskId is { } taskId)
            scoped = scoped.Where(e => e.ProjectTaskId == taskId);

        if (query.BudgetLineId is { } budgetLineId)
            scoped = scoped.Where(e => e.BudgetLineId == budgetLineId);

        if (query.VariationId is { } variationId)
            scoped = scoped.Where(e => e.VariationId == variationId);

        var total = await scoped.CountAsync(ct);

        var page = scoped
            .OrderByDescending(e => e.OccurredOn)
            .ThenByDescending(e => e.CreatedAtUtc)
            .Skip(query.Skip)
            .Take(query.Take);

        var items = await LedgerMovementProjection.Project(db, page).ToListAsync(ct);

        return new PagedResult<LedgerMovementResponse>(
            items, query.Skip / Math.Max(query.Take, 1), query.Take, total);
    }
}

internal sealed class GetProjectCostCategoryBreakdownQueryValidator
    : AbstractValidator<GetProjectCostCategoryBreakdownQuery>
{
    public GetProjectCostCategoryBreakdownQueryValidator() => RuleFor(x => x.ProjectId).NotEmpty();
}

/// <summary>
/// Spend by cost category. <b>Allocated here is the EFFECTIVE allocation</b> — original plus applied
/// adjustments — unlike the budget-line list, which reports the two separately. Ported as the backend
/// has it; a category view is about where money is going, not about how its ceiling was arrived at.
///
/// <para><b>Committed and Actual roll up only through budget lines</b>, because a cost category is a
/// budget-line dimension: a ledger entry naming no budget line has no category to belong to. That is
/// why these totals can be less than the project's, and it is not a bug.</para>
///
/// <para>Budget lines with no category roll into one "Uncategorized" bucket with a null id, rather than
/// being dropped — dropping them would make the rows silently fail to sum to the project allocation.</para>
/// </summary>
internal sealed class GetProjectCostCategoryBreakdownQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetProjectCostCategoryBreakdownQuery, IReadOnlyList<CostCategoryRow>>
{
    private static readonly Guid Uncategorized = Guid.Empty;

    public async Task<Result<IReadOnlyList<CostCategoryRow>>> HandleAsync(
        GetProjectCostCategoryBreakdownQuery query, CancellationToken ct = default)
    {
        var budgetLines = await db.BudgetLines
            .AsNoTracking()
            .Where(b => b.ProjectId == query.ProjectId && b.IsActive)
            .Select(b => new
            {
                b.Id,
                b.CostCategoryId,
                CategoryName = db.CostCategories
                    .Where(c => c.Id == b.CostCategoryId).Select(c => c.Name).FirstOrDefault(),
                b.AllocatedAmount,
                b.AdjustmentAllocation,
            })
            .ToListAsync(ct);

        if (budgetLines.Count == 0)
            return Array.Empty<CostCategoryRow>();

        static Guid Key(Guid? categoryId) => categoryId ?? Uncategorized;

        var budgetLineCategory = budgetLines.ToDictionary(b => b.Id, b => Key(b.CostCategoryId));

        var categoryNames = budgetLines
            .GroupBy(b => Key(b.CostCategoryId))
            .ToDictionary(g => g.Key, g => g.First().CategoryName);

        var allocatedByCategory = budgetLines
            .GroupBy(b => Key(b.CostCategoryId))
            .ToDictionary(g => g.Key, g => g.Sum(b => b.AllocatedAmount + b.AdjustmentAllocation));

        var ledgerRows = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.ProjectId == query.ProjectId && e.IsActive && e.BudgetLineId != null
                        && (e.Type == LedgerEntryType.Commitment || e.Type == LedgerEntryType.ActualCost))
            .Select(e => new { e.BudgetLineId, e.Type, e.Direction, e.Amount })
            .ToListAsync(ct);

        var committedSigned = new Dictionary<Guid, decimal>();
        var actualSigned = new Dictionary<Guid, decimal>();

        foreach (var row in ledgerRows)
        {
            if (row.BudgetLineId is not Guid lineId
                || !budgetLineCategory.TryGetValue(lineId, out var categoryId))
                continue;

            var signed = LedgerSums.Signed(row.Direction, row.Amount);

            if (row.Type == LedgerEntryType.Commitment)
                committedSigned[categoryId] = committedSigned.GetValueOrDefault(categoryId) + signed;
            else
                actualSigned[categoryId] = actualSigned.GetValueOrDefault(categoryId) + signed;
        }

        List<CostCategoryRow> rows = [.. allocatedByCategory.Keys
            .Select(categoryId =>
            {
                var actual = actualSigned.GetValueOrDefault(categoryId);
                var committed = committedSigned.GetValueOrDefault(categoryId) - actual;

                return new CostCategoryRow(
                    categoryId == Uncategorized ? null : categoryId,
                    categoryNames[categoryId] ?? "Uncategorized",
                    allocatedByCategory[categoryId],
                    committed,
                    actual);
            })
            .OrderBy(r => r.CategoryName, StringComparer.Ordinal)];

        return rows;
    }
}

internal sealed class GetProjectCashflowQueryValidator : AbstractValidator<GetProjectCashflowQuery>
{
    public GetProjectCashflowQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();

        RuleFor(x => x.FromDate!.Value)
            .LessThanOrEqualTo(x => x.ToDate!.Value)
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);
    }
}

/// <summary>
/// Monthly inflow and outflow over a window defaulting to the trailing twelve months.
///
/// <para><b>Unsigned by design, unlike every other aggregate here.</b> Inflow sums <c>In</c> amounts and
/// outflow sums <c>Out</c> amounts as separate positive figures — a cashflow chart wants two bars, not
/// one net number. The consequence is that a reversal appears as a movement in the opposite column
/// rather than cancelling the original, which is the correct depiction: the money really did move
/// twice.</para>
///
/// <para>Months with no movement are absent rather than zero-filled, matching the backend. A caller
/// rendering a continuous axis has to fill the gaps.</para>
/// </summary>
internal sealed class GetProjectCashflowQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetProjectCashflowQuery, IReadOnlyList<MonthlyCashRow>>
{
    public async Task<Result<IReadOnlyList<MonthlyCashRow>>> HandleAsync(
        GetProjectCashflowQuery query, CancellationToken ct = default)
    {
        var toDate = query.ToDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = query.FromDate ?? toDate.AddMonths(-12);

        var rows = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.IsActive && e.ProjectId == query.ProjectId
                        && e.OccurredOn >= fromDate && e.OccurredOn <= toDate)
            .GroupBy(e => new { e.OccurredOn.Year, e.OccurredOn.Month })
            .Select(g => new MonthlyCashRow(
                g.Key.Year,
                g.Key.Month,
                g.Sum(e => e.Direction == LedgerDirection.In ? e.Amount : 0m),
                g.Sum(e => e.Direction == LedgerDirection.Out ? e.Amount : 0m)))
            .ToListAsync(ct);

        List<MonthlyCashRow> ordered = [.. rows.OrderBy(r => r.Year).ThenBy(r => r.Month)];

        return ordered;
    }
}

internal sealed class GetProjectInvoicesQueryValidator : AbstractValidator<GetProjectInvoicesQuery>
{
    public GetProjectInvoicesQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 200);
    }
}

internal sealed class GetProjectInvoicesQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetProjectInvoicesQuery, PagedResult<InvoiceListItemResponse>>
{
    public async Task<Result<PagedResult<InvoiceListItemResponse>>> HandleAsync(
        GetProjectInvoicesQuery query, CancellationToken ct = default)
    {
        var q = db.Invoices.AsNoTracking().Where(i => i.IsActive && i.ProjectId == query.ProjectId);

        if (query.Status is { } status)
        {
            var value = FinanceEnumMapping.ToDomain(status);
            q = q.Where(i => i.Status == value);
        }

        if (query.Type is { } type)
        {
            var value = FinanceEnumMapping.ToDomain(type);
            q = q.Where(i => i.Type == value);
        }

        if (query.ProjectStageId is { } stageId)
            q = q.Where(i => i.ProjectStageId == stageId);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(i => i.IssuedOn)
            .ThenByDescending(i => i.CreatedAtUtc)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(FinancialProjections.InvoiceRow(db))
            .ToListAsync(ct);

        return new PagedResult<InvoiceListItemResponse>(
            items, query.Skip / Math.Max(query.Take, 1), query.Take, total);
    }
}

internal sealed class GetProjectVariationsQueryValidator : AbstractValidator<GetProjectVariationsQuery>
{
    public GetProjectVariationsQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 200);
    }
}

internal sealed class GetProjectVariationsQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetProjectVariationsQuery, PagedResult<VariationListItemResponse>>
{
    public async Task<Result<PagedResult<VariationListItemResponse>>> HandleAsync(
        GetProjectVariationsQuery query, CancellationToken ct = default)
    {
        var q = db.Variations.AsNoTracking().Where(v => v.IsActive && v.ProjectId == query.ProjectId);

        if (query.Status is { } status)
        {
            var value = FinanceEnumMapping.ToDomain(status);
            q = q.Where(v => v.Status == value);
        }

        if (query.Kind is { } kind)
        {
            var value = FinanceEnumMapping.ToDomain(kind);
            q = q.Where(v => v.Kind == value);
        }

        if (query.ProjectStageId is { } stageId)
            q = q.Where(v => v.ProjectStageId == stageId);

        if (query.ProjectTaskId is { } taskId)
            q = q.Where(v => v.ProjectTaskId == taskId);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(v => v.CreatedAtUtc)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(FinancialProjections.VariationRow())
            .ToListAsync(ct);

        return new PagedResult<VariationListItemResponse>(
            items, query.Skip / Math.Max(query.Take, 1), query.Take, total);
    }
}

internal sealed class GetProjectBoqSnapshotsQueryValidator : AbstractValidator<GetProjectBoqSnapshotsQuery>
{
    public GetProjectBoqSnapshotsQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Version!.Value).GreaterThan(0).When(x => x.Version.HasValue);
    }
}

internal sealed class GetProjectBoqSnapshotsQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetProjectBoqSnapshotsQuery, IReadOnlyList<BoqSnapshotSummaryResponse>>
{
    public async Task<Result<IReadOnlyList<BoqSnapshotSummaryResponse>>> HandleAsync(
        GetProjectBoqSnapshotsQuery query, CancellationToken ct = default)
    {
        var q = db.BoqSnapshots.AsNoTracking()
            .Where(s => s.IsActive && s.ProjectId == query.ProjectId);

        if (query.Type is { } type)
        {
            var value = FinanceEnumMapping.ToDomain(type);
            q = q.Where(s => s.Type == value);
        }

        if (query.Version is { } version)
            q = q.Where(s => s.Version == version);

        var items = await q
            .OrderByDescending(s => s.Version)
            .ThenByDescending(s => s.GeneratedAt)
            .Select(FinancialProjections.BoqSnapshotRow(db))
            .ToListAsync(ct);

        return items;
    }
}
