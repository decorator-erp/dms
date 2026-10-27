using Crm.Contracts;
using Crm.Contracts.Items;
using SharedKernel;
using Xunit;
using RequirementCategory = Crm.Contracts.RequirementCategory;

namespace Crm.Application.Tests;

/// <summary>A-6 (B-M3): an item can be superseded once — the ledger cannot fork.</summary>
public sealed partial class ItemsPeriodsTests
{
    private static SupersedeCrmRequirementItemCommand Supersede(BriefTestProject p, Guid itemId, string title = "Correction") =>
        new(itemId, p.EmployeeId, RequirementCategory.Style, title, RequirementPriority.Low, p.OwnerAppUserId);

    [Fact]
    public async Task A6_SupersedingTheSameItemTwice_IsOne409()
    {
        BriefTestProject p = await SeedAsync("a6-twice");
        Guid periodId = await OpenPeriodAsync(p);
        Guid lockedId = await CreateLockedItemAsync(p, periodId);

        Result<Guid> first = await CommandFor<SupersedeCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, Supersede(p, lockedId));
        Result<Guid> second = await CommandFor<SupersedeCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, Supersede(p, lockedId, "Again"));

        Assert.True(first.IsSuccess, first.Error?.Description);
        AssertError(Plain(second), 409, "crm.items.AlreadySuperseded");
    }

    [Fact]
    public async Task A6_ConcurrentSupersedes_ExactlyOneWins()
    {
        BriefTestProject p = await SeedAsync("a6-race");
        Guid periodId = await OpenPeriodAsync(p);
        Guid lockedId = await CreateLockedItemAsync(p, periodId);

        Result<Guid>[] results = await Task.WhenAll(
            CommandFor<SupersedeCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, Supersede(p, lockedId, "A")),
            CommandFor<SupersedeCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, Supersede(p, lockedId, "B")));

        Assert.Single(results, r => r.IsSuccess);
        AssertError(Plain(Assert.Single(results, r => r.IsFailure)), 409, "crm.items.AlreadySuperseded");
    }
}
