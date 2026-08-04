using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using BuildingBlocks.Messaging;
using Identity.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;
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
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var requesterUserId = await RegisterUserAsync(sp);
        var approverUserId = await RegisterUserAsync(sp);

        var requester = sp.GetRequiredService<IApprovalRequester>();
        var requestResult = await requester.RequestAsync(new ApprovalRequest(
            ApprovalSubjectType.GateCondition, Guid.NewGuid(), ApprovalType.ManagerApproval,
            requesterUserId, [new ApproverSpec(ApproverKind.Employee, approverUserId)], DueAtUtc: null),
            CancellationToken.None);
        Assert.True(requestResult.IsSuccess);
        var approvalId = requestResult.Value;

        var currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = approverUserId;

        var decide = sp.GetRequiredService<ICommandHandler<DecideApprovalCommand>>();
        var decideResult = await decide.HandleAsync(
            new DecideApprovalCommand(approvalId, ApprovalDecision.Approved, Comment: null));
        Assert.True(decideResult.IsSuccess);

        var snapshot = await requester.GetAsync(approvalId, CancellationToken.None);
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
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var requesterUserId = await RegisterUserAsync(sp);
        var approverUserId = await RegisterUserAsync(sp);
        var strangerUserId = await RegisterUserAsync(sp);

        var requester = sp.GetRequiredService<IApprovalRequester>();
        var requestResult = await requester.RequestAsync(new ApprovalRequest(
            ApprovalSubjectType.GateCondition, Guid.NewGuid(), ApprovalType.ManagerApproval,
            requesterUserId, [new ApproverSpec(ApproverKind.Employee, approverUserId)], DueAtUtc: null),
            CancellationToken.None);
        var approvalId = requestResult.Value;

        var currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = strangerUserId;

        var decide = sp.GetRequiredService<ICommandHandler<DecideApprovalCommand>>();
        var forbidden = await decide.HandleAsync(
            new DecideApprovalCommand(approvalId, ApprovalDecision.Approved, Comment: null));
        Assert.True(forbidden.IsFailure);
        Assert.Equal(Domain.ApprovalErrors.NotAssignedApprover, forbidden.Error);
        Assert.Equal(403, forbidden.Error.Status);

        currentUser.Grant(ApprovalsPermissions.DecideAny);
        var overridden = await decide.HandleAsync(
            new DecideApprovalCommand(approvalId, ApprovalDecision.Approved, Comment: null));
        Assert.True(overridden.IsSuccess);
    }

    [Fact]
    public async Task Decide_AnAlreadyDecidedApproval_IsRejectedAsConflict()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var requesterUserId = await RegisterUserAsync(sp);
        var approverUserId = await RegisterUserAsync(sp);

        var requester = sp.GetRequiredService<IApprovalRequester>();
        var requestResult = await requester.RequestAsync(new ApprovalRequest(
            ApprovalSubjectType.GateCondition, Guid.NewGuid(), ApprovalType.ManagerApproval,
            requesterUserId, [new ApproverSpec(ApproverKind.Employee, approverUserId)], DueAtUtc: null),
            CancellationToken.None);
        var approvalId = requestResult.Value;

        var currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = approverUserId;

        var decide = sp.GetRequiredService<ICommandHandler<DecideApprovalCommand>>();
        var first = await decide.HandleAsync(new DecideApprovalCommand(approvalId, ApprovalDecision.Approved, null));
        Assert.True(first.IsSuccess);

        var second = await decide.HandleAsync(new DecideApprovalCommand(approvalId, ApprovalDecision.Rejected, "changed my mind"));
        Assert.True(second.IsFailure);
        Assert.Equal(Domain.ApprovalErrors.AlreadyDecided, second.Error);
        Assert.Equal(409, second.Error.Status);
    }

    [Fact]
    public async Task RequestingASecondApproval_ForAnAlreadyOpenSubject_IsRejected()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var requesterUserId = await RegisterUserAsync(sp);
        var approverUserId = await RegisterUserAsync(sp);
        var subjectId = Guid.NewGuid();

        var requester = sp.GetRequiredService<IApprovalRequester>();
        var first = await requester.RequestAsync(new ApprovalRequest(
            ApprovalSubjectType.GateCondition, subjectId, ApprovalType.ManagerApproval,
            requesterUserId, [new ApproverSpec(ApproverKind.Employee, approverUserId)], DueAtUtc: null),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await requester.RequestAsync(new ApprovalRequest(
            ApprovalSubjectType.GateCondition, subjectId, ApprovalType.ManagerApproval,
            requesterUserId, [new ApproverSpec(ApproverKind.Employee, approverUserId)], DueAtUtc: null),
            CancellationToken.None);
        Assert.True(second.IsFailure);
        Assert.Equal(Domain.ApprovalErrors.OpenApprovalExists, second.Error);
    }

    private static async Task<string> RegisterUserAsync(IServiceProvider sp)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"user-{suffix}@example.com";

        var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var result = await register.HandleAsync(
            new RegisterCommand(new RegisterRequest(email, "P@ssw0rd123!", "Test", "User", null, "employee")));

        Assert.True(result.IsSuccess);
        return result.Value!;
    }
}
