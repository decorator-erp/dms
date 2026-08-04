using SharedKernel;

namespace Projects.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/ProjectSubTask.cs</c>.</summary>
internal sealed class ProjectSubTask : Entity, IAuditable
{
    public Guid ProjectTaskId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int Order { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsDone { get; private set; }
    public Guid? CompletedById { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    private ProjectSubTask()
    {
        // EF materialization.
    }

    public static ProjectSubTask Create(Guid projectTaskId, string title, int order, bool isRequired = true, string? description = null) =>
        new()
        {
            ProjectTaskId = projectTaskId,
            Title = title,
            Order = order,
            IsRequired = isRequired,
            Description = description,
        };

    public Result Complete(Guid completedById)
    {
        if (IsDone)
            return SubTaskErrors.AlreadyDone;

        IsDone = true;
        CompletedById = completedById;
        CompletedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }
}
