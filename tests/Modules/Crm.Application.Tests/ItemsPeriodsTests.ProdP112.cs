using Crm.Application.Features.Jobs;
using Crm.Domain;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>FINDING-P1-12: the notify retry of FINDING-P1-3 is bounded by a recency window. Without it, the
/// first run after a deploy would tell every client whose period was closed after its due date — months ago,
/// some of them — and never stamped.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task P112_APeriodClosedThirtyDaysAgo_NeverNotified_IsNotNotifiedByTheRetry()
    {
        BriefTestProject p = await SeedAsync("p112-old-close");
        Guid periodId = await OpenPeriodAsync(p);
        await ExpirePeriodAsync(periodId);

        // Run 1 closes it but the notify fails, so CloseNotifiedAtUtc stays null.
        await RunCloseJobWithAsync(sp => new RecordingNotifications(sp.GetRequiredService<INotificationService>(), throwFor: periodId));
        CrmRequirementPeriod closed = await LoadPeriodAsync(periodId);
        Assert.Equal(Domain.RequirementPeriodStatus.Closed, closed.Status);
        Assert.Null(closed.CloseNotifiedAtUtc);

        // Thirty days on: it was closed (on or after its target time) a month ago.
        await BackdateClosedPeriodAsync(periodId, TimeSpan.FromDays(30));

        RecordingNotifications second = null!;
        await RunCloseJobWithAsync(sp => second = new RecordingNotifications(sp.GetRequiredService<INotificationService>()));

        Assert.Equal(0, second.CountFor(periodId));
        Assert.Null((await LoadPeriodAsync(periodId)).CloseNotifiedAtUtc);
    }

    /// <summary>FINDING-P1-19: inside the window — closed a day ago, the notify failed — the retry tells the
    /// client once and stamps the period.</summary>
    [Fact]
    public async Task P112_APeriodClosedOneDayAgo_WithAFailedNotify_IsRetried()
    {
        Guid periodId = await ClosedWithAFailedNotifyAsync("p112-one-day");

        await BackdateClosedPeriodAsync(periodId, TimeSpan.FromDays(1));

        RecordingNotifications second = null!;
        await RunCloseJobWithAsync(sp => second = new RecordingNotifications(sp.GetRequiredService<INotificationService>()));

        Assert.Equal(1, second.CountFor(periodId));
        Assert.NotNull((await LoadPeriodAsync(periodId)).CloseNotifiedAtUtc);
    }

    /// <summary>FINDING-P1-19: just past the window — closed <c>RetryWindow</c> plus a minute ago — the retry
    /// leaves it alone.</summary>
    [Fact]
    public async Task P112_APeriodClosedJustOutsideTheRetryWindow_WithAFailedNotify_IsNotRetried()
    {
        Guid periodId = await ClosedWithAFailedNotifyAsync("p112-window-edge");

        await BackdateClosedPeriodAsync(periodId, CrmPeriodCloseJob.RetryWindow + TimeSpan.FromMinutes(1));

        RecordingNotifications second = null!;
        await RunCloseJobWithAsync(sp => second = new RecordingNotifications(sp.GetRequiredService<INotificationService>()));

        Assert.Equal(0, second.CountFor(periodId));
        Assert.Null((await LoadPeriodAsync(periodId)).CloseNotifiedAtUtc);
    }

    /// <summary>Run 1 closes an expired period but its notify fails, so <c>CloseNotifiedAtUtc</c> stays null.</summary>
    private async Task<Guid> ClosedWithAFailedNotifyAsync(string label)
    {
        BriefTestProject p = await SeedAsync(label);
        Guid periodId = await OpenPeriodAsync(p);
        await ExpirePeriodAsync(periodId);

        await RunCloseJobWithAsync(sp => new RecordingNotifications(sp.GetRequiredService<INotificationService>(), throwFor: periodId));
        CrmRequirementPeriod closed = await LoadPeriodAsync(periodId);
        Assert.Equal(Domain.RequirementPeriodStatus.Closed, closed.Status);
        Assert.Null(closed.CloseNotifiedAtUtc);
        return periodId;
    }

    private async Task BackdateClosedPeriodAsync(Guid periodId, TimeSpan by)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using Npgsql.NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE crm.crm_requirement_periods
            SET "ClosesAtUtc" = "ClosesAtUtc" - @by, "ClosedAtUtc" = "ClosedAtUtc" - @by
            WHERE "Id" = @id
            """;
        command.Parameters.AddWithValue("by", by);
        command.Parameters.AddWithValue("id", periodId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }
}
