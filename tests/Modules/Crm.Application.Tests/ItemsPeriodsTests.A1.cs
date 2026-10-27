using Crm.Contracts;
using Crm.Contracts.Items;
using Crm.Contracts.Periods;
using Crm.Contracts.Spaces;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>A-1 (X-4): every office item, period and space message is project-scoped.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task A1_EmployeeOffTheStageTeam_WithoutReadAll_Gets404_OnEveryItemPeriodAndSpaceRoute()
    {
        BriefTestProject p = await SeedAsync("a1-outsider");
        Guid periodId = await OpenPeriodAsync(p, p.StageId);
        Guid itemId = await CreateItemAsync(p, periodId, p.TaskId);
        Guid spaceId = await CreateSpaceAsync(p);
        Guid typeId = await CreateSpaceTypeAsync();
        (Guid outsiderEmployeeId, string outsider) = await HireEmployeeAsync("a1-outsider");

        Result[] results =
        [
            Plain(await CommandFor<OpenCrmRequirementPeriodCommand, Guid>(outsider,
                new OpenCrmRequirementPeriodCommand(p.ProjectId, null, DateTime.UtcNow.AddDays(3), outsiderEmployeeId, outsider))),
            await Command(outsider, new ExtendCrmRequirementPeriodCommand(periodId, DateTime.UtcNow.AddDays(30), outsider)),
            await Command(outsider, new CloseCrmRequirementPeriodCommand(periodId, outsider)),
            Plain(await QueryFor<GetCrmRequirementPeriodByIdQuery, CrmRequirementPeriodResponse>(outsider,
                new GetCrmRequirementPeriodByIdQuery(periodId, outsider))),
            Plain(await QueryFor<GetCrmRequirementPeriodsByProjectQuery, IReadOnlyList<CrmRequirementPeriodResponse>>(outsider,
                new GetCrmRequirementPeriodsByProjectQuery(p.ProjectId, outsider))),
            Plain(await CommandFor<CreateCrmRequirementItemCommand, Guid>(outsider,
                Item(p, periodId) with { EmployeeId = outsiderEmployeeId, UserId = outsider })),
            await Command(outsider, new UpdateCrmRequirementItemCommand(
                itemId, RequirementCategory.Style, "x", RequirementPriority.Low, null, null, null, null, null, null, null, null, outsider)),
            await Command(outsider, new SendCrmRequirementItemCommand(itemId, outsider)),
            Plain(await CommandFor<SupersedeCrmRequirementItemCommand, Guid>(outsider, new SupersedeCrmRequirementItemCommand(
                itemId, outsiderEmployeeId, RequirementCategory.Style, "x", RequirementPriority.Low, outsider))),
            Plain(await QueryFor<GetCrmRequirementItemsByPeriodQuery, IReadOnlyList<CrmRequirementItemResponse>>(outsider,
                new GetCrmRequirementItemsByPeriodQuery(periodId, outsider))),
            Plain(await QueryFor<GetCrmRequirementItemsByTaskQuery, IReadOnlyList<CrmRequirementItemResponse>>(outsider,
                new GetCrmRequirementItemsByTaskQuery(p.TaskId, outsider))),
            Plain(await CommandFor<CreateCrmSpaceCommand, Guid>(outsider, new CreateCrmSpaceCommand(p.ProjectId, typeId, "Sneaky", outsider))),
            await Command(outsider, new UpdateCrmSpaceCommand(spaceId, typeId, "Renamed", null, outsider)),
            await Command(outsider, new SetCrmSpaceActiveCommand(spaceId, false, outsider)),
            Plain(await QueryFor<GetCrmSpacesByProjectQuery, IReadOnlyList<CrmSpaceResponse>>(outsider,
                new GetCrmSpacesByProjectQuery(p.ProjectId, outsider))),
        ];

        Assert.All(results, r => AssertError(r, 404));
        Assert.Equal("Matte flooring", (await LoadItemAsync(itemId)).Title);
    }

    [Fact]
    public async Task A1_ReadAll_OrStageTeamMembership_GetsThrough()
    {
        (Guid headEmployeeId, _) = await HireEmployeeAsync("a1-head");
        BriefTestProject p = await SeedAsync("a1-member", departmentHeadEmployeeId: headEmployeeId);
        Guid periodId = await OpenPeriodAsync(p);
        (Guid memberEmployeeId, string member) = await HireEmployeeAsync("a1-member");
        await using (Microsoft.Extensions.DependencyInjection.AsyncServiceScope scope = fixture.CreateScope())
        {
            await BriefTestSeeding.AddStageTeamMemberAsync(scope.ServiceProvider, fixture.ConnectionString, p, p.StageId, memberEmployeeId);
        }

        (_, string reader) = await HireEmployeeAsync("a1-reader");

        Result<Guid> byMember = await CommandFor<CreateCrmRequirementItemCommand, Guid>(member,
            Item(p, periodId) with { EmployeeId = memberEmployeeId, UserId = member }, readAll: false);
        Assert.True(byMember.IsSuccess, byMember.Error?.Description);

        Result<IReadOnlyList<CrmRequirementItemResponse>> byReader = await QueryFor<GetCrmRequirementItemsByPeriodQuery, IReadOnlyList<CrmRequirementItemResponse>>(
            reader, new GetCrmRequirementItemsByPeriodQuery(periodId, reader), readAll: true);
        Assert.True(byReader.IsSuccess, byReader.Error?.Description);
        Assert.Single(byReader.Value!);

        Result<IReadOnlyList<CrmSpaceResponse>> spaces = await QueryFor<GetCrmSpacesByProjectQuery, IReadOnlyList<CrmSpaceResponse>>(
            member, new GetCrmSpacesByProjectQuery(p.ProjectId, member), readAll: false);
        Assert.True(spaces.IsSuccess, spaces.Error?.Description);
    }
}
