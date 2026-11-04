using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Approvals.Application.Tests;

/// <summary>FINDING-P1-2: the office decide path (<see cref="DecideApprovalCommand"/>, override via
/// <c>approvals.decide_any</c>) takes the same row lock as the portal's <see cref="IApprovalDecider"/>. Without
/// it, an office override and a client decision can both load the undecided row and both "win" — a lost
/// update. With it, exactly one succeeds and the other is <c>approvals.AlreadyDecided</c> (409).</summary>
[Collection(ApprovalsApplicationCollection.Name)]
public sealed class OfficeDecideRaceTests(ApprovalsApplicationFixture fixture)
{
    private const int Rounds = 10;

    [Fact]
    public async Task ClientDecide_And_OfficeOverride_InParallel_ExactlyOneWins_TheOtherIsAlreadyDecided()
    {
        for (int round = 0; round < Rounds; round++)
        {
            (string client, string office, Guid approvalId) = await RequestAsync();

            Result[] results = await Task.WhenAll(
                ClientDecideAsync(approvalId, client, ApprovalDecision.Approved),
                OfficeDecideAsync(approvalId, office, ApprovalDecision.ChangesRequested, "Office override"));

            AssertOneWinner(results);
        }
    }

    [Fact]
    public async Task TwoOfficeDecides_InParallel_ExactlyOneWins_TheOtherIsAlreadyDecided()
    {
        for (int round = 0; round < Rounds; round++)
        {
            (_, string office, Guid approvalId) = await RequestAsync();

            Result[] results = await Task.WhenAll(
                OfficeDecideAsync(approvalId, office, ApprovalDecision.Approved, null),
                OfficeDecideAsync(approvalId, office, ApprovalDecision.Rejected, "No"));

            AssertOneWinner(results);
        }
    }

    private static void AssertOneWinner(Result[] results)
    {
        Assert.Single(results, r => r.IsSuccess);
        Result loser = Assert.Single(results, r => r.IsFailure);
        Assert.Equal("approvals.AlreadyDecided", loser.Error.Code);
        Assert.Equal(409, loser.Error.Status);
    }

    private async Task<(string Client, string Office, Guid ApprovalId)> RequestAsync()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string requester = await IdentityTestSeeding.ProvisionActiveUserAsync(sp, $"race-{Guid.NewGuid():N}@example.com");
        string client = await IdentityTestSeeding.ProvisionActiveUserAsync(sp, $"race-{Guid.NewGuid():N}@example.com");
        string office = await IdentityTestSeeding.ProvisionActiveUserAsync(sp, $"race-{Guid.NewGuid():N}@example.com");
        Result<ApprovalRequestOutcome> requested = await sp.GetRequiredService<IApprovalRequester>().RequestAsync(
            new ApprovalRequest(
                Guid.NewGuid(), ApprovalSubjectType.DesignBrief, Guid.NewGuid(), ApprovalType.ClientReview,
                requester, [new ApproverSpec(ApproverKind.Client, client)], DueAtUtc: null),
            CancellationToken.None);
        Assert.True(requested.IsSuccess, requested.Error?.Description);
        return (client, office, requested.Value!.ApprovalId);
    }

    private async Task<Result> ClientDecideAsync(Guid approvalId, string client, ApprovalDecision decision)
    {
        await Task.Yield();
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IApprovalDecider>()
            .DecideAsClientAsync(approvalId, client, decision, null, CancellationToken.None);
    }

    private async Task<Result> OfficeDecideAsync(Guid approvalId, string office, ApprovalDecision decision, string? comment)
    {
        await Task.Yield();
        await using AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = office;
        currentUser.Grant(ApprovalsPermissions.DecideAny);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<DecideApprovalCommand>>()
            .HandleAsync(new DecideApprovalCommand(approvalId, decision, comment));
    }
}
