using SharedKernel;

namespace Finance.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/BoqSnapshot.cs</c>. A versioned, immutable BOQ report
/// generated from current budgets, purchases and signed differences.
///
/// <para><b>Immutable by design: there is no update and no delete.</b> A snapshot is a statement about
/// a moment; correcting it means generating the next version, which is why <see cref="Version"/> exists
/// and why <see cref="Create"/> takes it rather than deriving it — the next version number depends on
/// the snapshots already stored, which an entity cannot see.</para>
///
/// <para><b>The four totals are raw sums, not rounded.</b> Verified by reading
/// <c>BoqSnapshotDomainService.cs:61-64</c>: every one is a plain <c>Sum(...)</c>. The only rounding
/// anywhere in Finance is <see cref="InvoiceLine"/>'s line total, so a purchase total here is a sum of
/// already-rounded line totals and is exact.</para>
/// </summary>
internal sealed class BoqSnapshot : Entity, IAuditable
{
    /// <summary>Bare Guid → <c>projects.projects</c>.</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>Bare Guid → <c>projects.project_stages</c>. Null for a project-wide snapshot.</summary>
    public Guid? ProjectStageId { get; private set; }

    public BoqSnapshotType Type { get; private set; }

    /// <summary>Monotonic per project. Assigned by the handler under a repeatable-read transaction,
    /// because "the next version" is a fact about the set.</summary>
    public int Version { get; private set; }

    public DateTime GeneratedAt { get; private set; }

    /// <summary>Bare Guid → <c>hr.employees</c>.</summary>
    public Guid GeneratedById { get; private set; }

    public decimal BudgetedTotal { get; private set; }
    public decimal PurchaseTotal { get; private set; }
    public decimal DifferenceTotal { get; private set; }
    public decimal ActualTotal { get; private set; }

    public string? Notes { get; private set; }

    private BoqSnapshot()
    {
        // EF materialization.
    }

    public static BoqSnapshot Create(
        Guid projectId,
        BoqSnapshotType type,
        int version,
        Guid generatedById,
        decimal budgetedTotal,
        decimal purchaseTotal,
        decimal differenceTotal,
        decimal actualTotal,
        Guid? projectStageId = null,
        string? notes = null) =>
        new()
        {
            ProjectId = projectId,
            ProjectStageId = projectStageId,
            Type = type,
            Version = version,
            GeneratedAt = DateTime.UtcNow,
            GeneratedById = generatedById,
            BudgetedTotal = budgetedTotal,
            PurchaseTotal = purchaseTotal,
            DifferenceTotal = differenceTotal,
            ActualTotal = actualTotal,
            Notes = notes,
        };
}
