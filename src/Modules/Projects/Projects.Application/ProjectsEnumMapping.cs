namespace Projects.Application;

/// <summary>
/// <c>Projects.Application</c> legitimately references both <c>Projects.Contracts</c> and
/// <c>Projects.Domain</c> — those two can't reference each other (see PROGRESS.md's G5-01/G5-02 entries
/// on the forced-mirroring pattern), so every command/query handler here needs to convert between them.
/// Twelve enum pairs (unlike Approvals' five) made hand-written exhaustive switches for each direction
/// impractical to keep up given this run's remaining scope — <c>Contracts.X</c> and <c>Domain.X</c> are
/// guaranteed to share the same underlying values by construction (both ported from the same backend
/// enum, in the same order, in the same commit), so a numeric cast is correct today. This is a
/// deliberate, disclosed trade-off, not an oversight: <see cref="EnumMappingParityTests"/> (in
/// <c>Projects.Application.Tests</c>) enumerates every value of every pair and asserts the names and
/// underlying values match exactly — the same safety an exhaustive switch would give (a value added to
/// one side without the other fails a test, not silently at runtime), for a fraction of the code.
/// </summary>
internal static class ProjectsEnumMapping
{
    public static Domain.ProjectStatus ToDomain(Contracts.ProjectStatus value) => (Domain.ProjectStatus)value;
    public static Contracts.ProjectStatus ToContracts(Domain.ProjectStatus value) => (Contracts.ProjectStatus)value;

    public static Domain.ProjectTaskStatus ToDomain(Contracts.ProjectTaskStatus value) => (Domain.ProjectTaskStatus)value;
    public static Contracts.ProjectTaskStatus ToContracts(Domain.ProjectTaskStatus value) => (Contracts.ProjectTaskStatus)value;

    public static Domain.StageStatus ToDomain(Contracts.StageStatus value) => (Domain.StageStatus)value;
    public static Contracts.StageStatus ToContracts(Domain.StageStatus value) => (Contracts.StageStatus)value;

    public static Domain.TaskPriority ToDomain(Contracts.TaskPriority value) => (Domain.TaskPriority)value;
    public static Contracts.TaskPriority ToContracts(Domain.TaskPriority value) => (Contracts.TaskPriority)value;

    public static Domain.StageTeamMemberRole ToDomain(Contracts.StageTeamMemberRole value) => (Domain.StageTeamMemberRole)value;
    public static Contracts.StageTeamMemberRole ToContracts(Domain.StageTeamMemberRole value) => (Contracts.StageTeamMemberRole)value;

    public static Domain.WorkflowType ToDomain(Contracts.WorkflowType value) => (Domain.WorkflowType)value;
    public static Contracts.WorkflowType ToContracts(Domain.WorkflowType value) => (Contracts.WorkflowType)value;

    public static Domain.StageTemplateType ToDomain(Contracts.StageTemplateType value) => (Domain.StageTemplateType)value;
    public static Contracts.StageTemplateType ToContracts(Domain.StageTemplateType value) => (Contracts.StageTemplateType)value;

    public static Domain.ConditionType ToDomain(Contracts.ConditionType value) => (Domain.ConditionType)value;
    public static Contracts.ConditionType ToContracts(Domain.ConditionType value) => (Contracts.ConditionType)value;

    public static Domain.GateOwnerKind ToDomain(Contracts.GateOwnerKind value) => (Domain.GateOwnerKind)value;
    public static Contracts.GateOwnerKind ToContracts(Domain.GateOwnerKind value) => (Contracts.GateOwnerKind)value;

    public static Domain.GateStatus ToDomain(Contracts.GateStatus value) => (Domain.GateStatus)value;
    public static Contracts.GateStatus ToContracts(Domain.GateStatus value) => (Contracts.GateStatus)value;

    public static Domain.GateTrigger ToDomain(Contracts.GateTrigger value) => (Domain.GateTrigger)value;
    public static Contracts.GateTrigger ToContracts(Domain.GateTrigger value) => (Contracts.GateTrigger)value;

    public static Domain.ApprovalType ToDomain(Contracts.ApprovalType value) => (Domain.ApprovalType)value;
    public static Contracts.ApprovalType ToContracts(Domain.ApprovalType value) => (Contracts.ApprovalType)value;
}
