using BuildingBlocks.Jobs;
using Crm.Application.Features.Jobs;
using Crm.Contracts;
using Crm.Contracts.Items;
using Crm.Contracts.Periods;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ApprovalDecision = Approvals.Contracts.ApprovalDecision;

namespace Crm.Application.Tests;

/// <summary>B3 (CRM production review): a period the clock closes drops its Draft and Rejected items from
/// the gate exactly as a manual close does, so it announces the scope it resolved. Both paths now go
/// through <c>CrmPeriodCloser</c>.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task B3_TheCloseJob_OnAnExpiredPeriod_WhoseLastItemsAreDraftAndRejected_PublishesItemsResolvedOnce()
    {
        BriefTestProject p = await SeedAsync("b3-job");
        Guid periodId = await OpenPeriodAsync(p, p.StageId);
        Guid rejectedId = await CreateItemAsync(p, periodId);
        await CreateItemAsync(p, periodId);
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(rejectedId, p.OwnerAppUserId))).IsSuccess);
        await DecideOpenApprovalAsync(p, rejectedId, ApprovalDecision.Rejected);
        Assert.Equal(2, await CountUnresolvedForStageAsync(p.StageId));

        await ExpirePeriodAsync(periodId);
        await RunPeriodCloseJobAsync();

        Assert.Equal(Domain.RequirementPeriodStatus.Closed, (await LoadPeriodAsync(periodId)).Status);
        Assert.Equal(0, await CountUnresolvedForStageAsync(p.StageId));
        Assert.Equal(1, await CountOutboxAsync(nameof(CrmItemsResolvedIntegrationEvent), p.StageId));
        Assert.Equal(1, await CountOutboxAsync(nameof(CrmPeriodClosedIntegrationEvent), p.StageId));
    }

    /// <summary>Moves the period's target close into the past, as the clock eventually would.</summary>
    private async Task ExpirePeriodAsync(Guid periodId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using Npgsql.NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """UPDATE crm.crm_requirement_periods SET "ClosesAtUtc" = now() - interval '1 hour' WHERE "Id" = @id""";
        command.Parameters.AddWithValue("id", periodId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task RunPeriodCloseJobAsync()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        await scope.ServiceProvider.GetServices<IPeriodicJob>().OfType<CrmPeriodCloseJob>().Single().RunAsync(CancellationToken.None);
    }

    private async Task<CrmRequirementPeriod> LoadPeriodAsync(Guid periodId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<Crm.Infrastructure.Persistence.CrmDbContext>()
            .CrmRequirementPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId);
    }
}
