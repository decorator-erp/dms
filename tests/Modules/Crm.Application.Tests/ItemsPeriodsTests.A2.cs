using BuildingBlocks.Outbox;
using Crm.Contracts;
using Crm.Contracts.Periods;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>A-2 (X-5): a period can only be opened on a stage of its own project.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task A2_OpeningAPeriod_OnAnotherProjectsStage_Is400_AndWritesNothing()
    {
        BriefTestProject p = await SeedAsync("a2-own");
        BriefTestProject other = await SeedAsync("a2-other");

        Result<Guid> opened = await CommandFor<OpenCrmRequirementPeriodCommand, Guid>(p.OwnerAppUserId,
            new OpenCrmRequirementPeriodCommand(p.ProjectId, other.StageId, DateTime.UtcNow.AddDays(7), p.EmployeeId, p.OwnerAppUserId));

        AssertError(Plain(opened), 400, "crm.briefs.StageProjectMismatch");
        Result<IReadOnlyList<CrmRequirementPeriodResponse>> periods = await QueryFor<GetCrmRequirementPeriodsByProjectQuery, IReadOnlyList<CrmRequirementPeriodResponse>>(
            p.OwnerAppUserId, new GetCrmRequirementPeriodsByProjectQuery(p.ProjectId, p.OwnerAppUserId));
        Assert.Empty(periods.Value!);
    }

    [Fact]
    public async Task A2_ClosingAPeriodOnItsOwnStage_StillPublishesThatStagesGateEvent()
    {
        BriefTestProject p = await SeedAsync("a2-close");
        Guid periodId = await OpenPeriodAsync(p, p.StageId);

        Assert.True((await Command(p.OwnerAppUserId, new CloseCrmRequirementPeriodCommand(periodId, p.OwnerAppUserId))).IsSuccess);

        Assert.Equal(1, await CountOutboxAsync(nameof(CrmPeriodClosedIntegrationEvent), p.StageId));
    }

    private async Task<int> CountOutboxAsync(string eventTypeName, Guid subjectId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        List<string> contents = await scope.ServiceProvider.GetRequiredService<SharedDbContext>()
            .OutboxMessages.AsNoTracking()
            .Where(m => m.Type.Contains(eventTypeName))
            .Select(m => m.Content)
            .ToListAsync();
        return contents.Count(c => c.Contains(subjectId.ToString(), StringComparison.OrdinalIgnoreCase));
    }
}
