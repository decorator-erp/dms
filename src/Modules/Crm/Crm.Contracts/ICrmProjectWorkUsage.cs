namespace Crm.Contracts;

/// <summary>
/// crm-complete E2/E3 (D2): which project stages and tasks CRM still points at, so Projects can refuse to
/// hard-delete a stage or task that a brief, a requirement period, a requirement item or a brief-space entry
/// references (409) instead of leaving CRM rows pointing at nothing. Batch only: Projects asks once for the
/// whole set it is about to delete.
/// </summary>
public interface ICrmProjectWorkUsage
{
    /// <summary>The subset of <paramref name="projectStageIds"/> that a design brief or a requirement period
    /// names.</summary>
    Task<IReadOnlyList<Guid>> StagesInUseAsync(IReadOnlyCollection<Guid> projectStageIds, CancellationToken ct);

    /// <summary>The subset of <paramref name="projectTaskIds"/> that a requirement item or a brief-space
    /// entry names.</summary>
    Task<IReadOnlyList<Guid>> TasksInUseAsync(IReadOnlyCollection<Guid> projectTaskIds, CancellationToken ct);
}
