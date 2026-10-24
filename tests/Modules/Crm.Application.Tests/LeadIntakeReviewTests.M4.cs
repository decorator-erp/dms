using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Application.Features.Leads;
using Crm.Contracts;
using Crm.Contracts.Leads;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

public sealed partial class LeadIntakeReviewTests
{
    // --- M4: the lead list is paged, filterable and searchable ---

    [Fact]
    public async Task LeadList_pages_newest_first_with_a_total_count()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid owner = await SeedEmployeeAsync(sp, "paging-owner");
        Guid oldest = await CreateLeadAsync(sp, "First", ownerId: owner);
        Guid middle = await CreateLeadAsync(sp, "Second", ownerId: owner);
        Guid newest = await CreateLeadAsync(sp, "Third", ownerId: owner);

        PagedResult<CrmLeadResponse> page1 = await ListLeadsAsync(sp, new GetCrmLeadsQuery(1, 2, OwnerId: owner));
        PagedResult<CrmLeadResponse> page2 = await ListLeadsAsync(sp, new GetCrmLeadsQuery(2, 2, OwnerId: owner));

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal([newest, middle], page1.Items.Select(l => l.Id));
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal([oldest], page2.Items.Select(l => l.Id));
    }

    [Fact]
    public async Task LeadList_filters_by_status_and_by_owner()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid owner = await SeedEmployeeAsync(sp, "filter-owner");
        Guid otherOwner = await SeedEmployeeAsync(sp, "filter-other");
        Guid qualified = await CreateLeadAsync(sp, ownerId: owner);
        Guid fresh = await CreateLeadAsync(sp, ownerId: owner);
        Guid others = await CreateLeadAsync(sp, ownerId: otherOwner);
        Assert.True((await sp.GetRequiredService<ICommandHandler<ChangeCrmLeadStatusCommand>>()
            .HandleAsync(new ChangeCrmLeadStatusCommand(qualified, LeadStatus.Qualified))).IsSuccess);

        PagedResult<CrmLeadResponse> byOwner = await ListLeadsAsync(sp, new GetCrmLeadsQuery(OwnerId: owner));
        Assert.Equal([fresh, qualified], byOwner.Items.Select(l => l.Id));

        PagedResult<CrmLeadResponse> byStatus = await ListLeadsAsync(sp, new GetCrmLeadsQuery(PageSize: 200, Status: LeadStatus.Qualified));
        Assert.Contains(byStatus.Items, l => l.Id == qualified);
        Assert.DoesNotContain(byStatus.Items, l => l.Id == fresh || l.Id == others);
        Assert.All(byStatus.Items, l => Assert.Equal(LeadStatus.Qualified, l.Status));
    }

    [Fact]
    public async Task LeadList_searches_first_name_last_name_email_and_phone_case_insensitively()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string token = Guid.NewGuid().ToString("N")[..10];
        Guid byFirst = await CreateLeadAsync(sp, firstName: $"Fn{token}");
        Guid byLast = await CreateLeadAsync(sp, lastName: $"Ln{token}");
        Guid byEmail = await CreateLeadAsync(sp, email: $"em{token}@example.com");
        Guid byPhone = await CreateLeadAsync(sp, phone: $"+20{token[..8]}");

        Assert.Equal([byFirst], await SearchIdsAsync(sp, $"FN{token.ToUpperInvariant()}"));
        Assert.Equal([byLast], await SearchIdsAsync(sp, $"ln{token}"));
        Assert.Equal([byEmail], await SearchIdsAsync(sp, $"EM{token}@EXAMPLE"));
        Assert.Equal([byPhone], await SearchIdsAsync(sp, $"+20{token[..8]}"));
        Assert.Equal(new HashSet<Guid> { byFirst, byLast, byEmail }, (await SearchIdsAsync(sp, token)).ToHashSet());
    }

    [Fact]
    public async Task LeadList_search_treats_like_wildcards_literally()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string token = Guid.NewGuid().ToString("N")[..10];
        Guid literal = await CreateLeadAsync(sp, firstName: $"{token}%off");
        await CreateLeadAsync(sp, firstName: $"{token}Xoff");

        Assert.Equal([literal], await SearchIdsAsync(sp, $"{token}%off"));
        Assert.Empty(await SearchIdsAsync(sp, $"{token}_off"));
    }

    [Fact]
    public async Task LeadList_with_a_page_size_over_200_is_400()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();

        Result<PagedResult<CrmLeadResponse>> result = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetCrmLeadsQuery, PagedResult<CrmLeadResponse>>>()
            .HandleAsync(new GetCrmLeadsQuery(PageSize: 201));

        AssertValidation400(result.Error, "PageSize");
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    public void LeadList_validator_rejects_out_of_range_paging(int pageNumber, int pageSize)
    {
        ValidationResult result = new GetCrmLeadsQueryValidator().Validate(new GetCrmLeadsQuery(pageNumber, pageSize));

        Assert.False(result.IsValid);
    }

    private static async Task<PagedResult<CrmLeadResponse>> ListLeadsAsync(IServiceProvider sp, GetCrmLeadsQuery query)
    {
        Result<PagedResult<CrmLeadResponse>> result = await sp
            .GetRequiredService<IQueryHandler<GetCrmLeadsQuery, PagedResult<CrmLeadResponse>>>().HandleAsync(query);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value!;
    }

    private static async Task<List<Guid>> SearchIdsAsync(IServiceProvider sp, string search) =>
        [.. (await ListLeadsAsync(sp, new GetCrmLeadsQuery(PageSize: 200, Search: search))).Items.Select(l => l.Id)];
}
