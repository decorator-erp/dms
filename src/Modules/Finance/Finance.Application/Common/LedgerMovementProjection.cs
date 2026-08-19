using Finance.Contracts.Financial;
using Finance.Domain;
using Finance.Infrastructure.Persistence;

namespace Finance.Application.Common;

/// <summary>
/// Projects an already-scoped, ordered and paged <see cref="LedgerEntry"/> query into
/// <see cref="LedgerMovementResponse"/>. Ported from
/// <c>../backend/Application/Common/Financial/LedgerMovementProjection.cs</c>.
///
/// <para>The budget-line name is a correlated subquery rather than a join, because
/// <see cref="LedgerEntry"/> has no navigation to <c>BudgetLine</c> and the relationship is optional —
/// an inner join would drop every movement not tied to a budget line, which is most client payments.</para>
///
/// <para><b>Applied after paging, never before.</b> The projection is the last step so the subquery runs
/// for one page of rows rather than for the whole scope.</para>
/// </summary>
internal static class LedgerMovementProjection
{
    public static IQueryable<LedgerMovementResponse> Project(
        FinanceDbContext db, IQueryable<LedgerEntry> source) =>
        source.Select(e => new LedgerMovementResponse(
            e.Id,
            e.OccurredOn,
            FinanceEnumMapping.ToContracts(e.Type),
            FinanceEnumMapping.ToContracts(e.Direction),
            e.Amount,
            e.ProjectId,
            e.ProjectStageId,
            e.ProjectTaskId,
            e.BudgetLineId,
            e.BudgetLineId == null
                ? null
                : db.BudgetLines.Where(b => b.Id == e.BudgetLineId).Select(b => b.Name).FirstOrDefault(),
            e.VariationId,
            e.ReversalOfId,
            e.VendorName,
            e.ClientId,
            e.Reference,
            e.Note));
}
