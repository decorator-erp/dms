namespace Finance.Contracts;

/// <summary>
/// crm-complete E2/E3 (D2): which project stages and tasks Finance still points at (budget lines, ledger
/// entries, invoices and their lines, variations, BOQ snapshots and their lines), so Projects can refuse to
/// hard-delete a stage or task with money attached (409). Batch only.
/// </summary>
public interface IFinanceProjectWorkUsage
{
    Task<IReadOnlyList<Guid>> StagesInUseAsync(IReadOnlyCollection<Guid> projectStageIds, CancellationToken ct);

    Task<IReadOnlyList<Guid>> TasksInUseAsync(IReadOnlyCollection<Guid> projectTaskIds, CancellationToken ct);

    /// <summary>P1: the subset of <paramref name="projectIds"/> that any budget line, ledger entry, invoice,
    /// variation or BOQ snapshot names, so Projects can refuse to hard-delete such a project (409).</summary>
    Task<IReadOnlyList<Guid>> ProjectsInUseAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct);
}
