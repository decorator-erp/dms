using Crm.Application.Features.Jobs;
using Crm.Domain;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>L-3 (CRM production review): one period's <c>xmin</c> conflict costs that period only. The close
/// job logs it and moves on to the next due period, as the meeting-reminder job does per meeting.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task L3_AConflictOnOnePeriod_DoesNotStopTheCloseJob_TheOtherPeriodStillCloses()
    {
        BriefTestProject first = await SeedAsync("l3-conflict");
        BriefTestProject second = await SeedAsync("l3-healthy");
        Guid conflicting = await OpenPeriodAsync(first);
        Guid healthy = await OpenPeriodAsync(second);
        await ExpirePeriodAsync(conflicting);
        await ExpirePeriodAsync(healthy);

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            INotificationService touching = new TouchPeriodBeforeNotify(
                conflicting, fixture.ConnectionString, sp.GetRequiredService<INotificationService>());
            CrmPeriodCloseJob job = ActivatorUtilities.CreateInstance<CrmPeriodCloseJob>(sp, touching);

            await job.RunAsync(CancellationToken.None);
        }

        CrmRequirementPeriod other = await LoadPeriodAsync(healthy);
        Assert.Equal(Domain.RequirementPeriodStatus.Closed, other.Status);
        Assert.NotNull(other.CloseNotifiedAtUtc);

        // The conflicting period's close committed before the conflict; only its notified stamp lost the race.
        CrmRequirementPeriod lost = await LoadPeriodAsync(conflicting);
        Assert.Equal(Domain.RequirementPeriodStatus.Closed, lost.Status);
        Assert.Null(lost.CloseNotifiedAtUtc);
    }

    /// <summary>Commits a no-op update to one period on its own connection just before that period's close
    /// notification, which moves its <c>xmin</c> under the job's tracked copy, exactly as a concurrent
    /// request would. Everything else goes to the real service.</summary>
    private sealed class TouchPeriodBeforeNotify(Guid periodId, string connectionString, INotificationService inner) : INotificationService
    {
        public async Task NotifyAsync(string recipientUserId, NotificationType type, string title, string? body = null,
            NotificationEntityType? entityType = null, Guid? entityId = null, string? dedupeKey = null, Guid? projectId = null, CancellationToken ct = default)
        {
            if (entityId == periodId)
            {
                await using var connection = new Npgsql.NpgsqlConnection(connectionString);
                await connection.OpenAsync(ct);
                await using Npgsql.NpgsqlCommand command = connection.CreateCommand();
                command.CommandText = """UPDATE crm.crm_requirement_periods SET "ClosesAtUtc" = "ClosesAtUtc" WHERE "Id" = @id""";
                command.Parameters.AddWithValue("id", periodId);
                await command.ExecuteNonQueryAsync(ct);
            }

            await inner.NotifyAsync(recipientUserId, type, title, body, entityType, entityId, dedupeKey, projectId, ct);
        }

        public Task NotifyManyAsync(IReadOnlyCollection<string> recipientUserIds, NotificationType type, string title, string? body = null,
            NotificationEntityType? entityType = null, Guid? entityId = null, string? dedupeKey = null, Guid? projectId = null, CancellationToken ct = default) =>
            inner.NotifyManyAsync(recipientUserIds, type, title, body, entityType, entityId, dedupeKey, projectId, ct);
    }
}
