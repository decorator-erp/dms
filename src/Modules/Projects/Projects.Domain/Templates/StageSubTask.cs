using SharedKernel;

namespace Projects.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/StageSubTask.cs</c>. Materialized into a runtime
/// <see cref="ProjectSubTask"/> at instantiation (G5-07).</summary>
internal sealed class StageSubTask : Entity, IAuditable
{
    public Guid StageTaskId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int Order { get; private set; }
    public bool IsRequired { get; private set; }

    private StageSubTask()
    {
        // EF materialization.
    }

    public static StageSubTask Create(Guid stageTaskId, string title, int order, bool isRequired = true, string? description = null) =>
        new() { StageTaskId = stageTaskId, Title = title, Order = order, IsRequired = isRequired, Description = description };

    public void Update(string? title = null, int? order = null, bool? isRequired = null, string? description = null)
    {
        if (title is not null)
        {
            Title = title;
        }

        if (order is not null)
        {
            Order = order.Value;
        }

        if (isRequired is not null)
        {
            IsRequired = isRequired.Value;
        }

        if (description is not null)
        {
            Description = description;
        }
    }
}
