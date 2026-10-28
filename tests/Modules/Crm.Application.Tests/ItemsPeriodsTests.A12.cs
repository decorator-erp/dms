using BuildingBlocks.Messaging;
using Crm.Contracts.Spaces;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>A-12 (B-L2, B-L3): accurate codes, and the space-name index really is case-insensitive.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task A12_AnUnknownSpaceType_Is404_NotCodeRequired()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        Result updated = await sp.GetRequiredService<ICommandHandler<UpdateCrmSpaceTypeCommand>>()
            .HandleAsync(new UpdateCrmSpaceTypeCommand(Guid.NewGuid(), "Room", "غرفة", 1));
        Result toggled = await sp.GetRequiredService<ICommandHandler<SetCrmSpaceTypeActiveCommand>>()
            .HandleAsync(new SetCrmSpaceTypeActiveCommand(Guid.NewGuid(), false));

        AssertError(updated, 404, "crm.space_types.NotFound");
        AssertError(toggled, 404, "crm.space_types.NotFound");
    }

    /// <summary>Written straight through the context, past the handler's pre-check: only the index can
    /// catch a race between "Kitchen" and "kitchen".</summary>
    [Fact]
    public async Task A12_TheSpaceNameIndex_TreatsKitchenAndKitchenInLowerCaseAsTheSameName()
    {
        BriefTestProject p = await SeedAsync("a12-index");
        Guid typeId = await CreateSpaceTypeAsync();
        await CreateSpaceAsync(p, "Kitchen");

        await using AsyncServiceScope scope = fixture.CreateScope();
        CrmDbContext db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        db.CrmSpaces.Add(CrmSpace.Create(p.ProjectId, typeId, "kitchen", 0, null).Value!);

        AssertError(await CatalogWrites.SaveAsync(db, CancellationToken.None), 409, "crm.spaces.DuplicateName");
    }
}
