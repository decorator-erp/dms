using Crm.Application.Features.Spaces;
using Crm.Contracts.Spaces;
using Crm.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>N-1 (CRM production review): the space-name pre-check trims with C#'s <c>Trim()</c>, the same
/// trim the stored name gets, so a name padded with a tab or a line break (which SQL's <c>trim()</c> leaves
/// in place) is found by the pre-check itself, not only by the unique index behind it.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task N1_ANamePaddedWithATabOrNewline_IsTakenByThePreCheck()
    {
        BriefTestProject p = await SeedAsync("n1-trim");
        await CreateSpaceAsync(p, "Kitchen");

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            Assert.True(await SpaceNames.TakenAsync(
                scope.ServiceProvider.GetRequiredService<CrmDbContext>(), p.ProjectId, "\tKITCHEN\n", exceptSpaceId: null, CancellationToken.None));
        }

        Guid typeId = await CreateSpaceTypeAsync();
        Result<Guid> created = await CommandFor<CreateCrmSpaceCommand, Guid>(
            p.OwnerAppUserId, new CreateCrmSpaceCommand(p.ProjectId, typeId, "\tkitchen\r\n", p.OwnerAppUserId));
        AssertError(Plain(created), 409, "crm.spaces.DuplicateName");
    }
}
