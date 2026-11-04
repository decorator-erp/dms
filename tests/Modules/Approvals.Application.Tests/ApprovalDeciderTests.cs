using Approvals.Contracts;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Approvals.Application.Tests;

/// <summary>B1.1: <see cref="IApprovalDecider"/> — the client's portal decision. Only a Client-kind approver
/// entry naming the caller may decide; anything else is a 404, and the row lock makes two parallel decisions
/// resolve to exactly one winner and one 409.</summary>
[Collection(ApprovalsApplicationCollection.Name)]
public sealed class ApprovalDeciderTests(ApprovalsApplicationFixture fixture)
{
    [Fact]
    public async Task ClientApprover_Approves_DecisionStored_AndDecidedEventEnqueuedInTheSameSave()
    {
        (string client, Guid approvalId, Guid subjectId) = await RequestAsync(ApproverKind.Client);

        Result result = await DecideAsync(approvalId, client, ApprovalDecision.Approved, null);

        Assert.True(result.IsSuccess, result.Error?.Description);
        await using AsyncServiceScope scope = fixture.CreateScope();
        ApprovalSnapshot snapshot = (await scope.ServiceProvider.GetRequiredService<IApprovalRequester>()
            .GetAsync(approvalId, CancellationToken.None))!;
        Assert.Equal(ApprovalDecision.Approved, snapshot.Decision);
        List<string> decided = await scope.ServiceProvider.GetRequiredService<SharedDbContext>().OutboxMessages
            .AsNoTracking().Where(m => m.Type.Contains(nameof(ApprovalDecidedIntegrationEvent)))
            .Select(m => m.Content).ToListAsync();
        Assert.Single(decided, c => c.Contains(approvalId.ToString(), StringComparison.OrdinalIgnoreCase)
            && c.Contains(subjectId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnknownApproval_Is404()
    {
        (string client, _, _) = await RequestAsync(ApproverKind.Client);

        Result result = await DecideAsync(Guid.NewGuid(), client, ApprovalDecision.Approved, null);

        Assert.Equal("approvals.NotFound", result.Error.Code);
        Assert.Equal(404, result.Error.Status);
    }

    [Fact]
    public async Task EmployeeKindApprover_Is404_EvenForTheSameUserId()
    {
        (string employee, Guid approvalId, _) = await RequestAsync(ApproverKind.Employee);

        Result result = await DecideAsync(approvalId, employee, ApprovalDecision.Approved, null);

        Assert.Equal("approvals.NotFound", result.Error.Code);
        Assert.Equal(404, result.Error.Status);
    }

    [Fact]
    public async Task AnotherClient_Is404()
    {
        (_, Guid approvalId, _) = await RequestAsync(ApproverKind.Client);
        (string otherClient, _, _) = await RequestAsync(ApproverKind.Client);

        Result result = await DecideAsync(approvalId, otherClient, ApprovalDecision.Approved, null);

        Assert.Equal(404, result.Error.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task ChangesRequested_WithoutAComment_Is400(string? comment)
    {
        (string client, Guid approvalId, _) = await RequestAsync(ApproverKind.Client);

        Result result = await DecideAsync(approvalId, client, ApprovalDecision.ChangesRequested, comment);

        Assert.Equal("approvals.CommentRequired", result.Error.Code);
        Assert.Equal(400, result.Error.Status);
    }

    [Fact]
    public async Task CommentOver2000_Is400()
    {
        (string client, Guid approvalId, _) = await RequestAsync(ApproverKind.Client);

        Result result = await DecideAsync(approvalId, client, ApprovalDecision.ChangesRequested, new string('x', 2001));

        Assert.Equal("approvals.CommentTooLong", result.Error.Code);
        Assert.Equal(400, result.Error.Status);
    }

    [Fact]
    public async Task AlreadyDecided_Is409()
    {
        (string client, Guid approvalId, _) = await RequestAsync(ApproverKind.Client);
        Assert.True((await DecideAsync(approvalId, client, ApprovalDecision.Approved, null)).IsSuccess);

        Result second = await DecideAsync(approvalId, client, ApprovalDecision.ChangesRequested, "Too late");

        Assert.Equal("approvals.AlreadyDecided", second.Error.Code);
        Assert.Equal(409, second.Error.Status);
    }

    [Fact]
    public async Task TwoParallelDecisions_ExactlyOneWins_TheOtherIs409()
    {
        for (int round = 0; round < 5; round++)
        {
            (string client, Guid approvalId, _) = await RequestAsync(ApproverKind.Client);

            Result[] results = await Task.WhenAll(
                DecideAsync(approvalId, client, ApprovalDecision.Approved, null),
                DecideAsync(approvalId, client, ApprovalDecision.ChangesRequested, "Change it"));

            Assert.Single(results, r => r.IsSuccess);
            Result loser = Assert.Single(results, r => r.IsFailure);
            Assert.Equal(409, loser.Error.Status);
        }
    }

    private async Task<(string ApproverUserId, Guid ApprovalId, Guid SubjectId)> RequestAsync(ApproverKind kind)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string requester = await IdentityTestSeeding.ProvisionActiveUserAsync(sp, $"decider-{Guid.NewGuid():N}@example.com");
        string approver = await IdentityTestSeeding.ProvisionActiveUserAsync(sp, $"decider-{Guid.NewGuid():N}@example.com");
        var subjectId = Guid.NewGuid();
        Result<ApprovalRequestOutcome> requested = await sp.GetRequiredService<IApprovalRequester>().RequestAsync(
            new ApprovalRequest(
                Guid.NewGuid(), ApprovalSubjectType.DesignBrief, subjectId, ApprovalType.ClientReview,
                requester, [new ApproverSpec(kind, approver)], DueAtUtc: null),
            CancellationToken.None);
        Assert.True(requested.IsSuccess, requested.Error?.Description);
        return (approver, requested.Value!.ApprovalId, subjectId);
    }

    private async Task<Result> DecideAsync(Guid approvalId, string appUserId, ApprovalDecision decision, string? comment)
    {
        await Task.Yield();
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IApprovalDecider>()
            .DecideAsClientAsync(approvalId, appUserId, decision, comment, CancellationToken.None);
    }
}
