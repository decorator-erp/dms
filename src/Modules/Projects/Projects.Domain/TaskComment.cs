using SharedKernel;

namespace Projects.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/TaskComment.cs</c>. <see cref="EmployeeId"/> is a
/// bare Guid → <c>hr.employees</c>, never a navigation.</summary>
internal sealed class TaskComment : Entity, IAuditable
{
    public Guid TaskId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string Content { get; private set; } = string.Empty;

    /// <summary>E6: when the text was last edited (UTC); null for an unedited comment.</summary>
    public DateTime? EditedAtUtc { get; private set; }

    private TaskComment()
    {
        // EF materialization.
    }

    public static TaskComment Create(string content, Guid taskId, Guid employeeId) =>
        new() { Content = content, TaskId = taskId, EmployeeId = employeeId };

    public void UpdateContent(string content)
    {
        Content = content;
        EditedAtUtc = DateTime.UtcNow;
    }
}
