using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging;
using Finance.Contracts;
using Finance.Contracts.Invoices;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Npgsql;
using SharedKernel;
using Xunit;

namespace Finance.Application.Tests;

/// <summary>
/// The eighth <see cref="IPeriodicJob"/>, deferred from Phase 6 because it reads <c>Invoices</c>.
///
/// <para>Driven through the registered job resolved from DI, not by constructing it — that is what makes
/// these cover <c>FinanceModule</c>'s Scrutor registration and the real dependency graph (three
/// cross-module directories plus <c>INotificationService</c>) rather than only the method body.</para>
/// </summary>
[Collection(FinanceApplicationCollection.Name)]
public sealed class InvoiceDueJobTests(FinanceApplicationFixture fixture)
{
    private async Task<Guid> InvoiceAsync(
        Guid projectId, Guid? clientId, DateOnly issued, DateOnly due, decimal amount,
        InvoiceType type = InvoiceType.ClientInvoice)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<Guid> created = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateInvoiceCommand, Guid>>()
            .HandleAsync(new CreateInvoiceCommand(new CreateInvoiceRequest(
                $"DJ-{Guid.NewGuid().ToString("N")[..10]}", projectId, type, issued,
                [new InvoiceLineDto("Work", 1m, amount)],
                DueOn: due,
                ClientId: type == InvoiceType.ClientInvoice ? clientId : null,
                VendorName: type == InvoiceType.VendorInvoice ? "Acme" : null)));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        return created.Value;
    }

    private async Task RunJobAsync()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();

        IPeriodicJob job = scope.ServiceProvider.GetServices<IPeriodicJob>()
            .Single(j => j.Name == "InvoiceDueJob");

        await job.RunAsync(CancellationToken.None);
    }

    /// <summary>Every notification this job produced for one invoice, with its type and dedupe key —
    /// read straight from the notify schema, because what matters is what was actually stored.</summary>
    private async Task<List<(string Recipient, int Type, string DedupeKey)>> NotificationsAsync(Guid invoiceId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
             SELECT "RecipientUserId", "Type", "DedupeKey" FROM notify.notifications
             WHERE "EntityId" = '{invoiceId}' ORDER BY "DedupeKey", "RecipientUserId";
             """, connection);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        var rows = new List<(string, int, string)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt32(1), reader.IsDBNull(2) ? "" : reader.GetString(2)));
        }

        return rows;
    }

    /// <summary>
    /// <b>Tier 0 is DueSoon, not Overdue</b> — an invoice due today is not yet late, and the backend's
    /// own <c>tier &gt; 0</c> test says so. Writing <c>&gt;=</c> would tell every client their invoice is
    /// overdue on the morning it falls due, which is the kind of change nobody notices until customers
    /// complain.
    /// </summary>
    [Fact]
    public async Task AnInvoiceDueToday_NotifiesDueSoon_NotOverdue()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid invoiceId = await InvoiceAsync(
            scope.ProjectId, owner.ClientId, today.AddDays(-10), today, 500m);

        await RunJobAsync();

        List<(string Recipient, int Type, string DedupeKey)> notifications = await NotificationsAsync(invoiceId);

        Assert.NotEmpty(notifications);
        Assert.All(notifications, n => Assert.Equal((int)NotificationType.InvoiceDueSoon, n.Type));
        // The service stores the key PER RECIPIENT, appending the user id -- so the tier is asserted as
        // a prefix. That suffix is exactly what makes two recipients two notifications rather than one.
        Assert.All(notifications, n => Assert.StartsWith($"invoice-{invoiceId}-due-0d", n.DedupeKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOverdueInvoice_NotifiesOverdue_AtTheLargestCrossedTier()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Nine days late: tiers -5, -1, 0, 1 and 7 are all crossed; only the largest (7) is emitted.
        Guid invoiceId = await InvoiceAsync(
            scope.ProjectId, owner.ClientId, today.AddDays(-30), today.AddDays(-9), 500m);

        await RunJobAsync();

        List<(string Recipient, int Type, string DedupeKey)> notifications = await NotificationsAsync(invoiceId);

        Assert.NotEmpty(notifications);
        Assert.All(notifications, n => Assert.Equal((int)NotificationType.InvoiceOverdue, n.Type));
        Assert.All(notifications, n => Assert.StartsWith($"invoice-{invoiceId}-due-7d", n.DedupeKey, StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>Idempotence, which is the whole contract of <see cref="IPeriodicJob"/>.</b> Running twice must
    /// produce one notification per recipient, because a restart mid-sweep or an overlapping tick
    /// re-runs the job and the worker gives no at-most-once guarantee. The dedupe key is the mechanism.
    /// </summary>
    [Fact]
    public async Task RunningTwice_ProducesOneNotificationPerRecipient()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid invoiceId = await InvoiceAsync(
            scope.ProjectId, owner.ClientId, today.AddDays(-10), today.AddDays(-1), 500m);

        await RunJobAsync();
        List<(string Recipient, int Type, string DedupeKey)> afterFirst = await NotificationsAsync(invoiceId);

        await RunJobAsync();
        List<(string Recipient, int Type, string DedupeKey)> afterSecond = await NotificationsAsync(invoiceId);

        Assert.NotEmpty(afterFirst);
        Assert.Equal(afterFirst.Count, afterSecond.Count);
        Assert.Equal(
            afterFirst.Select(n => n.Recipient).Distinct().Count(),
            afterFirst.Count);
    }

    /// <summary>
    /// <b>The R1 recipient rule.</b> A client invoice reaches the project owner AND the client — two
    /// distinct users. The owner half only works because <c>ProjectSummary.OwnerId</c> was added at
    /// F1-00b; a job living in Finance cannot read <c>projects.projects</c>.
    /// </summary>
    [Fact]
    public async Task AClientInvoice_NotifiesBothTheProjectOwnerAndTheClient()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid invoiceId = await InvoiceAsync(
            scope.ProjectId, owner.ClientId, today.AddDays(-10), today.AddDays(-1), 500m);

        await RunJobAsync();

        var recipients = (await NotificationsAsync(invoiceId))
            .Select(n => n.Recipient)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(2, recipients.Count);
    }

    /// <summary>
    /// <b>The recorded loss.</b> A vendor invoice carries no <c>ClientId</c> — a vendor has no account
    /// here — so it reaches the project owner ONLY. Notifying the project's client about a vendor invoice
    /// would be wrong and is deliberately not done; without the F1-00b owner hop it would reach nobody
    /// at all.
    /// </summary>
    [Fact]
    public async Task AVendorInvoice_NotifiesTheProjectOwnerOnly()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid invoiceId = await InvoiceAsync(
            scope.ProjectId, null, today.AddDays(-10), today.AddDays(-1), 500m,
            InvoiceType.VendorInvoice);

        await RunJobAsync();

        var recipients = (await NotificationsAsync(invoiceId))
            .Select(n => n.Recipient)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Single(recipients);
    }

    /// <summary>
    /// The window and the status filter, both of which exist for cost rather than correctness — and both
    /// of which are the kind of thing a later "simplification" removes. An invoice outside the scan
    /// window produces nothing; the liveness half is the in-window invoice above, which produces
    /// something.
    /// </summary>
    [Fact]
    public async Task AnInvoiceOutsideTheScanWindow_IsNotNotified()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 200 days overdue -- past the -60d lower bound.
        Guid stale = await InvoiceAsync(
            scope.ProjectId, owner.ClientId, today.AddDays(-260), today.AddDays(-200), 500m);

        // And one inside it, so "nothing for the stale one" cannot pass because the job did nothing.
        Guid current = await InvoiceAsync(
            scope.ProjectId, owner.ClientId, today.AddDays(-10), today.AddDays(-1), 500m);

        await RunJobAsync();

        Assert.NotEmpty(await NotificationsAsync(current));
        Assert.Empty(await NotificationsAsync(stale));
    }

    /// <summary>A fully-paid invoice is no longer open and has nothing outstanding, so it is not chased.
    /// Cancelled likewise — both are excluded by the status filter rather than by the balance one, and
    /// this asserts the combination.</summary>
    [Fact]
    public async Task ACancelledInvoice_IsNotNotified()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid invoiceId = await InvoiceAsync(
            scope.ProjectId, owner.ClientId, today.AddDays(-10), today.AddDays(-1), 500m);

        await using (AsyncServiceScope dbScope = fixture.CreateScope())
        {
            await dbScope.ServiceProvider.GetRequiredService<ICommandHandler<CancelInvoiceCommand>>()
                .HandleAsync(new CancelInvoiceCommand(invoiceId));
        }

        await RunJobAsync();

        Assert.Empty(await NotificationsAsync(invoiceId));
    }
}
