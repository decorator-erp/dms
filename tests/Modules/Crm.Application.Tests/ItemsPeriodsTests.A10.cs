using Approvals.Contracts;
using Crm.Contracts.Items;
using Crm.Domain;
using Crm.Infrastructure.IntegrationEvents;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel;
using Xunit;
using ApprovalDecision = Approvals.Contracts.ApprovalDecision;
using ApprovalState = Crm.Domain.ApprovalState;

namespace Crm.Application.Tests;

/// <summary>A-10 (B-M7): an item send is bound to one approval round — the brief-space pattern.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task A10_AnOrphanedSend_WhoseApprovalNeverCommitted_CanBeSentAgain()
    {
        BriefTestProject p = await SeedAsync("a10-orphan");
        Guid periodId = await OpenPeriodAsync(p);
        Guid itemId = await CreateItemAsync(p, periodId);
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId))).IsSuccess);

        // Approvals rolled back while CRM committed: the item is Pending on an approval that does not exist.
        await ExecuteSqlAsync($"""DELETE FROM approvals.approvals WHERE "SubjectId" = '{itemId}'""");

        Result resent = await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId));

        Assert.True(resent.IsSuccess, resent.Error?.Description);
        Assert.Equal(1, await CountApprovalsForClientAsync(itemId, p.ClientAppUserId));
        await DecideOpenApprovalAsync(p, itemId, ApprovalDecision.Approved);
        Assert.Equal(ApprovalState.Approved, (await LoadItemAsync(itemId)).ApprovalStatus);
    }

    [Fact]
    public async Task A10_ADecisionOnAnApprovalTheItemIsNotBoundTo_IsIgnored()
    {
        BriefTestProject p = await SeedAsync("a10-stale");
        Guid periodId = await OpenPeriodAsync(p);
        Guid itemId = await CreateItemAsync(p, periodId);
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId))).IsSuccess);

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            CrmDbContext db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var handler = new ApprovalDecidedIntegrationEventHandler(
                db, scope.ServiceProvider.GetRequiredService<CrmApprovalOutcomes>(),
                NullLogger<ApprovalDecidedIntegrationEventHandler>.Instance);
            await handler.HandleAsync(new ApprovalDecidedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), ApprovalSubjectType.RequirementItem, itemId, ApprovalDecision.Approved,
                "someone", DateTimeOffset.UtcNow), CancellationToken.None);
            await db.SaveChangesAsync();
        }

        Assert.Equal(ApprovalState.PendingApproval, (await LoadItemAsync(itemId)).ApprovalStatus);
    }

    [Fact]
    public async Task A10_ConcurrentSends_OneSucceeds_TheOtherIs409_AndOneApprovalExists()
    {
        BriefTestProject p = await SeedAsync("a10-race");
        Guid periodId = await OpenPeriodAsync(p);
        Guid itemId = await CreateItemAsync(p, periodId);

        Result[] results = await Task.WhenAll(
            Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId)),
            Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId)));

        Assert.Single(results, r => r.IsSuccess);
        AssertError(Assert.Single(results, r => r.IsFailure), 409);
        Assert.Equal(1, await CountApprovalsForClientAsync(itemId, p.ClientAppUserId));
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using Npgsql.NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
