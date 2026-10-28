using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Contacts;
using Crm.Contracts.Items;
using Crm.Contracts.Meetings;
using Crm.Contracts.Periods;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts.Projects;
using SharedKernel;
using Xunit;
using RequirementCategory = Crm.Contracts.RequirementCategory;

namespace Crm.Application.Tests;

/// <summary>A-15 (portal I3): a deactivated project takes no new CRM work — the portal sees it as missing
/// (404), the office gets a 409.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task A15_OnAnInactiveProject_PortalWritesAre404_AndOfficeWritesAre409()
    {
        BriefTestProject p = await SeedAsync("a15-inactive");
        Guid periodId = await OpenPeriodAsync(p);
        Guid draftId = await CreateItemAsync(p, periodId);
        Guid contactId = await AddContactAsync(p);
        Assert.True((await Command(p.OwnerAppUserId, new SetProjectActiveCommand(p.ProjectId, p.OwnerAppUserId, false))).IsSuccess);

        Result<Guid> portalItem = await CommandFor<CreateCrmRequirementItemByClientCommand, Guid>(p.ClientAppUserId,
            new CreateCrmRequirementItemByClientCommand(p.ProjectId, periodId, contactId, RequirementCategory.Style, "Late", RequirementPriority.Low),
            readAll: false);
        AssertError(Plain(portalItem), 404, "crm.portal.NotFound");

        Result<Guid> portalMeeting = await CommandFor<RequestCrmMeetingByClientCommand, Guid>(p.ClientAppUserId,
            new RequestCrmMeetingByClientCommand(p.ProjectId, contactId, "Visit", MeetingType.OnSite,
                DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1)), readAll: false);
        AssertError(Plain(portalMeeting), 404, "crm.portal.NotFound");

        Result[] office =
        [
            Plain(await CommandFor<CreateCrmRequirementItemCommand, Guid>(p.OwnerAppUserId, Item(p, periodId))),
            await Command(p.OwnerAppUserId, new UpdateCrmRequirementItemCommand(
                draftId, RequirementCategory.Style, "Edit", RequirementPriority.Low, null, null, null, null, null, null, null, null, p.OwnerAppUserId)),
            await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(draftId, p.OwnerAppUserId)),
            await Command(p.OwnerAppUserId, new ExtendCrmRequirementPeriodCommand(periodId, DateTime.UtcNow.AddDays(20), p.OwnerAppUserId)),
            await Command(p.OwnerAppUserId, new CloseCrmRequirementPeriodCommand(periodId, p.OwnerAppUserId)),
        ];
        Assert.All(office, r => AssertError(r, 409, "crm.projects.Inactive"));
    }

    private async Task<Guid> AddContactAsync(BriefTestProject p)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<Guid> contact = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateCrmContactCommand, Guid>>()
            .HandleAsync(new CreateCrmContactCommand(p.ClientId, "Jane", "Doe", ContactRole.Owner, IsPrimary: true));
        Assert.True(contact.IsSuccess, contact.Error?.Description);
        return contact.Value;
    }
}
