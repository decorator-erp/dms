using BuildingBlocks.Pagination;
using Crm.Application.Features.Clients;
using Crm.Contracts;
using Crm.Contracts.Clients;
using Crm.Infrastructure.Decorators;
using Crm.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-16 / C-F8: a <c>crm.clients.read_all</c> caller's page is paged in SQL — no unbounded read of
/// every matching client id first — and still answers the same page and total.</summary>
public sealed partial class ClientScopeTests
{
    [Fact]
    public async Task D16_C8_AReadAllCaller_IsPagedInSql_WithTheSameTotal()
    {
        await SeedClientWithoutProjectAsync("d16-c8-a");
        await SeedClientWithoutProjectAsync("d16-c8-b");
        string office = CrmTestAccess.OfficeUserId;
        var query = new GetClientsQuery(office, PageNumber: 1, PageSize: 1, IsActive: null);

        List<string> commands = [];
        DbContextOptions<CrmDbContext> options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .LogTo(commands.Add, [RelationalEventId.CommandExecuted], LogLevel.Information)
            .Options;

        Result<PagedResult<ClientListItemResponse>> page;
        await using (AsyncServiceScope scope = ScopeFor(office, Office))
        await using (var db = new CrmDbContext(options))
        {
            IServiceProvider sp = scope.ServiceProvider;
            page = await new GetClientsQueryHandler(db, sp.GetRequiredService<CrmClientScope>(), sp.GetRequiredService<IUserDirectory>())
                .HandleAsync(query);
        }

        AssertOk(Plain(page));
        Assert.Single(page.Value!.Items);
        Assert.True(page.Value.TotalCount >= 2);
        Assert.Equal(page.Value.TotalCount, (await QueryFor<GetClientsQuery, PagedResult<ClientListItemResponse>>(office,
            query with { PageSize = 200 }, Office)).Value!.TotalCount);

        // Every read of crm.clients is either the page (LIMIT) or the count — never every id.
        Assert.All(commands.Where(c => c.Contains("FROM crm.clients", StringComparison.Ordinal)),
            c => Assert.True(c.Contains("LIMIT", StringComparison.Ordinal) || c.Contains("count(*)", StringComparison.OrdinalIgnoreCase), c));
    }
}
