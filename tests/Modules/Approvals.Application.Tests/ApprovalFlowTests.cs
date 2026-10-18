using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;
using Domain = Approvals.Domain;

namespace Approvals.Application.Tests;

[Collection(ApprovalsApplicationCollection.Name)]
public sealed class ApprovalFlowTests(ApprovalsApplicationFixture fixture)
{
    /// <summary>
    /// The plan's own verify block for G4-04: request (via <see cref="IApprovalRequester"/> directly —
    /// that is its API) → decide via the command surface → outbox row exists → processor delivers →
    /// zero-handler mark-processed (no subscriber exists until G5-08 — this exercises R2 on a real event).
    /// </summary>
    [Fact]
    public async Task Request_then_decide_then_outbox_delivers_with_zero_handlers()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string requesterUserId = await RegisterUserAsync(sp);
        string approverUserId = await RegisterUserAsync(sp);

        IApprovalRequester requester = sp.GetRequiredService<IApprovalRequester>();
        Result<ApprovalRequestOutcome> requestResult = await requester.RequestAsync(new ApprovalRequest(
            Guid.NewGuid(), ApprovalSubjectType.GateCondition, Guid.NewGuid(), ApprovalType.ManagerApproval,
            requesterUserId, [new ApproverSpec(ApproverKind.Employee, approverUserId)], DueAtUtc: null),
            CancellationToken.None);
        Assert.True(requestResult.IsSuccess);
        Guid approvalId = requestResult.Value!.ApprovalId;

        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = approverUserId;

        ICommandHandler<DecideApprovalCommand> decide = sp.GetRequiredService<ICommandHandler<DecideApprovalCommand>>();
        Result decideResult = await decide.HandleAsync(
            new DecideApprovalCommand(approvalId, ApprovalDecision.Approved, Comment: null));
        Assert.True(decideResult.IsSuccess);

        ApprovalSnapshot? snapshot = await requester.GetAsync(approvalId, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal(ApprovalState.Approved, snapshot.State);
        Assert.Equal(ApprovalDecision.Approved, snapshot.Decision);

        // The decide command enqueued ApprovalDecidedIntegrationEvent in its own SaveChangesAsync; no
        // subscriber exists in this module (R2: zero-handler is a silent, successful no-op) - driving the
        // processor directly rather than waiting for its real poll interval.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        // A second run must be a safe no-op too - nothing to redeliver, no exception.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Decide_ByNonApprover_IsForbidden_UnlessCallerHasDecideAny()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string requesterUserId = await RegisterUserAsync(sp);
        string approverUserId = await RegisterUserAsync(sp);
        string strangerUserId = await RegisterUserAsync(sp);

        IApprovalRequester requester = sp.GetRequiredService<IApprovalRequester>();
        Result<ApprovalRequestOutcome> requestResult = await requester.RequestAsync(new ApprovalRequest(
            Guid.NewGuid(), ApprovalSubjectType.GateCondition, Guid.NewGuid(), ApprovalType.ManagerApproval,
            requesterUserId, [new ApproverSpec(ApproverKind.Employee, approverUserId)], DueAtUtc: null),
            CancellationToken.None);
        Guid approvalId = requestResult.Value!.ApprovalId;

        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = strangerUserId;

        ICommandHandler<DecideApprovalCommand> decide = sp.GetRequiredService<ICommandHandler<DecideApprovalCommand>>();
        Result forbidden = await decide.HandleAsync(
            new DecideApprovalCommand(approvalId, ApprovalDecision.Approved, Comment: null));
        Assert.True(forbidden.IsFailure);
        Assert.Equal(Domain.ApprovalErrors.NotAssignedApprover, forbidden.Error);
        Assert.Equal(403, forbidden.Error.Status);

        currentUser.Grant(ApprovalsPermissions.DecideAny);
        Result overridden = await decide.HandleAsync(
            new DecideApprovalCommand(approvalId, ApprovalDecision.Approved, Comment: null));
        Assert.True(overridden.IsSuccess);
    }

    [Fact]
    public async Task Decide_AnAlreadyDecidedApproval_IsRejectedAsConflict()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string requesterUserId = await RegisterUserAsync(sp);
        string approverUserId = await RegisterUserAsync(sp);

        IApprovalRequester requester = sp.GetRequiredService<IApprovalRequester>();
        Result<ApprovalRequestOutcome> requestResult = await requester.RequestAsync(new ApprovalRequest(
            Guid.NewGuid(), ApprovalSubjectType.GateCondition, Guid.NewGuid(), ApprovalType.ManagerApproval,
            requesterUserId, [new ApproverSpec(ApproverKind.Employee, approverUserId)], DueAtUtc: null),
            CancellationToken.None);
        Guid approvalId = requestResult.Value!.ApprovalId;

        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = approverUserId;

        ICommandHandler<DecideApprovalCommand> decide = sp.GetRequiredService<ICommandHandler<DecideApprovalCommand>>();
        Result first = await decide.HandleAsync(new DecideApprovalCommand(approvalId, ApprovalDecision.Approved, null));
        Assert.True(first.IsSuccess);

        Result second = await decide.HandleAsync(new DecideApprovalCommand(approvalId, ApprovalDecision.Rejected, "changed my mind"));
        Assert.True(second.IsFailure);
        Assert.Equal(Domain.ApprovalErrors.AlreadyDecided, second.Error);
        Assert.Equal(409, second.Error.Status);
    }

    [Fact]
    public async Task RequestingASecondApproval_ForAnAlreadyOpenSubject_IsRejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string requesterUserId = await RegisterUserAsync(sp);
        string approverUserId = await RegisterUserAsync(sp);
        var subjectId = Guid.NewGuid();

        IApprovalRequester requester = sp.GetRequiredService<IApprovalRequester>();
        Result<ApprovalRequestOutcome> first = await requester.RequestAsync(new ApprovalRequest(
            Guid.NewGuid(), ApprovalSubjectType.GateCondition, subjectId, ApprovalType.ManagerApproval,
            requesterUserId, [new ApproverSpec(ApproverKind.Employee, approverUserId)], DueAtUtc: null),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        Result<ApprovalRequestOutcome> second = await requester.RequestAsync(new ApprovalRequest(
            Guid.NewGuid(), ApprovalSubjectType.GateCondition, subjectId, ApprovalType.ManagerApproval,
            requesterUserId, [new ApproverSpec(ApproverKind.Employee, approverUserId)], DueAtUtc: null),
            CancellationToken.None);
        Assert.True(second.IsFailure);
        Assert.Equal(Domain.ApprovalErrors.OpenApprovalExists, second.Error);
    }

    [Fact]
    public async Task RequestingTwice_WithTheSameApprovalId_ReturnsTheSameApproval_AndReportsItsDecision()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string requesterUserId = await RegisterUserAsync(sp);
        string approverUserId = await RegisterUserAsync(sp);
        var subjectId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var request = new ApprovalRequest(
            approvalId, ApprovalSubjectType.GateCondition, subjectId, ApprovalType.ManagerApproval,
            requesterUserId, [new ApproverSpec(ApproverKind.Employee, approverUserId)], DueAtUtc: null);

        IApprovalRequester requester = sp.GetRequiredService<IApprovalRequester>();
        Result<ApprovalRequestOutcome> first = await requester.RequestAsync(request, CancellationToken.None);
        Result<ApprovalRequestOutcome> retry = await requester.RequestAsync(request, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.Equal(approvalId, first.Value!.ApprovalId);
        Assert.Equal(approvalId, retry.Value!.ApprovalId);
        Assert.Null(retry.Value.Decision);

        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = approverUserId;
        ICommandHandler<DecideApprovalCommand> decide = sp.GetRequiredService<ICommandHandler<DecideApprovalCommand>>();
        Assert.True((await decide.HandleAsync(new DecideApprovalCommand(approvalId, ApprovalDecision.Approved, Comment: null))).IsSuccess);

        Result<ApprovalRequestOutcome> afterDecision = await requester.RequestAsync(request, CancellationToken.None);
        Assert.True(afterDecision.IsSuccess);
        Assert.Equal(ApprovalDecision.Approved, afterDecision.Value!.Decision);
    }

    [Fact]
    public async Task RequestingWithAnIdOwnedByAnotherSubject_IsRejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string requesterUserId = await RegisterUserAsync(sp);
        string approverUserId = await RegisterUserAsync(sp);
        var approvalId = Guid.NewGuid();

        IApprovalRequester requester = sp.GetRequiredService<IApprovalRequester>();
        Assert.True((await requester.RequestAsync(new ApprovalRequest(
            approvalId, ApprovalSubjectType.GateCondition, Guid.NewGuid(), ApprovalType.ManagerApproval,
            requesterUserId, [new ApproverSpec(ApproverKind.Employee, approverUserId)], DueAtUtc: null),
            CancellationToken.None)).IsSuccess);

        Result<ApprovalRequestOutcome> other = await requester.RequestAsync(new ApprovalRequest(
            approvalId, ApprovalSubjectType.GateCondition, Guid.NewGuid(), ApprovalType.ManagerApproval,
            requesterUserId, [new ApproverSpec(ApproverKind.Employee, approverUserId)], DueAtUtc: null),
            CancellationToken.None);

        Assert.True(other.IsFailure);
        Assert.Equal(Domain.ApprovalErrors.ApprovalIdInUse, other.Error);
    }

    private static async Task<string> RegisterUserAsync(IServiceProvider sp)
    {
        string suffix = Guid.NewGuid().ToString("N");
        string email = $"user-{suffix}@example.com";

        return await IdentityTestSeeding.ProvisionActiveUserAsync(sp, email);
    }
}
