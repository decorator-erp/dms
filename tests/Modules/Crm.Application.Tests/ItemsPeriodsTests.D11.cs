using Crm.Contracts.Items;
using Crm.Contracts.Periods;
using SharedKernel;
using Xunit;
using ApprovalDecision = Approvals.Contracts.ApprovalDecision;

namespace Crm.Application.Tests;

/// <summary>D-11 (A-F2, owner default): a Closed period is frozen for sends too, and closing a period drops
/// its unsent and rejected requests from the items gate.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task D11_SendingADraftItem_InAClosedPeriod_Is409()
    {
        BriefTestProject p = await SeedAsync("d11-send");
        Guid periodId = await OpenPeriodAsync(p);
        Guid itemId = await CreateItemAsync(p, periodId);
        Assert.True((await Command(p.OwnerAppUserId, new CloseCrmRequirementPeriodCommand(periodId, p.OwnerAppUserId))).IsSuccess);

        AssertError(await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId)), 409, "crm.periods.NotOpen");
    }

    [Fact]
    public async Task D11_ARejectedOrDraftItem_InAClosedPeriod_DoesNotBlockTheGate()
    {
        BriefTestProject p = await SeedAsync("d11-gate");
        Guid periodId = await OpenPeriodAsync(p, p.StageId);
        Guid rejectedId = await CreateItemAsync(p, periodId);
        await CreateItemAsync(p, periodId);
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(rejectedId, p.OwnerAppUserId))).IsSuccess);
        await DecideOpenApprovalAsync(p, rejectedId, ApprovalDecision.Rejected);
        Assert.Equal(2, await CountUnresolvedForStageAsync(p.StageId));

        Assert.True((await Command(p.OwnerAppUserId, new CloseCrmRequirementPeriodCommand(periodId, p.OwnerAppUserId))).IsSuccess);

        Assert.Equal(0, await CountUnresolvedForStageAsync(p.StageId));
    }
}
