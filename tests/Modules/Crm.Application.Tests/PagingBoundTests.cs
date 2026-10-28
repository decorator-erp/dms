using Crm.Application.Features.Clients;
using Crm.Application.Features.Items;
using Crm.Application.Features.Leads;
using Crm.Application.Features.Meetings;
using Crm.Application.Features.Portal;
using Crm.Contracts.Clients;
using Crm.Contracts.Items;
using Crm.Contracts.Leads;
using Crm.Contracts.Meetings;
using Crm.Contracts.Portal;
using FluentValidation;
using FluentValidation.Results;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-16 / A-F9: every CRM paged query caps <c>PageNumber</c> at 1,000,000, so the skip
/// (<c>(PageNumber - 1) * PageSize</c>) can't overflow into a negative offset. The cap itself passes.</summary>
public sealed class PagingBoundTests
{
    public static TheoryData<int, bool> Pages => new() { { 1_000_000, true }, { 1_000_001, false }, { int.MaxValue, false } };

    [Theory]
    [MemberData(nameof(Pages))]
    public void D16_A9_EveryCrmPagedQuery_CapsThePageNumber(int page, bool valid)
    {
        ValidationResult[] results =
        [
            new GetClientsQueryValidator().Validate(new GetClientsQuery("u", page)),
            new GetMyPortalMeetingsQueryValidator().Validate(new GetMyPortalMeetingsQuery("u", page)),
            new GetCrmLeadsQueryValidator().Validate(new GetCrmLeadsQuery(page)),
            new GetCrmRequirementItemsByPeriodQueryValidator().Validate(new GetCrmRequirementItemsByPeriodQuery(Guid.NewGuid(), "u", page)),
            new GetCrmRequirementItemsByTaskQueryValidator().Validate(new GetCrmRequirementItemsByTaskQuery(Guid.NewGuid(), "u", page)),
            new GetCrmMeetingsByProjectQueryValidator().Validate(new GetCrmMeetingsByProjectQuery(Guid.NewGuid(), "u", page, 50)),
        ];

        Assert.All(results, r => Assert.Equal(valid, r.IsValid));
    }
}
