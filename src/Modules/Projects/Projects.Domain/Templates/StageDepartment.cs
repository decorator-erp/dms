using SharedKernel;

namespace Projects.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/StageDepartment.cs</c> — one row per (template stage,
/// department) pair. Composite-keyed, not deriving from <see cref="SharedKernel.Entity"/> — same
/// reasoning as <c>Hr.Domain.EmployeeDepartment</c>, a pure join row with no identity of its own. Used by
/// instantiation (G5-07) to materialize one <see cref="StageTeam"/> per active row.
/// </summary>
internal sealed class StageDepartment : IAuditable
{
    public Guid StageId { get; private set; }
    public Guid DepartmentId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private StageDepartment()
    {
        // EF materialization.
    }

    public static StageDepartment Create(Guid stageId, Guid departmentId)
    {
        if (stageId == Guid.Empty || departmentId == Guid.Empty)
        {
            throw new ArgumentException("StageDepartment requires non-empty stage and department ids.");
        }

        return new StageDepartment { StageId = stageId, DepartmentId = departmentId };
    }

    public void Remove() => IsActive = false;
}
