using BuildingBlocks.Pagination;
using Crm.Contracts.Items;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>A-13 (B-L4): items by period and by task are paged (1..200, default 50), newest first.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task A13_ItemsByPeriodAndByTask_ArePaged_WithTheTotalCount()
    {
        BriefTestProject p = await SeedAsync("a13-paging");
        Guid periodId = await OpenPeriodAsync(p, p.StageId);
        for (int i = 0; i < 3; i++)
        {
            await CreateItemAsync(p, periodId, p.TaskId);
        }

        Result<PagedResult<CrmRequirementItemResponse>> first = await QueryFor<GetCrmRequirementItemsByPeriodQuery, PagedResult<CrmRequirementItemResponse>>(
            p.OwnerAppUserId, new GetCrmRequirementItemsByPeriodQuery(periodId, p.OwnerAppUserId, PageNumber: 1, PageSize: 2));
        Result<PagedResult<CrmRequirementItemResponse>> second = await QueryFor<GetCrmRequirementItemsByPeriodQuery, PagedResult<CrmRequirementItemResponse>>(
            p.OwnerAppUserId, new GetCrmRequirementItemsByPeriodQuery(periodId, p.OwnerAppUserId, PageNumber: 2, PageSize: 2));
        Result<PagedResult<CrmRequirementItemResponse>> byTask = await QueryFor<GetCrmRequirementItemsByTaskQuery, PagedResult<CrmRequirementItemResponse>>(
            p.OwnerAppUserId, new GetCrmRequirementItemsByTaskQuery(p.TaskId, p.OwnerAppUserId, PageNumber: 1, PageSize: 2));

        Assert.Equal((2, 3), (first.Value!.Items.Count, first.Value.TotalCount));
        Assert.Equal((1, 3), (second.Value!.Items.Count, second.Value.TotalCount));
        Assert.Empty(first.Value.Items.Select(i => i.Id).Intersect(second.Value.Items.Select(i => i.Id)));
        Assert.Equal((2, 3), (byTask.Value!.Items.Count, byTask.Value.TotalCount));

        AssertError(Plain(await QueryFor<GetCrmRequirementItemsByPeriodQuery, PagedResult<CrmRequirementItemResponse>>(
            p.OwnerAppUserId, new GetCrmRequirementItemsByPeriodQuery(periodId, p.OwnerAppUserId, PageSize: 201))), 400);
        AssertError(Plain(await QueryFor<GetCrmRequirementItemsByTaskQuery, PagedResult<CrmRequirementItemResponse>>(
            p.OwnerAppUserId, new GetCrmRequirementItemsByTaskQuery(p.TaskId, p.OwnerAppUserId, PageNumber: 0))), 400);
    }
}
