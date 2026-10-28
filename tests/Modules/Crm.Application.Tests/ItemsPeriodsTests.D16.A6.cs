using Crm.Application.Features.Spaces;
using Crm.Contracts.Spaces;
using Crm.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-16 / A-F6: the space-name check compares lower(trim(name)) in SQL — the stored name is trimmed
/// and the unique index is on lower("Name"), so a padded or differently cased name is the same name.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task D16_A6_APaddedDifferentlyCasedName_IsADuplicate_OnCreateAndOnRename()
    {
        BriefTestProject p = await SeedAsync("d16-a6");
        await CreateSpaceAsync(p, "Kitchen");
        Guid typeId = await CreateSpaceTypeAsync();
        Guid otherId = await CreateSpaceAsync(p, "Pantry");

        Result<Guid> created = await CommandFor<CreateCrmSpaceCommand, Guid>(
            p.OwnerAppUserId, new CreateCrmSpaceCommand(p.ProjectId, typeId, "  KITCHEN ", p.OwnerAppUserId));
        AssertError(Plain(created), 409, "crm.spaces.DuplicateName");

        Result renamed = await Command(p.OwnerAppUserId, new UpdateCrmSpaceCommand(otherId, typeId, " kitchen", null, p.OwnerAppUserId));
        AssertError(renamed, 409, "crm.spaces.DuplicateName");

        // The pre-check itself answers it — not only the unique index on the insert behind it.
        await using AsyncServiceScope scope = fixture.CreateScope();
        Assert.True(await SpaceNames.TakenAsync(
            scope.ServiceProvider.GetRequiredService<CrmDbContext>(), p.ProjectId, "  KITCHEN ", exceptSpaceId: null, CancellationToken.None));
    }
}
