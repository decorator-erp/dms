using Crm.Application.Features.Jobs;
using Crm.Domain;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>FINDING-P1-15: a lost stamp. The close notification is sent, but saving <c>CloseNotifiedAtUtc</c>
/// loses an <c>xmin</c> race (forced as in L-3), so run 2 retries the notify. The dedupe key
/// <c>crm-period-{id}-closed</c> must make that retry a no-op on the Notifications side — one row for the
/// recipient, not two — while run 2 still stamps the period.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task P115_NotifySucceedsButTheStampIsLost_RunTwoLeavesOneNotification_AndStamps()
    {
        BriefTestProject p = await SeedAsync("p115-lost-stamp");
        Guid periodId = await OpenPeriodAsync(p);
        await ExpirePeriodAsync(periodId);

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            INotificationService touching = new TouchPeriodBeforeNotify(
                periodId, fixture.ConnectionString, sp.GetRequiredService<INotificationService>());
            await ActivatorUtilities.CreateInstance<CrmPeriodCloseJob>(sp, touching).RunAsync(CancellationToken.None);
        }

        CrmRequirementPeriod afterFirst = await LoadPeriodAsync(periodId);
        Assert.Equal(Domain.RequirementPeriodStatus.Closed, afterFirst.Status);
        Assert.Null(afterFirst.CloseNotifiedAtUtc);
        Assert.Equal(1, await CountClosedNotificationsAsync(p.ClientAppUserId, periodId));

        await RunPeriodCloseJobAsync();

        Assert.NotNull((await LoadPeriodAsync(periodId)).CloseNotifiedAtUtc);
        Assert.Equal(1, await CountClosedNotificationsAsync(p.ClientAppUserId, periodId));
        Assert.Equal(1, await CountNotificationsWithKeyAsync(p.ClientAppUserId, $"crm-period-{periodId}-closed"));
    }

    /// <summary>Every close notification row for this recipient and period, whatever its key, so a retry under
    /// a different key would show as two.</summary>
    private async Task<long> CountClosedNotificationsAsync(string recipientUserId, Guid periodId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using Npgsql.NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM notify.notifications
            WHERE "RecipientUserId" = @recipient AND "EntityId" = @period AND "Type" = @type
            """;
        command.Parameters.AddWithValue("recipient", recipientUserId);
        command.Parameters.AddWithValue("period", periodId);
        command.Parameters.AddWithValue("type", (int)NotificationType.CrmPeriodClosed);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Rows carrying the job's logical dedupe key, as Notifications scopes it per recipient.</summary>
    private async Task<long> CountNotificationsWithKeyAsync(string recipientUserId, string logicalKey)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using Npgsql.NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM notify.notifications
            WHERE "RecipientUserId" = @recipient AND "DedupeKey" = @key
            """;
        command.Parameters.AddWithValue("recipient", recipientUserId);
        command.Parameters.AddWithValue("key", $"{logicalKey}:{recipientUserId}");
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
