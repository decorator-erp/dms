using System.Linq.Expressions;
using Finance.Contracts.Financial;
using Finance.Domain;
using Finance.Infrastructure.Persistence;

namespace Finance.Application.Features.Financial;

/// <summary>
/// The three drilldown row shapes, written once each.
///
/// <para>Each appears in three or four of the fifteen queries in this slice — project, stage and task
/// scope all list invoices, variations and BOQ snapshots the same way and differ only in their filter.
/// The backend repeats each projection per query file, and the copies have already drifted once: two
/// <c>InvoiceListItemResponse</c> records exist under one name in two namespaces with different
/// members. Repeating a money projection is how the drift starts.</para>
///
/// <para><b>These are <see cref="Expression"/>-returning factories, not ordinary methods, and the
/// difference is not stylistic.</b> A plain method call inside a <c>Select</c> is a
/// <c>MethodCallExpression</c> EF cannot translate — the compiler does not inline the body into the
/// tree — so EF silently evaluates it client-side, per row, while the outer reader is still open. That
/// fails with <c>NpgsqlOperationInProgressException</c> the moment the projection itself queries, which
/// two of these do for their counts. Returning the expression keeps the whole projection in SQL.</para>
/// </summary>
internal static class FinancialProjections
{
    /// <summary><c>LinesCount</c> is a correlated subquery: <c>Invoice</c> carries no navigation to its
    /// lines, and this repo drops the backend's navigations on purpose.</summary>
    public static Expression<Func<Invoice, InvoiceListItemResponse>> InvoiceRow(FinanceDbContext db) =>
        i => new InvoiceListItemResponse(
            i.Id,
            i.Number,
            (Contracts.InvoiceType)i.Type,
            (Contracts.InvoiceStatus)i.Status,
            i.IssuedOn,
            i.DueOn,
            i.ProjectStageId,
            i.ClientId,
            i.VendorName,
            i.TotalAmount,
            i.PaidAmount,
            i.TotalAmount - i.PaidAmount,
            db.InvoiceLines.Count(l => l.InvoiceId == i.Id));

    public static Expression<Func<Variation, VariationListItemResponse>> VariationRow() =>
        v => new VariationListItemResponse(
            v.Id,
            (Contracts.VariationKind)v.Kind,
            (Contracts.VariationStatus)v.Status,
            v.ProjectStageId,
            v.ProjectTaskId,
            v.BudgetLineId,
            v.Amount,
            v.Reason,
            v.ApprovedAt,
            v.SignedAt);

    public static Expression<Func<BoqSnapshot, BoqSnapshotSummaryResponse>> BoqSnapshotRow(
        FinanceDbContext db) =>
        s => new BoqSnapshotSummaryResponse(
            s.Id,
            s.Version,
            (Contracts.BoqSnapshotType)s.Type,
            s.GeneratedAt,
            s.BudgetedTotal,
            s.PurchaseTotal,
            s.DifferenceTotal,
            s.ActualTotal,
            db.BoqSnapshotLines.Count(l => l.BoqSnapshotId == s.Id));
}
