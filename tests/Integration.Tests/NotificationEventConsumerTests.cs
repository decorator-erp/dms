using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Events;
using BuildingBlocks.Outbox;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Identity.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// N1-06 verify: the accumulated zero-handler events now have consumers, and every one of them is
/// reached the way production reaches it.
///
/// <para><b>Nothing here calls a handler directly.</b> Every assertion drives a real command, lets it
/// stage an outbox row, and then runs <c>OutboxProcessor.ProcessPendingAsync</c>. A direct call would
/// prove the handler's body works and nothing about whether it is registered — and "registered nowhere,
/// so the event is marked processed with zero handlers" is the exact silent failure three modules were
/// one missing line away from, since <c>Identity</c>, <c>Approvals</c>, <c>Sites</c> and
/// <c>Notifications</c> all lacked <c>AddModuleIntegrationEvents</c> before this task.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationEventConsumerTests(ApiFixture fixture)
{
    /// <summary>
    /// <b>The plan's required clause: a previously-silent event now produces a notification end to
    /// end.</b> <c>EmployeeHiredIntegrationEvent</c> has been published since G2-03 with no subscriber
    /// at all — legitimate under R2, and recorded every phase since.
    ///
    /// <para>Asserted in three steps rather than one, because each could fail independently: the event
    /// is on the outbox unprocessed; the handler runs when the processor sweeps; the notification row
    /// exists for the right recipient. Checking only the last would leave "was it the outbox or a
    /// coincidence?" unanswered.</para>
    /// </summary>
    [Fact]
    public async Task EmployeeHired_PreviouslySilent_NowNotifiesTheNewEmployee_ThroughTheOutbox()
    {
        var appUserId = await RegisterUserAsync();
        var employeeId = await CreateEmployeeAsync(appUserId);

        Assert.Equal(0, await CountNotificationsAsync(appUserId));
        Assert.True(await HasUnprocessedOutboxMessageAsync(nameof(EmployeeHiredIntegrationEvent)),
            "CreateEmployee did not stage an EmployeeHiredIntegrationEvent on the outbox.");

        await DrainOutboxAsync();

        Assert.Equal(1, await CountNotificationsAsync(appUserId));
        Assert.Equal(
            [$"employee-{employeeId}-hired:{appUserId}"],
            await ReadDedupeKeysAsync(appUserId));
    }

    /// <summary>
    /// The same event delivered twice produces one notification. Not a restatement of the dedupe tests
    /// in <c>NotificationDeliveryTests</c>: those call the service directly, this goes through the
    /// outbox, where <c>InboxDedupeDecorator</c> is the FIRST line of defence and the dedupe key the
    /// second. Draining twice exercises both — the inbox row should stop the second delivery before the
    /// handler even runs.
    /// </summary>
    [Fact]
    public async Task DrainingTheOutboxTwice_DoesNotDuplicateTheNotification()
    {
        var appUserId = await RegisterUserAsync();
        await CreateEmployeeAsync(appUserId);

        await DrainOutboxAsync();
        var afterFirstDrain = await CountNotificationsAsync(appUserId);

        await DrainOutboxAsync();
        var afterSecondDrain = await CountNotificationsAsync(appUserId);

        Assert.Equal(1, afterFirstDrain);
        Assert.Equal(afterFirstDrain, afterSecondDrain);
    }

    /// <summary>
    /// Every notification handler is registered somewhere. Resolving <c>IIntegrationEventHandler&lt;T&gt;</c>
    /// from the host container is the only way to see this: a handler in a module that never calls
    /// <c>AddModuleIntegrationEvents</c> compiles, ships, and is simply never invoked.
    ///
    /// <para>Four modules gained that call across N1-06 (Notifications, Approvals, Sites) or already had
    /// it (Projects, Crm). This test is what would notice if a fifth module's handler were added without
    /// one.</para>
    /// </summary>
    [Theory]
    [InlineData(typeof(EmployeeHiredIntegrationEvent))]
    [InlineData(typeof(Crm.Contracts.ClientCreatedIntegrationEvent))]
    [InlineData(typeof(Crm.Contracts.MeetingScheduledIntegrationEvent))]
    [InlineData(typeof(Projects.Contracts.ProjectTaskAssignedIntegrationEvent))]
    [InlineData(typeof(Projects.Contracts.ProjectTaskCompletedIntegrationEvent))]
    [InlineData(typeof(Projects.Contracts.ProjectTaskRejectedIntegrationEvent))]
    [InlineData(typeof(Projects.Contracts.ProjectTaskReopenedIntegrationEvent))]
    [InlineData(typeof(Projects.Contracts.ProjectStageCompletedIntegrationEvent))]
    [InlineData(typeof(Projects.Contracts.ProjectStageRejectedIntegrationEvent))]
    [InlineData(typeof(Approvals.Contracts.ApprovalRequestedIntegrationEvent))]
    [InlineData(typeof(Approvals.Contracts.ApprovalDecidedIntegrationEvent))]
    [InlineData(typeof(Sites.Contracts.SiteApprovedIntegrationEvent))]
    public void EveryNotifyingEvent_HasAtLeastOneRegisteredHandler(Type eventType)
    {
        using var scope = fixture.Services.CreateScope();

        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var handlers = scope.ServiceProvider.GetServices(handlerType).ToList();

        Assert.True(handlers.Count > 0,
            $"{eventType.Name} was decided 'notify' at N1-06 but resolves zero handlers — its module is "
            + "probably missing AddModuleIntegrationEvents, which fails silently.");
    }

    /// <summary>
    /// The counterpart, and the reason the table in <c>PROGRESS.md</c> is a decision rather than a
    /// description: these four were considered and deliberately left silent. Asserting the absence
    /// keeps "we chose not to" from quietly becoming "someone added one without revisiting the
    /// reasoning".
    /// </summary>
    [Theory]
    [InlineData(typeof(Projects.Contracts.ProjectTaskStartedIntegrationEvent))]
    [InlineData(typeof(Projects.Contracts.ProjectTaskCancelledIntegrationEvent))]
    [InlineData(typeof(Sites.Contracts.SiteSubmittedIntegrationEvent))]
    [InlineData(typeof(Crm.Contracts.LeadConvertedIntegrationEvent))]
    public void DeliberatelySilentEvents_HaveNoNotificationHandler(Type eventType)
    {
        // PC-03 discrimination, not just liveness — and it found a live defect. This assertion has two
        // ways to pass without meaning anything, and the sibling test above closes only the first: the
        // lookup could find nothing for the constructed handler type, OR the `Contains("Notification")`
        // filter could match nothing because the names it sees are not handler names at all. Running
        // the IDENTICAL lookup and filter against an event that IS wired proves both halves live.
        //
        // The second failure mode was real: see NotificationHandlerNamesFor for what the DI route
        // actually returned, and why this now scans types instead.
        var wiredHandlers = NotificationHandlerNamesFor(typeof(EmployeeHiredIntegrationEvent));
        Assert.True(wiredHandlers.Count > 0,
            "The positive control failed: EmployeeHiredIntegrationEvent has no handler class whose name "
            + "contains 'Notification', so the four absence assertions below prove nothing. Either the "
            + "assembly scan or the name filter is broken.");

        var handlerNames = NotificationHandlerNamesFor(eventType);

        Assert.True(handlerNames.Count == 0,
            $"{eventType.Name} was recorded as deliberately silent at N1-06 but now has a notification "
            + $"handler ({string.Join(", ", handlerNames)}). Update the decision table, or remove it.");
    }

    /// <summary>
    /// The handler classes registered for <paramref name="eventType"/>, by name.
    ///
    /// <para><b>Reflection over the module assemblies, deliberately, not <c>GetServices</c>.</b> PC-03
    /// found that the DI route cannot work here: <c>AddModuleIntegrationEvents</c> wraps every handler
    /// in <c>InboxDedupeDecorator&lt;TContext, TEvent&gt;</c>, so the container hands back the
    /// decorator and <c>GetType().Name</c> is always <c>InboxDedupeDecorator`2</c> — a string that
    /// never contains "Notification". The filter therefore matched nothing for ANY event, and the four
    /// absence assertions below had been passing vacuously since N1-06.</para>
    ///
    /// <para>The claim being made is structural — "no notification handler class exists for this
    /// event" — so scanning types answers it directly and is immune to how the container wraps them.
    /// Handlers live in <c>*.Infrastructure</c> assemblies by the N1-06 placement rule; the open
    /// generic decorator cannot match a closed <c>IIntegrationEventHandler&lt;TEvent&gt;</c> and so
    /// excludes itself.</para>
    /// </summary>
    private static List<string> NotificationHandlerNamesFor(Type eventType)
    {
        var handlerInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);

        return [.. HandlerAssemblies.Value
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .Where(handlerInterface.IsAssignableFrom)
            .Select(t => t.Name)
            .Where(name => name.Contains("Notification", StringComparison.Ordinal))
            .Distinct()
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>Every module's Infrastructure assembly, loaded by name the way <c>ApiFixture</c> does —
    /// the test project references them all, so this cannot silently miss one.</summary>
    private static readonly Lazy<IReadOnlyList<System.Reflection.Assembly>> HandlerAssemblies = new(() =>
        [.. new[]
        {
            "Identity.Infrastructure", "Hr.Infrastructure", "Approvals.Infrastructure",
            "Crm.Infrastructure", "Projects.Infrastructure", "Documents.Infrastructure",
            "Sites.Infrastructure", "Notifications.Infrastructure",
        }.Select(System.Reflection.Assembly.Load)]);

    private async Task DrainOutboxAsync()
    {
        var processor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
        await processor.ProcessPendingAsync(CancellationToken.None);
    }

    private async Task<string> RegisterUserAsync()
    {
        var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            $"consumer-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Event", "Consumer", null, "employee"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString()!;
    }

    private async Task<Guid> CreateEmployeeAsync(string appUserId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<BuildingBlocks.Messaging.ICommandHandler<CreateEmployeeCommand, Guid>>();

        var result = await handler.HandleAsync(
            new CreateEmployeeCommand(appUserId, "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"));

        Assert.True(result.IsSuccess, $"Creating an employee failed: {result.Error?.Description}");
        return result.Value;
    }

    private async Task<bool> HasUnprocessedOutboxMessageAsync(string eventTypeName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM shared.outbox_messages
            WHERE processed_utc IS NULL AND type LIKE @pattern
            """, connection);
        command.Parameters.AddWithValue("pattern", $"%{eventTypeName}%");

        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private async Task<int> CountNotificationsAsync(string recipientUserId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """SELECT COUNT(*) FROM notify.notifications WHERE "RecipientUserId" = @id""", connection);
        command.Parameters.AddWithValue("id", recipientUserId);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<List<string>> ReadDedupeKeysAsync(string recipientUserId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """SELECT "DedupeKey" FROM notify.notifications WHERE "RecipientUserId" = @id AND "DedupeKey" IS NOT NULL""",
            connection);
        command.Parameters.AddWithValue("id", recipientUserId);

        var keys = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            keys.Add(reader.GetString(0));

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }
}
