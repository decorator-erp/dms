using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Items;
using Crm.Contracts.Spaces;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;
using RequirementCategory = Crm.Contracts.RequirementCategory;

namespace Crm.Application.Tests;

/// <summary>A-8 (B-M5): every item, space and space-type write is bounded like its columns, its enums are
/// defined, and a space type it names exists — a 400, never a database error.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task A8_ItemCommands_RejectOverLongFields_UndefinedEnums_AndUnknownSpaceTypes()
    {
        BriefTestProject p = await SeedAsync("a8-items");
        Guid periodId = await OpenPeriodAsync(p);
        Guid draftId = await CreateItemAsync(p, periodId);
        Guid lockedId = await CreateLockedItemAsync(p, periodId);
        Guid inactiveType = await CreateSpaceTypeAsync();
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<ICommandHandler<SetCrmSpaceTypeActiveCommand>>()
                .HandleAsync(new SetCrmSpaceTypeActiveCommand(inactiveType, false))).IsSuccess);
        }

        CreateCrmRequirementItemCommand create = Item(p, periodId);
        Result[] creates =
        [
            Plain(await CommandFor<CreateCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, create with { Title = new string('t', 257) })),
            Plain(await CommandFor<CreateCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, create with { Room = new string('r', 129) })),
            Plain(await CommandFor<CreateCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, create with { Description = new string('d', 2001) })),
            Plain(await CommandFor<CreateCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, create with { Category = (RequirementCategory)99 })),
            Plain(await CommandFor<CreateCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, create with { Priority = (RequirementPriority)99 })),
            Plain(await CommandFor<CreateCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, create with { SourceType = (RequirementSource)99 })),
        ];
        Assert.All(creates, r => AssertError(r, 400));
        AssertError(Plain(await CommandFor<CreateCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, create with { SpaceTypeId = Guid.NewGuid() })),
            400, "crm.items.UnknownSpaceType");
        AssertError(Plain(await CommandFor<CreateCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, create with { SpaceTypeId = inactiveType })),
            400, "crm.items.UnknownSpaceType");

        var update = new UpdateCrmRequirementItemCommand(
            draftId, RequirementCategory.Style, "Ok", RequirementPriority.Low, null, null, null, null, null, null, null, null, p.OwnerAppUserId);
        AssertError(await Command(p.OwnerAppUserId, update with { Title = new string('t', 257) }), 400);
        AssertError(await Command(p.OwnerAppUserId, update with { Category = (RequirementCategory)99 }), 400);
        AssertError(await Command(p.OwnerAppUserId, update with { SpaceTypeId = Guid.NewGuid() }), 400, "crm.items.UnknownSpaceType");

        var supersede = new SupersedeCrmRequirementItemCommand(lockedId, p.EmployeeId, RequirementCategory.Style, "Ok", RequirementPriority.Low, p.OwnerAppUserId);
        AssertError(Plain(await CommandFor<SupersedeCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, supersede with { Room = new string('r', 129) })), 400);
        AssertError(Plain(await CommandFor<SupersedeCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, supersede with { Priority = (RequirementPriority)99 })), 400);
        AssertError(Plain(await CommandFor<SupersedeCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, supersede with { SpaceTypeId = Guid.NewGuid() })),
            400, "crm.items.UnknownSpaceType");
    }

    [Fact]
    public async Task A8_SpaceAndSpaceTypeCommands_AreBoundedLikeTheirColumns()
    {
        BriefTestProject p = await SeedAsync("a8-spaces");
        Guid typeId = await CreateSpaceTypeAsync();
        Guid spaceId = await CreateSpaceAsync(p, "Hall");

        AssertError(Plain(await CommandFor<CreateCrmSpaceCommand, Guid>(p.OwnerAppUserId,
            new CreateCrmSpaceCommand(p.ProjectId, typeId, new string('n', 257), p.OwnerAppUserId))), 400);
        AssertError(Plain(await CommandFor<CreateCrmSpaceCommand, Guid>(p.OwnerAppUserId,
            new CreateCrmSpaceCommand(p.ProjectId, typeId, "Den", p.OwnerAppUserId, Description: new string('d', 1001)))), 400);
        AssertError(Plain(await CommandFor<CreateCrmSpaceCommand, Guid>(p.OwnerAppUserId,
            new CreateCrmSpaceCommand(p.ProjectId, Guid.NewGuid(), "Den", p.OwnerAppUserId))), 400, "crm.spaces.UnknownSpaceType");
        AssertError(await Command(p.OwnerAppUserId, new UpdateCrmSpaceCommand(spaceId, typeId, new string('n', 257), null, p.OwnerAppUserId)), 400);
        AssertError(await Command(p.OwnerAppUserId, new UpdateCrmSpaceCommand(spaceId, Guid.NewGuid(), "Hall", null, p.OwnerAppUserId)),
            400, "crm.spaces.UnknownSpaceType");

        AssertError(Plain(await TryCreateSpaceTypeWithAsync(new CreateCrmSpaceTypeCommand(new string('c', 65), "Room", "غرفة"))), 400);
        AssertError(Plain(await TryCreateSpaceTypeWithAsync(new CreateCrmSpaceTypeCommand($"C{Guid.NewGuid():N}"[..20], new string('e', 129), "غرفة"))), 400);
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result updated = await scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateCrmSpaceTypeCommand>>()
            .HandleAsync(new UpdateCrmSpaceTypeCommand(typeId, "Room", new string('a', 129), 1));
        AssertError(updated, 400);
    }

    private async Task<Result<Guid>> TryCreateSpaceTypeWithAsync(CreateCrmSpaceTypeCommand command)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateCrmSpaceTypeCommand, Guid>>().HandleAsync(command);
    }
}
