using Crm.Application.Features.Jobs;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>FINDING-P1-3: once the close job has committed a period Closed, a failure in the notify step is
/// retried on the next run. Before, the job only ever selected Open periods, so a client whose notification
/// failed after the close was never told.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task P13_NotifyThrowsOnRunOne_RunTwoNotifiesTheClient_AndStampsCloseNotified()
    {
        BriefTestProject p = await SeedAsync("p13-notify-throws");
        Guid periodId = await OpenPeriodAsync(p);
        await ExpirePeriodAsync(periodId);

        await RunCloseJobWithAsync(sp => new RecordingNotifications(sp.GetRequiredService<INotificationService>(), throwFor: periodId));

        CrmRequirementPeriod afterFirst = await LoadPeriodAsync(periodId);
        Assert.Equal(Domain.RequirementPeriodStatus.Closed, afterFirst.Status);
        Assert.Null(afterFirst.CloseNotifiedAtUtc);

        RecordingNotifications second = null!;
        await RunCloseJobWithAsync(sp => second = new RecordingNotifications(sp.GetRequiredService<INotificationService>()));

        Assert.Equal(1, second.CountFor(periodId));
        Assert.NotNull((await LoadPeriodAsync(periodId)).CloseNotifiedAtUtc);

        // Stamped: a third run neither closes nor notifies it again.
        RecordingNotifications third = null!;
        await RunCloseJobWithAsync(sp => third = new RecordingNotifications(sp.GetRequiredService<INotificationService>()));
        Assert.Equal(0, third.CountFor(periodId));
    }

    [Fact]
    public async Task P13_CloseSaveLosesAnXminRaceOnRunOne_RunTwoClosesAndNotifies()
    {
        BriefTestProject p = await SeedAsync("p13-close-race");
        Guid periodId = await OpenPeriodAsync(p);
        await ExpirePeriodAsync(periodId);

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            // Track the period in the job's own context, then move its xmin underneath: the job's load resolves
            // to this stale tracked copy, so its close save loses the race exactly as against a concurrent extend.
            Crm.Infrastructure.Persistence.CrmDbContext db = sp.GetRequiredService<Crm.Infrastructure.Persistence.CrmDbContext>();
            _ = await db.CrmRequirementPeriods.SingleAsync(x => x.Id == periodId);
            await TouchPeriodAsync(periodId);

            var recording = new RecordingNotifications(sp.GetRequiredService<INotificationService>());
            await ActivatorUtilities.CreateInstance<CrmPeriodCloseJob>(sp, (INotificationService)recording).RunAsync(CancellationToken.None);
            Assert.Equal(0, recording.CountFor(periodId));
        }

        Assert.Equal(Domain.RequirementPeriodStatus.Open, (await LoadPeriodAsync(periodId)).Status);

        RecordingNotifications second = null!;
        await RunCloseJobWithAsync(sp => second = new RecordingNotifications(sp.GetRequiredService<INotificationService>()));

        CrmRequirementPeriod closed = await LoadPeriodAsync(periodId);
        Assert.Equal(Domain.RequirementPeriodStatus.Closed, closed.Status);
        Assert.NotNull(closed.CloseNotifiedAtUtc);
        Assert.Equal(1, second.CountFor(periodId));
    }

    private async Task RunCloseJobWithAsync(Func<IServiceProvider, INotificationService> notifications)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        await ActivatorUtilities.CreateInstance<CrmPeriodCloseJob>(sp, notifications(sp)).RunAsync(CancellationToken.None);
    }

    private async Task TouchPeriodAsync(Guid periodId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using Npgsql.NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """UPDATE crm.crm_requirement_periods SET "ClosesAtUtc" = "ClosesAtUtc" WHERE "Id" = @id""";
        command.Parameters.AddWithValue("id", periodId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    /// <summary>Counts single notifications per entity and, for <c>throwFor</c>, throws instead of sending.
    /// Everything else goes to the real service.</summary>
    private sealed class RecordingNotifications(INotificationService inner, Guid? throwFor = null) : INotificationService
    {
        private readonly List<Guid?> _sent = [];

        public int CountFor(Guid entityId) => _sent.Count(id => id == entityId);

        public async Task NotifyAsync(string recipientUserId, NotificationType type, string title, string? body = null,
            NotificationEntityType? entityType = null, Guid? entityId = null, string? dedupeKey = null, CancellationToken ct = default)
        {
            if (entityId is not null && entityId == throwFor)
            {
                throw new InvalidOperationException("Simulated notification failure.");
            }

            await inner.NotifyAsync(recipientUserId, type, title, body, entityType, entityId, dedupeKey, ct);
            _sent.Add(entityId);
        }

        public Task NotifyManyAsync(IReadOnlyCollection<string> recipientUserIds, NotificationType type, string title, string? body = null,
            NotificationEntityType? entityType = null, Guid? entityId = null, string? dedupeKey = null, CancellationToken ct = default) =>
            inner.NotifyManyAsync(recipientUserIds, type, title, body, entityType, entityId, dedupeKey, ct);
    }
}
