using SharedKernel;

namespace Projects.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/Project.cs</c>. <see cref="ClientId"/> stays an unvalidated
/// <c>Guid</c> — CRM absent, no FK (documented in the G5-04 migration and
/// <c>docs/cross-schema-foreign-keys.md</c>). <see cref="OwnerId"/> is a bare Guid → <c>hr.employees</c>,
/// resolved server-side from the caller's identity at creation, never a navigation. No
/// <c>ProjectSector</c> property — that's a CRM lead-intake concept in the backend, not a <c>Project</c>
/// field at all (decision #6).
/// </summary>
internal sealed class Project : Entity, IAuditable
{
    private static readonly Dictionary<ProjectStatus, ProjectStatus[]> AllowedTransitions = new()
    {
        [ProjectStatus.Draft] = [ProjectStatus.InProgress, ProjectStatus.Cancelled],
        [ProjectStatus.InProgress] = [ProjectStatus.OnHold, ProjectStatus.Completed, ProjectStatus.Cancelled],
        [ProjectStatus.OnHold] = [ProjectStatus.InProgress, ProjectStatus.Cancelled],
        [ProjectStatus.Completed] = [],
        [ProjectStatus.Cancelled] = [],
    };

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public ProjectStatus Status { get; private set; }
    public WorkflowType WorkflowType { get; private set; }
    public string Address { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public decimal ProgressPercentage { get; private set; }
    public DateTime? EstimatedStartDate { get; private set; }
    public DateTime? EstimatedEndDate { get; private set; }
    public DateTime? ActualStartDate { get; private set; }
    public DateTime? ActualEndDate { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid OwnerId { get; private set; }

    private Project()
    {
        // EF materialization.
    }

    public static Project Create(
        string code, string name, string address, WorkflowType workflowType, Guid clientId, Guid ownerId,
        string? description = null, string? notes = null,
        DateTime? estimatedStartDate = null, DateTime? estimatedEndDate = null) =>
        new()
        {
            Code = code,
            Name = name,
            Address = address,
            WorkflowType = workflowType,
            ClientId = clientId,
            OwnerId = ownerId,
            Description = description,
            Notes = notes,
            EstimatedStartDate = estimatedStartDate,
            EstimatedEndDate = estimatedEndDate,
            Status = ProjectStatus.Draft,
        };

    public void Update(
        string? name = null, string? description = null, string? address = null, string? notes = null,
        DateTime? estimatedStartDate = null, DateTime? estimatedEndDate = null)
    {
        if (name is not null)
            Name = name;
        if (description is not null)
            Description = description;
        if (address is not null)
            Address = address;
        if (notes is not null)
            Notes = notes;
        if (estimatedStartDate is not null)
            EstimatedStartDate = estimatedStartDate;
        if (estimatedEndDate is not null)
            EstimatedEndDate = estimatedEndDate;
    }

    public Result ChangeStatus(ProjectStatus status)
    {
        if (Status == status)
            return Result.Success();

        if (!AllowedTransitions[Status].Contains(status))
            return ProjectErrors.InvalidStatusTransition;

        if (status == ProjectStatus.InProgress && ActualStartDate is null)
            ActualStartDate = DateTime.UtcNow;
        if (status is ProjectStatus.Completed or ProjectStatus.Cancelled)
            ActualEndDate = DateTime.UtcNow;

        Status = status;
        return Result.Success();
    }

    public void RecalculateProgress(decimal progressPercentage) => ProgressPercentage = progressPercentage;

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);
}
