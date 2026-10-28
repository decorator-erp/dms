using Crm.Contracts;
using Crm.Contracts.Items;
using Crm.Contracts.Periods;
using SharedKernel;
using Xunit;
using RequirementCategory = Crm.Contracts.RequirementCategory;

namespace Crm.Application.Tests;

/// <summary>A-5 (B-M2): once a period is closed (its gate may have fired), its items are frozen.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task A5_CreateUpdateAndSupersede_OnAClosedPeriod_Are409()
    {
        BriefTestProject p = await SeedAsync("a5-closed");
        Guid periodId = await OpenPeriodAsync(p);
        Guid draftId = await CreateItemAsync(p, periodId);
        Guid lockedId = await CreateLockedItemAsync(p, periodId);
        Assert.True((await Command(p.OwnerAppUserId, new CloseCrmRequirementPeriodCommand(periodId, p.OwnerAppUserId))).IsSuccess);

        Result<Guid> created = await CommandFor<CreateCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, Item(p, periodId));
        AssertError(Plain(created), 409, "crm.periods.NotOpen");

        Result updated = await Command(p.OwnerAppUserId, new UpdateCrmRequirementItemCommand(
            draftId, RequirementCategory.Style, "Late edit", RequirementPriority.Low, null, null, null, null, null, null, null, null, p.OwnerAppUserId));
        AssertError(updated, 409, "crm.periods.NotOpen");

        Result<Guid> superseded = await CommandFor<SupersedeCrmRequirementItemCommand, Guid>(p.OwnerAppUserId,
            new SupersedeCrmRequirementItemCommand(lockedId, p.EmployeeId, RequirementCategory.Style, "Late fix", RequirementPriority.Low, p.OwnerAppUserId));
        AssertError(Plain(superseded), 409, "crm.periods.NotOpen");
    }
}
