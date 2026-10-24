using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Briefs;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using SharedKernel;
using Xunit;
using CrmBriefHeadData = Crm.Contracts.Briefs.CrmBriefHeadData;
using CrmLeadIntakeData = Crm.Contracts.Leads.CrmLeadIntakeData;
using PastExperience = Crm.Contracts.PastExperience;
using FinishingPurpose = Crm.Contracts.FinishingPurpose;
using LeadSource = Crm.Contracts.LeadSource;
using ProjectSector = Crm.Contracts.ProjectSector;
using UnitCondition = Crm.Contracts.UnitCondition;
using UnitType = Crm.Contracts.UnitType;

namespace Crm.Application.Tests;

public sealed partial class LeadIntakeReviewTests
{
    // --- H2: the converted lead's intake seeds the project's first brief, copy-once, request wins ---

    private static readonly CrmLeadIntakeData SeedingIntake = new(
        Sector: ProjectSector.Residential, UnitAddress: "Intake unit address", AreaSqm: 140m,
        UsedDesignerBefore: true, DesignerExperience: PastExperience.Bad, DesignerExperienceReason: "late",
        Residential: new CrmLeadIntakeResidentialData(
            UnitType.Duplex, UnitCondition.BareBrick, IsLicensed: true, IsRented: false,
            FinishingPurpose.FinishingWithKitchen));

    /// <summary>No unit type or condition: on create, an omitted (default) value is a blank to seed.</summary>
    private static readonly CrmBriefHeadData BlankHead = new(default, default);

    [Fact]
    public async Task FirstBrief_is_seeded_from_the_converted_leads_intake()
    {
        BriefTestProject p = await SeedProjectWithConvertedLeadAsync("seed-first", SeedingIntake);

        Result<Guid> created = await CreateBriefAsync(p, p.StageId, BlankHead);

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        CrmDesignBrief brief = await LoadBriefAsync(created.Value);
        Assert.Equal(Domain.ProjectSector.Residential, brief.Sector);
        Assert.Equal(140m, brief.AreaSqm);
        Assert.Equal("Intake unit address", brief.Address);
        Assert.True(brief.UsedDesignerBefore);
        Assert.Equal(Domain.PastExperience.Bad, brief.DesignerExperience);
        Assert.Equal("late", brief.DesignerExperienceReason);
        Assert.Equal(Domain.UnitType.Duplex, brief.UnitType);
        Assert.Equal(Domain.UnitCondition.BareBrick, brief.UnitCondition);
        Assert.True(brief.IsLicensed);
        Assert.False(brief.IsRented);
        Assert.Equal(Domain.FinishingPurpose.FinishingWithKitchen, brief.FinishingPurpose);
    }

    [Fact]
    public async Task FirstBrief_keeps_the_office_entered_values_over_the_intake()
    {
        BriefTestProject p = await SeedProjectWithConvertedLeadAsync("seed-request-wins", SeedingIntake);

        Result<Guid> created = await CreateBriefAsync(p, p.StageId, new CrmBriefHeadData(
            UnitType.Studio, UnitCondition.Renovation, Address: "Office address", AreaSqm: 90m));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        CrmDesignBrief brief = await LoadBriefAsync(created.Value);
        Assert.Equal("Office address", brief.Address);
        Assert.Equal(90m, brief.AreaSqm);
        Assert.Equal(Domain.UnitType.Studio, brief.UnitType);
        Assert.Equal(Domain.UnitCondition.Renovation, brief.UnitCondition);
        // Blanks are still filled.
        Assert.Equal(Domain.ProjectSector.Residential, brief.Sector);
        Assert.Equal(Domain.FinishingPurpose.FinishingWithKitchen, brief.FinishingPurpose);
    }

    [Fact]
    public async Task A_brief_for_a_second_stage_is_not_seeded()
    {
        BriefTestProject p = await SeedProjectWithConvertedLeadAsync("seed-second-stage", SeedingIntake, stageCount: 2);
        Assert.True((await CreateBriefAsync(p, p.StageIds[0], BlankHead)).IsSuccess);

        Result<Guid> second = await CreateBriefAsync(p, p.StageIds[1], new CrmBriefHeadData(
            UnitType.Apartment, UnitCondition.SemiFinished));

        Assert.True(second.IsSuccess, second.IsFailure ? second.Error.ToString() : null);
        CrmDesignBrief brief = await LoadBriefAsync(second.Value);
        Assert.Null(brief.Sector);
        Assert.Null(brief.Address);
        Assert.Null(brief.AreaSqm);
        Assert.Null(brief.FinishingPurpose);
        Assert.Equal(Domain.UnitType.Apartment, brief.UnitType);
    }

    [Fact]
    public async Task A_brief_for_a_client_without_a_converted_lead_is_unchanged()
    {
        BriefTestProject p;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            p = await BriefTestSeeding.CreateProjectWithStageAsync(scope.ServiceProvider, "seed-no-lead");
        }

        Result<Guid> created = await CreateBriefAsync(p, p.StageId, new CrmBriefHeadData(
            UnitType.Villa, UnitCondition.BareBrick));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        CrmDesignBrief brief = await LoadBriefAsync(created.Value);
        Assert.Null(brief.Sector);
        Assert.Null(brief.Address);
        Assert.Null(brief.AreaSqm);
        Assert.Equal(Domain.UnitType.Villa, brief.UnitType);
    }

    [Fact]
    public async Task A_first_brief_with_omitted_unit_details_and_no_intake_to_seed_from_is_400()
    {
        BriefTestProject p;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            p = await BriefTestSeeding.CreateProjectWithStageAsync(scope.ServiceProvider, "seed-blank-no-lead");
        }

        Result<Guid> created = await CreateBriefAsync(p, p.StageId, BlankHead);

        Assert.True(created.IsFailure);
        Assert.Equal(400, created.Error.Status);
        Assert.Contains("crm.briefs.UnitDetailsRequired", ErrorCodes(created.Error));
    }

    private async Task<BriefTestProject> SeedProjectWithConvertedLeadAsync(
        string label, CrmLeadIntakeData intake, int stageCount = 1)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, label, stageCount);

        Result<Guid> lead = await sp.GetRequiredService<ICommandHandler<CreateCrmLeadCommand, Guid>>()
            .HandleAsync(new CreateCrmLeadCommand("Seed", label, LeadSource.Referral, p.EmployeeId));
        Assert.True(lead.IsSuccess, lead.IsFailure ? lead.Error.ToString() : null);
        Result saved = await SaveIntakeAsync(sp, lead.Value, intake);
        Assert.True(saved.IsSuccess, saved.IsFailure ? saved.Error.ToString() : null);
        Result<Guid> converted = await ConvertAsync(sp, lead.Value, p.ClientId);
        Assert.True(converted.IsSuccess, converted.IsFailure ? converted.Error.ToString() : null);
        return p;
    }

    private async Task<Result<Guid>> CreateBriefAsync(BriefTestProject p, Guid stageId, CrmBriefHeadData data)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = p.OwnerAppUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>()
            .HandleAsync(new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, stageId, data, p.OwnerAppUserId));
    }

    private async Task<CrmDesignBrief> LoadBriefAsync(Guid briefId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CrmDbContext>()
            .CrmDesignBriefs.AsNoTracking().SingleAsync(b => b.Id == briefId);
    }
}
