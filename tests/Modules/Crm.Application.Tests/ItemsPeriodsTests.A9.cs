using Crm.Contracts;
using Crm.Contracts.Items;
using Crm.Contracts.Periods;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;
using RequirementCategory = Crm.Contracts.RequirementCategory;

namespace Crm.Application.Tests;

/// <summary>A-9 (B-M6): items and periods carry an <c>xmin</c> token, so the losing side of a race is a 409
/// instead of a silent last-write-wins. Each test holds a stale copy across another request's commit —
/// exactly the window two concurrent requests share — and saves it through ItemWrites.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task A9_AStaleExtend_AfterAConcurrentClose_Is409_AndTheCloseStands()
    {
        BriefTestProject p = await SeedAsync("a9-period");
        Guid periodId = await OpenPeriodAsync(p);

        await using AsyncServiceScope stale = fixture.CreateScope();
        CrmDbContext staleDb = stale.ServiceProvider.GetRequiredService<CrmDbContext>();
        CrmRequirementPeriod period = (await staleDb.CrmRequirementPeriods.FindAsync(periodId))!;

        Assert.True((await Command(p.OwnerAppUserId, new CloseCrmRequirementPeriodCommand(periodId, p.OwnerAppUserId))).IsSuccess);

        Assert.True(period.Extend(DateTime.UtcNow.AddDays(30)).IsSuccess);
        AssertError(await ItemWrites.SaveAsync(staleDb, CancellationToken.None, CrmPeriodErrors.ConcurrentUpdate), 409, "crm.periods.ConcurrentUpdate");

        Result<CrmRequirementPeriodResponse> reloaded = await QueryFor<GetCrmRequirementPeriodByIdQuery, CrmRequirementPeriodResponse>(
            p.OwnerAppUserId, new GetCrmRequirementPeriodByIdQuery(periodId, p.OwnerAppUserId));
        Assert.Equal(Contracts.RequirementPeriodStatus.Closed, reloaded.Value!.Status);
    }

    [Fact]
    public async Task A9_AStaleItemEdit_AfterAConcurrentEdit_Is409()
    {
        BriefTestProject p = await SeedAsync("a9-item");
        Guid periodId = await OpenPeriodAsync(p);
        Guid itemId = await CreateItemAsync(p, periodId);

        await using AsyncServiceScope stale = fixture.CreateScope();
        CrmDbContext staleDb = stale.ServiceProvider.GetRequiredService<CrmDbContext>();
        CrmRequirementItem item = (await staleDb.CrmRequirementItems.FindAsync(itemId))!;

        Assert.True((await Command(p.OwnerAppUserId, new UpdateCrmRequirementItemCommand(
            itemId, RequirementCategory.Style, "First edit", RequirementPriority.Low, null, null, null, null, null, null, null, null,
            p.OwnerAppUserId))).IsSuccess);

        Assert.True(item.Update(Domain.RequirementCategory.Style, "Second edit", TaskPriority.Low, null, null, null, null, null, null, null, null).IsSuccess);
        AssertError(await ItemWrites.SaveAsync(staleDb, CancellationToken.None), 409, "crm.items.ConcurrentUpdate");
        Assert.Equal("First edit", (await LoadItemAsync(itemId)).Title);
    }
}
