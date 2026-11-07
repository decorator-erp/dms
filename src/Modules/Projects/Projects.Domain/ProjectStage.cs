using SharedKernel;

namespace Projects.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/ProjectStage.cs</c>. <see cref="StageId"/> links
/// back to the template <c>Stage</c> it was materialized from (nullable — a stage can be added directly,
/// per the backend). <see cref="Lock"/>/<see cref="Unlock"/> are new relative to the backend's own factory
/// defaults: an <c>Unlock</c>-trigger <c>GateCondition</c> created against this stage (G5-08) calls
/// <see cref="Lock"/> at creation time — instantiation itself never locks a stage (decision #7).</summary>
internal sealed class ProjectStage : Entity, IAuditable
{
    public Guid ProjectId { get; private set; }
    public Guid? StageId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int Order { get; private set; }
    public StageStatus Status { get; private set; }
    /// <summary>P2/NP5: <see cref="ProgressPercent"/>, <see cref="CompletedTasks"/> and <see cref="TotalTasks"/> are
    /// set at creation and afterwards only recounted in SQL (<c>StageCounters</c>, Cancelled tasks excluded).</summary>
    public decimal ProgressPercent { get; private set; }
    public int CompletedTasks { get; private set; }
    public int TotalTasks { get; private set; }
    public decimal? Cost { get; private set; }
    public DateTime? EstimatedStartDate { get; private set; }
    public DateTime? EstimatedEndDate { get; private set; }
    public DateTime? ActualStartDate { get; private set; }
    public DateTime? ActualEndDate { get; private set; }
    public Guid? CompletedById { get; private set; }
    public string? Notes { get; private set; }

    private ProjectStage()
    {
        // EF materialization.
    }

    public static ProjectStage Create(
        string name, int order, Guid projectId, int totalTasks = 0, Guid? stageId = null,
        string? description = null, string? notes = null,
        DateTime? estimatedStartDate = null, DateTime? estimatedEndDate = null, decimal? cost = null) =>
        new()
        {
            Name = name,
            Order = order,
            ProjectId = projectId,
            TotalTasks = totalTasks,
            StageId = stageId,
            Description = description,
            Notes = notes,
            EstimatedStartDate = estimatedStartDate,
            EstimatedEndDate = estimatedEndDate,
            Cost = cost,
            Status = StageStatus.Active,
        };

    /// <summary>E2 (D2): the office edits a stage by hand. Status and progress are untouched.</summary>
    public void Update(
        string name, string? description, string? notes, DateTime? estimatedStartDate, DateTime? estimatedEndDate)
    {
        Name = name;
        Description = description;
        Notes = notes;
        EstimatedStartDate = estimatedStartDate;
        EstimatedEndDate = estimatedEndDate;
    }

    /// <summary>E2: the caller keeps the project's stage orders contiguous (1..n).</summary>
    public void SetOrder(int order) => Order = order;

    public Result SetCost(decimal? cost)
    {
        if (cost is < 0)
        {
            return StageErrors.NegativeCost;
        }

        Cost = cost;
        return Result.Success();
    }

    public Result Complete(Guid completedById)
    {
        if (Status != StageStatus.Active)
        {
            return StageErrors.InvalidStatusTransition;
        }

        Status = StageStatus.Completed;
        CompletedById = completedById;
        ActualEndDate = DateTime.UtcNow;
        AddDomainEvent(new ProjectStageCompletedDomainEvent(Id, ProjectId, Name));
        return Result.Success();
    }

    public Result Reject(string reason)
    {
        if (Status != StageStatus.Active)
        {
            return StageErrors.InvalidStatusTransition;
        }

        Status = StageStatus.Rejected;
        Notes = reason;
        AddDomainEvent(new ProjectStageRejectedDomainEvent(Id, ProjectId, Name));
        return Result.Success();
    }

    /// <summary>NP2: an <c>Unlock</c>-trigger gate may lock this stage only while it is Active or already Locked.</summary>
    public bool IsLockable => Status is StageStatus.Active or StageStatus.Locked;

    /// <summary>Called when an <c>Unlock</c>-trigger gate is created against this stage.</summary>
    public void Lock() => Status = StageStatus.Locked;

    /// <summary>Called when this stage's <c>Unlock</c>-trigger gate is satisfied. No-op if the stage
    /// isn't currently locked (e.g. it has no such gate).</summary>
    public Result Unlock()
    {
        if (Status != StageStatus.Locked)
        {
            return Result.Success();
        }

        Status = StageStatus.Active;
        ActualStartDate ??= DateTime.UtcNow;

        return Result.Success();
    }
}
