using Approvals.Contracts;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Approvals.Application.Tests;

/// <summary>D-16 / A-F5 (CRM review): two parallel requests under the same caller-supplied id — a CRM resend
/// of an orphaned send racing itself — both get the one approval back, never a duplicate-key 500.</summary>
[Collection(ApprovalsApplicationCollection.Name)]
public sealed class ApprovalRequestRaceTests(ApprovalsApplicationFixture fixture)
{
    [Fact]
    public async Task TwoParallelRequests_WithTheSameId_BothReturnTheOneApproval()
    {
        string requester;
        string approver;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            requester = await IdentityTestSeeding.ProvisionActiveUserAsync(scope.ServiceProvider, $"race-{Guid.NewGuid():N}@example.com");
            approver = await IdentityTestSeeding.ProvisionActiveUserAsync(scope.ServiceProvider, $"race-{Guid.NewGuid():N}@example.com");
        }

        for (int round = 0; round < 10; round++)
        {
            var request = new ApprovalRequest(
                Guid.NewGuid(), ApprovalSubjectType.RequirementItem, Guid.NewGuid(), ApprovalType.ClientReview,
                requester, [new ApproverSpec(ApproverKind.Client, approver)], DueAtUtc: null);

            Result<ApprovalRequestOutcome>[] results = await Task.WhenAll(RequestAsync(request), RequestAsync(request));

            foreach (Result<ApprovalRequestOutcome> result in results)
            {
                Assert.True(result.IsSuccess, result.Error?.Description);
                Assert.Equal(request.ApprovalId, result.Value!.ApprovalId);
                Assert.Null(result.Value.Decision);
            }
        }
    }

    private async Task<Result<ApprovalRequestOutcome>> RequestAsync(ApprovalRequest request)
    {
        await Task.Yield();
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IApprovalRequester>().RequestAsync(request, CancellationToken.None);
    }
}
