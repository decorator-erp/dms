using BuildingBlocks.Messaging;
using Finance.Application.Common;
using Finance.Contracts.Financial;
using Finance.Domain;
using Finance.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Finance.Application.Features.Financial;

// The stage-scope half: four queries, all unpaged in the backend and unpaged here — a stage's
// drilldowns are bounded by the stage.

internal sealed class GetStageFinancialMovementsQueryValidator
    : AbstractValidator<GetStageFinancialMovementsQuery>
{
    public GetStageFinancialMovementsQueryValidator()
    {
        RuleFor(x => x.ProjectStageId).NotEmpty();

        RuleFor(x => x.FromDate!.Value)
            .LessThanOrEqualTo(x => x.ToDate!.Value)
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);
    }
}

/// <summary>
/// The stage ledger — the one query in this slice whose SCOPE, not just its filter, needs Projects. See
/// <see cref="LedgerScopes.ForStageAsync"/> for why the resolution is exact rather than approximate.
///
/// <para>An unknown stage returns <c>NotFound</c> rather than an empty list, which is what
/// <c>ForStageAsync</c> returning null carries: "no such stage" and "a stage with no movements" are
/// different answers and a caller can act on the difference.</para>
/// </summary>
internal sealed class GetStageFinancialMovementsQueryHandler(
    FinanceDbContext db, IProjectDirectory projects)
    : IQueryHandler<GetStageFinancialMovementsQuery, IReadOnlyList<LedgerMovementResponse>>
{
    public async Task<Result<IReadOnlyList<LedgerMovementResponse>>> HandleAsync(
        GetStageFinancialMovementsQuery query, CancellationToken ct = default)
    {
        var scoped = await new LedgerScopes(db, projects).ForStageAsync(query.ProjectStageId, ct);
        if (scoped is null)
            return FinancialScopeErrors.StageNotFound;

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

internal sealed class GetStageInvoicesQueryValidator : AbstractValidator<GetStageInvoicesQuery>
{
    public GetStageInvoicesQueryValidator() => RuleFor(x => x.ProjectStageId).NotEmpty();
}

/// <summary>Invoices are scoped by their own <c>ProjectStageId</c> only — an invoice belongs to at most
/// one stage directly, and unlike a ledger entry there is no budget-line route into it.</summary>
internal sealed class GetStageInvoicesQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetStageInvoicesQuery, IReadOnlyList<InvoiceListItemResponse>>
{
    public async Task<Result<IReadOnlyList<InvoiceListItemResponse>>> HandleAsync(
        GetStageInvoicesQuery query, CancellationToken ct = default)
    {
        var q = db.Invoices.AsNoTracking()
            .Where(i => i.IsActive && i.ProjectStageId == query.ProjectStageId);

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

        var items = await q
            .OrderByDescending(i => i.IssuedOn)
            .ThenByDescending(i => i.CreatedAtUtc)
            .Select(FinancialProjections.InvoiceRow(db))
            .ToListAsync(ct);

        return items;
    }
}

internal sealed class GetStageVariationsQueryValidator : AbstractValidator<GetStageVariationsQuery>
{
    public GetStageVariationsQueryValidator() => RuleFor(x => x.ProjectStageId).NotEmpty();
}

internal sealed class GetStageVariationsQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetStageVariationsQuery, IReadOnlyList<VariationListItemResponse>>
{
    public async Task<Result<IReadOnlyList<VariationListItemResponse>>> HandleAsync(
        GetStageVariationsQuery query, CancellationToken ct = default)
    {
        var q = db.Variations.AsNoTracking()
            .Where(v => v.IsActive && v.ProjectStageId == query.ProjectStageId);

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

internal sealed class GetStageBoqSnapshotsQueryValidator : AbstractValidator<GetStageBoqSnapshotsQuery>
{
    public GetStageBoqSnapshotsQueryValidator() => RuleFor(x => x.ProjectStageId).NotEmpty();
}

internal sealed class GetStageBoqSnapshotsQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetStageBoqSnapshotsQuery, IReadOnlyList<BoqSnapshotSummaryResponse>>
{
    public async Task<Result<IReadOnlyList<BoqSnapshotSummaryResponse>>> HandleAsync(
        GetStageBoqSnapshotsQuery query, CancellationToken ct = default)
    {
        var q = db.BoqSnapshots.AsNoTracking()
            .Where(s => s.IsActive && s.ProjectStageId == query.ProjectStageId);

        if (query.Type is { } type)
        {
            var value = FinanceEnumMapping.ToDomain(type);
            q = q.Where(s => s.Type == value);
        }

        var items = await q
            .OrderByDescending(s => s.Version)
            .ThenByDescending(s => s.GeneratedAt)
            .Select(FinancialProjections.BoqSnapshotRow(db))
            .ToListAsync(ct);

        return items;
    }
}
