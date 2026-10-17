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

    /// <summary>Added at G7-02 (A1) for <see cref="Domain.ConditionType.DocumentApproved"/>: the
    /// document lineage (<c>ProjectDocument.RootDocumentId</c>, in Documents' own schema) this gate
    /// waits on. No FK — cross-schema in the direction rule 9's allow-list forbids
    /// (<c>projects → documents</c> would invert the module graph; Documents depends on Projects, not
    /// the reverse). Same treatment as <c>Project.ClientId</c> before CRM: a bare unvalidated
    /// <see cref="Guid"/>, validated in application code by Documents' own
    /// <c>DocumentApprovedHandler</c> against its own <c>DbContext</c>. Documented in the migration and
    /// in <c>docs/cross-schema-foreign-keys.md</c>.</summary>
    public Guid? ReferencedDocumentId { get; private set; }

    /// <summary>Added at F1-00 for <see cref="Domain.ConditionType.Budget"/>: the Finance variation
    /// (<c>finance.variations."Id"</c>) this gate's satisfaction is linked to. Stamped when a raised
    /// variation opens or reuses a Budget gate, and read back when that variation is approved to find
    /// the gate to satisfy — <see cref="Projects.Contracts.GateConditionContext"/> is deliberately NOT
    /// extended, because <c>BudgetHandler.ValidateAsync</c> returns success unconditionally: creation
    /// needs no references, only satisfaction needs to remember. The companion-rule case from
    /// <c>docs/adding-a-module.md</c>, in its purest form.
    ///
    /// <para>No FK, for the same reason as <see cref="ReferencedDocumentId"/> but a different graph
    /// edge: it would point at <c>finance.variations</c>, and Finance depends on Projects
    /// (<c>BudgetLine.ProjectId</c>, <c>Invoice.ProjectId</c>, …), so a <c>projects → finance</c>
    /// constraint makes the migration order unsatisfiable. Fourth instance of the
    /// deliberately-absent-FK pattern, after <c>projects.projects."ClientId"</c> (pre-Phase 4),
    /// <see cref="ReferencedDocumentId"/> (Phase 3) and <c>crm_artifact_documents."DocumentId"</c>
    /// (Phase 4). Documented in the migration and in <c>docs/cross-schema-foreign-keys.md</c>.</para>
    ///
    /// <para>Unlike the backend's <c>GateCondition.VariationId</c> there is no <c>Variation</c>
    /// navigation property beside it — a navigation to another module's entity type is exactly what
    /// arch rule 3 forbids. A bare <see cref="Guid"/>, validated in Finance's own application code
    /// against its own <c>DbContext</c>.</para>
    /// </summary>
    public Guid? VariationId { get; private set; }
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
        Guid? referencedTaskId = null, Guid? referencedStageId = null, ApprovalType? approvalType = null,
        Guid? referencedDocumentId = null, Guid? variationId = null)
    {
        if (projectStageId.HasValue == projectTaskId.HasValue)
        {
            return GateConditionErrors.InvalidScope;
        }

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
            ReferencedDocumentId = referencedDocumentId,
            VariationId = variationId,
            Status = GateStatus.Pending,
        };
    }

    public void LinkApproval(Guid approvalId) => ApprovalId = approvalId;

    /// <summary>
    /// Attaches an existing unlinked Budget gate to a newly raised variation, so approving that
    /// variation satisfies this gate.
    ///
    /// <para><b>Added at F1-08, not at F1-00, and the delay was deliberate.</b> The backend has an
    /// equivalent <c>LinkToVariation</c> that nothing calls — <c>VariationDomainService</c> stamps the
    /// column by <c>ExecuteUpdateAsync</c> in both of its branches — so F1-00 ported the column and the
    /// factory parameter but not the setter, on the rule that dead code is not ported. It arrives now
    /// because <c>VariationRaisedIntegrationEventHandler</c> is a real caller.</para>
    ///
    /// <para>Re-linking is what stops a second variation on the same owner opening a second gate beside
    /// the first, which would leave the owner needing both approved to proceed.</para>
    /// </summary>
    public void LinkVariation(Guid variationId) => VariationId = variationId;

    public void Update(string label, GateTrigger trigger)
    {
        Label = label;
        Trigger = trigger;
    }

    /// <summary><paramref name="satisfiedById"/> is nullable: a manual
    /// <c>SatisfyGateConditionCommand</c> always has a real employee behind it, but a CASCADE
    /// satisfaction (a stage/task completion whose own <c>CompletedById</c>/<c>AssignedToId</c> is
    /// unresolvable - e.g. an unassigned free-style task) has no one to attribute it to.
    /// <see cref="ResolvedById"/> carries a raw-SQL FK to <c>hr.employees</c> (G5-04's migration) with no
    /// "unknown employee" sentinel row, so passing a fabricated Guid here would violate that constraint
    /// rather than degrade gracefully - <c>null</c> is the only value that's always valid.</summary>
    public Result MarkSatisfied(Guid? satisfiedById, string? note = null)
    {
        if (Status == GateStatus.Satisfied)
        {
            return Result.Success();
        }

        Status = GateStatus.Satisfied;
        ResolvedById = satisfiedById;
        ResolvedAt = DateTime.UtcNow;
        ResolutionNote = note;
        AddDomainEvent(new GateConditionSatisfiedDomainEvent(Id, Scope, ProjectStageId ?? ProjectTaskId!.Value));
        return Result.Success();
    }

    public Result MarkRejected(Guid? rejectedById, string? note = null)
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
