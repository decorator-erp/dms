using BuildingBlocks.Messaging;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;
using CrmLeadIntakeData = Crm.Contracts.Leads.CrmLeadIntakeData;

namespace Crm.Application.Tests;

public sealed partial class LeadIntakeReviewTests
{
    // --- L1: lead races are 409s, not 500s or double events ---

    [Fact]
    public async Task A_lost_update_on_a_lead_is_ConcurrentUpdate()
    {
        Guid leadId;
        await using (AsyncServiceScope seed = fixture.CreateScope())
        {
            leadId = await CreateLeadAsync(seed.ServiceProvider);
        }

        await using AsyncServiceScope first = fixture.CreateScope();
        await using AsyncServiceScope second = fixture.CreateScope();
        CrmDbContext a = first.ServiceProvider.GetRequiredService<CrmDbContext>();
        CrmDbContext b = second.ServiceProvider.GetRequiredService<CrmDbContext>();
        CrmLead leadA = await a.CrmLeads.SingleAsync(l => l.Id == leadId);
        CrmLead leadB = await b.CrmLeads.SingleAsync(l => l.Id == leadId);
        Assert.True(leadA.ChangeStatus(Domain.LeadStatus.Contacted).IsSuccess);
        Assert.True(leadB.ChangeStatus(Domain.LeadStatus.Qualified).IsSuccess);

        Assert.True((await LeadWrites.SaveAsync(a, CancellationToken.None)).IsSuccess);
        Result lost = await LeadWrites.SaveAsync(b, CancellationToken.None);

        Assert.Equal(CrmLeadErrors.ConcurrentUpdate, lost.Error);
        Assert.Equal(409, lost.Error.Status);
    }

    [Fact]
    public async Task A_second_first_intake_for_one_lead_is_ConcurrentUpdate_not_a_unique_violation_500()
    {
        Guid leadId;
        await using (AsyncServiceScope seed = fixture.CreateScope())
        {
            leadId = await CreateLeadAsync(seed.ServiceProvider);
        }

        await using AsyncServiceScope first = fixture.CreateScope();
        await using AsyncServiceScope second = fixture.CreateScope();
        CrmDbContext a = first.ServiceProvider.GetRequiredService<CrmDbContext>();
        CrmDbContext b = second.ServiceProvider.GetRequiredService<CrmDbContext>();
        a.CrmLeadIntakes.Add(CrmLeadIntake.Create(leadId, new Domain.CrmLeadIntakeData(JobTitle: "A")).Value!);
        b.CrmLeadIntakes.Add(CrmLeadIntake.Create(leadId, new Domain.CrmLeadIntakeData(JobTitle: "B")).Value!);

        Assert.True((await LeadWrites.SaveAsync(a, CancellationToken.None)).IsSuccess);
        Result lost = await LeadWrites.SaveAsync(b, CancellationToken.None);

        Assert.Equal(CrmLeadErrors.ConcurrentUpdate, lost.Error);
        await using AsyncServiceScope check = fixture.CreateScope();
        Assert.Equal(1, await check.ServiceProvider.GetRequiredService<CrmDbContext>()
            .CrmLeadIntakes.CountAsync(i => i.LeadId == leadId));
    }

    [Fact]
    public async Task Two_parallel_converts_convert_once_and_publish_once()
    {
        for (int round = 0; round < 5; round++)
        {
            Guid leadId;
            Guid clientId;
            await using (AsyncServiceScope seed = fixture.CreateScope())
            {
                leadId = await CreateLeadAsync(seed.ServiceProvider);
                clientId = await CreateClientAsync(seed.ServiceProvider);
            }

            Result<Guid>[] results = await Task.WhenAll(ConvertInOwnScopeAsync(leadId, clientId), ConvertInOwnScopeAsync(leadId, clientId));

            output.WriteLine($"convert race {round}: loser = {results.FirstOrDefault(r => r.IsFailure)?.Error.Code ?? "none"}");
            Assert.Single(results, r => r.IsSuccess);
            Result<Guid> loser = Assert.Single(results, r => r.IsFailure);
            Assert.True(
                loser.Error == CrmLeadErrors.ConcurrentUpdate || loser.Error == CrmLeadErrors.AlreadyConverted,
                loser.Error.ToString());
            Assert.Equal(409, loser.Error.Status);
            Assert.Equal(1, await CountLeadConvertedEventsAsync(leadId));
        }
    }

    [Fact]
    public async Task Two_parallel_first_intake_saves_leave_one_intake_and_never_a_500()
    {
        for (int round = 0; round < 5; round++)
        {
            Guid leadId;
            await using (AsyncServiceScope seed = fixture.CreateScope())
            {
                leadId = await CreateLeadAsync(seed.ServiceProvider);
            }

            Result[] results = await Task.WhenAll(
                SaveIntakeInOwnScopeAsync(leadId, new CrmLeadIntakeData(JobTitle: "One")),
                SaveIntakeInOwnScopeAsync(leadId, new CrmLeadIntakeData(JobTitle: "Two")));

            output.WriteLine($"intake race {round}: {string.Join(", ", results.Select(r => r.IsSuccess ? "ok" : r.Error.Code))}");
            Assert.Contains(results, r => r.IsSuccess);
            Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal(CrmLeadErrors.ConcurrentUpdate, r.Error));
            await using AsyncServiceScope check = fixture.CreateScope();
            Assert.Equal(1, await check.ServiceProvider.GetRequiredService<CrmDbContext>()
                .CrmLeadIntakes.CountAsync(i => i.LeadId == leadId));
        }
    }

    private async Task<Result<Guid>> ConvertInOwnScopeAsync(Guid leadId, Guid clientId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await ConvertAsync(scope.ServiceProvider, leadId, clientId);
    }

    private async Task<Result> SaveIntakeInOwnScopeAsync(Guid leadId, CrmLeadIntakeData data)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await SaveIntakeAsync(scope.ServiceProvider, leadId, data);
    }
}
