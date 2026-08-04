using Projects.Domain;
using Xunit;

namespace Projects.UnitTests;

public class StageTemplateTests
{
    [Fact]
    public void Update_ChangesOnlyProvidedFields()
    {
        var template = StageTemplate.Create("Residential", isDefault: true);

        template.Update(name: "Residential v2");

        Assert.Equal("Residential v2", template.Name);
        Assert.True(template.IsDefault);
    }
}

public class StageDepartmentTests
{
    [Fact]
    public void Create_WithEmptyStageId_Throws()
    {
        Assert.Throws<ArgumentException>(() => StageDepartment.Create(Guid.Empty, Guid.NewGuid()));
    }

    [Fact]
    public void Remove_SetsIsActiveFalse()
    {
        var stageDepartment = StageDepartment.Create(Guid.NewGuid(), Guid.NewGuid());

        stageDepartment.Remove();

        Assert.False(stageDepartment.IsActive);
    }
}

public class GateConditionTests
{
    [Fact]
    public void Create_WithBothOwnersSet_IsRejected()
    {
        var result = GateCondition.Create(Guid.NewGuid(), Guid.NewGuid(), ConditionType.ManualCheck, GateTrigger.Complete, "label");

        Assert.True(result.IsFailure);
        Assert.Equal(GateConditionErrors.InvalidScope, result.Error);
    }

    [Fact]
    public void Create_WithNeitherOwnerSet_IsRejected()
    {
        var result = GateCondition.Create(null, null, ConditionType.ManualCheck, GateTrigger.Complete, "label");

        Assert.True(result.IsFailure);
        Assert.Equal(GateConditionErrors.InvalidScope, result.Error);
    }

    [Fact]
    public void Create_StageOwned_SetsScopeToStage()
    {
        var gate = GateCondition.Create(Guid.NewGuid(), null, ConditionType.ManualCheck, GateTrigger.Complete, "label").Value!;

        Assert.Equal(GateOwnerKind.Stage, gate.Scope);
        Assert.True(gate.IsPending);
    }

    [Fact]
    public void Create_TaskOwned_SetsScopeToTask()
    {
        var gate = GateCondition.Create(null, Guid.NewGuid(), ConditionType.ManualCheck, GateTrigger.Complete, "label").Value!;

        Assert.Equal(GateOwnerKind.Task, gate.Scope);
    }

    [Fact]
    public void MarkSatisfied_Succeeds_AndRaisesDomainEvent()
    {
        var gate = GateCondition.Create(Guid.NewGuid(), null, ConditionType.ManualCheck, GateTrigger.Unlock, "label").Value!;
        var satisfiedById = Guid.NewGuid();

        var result = gate.MarkSatisfied(satisfiedById, "looks good");

        Assert.True(result.IsSuccess);
        Assert.True(gate.IsSatisfied);
        Assert.Equal(satisfiedById, gate.ResolvedById);
        var raised = Assert.IsType<GateConditionSatisfiedDomainEvent>(Assert.Single(gate.DomainEvents));
        Assert.Equal(gate.Id, raised.GateConditionId);
        Assert.Equal(GateOwnerKind.Stage, raised.OwnerKind);
    }

    [Fact]
    public void MarkSatisfied_Twice_IsIdempotent_DoesNotRaiseASecondEvent()
    {
        var gate = GateCondition.Create(Guid.NewGuid(), null, ConditionType.ManualCheck, GateTrigger.Unlock, "label").Value!;
        gate.MarkSatisfied(Guid.NewGuid());

        var result = gate.MarkSatisfied(Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Single(gate.DomainEvents);
    }

    [Fact]
    public void MarkRejected_SetsStatusAndResolutionFields()
    {
        var gate = GateCondition.Create(null, Guid.NewGuid(), ConditionType.ManualCheck, GateTrigger.Complete, "label").Value!;

        var result = gate.MarkRejected(Guid.NewGuid(), "missing sign-off");

        Assert.True(result.IsSuccess);
        Assert.True(gate.IsRejected);
        Assert.Equal("missing sign-off", gate.ResolutionNote);
    }

    [Fact]
    public void MarkPending_AfterSatisfied_ResetsResolutionFields()
    {
        var gate = GateCondition.Create(Guid.NewGuid(), null, ConditionType.ManualCheck, GateTrigger.Unlock, "label").Value!;
        gate.MarkSatisfied(Guid.NewGuid());

        var result = gate.MarkPending("reopened for review");

        Assert.True(result.IsSuccess);
        Assert.True(gate.IsPending);
        Assert.Null(gate.ResolvedById);
        Assert.Null(gate.ResolvedAt);
    }

    [Fact]
    public void LinkApproval_SetsApprovalId()
    {
        var gate = GateCondition.Create(Guid.NewGuid(), null, ConditionType.Approval, GateTrigger.Complete, "manager approval",
            approvalType: ApprovalType.ManagerApproval).Value!;
        var approvalId = Guid.NewGuid();

        gate.LinkApproval(approvalId);

        Assert.Equal(approvalId, gate.ApprovalId);
    }
}
