using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Contracts;
using Crm.Contracts.Leads;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

public sealed partial class LeadIntakeReviewTests
{
    // --- GAP-1: lead counts per status, under the list's owner and search filters ---

    [Fact]
    public async Task StatusCounts_count_a_seeded_set_per_status()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string token = $"cnt{Guid.NewGuid():N}";
        await SeedStatusSetAsync(sp, token, await SeedEmployeeAsync(sp, "counts-owner"));

        CrmLeadStatusCountsResponse counts = await CountsAsync(sp, new GetCrmLeadStatusCountsQuery(Search: token));

        Assert.Equal(new CrmLeadStatusCountsResponse(Total: 7, New: 2, Contacted: 1, Qualified: 3, Unqualified: 0, Converted: 1), counts);
    }

    [Fact]
    public async Task StatusCounts_respect_the_owner_and_search_filters()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string token = $"own{Guid.NewGuid():N}";
        Guid owner = await SeedEmployeeAsync(sp, "counts-a");
        Guid other = await SeedEmployeeAsync(sp, "counts-b");
        await CreateLeadAsync(sp, token, "Alpha", owner);
        Guid qualified = await CreateLeadAsync(sp, token, "Beta", owner);
        await ChangeStatusAsync(sp, qualified, LeadStatus.Unqualified);
        await CreateLeadAsync(sp, token, "Gamma", other);

        Assert.Equal(new CrmLeadStatusCountsResponse(2, 1, 0, 0, 1, 0),
            await CountsAsync(sp, new GetCrmLeadStatusCountsQuery(owner, token)));
        Assert.Equal(new CrmLeadStatusCountsResponse(1, 1, 0, 0, 0, 0),
            await CountsAsync(sp, new GetCrmLeadStatusCountsQuery(other, token)));
        Assert.Equal(new CrmLeadStatusCountsResponse(1, 0, 0, 0, 1, 0),
            await CountsAsync(sp, new GetCrmLeadStatusCountsQuery(owner, "Beta")));
        Assert.Equal(0, (await CountsAsync(sp, new GetCrmLeadStatusCountsQuery(Search: $"{token}-nothing"))).Total);
    }

    [Fact]
    public async Task StatusCounts_agree_with_the_list()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string token = $"agr{Guid.NewGuid():N}";
        Guid owner = await SeedEmployeeAsync(sp, "counts-agree");
        await SeedStatusSetAsync(sp, token, owner);

        CrmLeadStatusCountsResponse counts = await CountsAsync(sp, new GetCrmLeadStatusCountsQuery(owner, token));
        IQueryHandler<GetCrmLeadsQuery, PagedResult<CrmLeadResponse>> list =
            sp.GetRequiredService<IQueryHandler<GetCrmLeadsQuery, PagedResult<CrmLeadResponse>>>();

        Assert.Equal(counts.Total, (await list.HandleAsync(new GetCrmLeadsQuery(OwnerId: owner, Search: token))).Value!.TotalCount);
        foreach ((LeadStatus status, int count) in new[]
                 {
                     (LeadStatus.New, counts.New), (LeadStatus.Contacted, counts.Contacted),
                     (LeadStatus.Qualified, counts.Qualified), (LeadStatus.Unqualified, counts.Unqualified),
                     (LeadStatus.Converted, counts.Converted),
                 })
        {
            PagedResult<CrmLeadResponse> page = (await list.HandleAsync(
                new GetCrmLeadsQuery(Status: status, OwnerId: owner, Search: token))).Value!;
            Assert.Equal(count, page.TotalCount);
        }
    }

    /// <summary>Seven leads whose last name holds <paramref name="token"/>: 2 New, 1 Contacted, 3 Qualified,
    /// 1 Converted.</summary>
    private static async Task SeedStatusSetAsync(IServiceProvider sp, string token, Guid owner)
    {
        LeadStatus?[] statuses = [null, null, LeadStatus.Contacted, LeadStatus.Qualified, LeadStatus.Qualified, LeadStatus.Qualified];
        foreach (LeadStatus? status in statuses)
        {
            Guid lead = await CreateLeadAsync(sp, "Count", token, owner);
            if (status is { } s)
            {
                await ChangeStatusAsync(sp, lead, s);
            }
        }

        Guid converted = await CreateLeadAsync(sp, "Converted", token, owner);
        Assert.True((await ConvertAsync(sp, converted, await CreateClientAsync(sp))).IsSuccess);
    }

    private static async Task ChangeStatusAsync(IServiceProvider sp, Guid leadId, LeadStatus status) =>
        Assert.True((await sp.GetRequiredService<ICommandHandler<ChangeCrmLeadStatusCommand>>()
            .HandleAsync(new ChangeCrmLeadStatusCommand(leadId, status))).IsSuccess);

    private static async Task<CrmLeadStatusCountsResponse> CountsAsync(IServiceProvider sp, GetCrmLeadStatusCountsQuery query)
    {
        Result<CrmLeadStatusCountsResponse> result = await sp
            .GetRequiredService<IQueryHandler<GetCrmLeadStatusCountsQuery, CrmLeadStatusCountsResponse>>()
            .HandleAsync(query);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value!;
    }
}
