using Crm.Contracts.Artifacts;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-12 (A-F3): the artifact lookups are project-scoped like every other office CRM read — by space
/// and by requirement item (the review's rows), and by brief and by meeting, which had the same gap.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task D12_AnOutsider_Gets404_OnEveryArtifactLookup_AndTheOwnerDoesNot()
    {
        BriefTestProject p = await SeedAsync("d12-artifacts");
        Guid periodId = await OpenPeriodAsync(p);
        Guid itemId = await CreateItemAsync(p, periodId);
        Guid spaceId = await CreateSpaceAsync(p);
        (Guid meetingId, Guid briefId, _) = await SeedForeignSourcesAsync(p);
        (_, string outsider) = await HireEmployeeAsync("d12-outsider");

        foreach (string user in new[] { outsider, p.OwnerAppUserId })
        {
            Result<IReadOnlyList<CrmArtifactDocumentResponse>>[] results =
            [
                await QueryFor<GetCrmArtifactDocumentsBySpaceQuery, IReadOnlyList<CrmArtifactDocumentResponse>>(user,
                    new GetCrmArtifactDocumentsBySpaceQuery(spaceId, user)),
                await QueryFor<GetCrmArtifactDocumentsByRequirementItemQuery, IReadOnlyList<CrmArtifactDocumentResponse>>(user,
                    new GetCrmArtifactDocumentsByRequirementItemQuery(itemId, user)),
                await QueryFor<GetCrmArtifactDocumentsByBriefQuery, IReadOnlyList<CrmArtifactDocumentResponse>>(user,
                    new GetCrmArtifactDocumentsByBriefQuery(briefId, user)),
                await QueryFor<GetCrmArtifactDocumentsByMeetingQuery, IReadOnlyList<CrmArtifactDocumentResponse>>(user,
                    new GetCrmArtifactDocumentsByMeetingQuery(meetingId, user)),
            ];

            foreach (Result<IReadOnlyList<CrmArtifactDocumentResponse>> result in results)
            {
                if (user == outsider)
                {
                    AssertError(Plain(result), 404);
                }
                else
                {
                    Assert.True(result.IsSuccess, result.Error?.Description);
                }
            }
        }
    }
}
