using Crm.Contracts.Leads;
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
    // --- F2: an intake save and a convert of the same lead serialize on the lead row ---

    private static readonly TimeSpan BlockedFor = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task An_intake_save_waits_for_an_uncommitted_convert_then_is_refused()
    {
        (Guid leadId, Guid clientId) = await SeedLeadAndClientAsync();

        await using AsyncServiceScope converting = fixture.CreateScope();
        CrmDbContext db = converting.ServiceProvider.GetRequiredService<CrmDbContext>();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        CrmLead lead = await db.CrmLeads.SingleAsync(l => l.Id == leadId);
        Assert.True(lead.Convert(clientId).IsSuccess);
        await db.SaveChangesAsync();

        Task<Result> save = SaveIntakeInOwnScopeAsync(leadId, new CrmLeadIntakeData(JobTitle: "Too late"));
        Assert.NotSame(save, await Task.WhenAny(save, Task.Delay(BlockedFor)));

        await transaction.CommitAsync();
        Result result = await save;

        Assert.Equal(CrmLeadErrors.AlreadyConverted, result.Error);
        Assert.False(await IntakeExistsAsync(leadId));
    }

    [Fact]
    public async Task A_convert_waits_for_a_lock_held_the_way_the_intake_save_takes_it_then_succeeds()
    {
        (Guid leadId, Guid clientId) = await SeedLeadAndClientAsync();

        await using AsyncServiceScope saving = fixture.CreateScope();
        CrmDbContext db = saving.ServiceProvider.GetRequiredService<CrmDbContext>();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        Assert.NotNull(await db.CrmLeads
            .FromSql($"SELECT *, xmin FROM crm.crm_leads WHERE \"Id\" = {leadId} FOR UPDATE")
            .SingleOrDefaultAsync());

        Task<Result<Guid>> convert = ConvertInOwnScopeAsync(leadId, clientId);
        Assert.NotSame(convert, await Task.WhenAny(convert, Task.Delay(BlockedFor)));

        await transaction.CommitAsync();
        Result<Guid> result = await convert;

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
    }

    [Fact]
    public async Task A_convert_and_a_first_intake_save_in_parallel_never_leave_an_intake_written_after_conversion()
    {
        for (int round = 0; round < 10; round++)
        {
            (Guid leadId, Guid clientId) = await SeedLeadAndClientAsync();

            Task<Result<Guid>> convert = ConvertInOwnScopeAsync(leadId, clientId);
            Task<Result> save = SaveIntakeInOwnScopeAsync(leadId, new CrmLeadIntakeData(JobTitle: $"Round {round}"));
            await Task.WhenAll(convert, save);
            Result<Guid> converted = await convert;
            Result saved = await save;

            bool intakeExists = await IntakeExistsAsync(leadId);
            output.WriteLine($"convert/intake race {round}: convert {(converted.IsSuccess ? "ok" : converted.Error.Code)}, "
                + $"save {(saved.IsSuccess ? "ok" : saved.Error.Code)}, intake {(intakeExists ? "written" : "absent")}");

            Assert.True(converted.IsSuccess, converted.IsFailure ? converted.Error.ToString() : null);
            if (saved.IsSuccess)
            {
                // The save held the lead row first, so the convert waited for the intake to commit.
                Assert.True(intakeExists);
            }
            else
            {
                Assert.Equal(CrmLeadErrors.AlreadyConverted, saved.Error);
                Assert.False(intakeExists);
            }
        }
    }

    private async Task<(Guid LeadId, Guid ClientId)> SeedLeadAndClientAsync()
    {
        await using AsyncServiceScope seed = fixture.CreateScope();
        return (await CreateLeadAsync(seed.ServiceProvider), await CreateClientAsync(seed.ServiceProvider));
    }
}
