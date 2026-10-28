using Crm.Contracts.Items;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-16 / A-F7: the scope's 404 names the target it couldn't resolve, by target kind — an item
/// created in an unknown period is "period not found", not "item not found".</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task D16_A7_AnItemCreate_InAnUnknownPeriod_IsPeriodNotFound()
    {
        BriefTestProject p = await SeedAsync("d16-a7");

        Result<Guid> created = await CommandFor<CreateCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, Item(p, Guid.NewGuid()));

        AssertError(Plain(created), 404, "crm.periods.NotFound");
    }
}
