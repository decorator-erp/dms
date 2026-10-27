using Crm.Contracts.Portal;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>A-14 (B-L6): the portal brief's spaces carry no office-only fields.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public void A14_ThePortalBriefSpaces_ExposeNoCreatedByTaskOrMeeting()
    {
        Type spaceType = typeof(PortalBriefViewResponse).GetProperty(nameof(PortalBriefViewResponse.Spaces))!
            .PropertyType.GetGenericArguments().Single();
        string[] names = [.. spaceType.GetProperties().Select(p => p.Name)];

        Assert.DoesNotContain("CreatedById", names);
        Assert.DoesNotContain("ProjectTaskId", names);
        Assert.DoesNotContain("MeetingId", names);
        Assert.Contains("Requirements", names);
    }
}
