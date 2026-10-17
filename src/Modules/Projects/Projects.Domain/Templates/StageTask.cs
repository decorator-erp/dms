using SharedKernel;

namespace Projects.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/StageTask.cs</c>. Materialized into a runtime
/// <see cref="ProjectTask"/> at instantiation (G5-07). <see cref="DepartmentId"/> is a bare Guid →
/// <c>hr.departments</c>, never a navigation.</summary>
internal sealed class StageTask : Entity, IAuditable
{
    public Guid StageId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int Order { get; private set; }
    public decimal Weight { get; private set; }
    public Guid? DepartmentId { get; private set; }

    private StageTask()
    {
        // EF materialization.
    }

    public static StageTask Create(
        Guid stageId, string name, int order, decimal weight = 1.00m, string? description = null, Guid? departmentId = null) =>
        new() { StageId = stageId, Name = name, Order = order, Weight = weight, Description = description, DepartmentId = departmentId };

    public void Update(
        string? name = null, int? order = null, decimal? weight = null, string? description = null, Guid? departmentId = null)
    {
        if (name is not null)
        {
            Name = name;
        }

        if (order is not null)
        {
            Order = order.Value;
        }

        if (weight is not null)
        {
            Weight = weight.Value;
        }

        if (description is not null)
        {
            Description = description;
        }

        if (departmentId is not null)
        {
            DepartmentId = departmentId;
        }
    }
}
