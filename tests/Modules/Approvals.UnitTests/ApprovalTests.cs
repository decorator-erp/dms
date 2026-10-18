using Approvals.Domain;
using SharedKernel;
using Xunit;

namespace Approvals.UnitTests;

public class ApprovalTests
{
    private static Approval CreateApproval(params string[] approverUserIds) =>
        Approval.Create(
            ApprovalSubjectType.GateCondition,
            Guid.NewGuid(),
            ApprovalType.ManagerApproval,
            requestedByUserId: "requester-1",
            approvers: [.. approverUserIds.Select(id => new ApproverEntry(ApproverKind.Employee, id))]);

    [Fact]
    public void Create_SetsAllProvidedFields()
    {
        var subjectId = Guid.NewGuid();
        DateTimeOffset dueAt = DateTimeOffset.UtcNow.AddDays(3);

        var approval = Approval.Create(
            ApprovalSubjectType.GateCondition, subjectId, ApprovalType.ManagerApproval,
            "requester-1", [new ApproverEntry(ApproverKind.Employee, "approver-1")], dueAt);

        Assert.Equal(ApprovalSubjectType.GateCondition, approval.SubjectType);
        Assert.Equal(subjectId, approval.SubjectId);
        Assert.Equal(ApprovalType.ManagerApproval, approval.Type);
        Assert.Equal("requester-1", approval.RequestedByUserId);
        Assert.Equal(dueAt, approval.DueAtUtc);
        Assert.Equal(1, approval.Round);
        Assert.Null(approval.Decision);
        Assert.Equal(ApprovalState.PendingApproval, approval.State);
        Assert.True(approval.IsEligibleApprover("approver-1"));
        Assert.False(approval.IsEligibleApprover("someone-else"));
    }

    [Fact]
    public void Create_WithCallerSuppliedId_KeepsThatId()
    {
        var id = Guid.NewGuid();

        var approval = Approval.Create(
            id, ApprovalSubjectType.DesignBrief, Guid.NewGuid(), ApprovalType.ClientReview,
            "requester-1", [new ApproverEntry(ApproverKind.Client, "client-1")]);

        Assert.Equal(id, approval.Id);
        Assert.Equal(ApprovalState.PendingApproval, approval.State);
    }

    [Fact]
    public void Decide_ByEligibleApprover_Approved_Succeeds()
    {
        Approval approval = CreateApproval("approver-1", "approver-2");

        Result result = approval.Decide("approver-2", ApprovalDecision.Approved, comment: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalDecision.Approved, approval.Decision);
        Assert.Equal("approver-2", approval.DecidedByUserId);
        Assert.Equal(ApprovalState.Approved, approval.State);
        Assert.NotNull(approval.DecidedAtUtc);
    }

    [Fact]
    public void Decide_Rejected_RequiresComment()
    {
        Approval approval = CreateApproval("approver-1");

        Result withoutComment = approval.Decide("approver-1", ApprovalDecision.Rejected, comment: null);
        Assert.True(withoutComment.IsFailure);
        Assert.Equal(ApprovalErrors.CommentRequired, withoutComment.Error);
        Assert.Null(approval.Decision);

        Result withComment = approval.Decide("approver-1", ApprovalDecision.Rejected, comment: "not acceptable");
        Assert.True(withComment.IsSuccess);
        Assert.Equal(ApprovalDecision.Rejected, approval.Decision);
        Assert.Equal(ApprovalState.Rejected, approval.State);
    }

    [Fact]
    public void Decide_ChangesRequested_RequiresComment_AndMapsToNonTerminalState()
    {
        Approval approval = CreateApproval("approver-1");

        Result result = approval.Decide("approver-1", ApprovalDecision.ChangesRequested, comment: "please revise");

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalDecision.ChangesRequested, approval.Decision);
        // Non-terminal per decision #3/#4: reads as still-pending from the outside, no gate call.
        Assert.Equal(ApprovalState.PendingApproval, approval.State);
    }

    [Fact]
    public void Decide_ByNonApprover_WithoutOverride_IsRejected()
    {
        Approval approval = CreateApproval("approver-1");

        Result result = approval.Decide("stranger", ApprovalDecision.Approved, comment: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalErrors.NotAssignedApprover, result.Error);
        Assert.Null(approval.Decision);
    }

    [Fact]
    public void Decide_ByNonApprover_WithOverride_Succeeds()
    {
        Approval approval = CreateApproval("approver-1");

        Result result = approval.Decide("admin-user", ApprovalDecision.Approved, comment: null, isOverride: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("admin-user", approval.DecidedByUserId);
    }

    [Fact]
    public void Decide_Twice_SecondCallIsRejected()
    {
        Approval approval = CreateApproval("approver-1");
        approval.Decide("approver-1", ApprovalDecision.Approved, comment: null);

        Result result = approval.Decide("approver-1", ApprovalDecision.Rejected, comment: "changed my mind");

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalErrors.AlreadyDecided, result.Error);
        // First decision is untouched by the rejected second call.
        Assert.Equal(ApprovalDecision.Approved, approval.Decision);
    }

    [Fact]
    public void Decide_RaisesApprovalDecidedDomainEvent()
    {
        Approval approval = CreateApproval("approver-1");

        approval.Decide("approver-1", ApprovalDecision.Approved, comment: null);

        IDomainEvent raised = Assert.Single(approval.DomainEvents);
        ApprovalDecidedDomainEvent domainEvent = Assert.IsType<ApprovalDecidedDomainEvent>(raised);
        Assert.Equal(approval.Id, domainEvent.ApprovalId);
        Assert.Equal(ApprovalDecision.Approved, domainEvent.Decision);
        Assert.Equal("approver-1", domainEvent.DecidedByUserId);
    }
}
