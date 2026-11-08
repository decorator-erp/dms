using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Events;
using BuildingBlocks.Jobs;
using BuildingBlocks.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notifications.Contracts;
using Npgsql;
using Projects.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// A-2: a task or stage notification carries its parent <c>ProjectId</c>, so the client can open the
/// nested route (<c>/projects/{projectId}/tasks/{taskId}</c>, <c>…/stages/{stageId}</c>) without a
/// lookup that no longer exists. Every task and stage sender is covered — the event path through the
/// outbox, the two stage handlers, and both deadline jobs — plus the read and the "not project-scoped"
/// null.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationProjectLinkTests(ApiFixture fixture)
{
    private static readonly string[] OfficeCodes =
    [
        "project.create", "project.read", "clients.create", "clients.read", "stages.read", "stages.manage",
        "scope.read", "scope.manage", "scope.confirm", "stage_tasks.read", "stage_tasks.manage",
    ];

    /// <summary>End to end: create an assigned task over HTTP, let the outbox deliver, and read the
    /// assignee's inbox over HTTP. The row the client receives names the task's project.</summary>
    [Fact]
    public async Task A_task_assigned_notification_carries_the_tasks_project_in_the_inbox_read()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"np-owner-{suffix}@example.com");
        (Guid assigneeId, string assigneeUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"np-assignee-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId, OfficeCodes);

        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"NPT-{suffix}");
        HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/projects/{projectId}/tasks", new { Name = "Linked task", AssignedToId = assigneeId });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Guid taskId = await ReadIdAsync(created);

        await DrainOutboxAsync();

        JsonElement item = await FindInboxItemAsync(Client(assigneeUserId, NotificationsPermissions.ViewOwn), taskId);
        Assert.Equal(projectId, item.GetProperty("projectId").GetGuid());
    }

    /// <summary>A notification that is not project-scoped still reads, with <c>projectId: null</c> —
    /// the field is always present, so the client never has to guess between "missing" and "none".</summary>
    [Fact]
    public async Task A_notification_sent_without_a_project_reads_back_with_a_null_projectId()
    {
        string recipient = await IdentityTestSeeding.ProvisionActiveUserAsync(
            fixture, $"np-plain-{Guid.NewGuid():N}@example.com", firstName: "Plain");
        var entityId = Guid.NewGuid();

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
                recipient, NotificationType.TaskAssigned, "No project",
                entityType: NotificationEntityType.Lead, entityId: entityId, ct: CancellationToken.None);
        }

        JsonElement item = await FindInboxItemAsync(Client(recipient, NotificationsPermissions.ViewOwn), entityId);
        Assert.True(item.TryGetProperty("projectId", out JsonElement projectId), "projectId is missing from the inbox item.");
        Assert.Equal(JsonValueKind.Null, projectId.ValueKind);
    }

    /// <summary>Both stage handlers live in Projects and read the project's owner; each stores the
    /// event's project with the row.</summary>
    [Fact]
    public async Task Stage_completed_and_rejected_notifications_carry_the_project()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"np-stage-{suffix}@example.com");
        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(Client(ownerUserId, OfficeCodes), $"NPS-{suffix}");

        var completedStage = Guid.NewGuid();
        var rejectedStage = Guid.NewGuid();
        await HandleAsync(new ProjectStageCompletedIntegrationEvent(
            Guid.NewGuid(), projectId, completedStage, "Design", DateTimeOffset.UtcNow));
        await HandleAsync(new ProjectStageRejectedIntegrationEvent(
            Guid.NewGuid(), projectId, rejectedStage, "Design", DateTimeOffset.UtcNow));

        // Completion fans out to the owner and the client (one row each); rejection to the owner. Every
        // row, whoever it is for, names the project.
        foreach (Guid stageId in new[] { completedStage, rejectedStage })
        {
            List<Guid> projectIds = await ReadProjectIdsAsync(stageId);
            Assert.NotEmpty(projectIds);
            Assert.All(projectIds, id => Assert.Equal(projectId, id));
        }
    }

    /// <summary>The reminder sweeps are the other half of the task and stage traffic: a due-tomorrow task
    /// and a due-tomorrow stage each produce a reminder naming their project.</summary>
    [Fact]
    public async Task Task_and_stage_deadline_reminders_carry_the_project()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"np-due-{suffix}@example.com");
        (Guid assigneeId, _) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"np-due-a-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId, OfficeCodes);

        Guid freeStyle = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"NPD-{suffix}");
        HttpResponseMessage created = await owner.PostAsJsonAsync($"/api/projects/{freeStyle}/tasks", new
        {
            Name = "Due tomorrow",
            AssignedToId = assigneeId,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Guid taskId = await ReadIdAsync(created);

        (Guid stageProject, Guid stageId, _) = await ProjectTestSeeding.CreateConfirmedStageProjectAsync(owner, $"NPE-{suffix}");
        await ExecuteAsync(
            """UPDATE projects.project_stages SET "EstimatedEndDate" = @due, "Status" = 2 WHERE "Id" = @id""",
            ("due", DateTime.UtcNow.AddDays(1)), ("id", stageId));

        await RunJobsAsync("TaskDeadlineJob", "StageDeadlineJob");

        Assert.Contains(freeStyle, await ReadProjectIdsAsync(taskId));
        Assert.All(await ReadProjectIdsAsync(taskId), id => Assert.Equal(freeStyle, id));
        Assert.Contains(stageProject, await ReadProjectIdsAsync(stageId));
        Assert.All(await ReadProjectIdsAsync(stageId), id => Assert.Equal(stageProject, id));
    }

    private async Task HandleAsync<TEvent>(TEvent @event)
        where TEvent : IIntegrationEvent
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        List<IIntegrationEventHandler<TEvent>> handlers =
            [.. scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>()];
        Assert.NotEmpty(handlers);
        foreach (IIntegrationEventHandler<TEvent> handler in handlers)
        {
            await handler.HandleAsync(@event, CancellationToken.None);
        }
    }

    private async Task RunJobsAsync(params string[] names)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        List<IPeriodicJob> jobs = [.. scope.ServiceProvider.GetServices<IPeriodicJob>()
            .Where(job => names.Contains(job.Name, StringComparer.Ordinal))];
        Assert.Equal(names.Length, jobs.Count);
        foreach (IPeriodicJob job in jobs)
        {
            await job.RunAsync(CancellationToken.None);
        }
    }

    private async Task DrainOutboxAsync()
    {
        OutboxProcessor processor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
        await processor.ProcessPendingAsync(CancellationToken.None);
    }

    private static async Task<JsonElement> FindInboxItemAsync(HttpClient client, Guid entityId)
    {
        HttpResponseMessage response = await client.GetAsync("/api/notifications?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        return page.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("entityId").ValueKind == JsonValueKind.String
                            && item.GetProperty("entityId").GetGuid() == entityId);
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();

    /// <summary>Straight from Postgres: the stored column is what the deep link rests on.</summary>
    private async Task<List<Guid>> ReadProjectIdsAsync(Guid entityId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """SELECT "ProjectId" FROM notify.notifications WHERE "EntityId" = @id""", connection);
        command.Parameters.AddWithValue("id", entityId);

        var ids = new List<Guid>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.IsDBNull(0) ? Guid.Empty : reader.GetGuid(0));
        }

        return ids;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
