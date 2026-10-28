using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Briefs;
using Crm.Contracts.DesignProfile;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>C-2 (LC-M1): the design profile is project-scoped and reads the latest brief, not whichever
/// row the database returns first.</summary>
public sealed partial class ClientScopeTests
{
    [Fact]
    public async Task C2_TheDesignProfile_Is404_OffTheProject_AndReadsTheLatestBrief()
    {
        BriefTestProject p;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            p = await BriefTestSeeding.CreateProjectWithStageAsync(scope.ServiceProvider, "c2-profile", stageCount: 2);
        }

        await CreateBriefAsync(p, p.StageIds[0], "Classic");
        await CreateBriefAsync(p, p.StageIds[1], "Modern");
        (_, string outsider) = await HireEmployeeAsync("c2-outsider");

        AssertError(Plain(await QueryFor<GetClientDesignProfileQuery, ClientDesignProfileResponse>(outsider,
            new GetClientDesignProfileQuery(p.ProjectId, outsider))), 404);

        Result<ClientDesignProfileResponse> profile = await QueryFor<GetClientDesignProfileQuery, ClientDesignProfileResponse>(
            p.OwnerAppUserId, new GetClientDesignProfileQuery(p.ProjectId, p.OwnerAppUserId), ProjectsPermissions.Projects.ReadAll);
        AssertOk(Plain(profile));
        Assert.Equal("Modern", profile.Value!.PreferredStyle);
    }

    private async Task CreateBriefAsync(BriefTestProject p, Guid stageId, string style)
    {
        await using AsyncServiceScope scope = ScopeFor(p.OwnerAppUserId, ProjectsPermissions.Projects.ReadAll);
        Result<Guid> brief = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>()
            .HandleAsync(new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, stageId,
                new CrmBriefHeadData((UnitType)1, (UnitCondition)1, PreferredStyle: style), p.OwnerAppUserId));
        AssertOk(Plain(brief));
    }
}
