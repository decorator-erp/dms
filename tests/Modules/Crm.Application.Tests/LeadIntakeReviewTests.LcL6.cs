using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;
using CrmLeadIntakeData = Crm.Contracts.Leads.CrmLeadIntakeData;

namespace Crm.Application.Tests;

public sealed partial class LeadIntakeReviewTests
{
    // --- LC-L6: a convert takes the same lead lock as the intake save, and reads the intake after it ---

    [Fact]
    public async Task A_convert_waits_for_an_uncommitted_intake_save_then_fills_the_client_from_the_new_intake()
    {
        (Guid leadId, Guid clientId) = await SeedLeadAndClientAsync();
        Assert.True((await SaveIntakeInOwnScopeAsync(leadId, new CrmLeadIntakeData(JobTitle: "Old title"))).IsSuccess);

        // Scope A: hold the lead lock the way the intake save takes it, and change the intake, uncommitted.
        await using AsyncServiceScope saving = fixture.CreateScope();
        CrmDbContext db = saving.ServiceProvider.GetRequiredService<CrmDbContext>();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        Assert.NotNull(await db.CrmLeads
            .FromSql($"SELECT *, xmin FROM crm.crm_leads WHERE \"Id\" = {leadId} FOR UPDATE")
            .SingleOrDefaultAsync());
        CrmLeadIntake intake = await db.CrmLeadIntakes.SingleAsync(i => i.LeadId == leadId);
        Assert.True(intake.Update(new Domain.CrmLeadIntakeData(JobTitle: "New title")).IsSuccess);
        await db.SaveChangesAsync();

        // Scope B: the convert must wait for scope A's lock.
        Task<Result<Guid>> convert = ConvertInOwnScopeAsync(leadId, clientId);
        Assert.NotSame(convert, await Task.WhenAny(convert, Task.Delay(BlockedFor)));

        await transaction.CommitAsync();
        Result<Guid> result = await convert;

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        await using AsyncServiceScope reading = fixture.CreateScope();
        Client client = await reading.ServiceProvider.GetRequiredService<CrmDbContext>().Clients.AsNoTracking()
            .SingleAsync(c => c.Id == clientId);
        Assert.Equal("New title", client.JobTitle);
    }
}
