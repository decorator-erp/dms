using Approvals.Contracts;
using BuildingBlocks.Pagination;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Approvals.Application.Tests;

/// <summary>B1.3 (C5): the client inbox is cut in SQL on the Approvals side — Client-kind entries for the caller
/// only, the requested subject types only, pending newest request first, decided newest decision first.</summary>
[Collection(ApprovalsApplicationCollection.Name)]
public sealed class ClientApprovalReaderTests(ApprovalsApplicationFixture fixture)
{
    private static readonly ApprovalSubjectType[] Crm =
        [ApprovalSubjectType.DesignBrief, ApprovalSubjectType.RequirementItem, ApprovalSubjectType.BriefSpace];

    [Fact]
    public async Task PendingAndDecided_AreTheCallersClientRowsOnly_NewestFirst_PagedWithATotal()
    {
        string requester = await UserAsync();
        string client = await UserAsync();
        string otherClient = await UserAsync();
        Guid first = await RequestAsync(requester, ApprovalSubjectType.DesignBrief, ApproverKind.Client, client);
        Guid second = await RequestAsync(requester, ApprovalSubjectType.BriefSpace, ApproverKind.Client, client);
        Guid third = await RequestAsync(requester, ApprovalSubjectType.RequirementItem, ApproverKind.Client, client);
        await RequestAsync(requester, ApprovalSubjectType.GateCondition, ApproverKind.Client, client);
        await RequestAsync(requester, ApprovalSubjectType.DesignBrief, ApproverKind.Employee, client);
        await RequestAsync(requester, ApprovalSubjectType.DesignBrief, ApproverKind.Client, otherClient);
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IApprovalDecider decider = scope.ServiceProvider.GetRequiredService<IApprovalDecider>();
            Assert.True((await decider.DecideAsClientAsync(first, client, ApprovalDecision.Approved, null, CancellationToken.None)).IsSuccess);
        }

        PagedResult<ClientApprovalRow> pending = await ReadAsync(client, decided: false, 1, 50);
        PagedResult<ClientApprovalRow> pageOne = await ReadAsync(client, decided: false, 1, 1);
        PagedResult<ClientApprovalRow> decided = await ReadAsync(client, decided: true, 1, 50);

        Assert.Equal([third, second], pending.Items.Select(r => r.Id));
        Assert.Equal((2, third), (pageOne.TotalCount, Assert.Single(pageOne.Items).Id));
        ClientApprovalRow row = Assert.Single(decided.Items);
        Assert.Equal((first, ApprovalDecision.Approved, client), (row.Id, row.Decision, row.DecidedByUserId));
        Assert.NotNull(row.DecidedAtUtc);
    }

    private async Task<string> UserAsync()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await IdentityTestSeeding.ProvisionActiveUserAsync(scope.ServiceProvider, $"reader-{Guid.NewGuid():N}@example.com");
    }

    private async Task<Guid> RequestAsync(string requester, ApprovalSubjectType type, ApproverKind kind, string approver)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<ApprovalRequestOutcome> requested = await scope.ServiceProvider.GetRequiredService<IApprovalRequester>().RequestAsync(
            new ApprovalRequest(Guid.NewGuid(), type, Guid.NewGuid(), ApprovalType.ClientReview, requester,
                [new ApproverSpec(kind, approver)], DueAtUtc: null),
            CancellationToken.None);
        Assert.True(requested.IsSuccess, requested.Error?.Description);
        return requested.Value!.ApprovalId;
    }

    private async Task<PagedResult<ClientApprovalRow>> ReadAsync(string client, bool decided, int pageNumber, int pageSize)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IClientApprovalReader>()
            .GetForClientApproverAsync(client, Crm, decided, pageNumber, pageSize, CancellationToken.None);
    }
}
