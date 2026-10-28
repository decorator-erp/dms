using BuildingBlocks.Messaging;
using Crm.Contracts.Periods;
using Crm.Contracts.Spaces;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>A-7 (B-M4): the open-period, space-name and space-type-code unique rules are 409s, never 500s —
/// a pre-check for the sequential case, and the index mapped by ItemWrites/CatalogWrites for a race.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task A7_ASecondOpenPeriod_Is409_AlreadyOpen_EvenWhenRacing()
    {
        BriefTestProject p = await SeedAsync("a7-period");
        await OpenPeriodAsync(p);

        Result<Guid> second = await CommandFor<OpenCrmRequirementPeriodCommand, Guid>(p.OwnerAppUserId,
            new OpenCrmRequirementPeriodCommand(p.ProjectId, null, DateTime.UtcNow.AddDays(9), p.EmployeeId, p.OwnerAppUserId));
        AssertError(Plain(second), 409, "crm.periods.AlreadyOpen");

        BriefTestProject race = await SeedAsync("a7-period-race");
        Result<Guid>[] results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            CommandFor<OpenCrmRequirementPeriodCommand, Guid>(race.OwnerAppUserId,
                new OpenCrmRequirementPeriodCommand(race.ProjectId, null, DateTime.UtcNow.AddDays(9), race.EmployeeId, race.OwnerAppUserId))));
        Assert.Single(results, r => r.IsSuccess);
        AssertError(Plain(Assert.Single(results, r => r.IsFailure)), 409, "crm.periods.AlreadyOpen");
    }

    [Fact]
    public async Task A7_ADuplicateSpaceName_OnCreateOrRename_Is409_IgnoringCase()
    {
        BriefTestProject p = await SeedAsync("a7-space");
        Guid typeId = await CreateSpaceTypeAsync();
        await CreateSpaceAsync(p, "Kitchen");
        Guid otherSpace = await CreateSpaceAsync(p, "Pantry");

        Result<Guid> duplicate = await CommandFor<CreateCrmSpaceCommand, Guid>(p.OwnerAppUserId,
            new CreateCrmSpaceCommand(p.ProjectId, typeId, "kitchen", p.OwnerAppUserId));
        AssertError(Plain(duplicate), 409, "crm.spaces.DuplicateName");

        Result renamed = await Command(p.OwnerAppUserId, new UpdateCrmSpaceCommand(otherSpace, typeId, "KITCHEN", null, p.OwnerAppUserId));
        AssertError(renamed, 409, "crm.spaces.DuplicateName");
    }

    [Fact]
    public async Task A7_ADuplicateSpaceTypeCode_Is409_EvenWhenRacing()
    {
        string code = $"D{Guid.NewGuid():N}"[..20];
        Result<Guid> first = await TryCreateSpaceTypeAsync(code);
        Result<Guid> second = await TryCreateSpaceTypeAsync(code);
        Assert.True(first.IsSuccess, first.Error?.Description);
        AssertError(Plain(second), 409, "crm.spaceTypes.DuplicateCode");

        string raced = $"R{Guid.NewGuid():N}"[..20];
        Result<Guid>[] results = await Task.WhenAll(TryCreateSpaceTypeAsync(raced), TryCreateSpaceTypeAsync(raced));
        Assert.Single(results, r => r.IsSuccess);
        AssertError(Plain(Assert.Single(results, r => r.IsFailure)), 409, "crm.spaceTypes.DuplicateCode");
    }

    private async Task<Result<Guid>> TryCreateSpaceTypeAsync(string code)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateCrmSpaceTypeCommand, Guid>>()
            .HandleAsync(new CreateCrmSpaceTypeCommand(code, "Room", "غرفة"));
    }
}
