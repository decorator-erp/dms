using Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Finance.Application.Common;

/// <summary>
/// Signed-sum aggregation over <see cref="LedgerEntry"/>, ported from
/// <c>../backend/Application/Common/Financial/LedgerSums.cs</c> and identical to the arithmetic
/// <c>RecalculateBudgetLineCommandHandler</c> re-derives.
///
/// <para><b>Two rules, both easy to drop and both load-bearing.</b> Amounts are signed by direction
/// (<c>Out</c> positive, <c>In</c> negative) so a reversal — a mirror row with the opposite direction
/// and the same magnitude — cancels its original without this query knowing anything about reversals.
/// And <c>Committed</c> is NET of actuals, because an actual cost consumes the commitment that funded
/// it; dropping that subtraction restates every spent commitment as still outstanding.</para>
/// </summary>
internal static class LedgerSums
{
    /// <summary>In-memory signed amount, for rows already pulled into memory.</summary>
    public static decimal Signed(LedgerDirection direction, decimal amount) =>
        direction == LedgerDirection.Out ? amount : -amount;

    public static async Task<(decimal Committed, decimal Actual)> ComputeAsync(
        IQueryable<LedgerEntry> scopedLedger, CancellationToken ct)
    {
        var sums = await scopedLedger
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Commitments = g
                    .Where(e => e.Type == LedgerEntryType.Commitment)
                    .Sum(e => e.Direction == LedgerDirection.Out ? e.Amount : -e.Amount),
                ActualCosts = g
                    .Where(e => e.Type == LedgerEntryType.ActualCost)
                    .Sum(e => e.Direction == LedgerDirection.Out ? e.Amount : -e.Amount),
            })
            .FirstOrDefaultAsync(ct);

        if (sums is null)
            return (0m, 0m);

        return (sums.Commitments - sums.ActualCosts, sums.ActualCosts);
    }
}
