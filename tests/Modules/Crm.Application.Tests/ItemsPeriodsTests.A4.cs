using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using BuildingBlocks.Messaging;
using Crm.Application.Features.Briefs;
using Crm.Contracts;
using Crm.Contracts.Briefs;
using Crm.Contracts.Items;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;
using ApprovalDecision = Approvals.Contracts.ApprovalDecision;
using RequirementCategory = Crm.Contracts.RequirementCategory;

namespace Crm.Application.Tests;

/// <summary>A-4 (B-M1, decided): a Rejected item or brief-space entry stays unresolved for the items gate.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task A4_RejectingTheLastPendingItem_LeavesTheGateUnresolved_AndApprovingTheCorrectionResolvesIt()
    {
        BriefTestProject p = await SeedAsync("a4-items");
        Guid periodId = await OpenPeriodAsync(p, p.StageId);
        Guid itemId = await CreateItemAsync(p, periodId);

        Assert.True((await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId))).IsSuccess);
        await DecideOpenApprovalAsync(p, itemId, ApprovalDecision.Rejected);

        Assert.Equal(1, await CountUnresolvedForStageAsync(p.StageId));
        Assert.Equal(0, await CountOutboxAsync(nameof(CrmItemsResolvedIntegrationEvent), p.StageId));

        Assert.True((await Command(p.OwnerAppUserId, new UpdateCrmRequirementItemCommand(
            itemId, RequirementCategory.Materials, "Corrected", RequirementPriority.High, null, null, null,
            null, null, null, null, null, p.OwnerAppUserId))).IsSuccess);
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId))).IsSuccess);
        await DecideOpenApprovalAsync(p, itemId, ApprovalDecision.Approved);

        Assert.Equal(0, await CountUnresolvedForStageAsync(p.StageId));
        Assert.Equal(1, await CountOutboxAsync(nameof(CrmItemsResolvedIntegrationEvent), p.StageId));
    }

    [Fact]
    public async Task A4_ARejectedBriefSpaceEntry_CountsAsUnresolved()
    {
        BriefTestProject p = await SeedAsync("a4-entry");
        Result<Guid> brief = await CommandFor<CreateCrmDesignBriefCommand, Guid>(p.OwnerAppUserId,
            new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, p.StageId,
                new Contracts.Briefs.CrmBriefHeadData((Contracts.UnitType)1, (Contracts.UnitCondition)1), p.OwnerAppUserId));
        Result<Guid> entry = await CommandFor<CreateCrmBriefSpaceCommand, Guid>(p.OwnerAppUserId,
            new CreateCrmBriefSpaceCommand(brief.Value, p.EmployeeId, p.OwnerAppUserId, SpaceId: null, Requirements: "light"));
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmBriefSpaceCommand(entry.Value, p.OwnerAppUserId))).IsSuccess);

        await DecideAsync(p, CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, entry.Value, 1), ApprovalDecision.Rejected);

        Assert.Equal(1, await CountUnresolvedForStageAsync(p.StageId));
        Assert.Equal(0, await CountOutboxAsync(nameof(CrmItemsResolvedIntegrationEvent), p.StageId));
    }

    private async Task<int> CountUnresolvedForStageAsync(Guid stageId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICrmGateReader>().CountUnresolvedForStageAsync(stageId, CancellationToken.None);
    }

    /// <summary>The client decides the subject's one open approval (whatever its id), then the outbox runs.</summary>
    private async Task DecideOpenApprovalAsync(BriefTestProject p, Guid subjectId, ApprovalDecision decision)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using Npgsql.NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """SELECT "Id" FROM approvals.approvals WHERE "SubjectId" = @id AND "Decision" IS NULL""";
        command.Parameters.AddWithValue("id", subjectId);
        var approvalId = (Guid)(await command.ExecuteScalarAsync())!;
        await DecideAsync(p, approvalId, decision);
    }

    private async Task DecideAsync(BriefTestProject p, Guid approvalId, ApprovalDecision decision)
    {
        await using (AsyncServiceScope scope = ScopeFor(p.ClientAppUserId, readAll: false))
        {
            Result decided = await scope.ServiceProvider.GetRequiredService<ICommandHandler<DecideApprovalCommand>>()
                .HandleAsync(new DecideApprovalCommand(approvalId, decision, decision == ApprovalDecision.Approved ? null : "Not quite."));
            Assert.True(decided.IsSuccess, decided.Error?.Description);
        }

        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
    }
}
