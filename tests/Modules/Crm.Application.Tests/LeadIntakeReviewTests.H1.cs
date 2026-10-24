using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Leads;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

public sealed partial class LeadIntakeReviewTests
{
    // --- H1: the residential block is persisted, updated, kept and removed with the sector ---

    private static readonly CrmLeadIntakeResidentialData FullResidential = new(
        UnitType.Villa, UnitCondition.SemiFinished, IsLicensed: true, IsRented: false,
        FinishingPurpose.TurnkeyDelivery);

    [Fact]
    public async Task IntakeSave_with_residential_round_trips_every_residential_field()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid leadId = await CreateLeadAsync(sp);

        Result saved = await SaveIntakeAsync(sp, leadId, new CrmLeadIntakeData(
            Sector: ProjectSector.Residential, Residential: FullResidential));
        Assert.True(saved.IsSuccess, saved.IsFailure ? saved.Error.ToString() : null);

        CrmLeadIntakeResidentialData? residential = (await GetIntakeAsync(sp, leadId)).Value!.Data.Residential;
        Assert.Equal(FullResidential, residential);
    }

    [Fact]
    public async Task IntakeSave_updating_residential_returns_the_new_values()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid leadId = await CreateLeadAsync(sp);
        Assert.True((await SaveIntakeAsync(sp, leadId, new CrmLeadIntakeData(
            Sector: ProjectSector.Residential, Residential: FullResidential))).IsSuccess);

        var changed = new CrmLeadIntakeResidentialData(
            UnitType.Apartment, UnitCondition.Renovation, IsLicensed: false, IsRented: true,
            FinishingPurpose.Restoration);
        Result saved = await SaveIntakeAsync(sp, leadId, new CrmLeadIntakeData(
            Sector: ProjectSector.Residential, Residential: changed));
        Assert.True(saved.IsSuccess, saved.IsFailure ? saved.Error.ToString() : null);

        Assert.Equal(changed, (await GetIntakeAsync(sp, leadId)).Value!.Data.Residential);
    }

    [Fact]
    public async Task IntakeSave_without_residential_on_a_residential_lead_leaves_the_child_unchanged()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid leadId = await CreateLeadAsync(sp);
        Assert.True((await SaveIntakeAsync(sp, leadId, new CrmLeadIntakeData(
            Sector: ProjectSector.Residential, Residential: FullResidential))).IsSuccess);

        Result saved = await SaveIntakeAsync(sp, leadId, new CrmLeadIntakeData(
            JobTitle: "Engineer", Sector: ProjectSector.Residential));
        Assert.True(saved.IsSuccess, saved.IsFailure ? saved.Error.ToString() : null);

        CrmLeadIntakeData data = (await GetIntakeAsync(sp, leadId)).Value!.Data;
        Assert.Equal("Engineer", data.JobTitle);
        Assert.Equal(FullResidential, data.Residential);
    }

    [Fact]
    public async Task IntakeSave_switching_sector_away_from_residential_removes_the_child()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid leadId = await CreateLeadAsync(sp);
        Assert.True((await SaveIntakeAsync(sp, leadId, new CrmLeadIntakeData(
            Sector: ProjectSector.Residential, Residential: FullResidential))).IsSuccess);

        Result saved = await SaveIntakeAsync(sp, leadId, new CrmLeadIntakeData(Sector: ProjectSector.Medical));
        Assert.True(saved.IsSuccess, saved.IsFailure ? saved.Error.ToString() : null);

        CrmLeadIntakeData data = (await GetIntakeAsync(sp, leadId)).Value!.Data;
        Assert.Equal(ProjectSector.Medical, data.Sector);
        Assert.Null(data.Residential);
        await using AsyncServiceScope check = fixture.CreateScope();
        CrmDbContext db = check.ServiceProvider.GetRequiredService<CrmDbContext>();
        Guid intakeId = await db.CrmLeadIntakes.Where(i => i.LeadId == leadId).Select(i => i.Id).SingleAsync();
        Assert.False(await db.CrmLeadIntakeResidentials.AnyAsync(r => r.LeadIntakeId == intakeId));
    }

    [Fact]
    public async Task IntakeSave_with_residential_on_a_non_residential_sector_is_rejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Guid leadId = await CreateLeadAsync(sp);

        Result result = await SaveIntakeAsync(sp, leadId, new CrmLeadIntakeData(
            Sector: ProjectSector.Medical, Residential: FullResidential));

        Assert.True(result.IsFailure);
        Assert.Equal(400, result.Error.Status);
        Assert.Contains("crm.leads.ResidentialWithoutSector", ErrorCodes(result.Error));
        Assert.False(await IntakeExistsAsync(leadId));
    }
}
