using SharedKernel;

namespace Projects.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/GateCondition.cs</c>. Exactly one of
/// <see cref="ProjectStageId"/>/<see cref="ProjectTaskId"/> is set (enforced in <see cref="Create"/>),
/// same XOR-owner shape the backend's DB check constraint enforces. Create-time reference validation
/// (does <see cref="ReferencedStageId"/>/<see cref="ReferencedTaskId"/> exist, same project, not
/// self-referencing) needs <c>ProjectsDbContext</c> access this entity doesn't have — that logic lives in
/// <c>Projects.Application</c>'s handler implementations of <c>IGateConditionTypeHandler</c> (G5-08), not
/// here. This entity holds only the pure state machine: XOR-owner validation and satisfy/reject/reset.
/// </summary>
internal sealed class GateCondition : Entity, IAuditable
{
    public Guid? ProjectStageId { get; private set; }
    public Guid? ProjectTaskId { get; private set; }
    public ConditionType ConditionType { get; private set; }
    public GateTrigger Trigger { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public Guid? ReferencedTaskId { get; private set; }
    public Guid? ReferencedStageId { get; private set; }
    public ApprovalType? ApprovalType { get; private set; }
    public GateStatus Status { get; private set; }
    public Guid? ResolvedById { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public string? ResolutionNote { get; private set; }

    /// <summary>Links to <c>approvals.approvals</c> once an <see cref="Domain.ConditionType.Approval"/>
    /// gate has requested its approval (G5-08). No FK — cross-schema, handled the same way
    /// <c>Approvals.approvals."RequestedByUserId"</c> is (a bare id, constrained by raw SQL in the
    /// migration, not an EF navigation).</summary>
    public Guid? ApprovalId { get; private set; }

    public bool IsSatisfied => Status == GateStatus.Satisfied;
    public bool IsRejected => Status == GateStatus.Rejected;
    public bool IsPending => Status == GateStatus.Pending;

    public GateOwnerKind Scope => ProjectStageId.HasValue
        ? GateOwnerKind.Stage
        : ProjectTaskId.HasValue
            ? GateOwnerKind.Task
            : throw new InvalidOperationException("GateCondition has no owner — this should be unreachable, Create enforces the XOR.");

    private GateCondition()
    {
        // EF materialization.
    }

    public static Result<GateCondition> Create(
        Guid? projectStageId, Guid? projectTaskId, ConditionType conditionType, GateTrigger trigger, string label,
        Guid? referencedTaskId = null, Guid? referencedStageId = null, ApprovalType? approvalType = null)
    {
        if (projectStageId.HasValue == projectTaskId.HasValue)
            return GateConditionErrors.InvalidScope;

        return new GateCondition
        {
            ProjectStageId = projectStageId,
            ProjectTaskId = projectTaskId,
            ConditionType = conditionType,
            Trigger = trigger,
            Label = label,
            ReferencedTaskId = referencedTaskId,
            ReferencedStageId = referencedStageId,
            ApprovalType = approvalType,
            Status = GateStatus.Pending,
        };
    }

    public void LinkApproval(Guid approvalId) => ApprovalId = approvalId;

    public void Update(string label, GateTrigger trigger)
    {
        Label = label;
        Trigger = trigger;
    }

    public Result MarkSatisfied(Guid satisfiedById, string? note = null)
    {
        if (Status == GateStatus.Satisfied)
            return Result.Success();

        Status = GateStatus.Satisfied;
        ResolvedById = satisfiedById;
        ResolvedAt = DateTime.UtcNow;
        ResolutionNote = note;
        AddDomainEvent(new GateConditionSatisfiedDomainEvent(Id, Scope, ProjectStageId ?? ProjectTaskId!.Value));
        return Result.Success();
    }

    public Result MarkRejected(Guid rejectedById, string? note = null)
    {
        Status = GateStatus.Rejected;
        ResolvedById = rejectedById;
        ResolvedAt = DateTime.UtcNow;
        ResolutionNote = note;
        return Result.Success();
    }

    public Result MarkPending(string? note = null)
    {
        Status = GateStatus.Pending;
        ResolvedById = null;
        ResolvedAt = null;
        ResolutionNote = note;
        return Result.Success();
    }
}
