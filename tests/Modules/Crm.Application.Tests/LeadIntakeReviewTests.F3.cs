using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using SharedKernel;
using Xunit;
using CrmBriefHeadData = Crm.Contracts.Briefs.CrmBriefHeadData;
using ProjectSector = Crm.Contracts.ProjectSector;
using UnitCondition = Crm.Contracts.UnitCondition;
using UnitType = Crm.Contracts.UnitType;

namespace Crm.Application.Tests;

public sealed partial class LeadIntakeReviewTests
{
    // --- F3: the brief head carries an optional Sector; later briefs inherit the project's latest one ---

    [Fact]
    public async Task FirstBrief_with_a_sector_in_the_request_keeps_it_over_the_intakes()
    {
        BriefTestProject p = await SeedProjectWithConvertedLeadAsync("sector-request-wins", SeedingIntake);

        Result<Guid> created = await CreateBriefAsync(p, p.StageId, BlankHead with { Sector = ProjectSector.Medical });

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        CrmDesignBrief brief = await LoadBriefAsync(created.Value);
        Assert.Equal(Domain.ProjectSector.Medical, brief.Sector);
        // The other blanks are still seeded from the intake.
        Assert.Equal(140m, brief.AreaSqm);
    }

    [Fact]
    public async Task FirstBrief_without_a_sector_takes_the_intakes()
    {
        BriefTestProject p = await SeedProjectWithConvertedLeadAsync("sector-from-intake", SeedingIntake);

        Result<Guid> created = await CreateBriefAsync(p, p.StageId, BlankHead);

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        Assert.Equal(Domain.ProjectSector.Residential, (await LoadBriefAsync(created.Value)).Sector);
    }

    [Fact]
    public async Task A_later_stage_brief_without_a_sector_inherits_the_projects_latest_brief_sector()
    {
        BriefTestProject p;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            p = await BriefTestSeeding.CreateProjectWithStageAsync(scope.ServiceProvider, "sector-inherit", stageCount: 3);
        }

        Assert.True((await CreateBriefAsync(p, p.StageIds[0], new CrmBriefHeadData(
            UnitType.Villa, UnitCondition.BareBrick, Sector: ProjectSector.Medical))).IsSuccess);
        Result<Guid> second = await CreateBriefAsync(p, p.StageIds[1], new CrmBriefHeadData(
            UnitType.Villa, UnitCondition.BareBrick, Sector: ProjectSector.Residential));
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error.ToString() : null);

        Result<Guid> third = await CreateBriefAsync(p, p.StageIds[2], new CrmBriefHeadData(
            UnitType.Villa, UnitCondition.BareBrick));

        Assert.True(third.IsSuccess, third.IsFailure ? third.Error.ToString() : null);
        // The most recently created brief (stage 2's) is the source, not the first.
        Assert.Equal(Domain.ProjectSector.Residential, (await LoadBriefAsync(third.Value)).Sector);
    }

    [Fact]
    public async Task A_brief_for_a_client_without_a_converted_lead_stores_the_requested_sector()
    {
        BriefTestProject p;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            p = await BriefTestSeeding.CreateProjectWithStageAsync(scope.ServiceProvider, "sector-no-lead");
        }

        Result<Guid> created = await CreateBriefAsync(p, p.StageId, new CrmBriefHeadData(
            UnitType.Villa, UnitCondition.BareBrick, Sector: ProjectSector.Medical));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        CrmDesignBrief brief = await LoadBriefAsync(created.Value);
        Assert.Equal(Domain.ProjectSector.Medical, brief.Sector);
        Assert.Equal(ProjectSector.Medical, (await GetBriefByStageAsync(p, p.StageId)).Head.Sector);
    }

    [Fact]
    public async Task Update_with_a_sector_replaces_it_and_without_one_keeps_it()
    {
        BriefTestProject p;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            p = await BriefTestSeeding.CreateProjectWithStageAsync(scope.ServiceProvider, "sector-update");
        }

        var head = new CrmBriefHeadData(UnitType.Villa, UnitCondition.BareBrick, Sector: ProjectSector.Medical);
        Result<Guid> created = await CreateBriefAsync(p, p.StageId, head);
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);

        Result replaced = await UpdateBriefHeadAsync(p, created.Value, head with { Sector = ProjectSector.Residential });
        Assert.True(replaced.IsSuccess, replaced.IsFailure ? replaced.Error.ToString() : null);
        Assert.Equal(Domain.ProjectSector.Residential, (await LoadBriefAsync(created.Value)).Sector);

        Result kept = await UpdateBriefHeadAsync(p, created.Value, head with { Sector = null, Address = "Edited" });
        Assert.True(kept.IsSuccess, kept.IsFailure ? kept.Error.ToString() : null);
        CrmDesignBrief brief = await LoadBriefAsync(created.Value);
        Assert.Equal(Domain.ProjectSector.Residential, brief.Sector);
        Assert.Equal("Edited", brief.Address);
    }

    [Fact]
    public async Task An_undefined_sector_is_400_on_create_and_on_update()
    {
        BriefTestProject p;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            p = await BriefTestSeeding.CreateProjectWithStageAsync(scope.ServiceProvider, "sector-undefined");
        }

        var head = new CrmBriefHeadData(UnitType.Villa, UnitCondition.BareBrick);
        Result<Guid> bad = await CreateBriefAsync(p, p.StageId, head with { Sector = (ProjectSector)99 });
        Assert.True(bad.IsFailure);
        Assert.Equal(400, bad.Error.Status);

        Result<Guid> created = await CreateBriefAsync(p, p.StageId, head);
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        Result badUpdate = await UpdateBriefHeadAsync(p, created.Value, head with { Sector = (ProjectSector)99 });
        Assert.True(badUpdate.IsFailure);
        Assert.Equal(400, badUpdate.Error.Status);
    }

    private async Task<Result> UpdateBriefHeadAsync(BriefTestProject p, Guid briefId, CrmBriefHeadData data)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = p.OwnerAppUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateCrmBriefHeadCommand>>()
            .HandleAsync(new UpdateCrmBriefHeadCommand(briefId, data, p.OwnerAppUserId));
    }

    private async Task<CrmDesignBriefResponse> GetBriefByStageAsync(BriefTestProject p, Guid stageId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = p.OwnerAppUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);
        Result<CrmDesignBriefResponse> result = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetCrmBriefByStageQuery, CrmDesignBriefResponse>>()
            .HandleAsync(new GetCrmBriefByStageQuery(stageId, p.OwnerAppUserId));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value!;
    }
}
