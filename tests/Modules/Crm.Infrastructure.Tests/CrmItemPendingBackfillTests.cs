using System.Reflection;
using Approvals.Contracts;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Crm.Domain;
using Crm.Infrastructure.IntegrationEvents;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using ApprovalState = Crm.Domain.ApprovalState;

namespace Crm.Infrastructure.Tests;

/// <summary>
/// M3 (A-10): a requirement item that was already <c>PendingApproval</c> when <c>CrmItemsPeriodsHardening</c>
/// added <c>PendingApprovalId</c> must not be stranded — the item counterpart of
/// <c>CrmBriefPendingBackfillTests</c>. The migration copies the id of the item's open
/// <c>approvals.approvals</c> row, so the client's decision binds again. The item is planted in the
/// pre-migration schema exactly as the old code left it, then the real decided handler applies the decision.
/// </summary>
public sealed class CrmItemPendingBackfillTests : IAsyncLifetime
{
    private const string MigrationBefore = "20261027092002_CrmBriefRoundOutcome";

    private static readonly Guid ItemId = new("00000000-0000-0000-0000-0000000001a1");
    private static readonly Guid OpenItemApproval = new("00000000-0000-0000-0000-00000001a0a1");
    private static readonly Guid DecidedItemApproval = new("00000000-0000-0000-0000-00000001d0a1");

    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        // The outbox lives in the shared schema; approving the brief enqueues CrmBriefApprovedIntegrationEvent.
        await using (var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(_container.GetConnectionString()).Options))
        {
            await shared.Database.MigrateAsync();
        }

        // SchemaOrder: identity, hr, approvals, then crm - the backfill reads approvals.approvals.
        await MigrateModuleAsync(
            "Identity.Infrastructure", "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
        await MigrateModuleAsync("Hr.Infrastructure", "Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure");
        await MigrateModuleAsync(
            "Approvals.Infrastructure", "Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure");

        await using CrmDbContext db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(MigrationBefore);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task ALegacyPendingItem_GetsItsOpenApprovalBound_AndTheDecisionThenApplies()
    {
        // As the pre-fix code left it: Pending, no binding column yet, one open approval (plus an older,
        // decided approval that must not be picked).
        await PlantAsync($"""
            INSERT INTO crm.crm_requirement_items
                ("Id", "ProjectId", "PeriodId", "Category", "Title", "Priority", "ApprovalStatus",
                 "CreatedByEmployeeId", "IsActive", "CreatedAtUtc")
            VALUES ('{ItemId}', gen_random_uuid(), gen_random_uuid(), 1, 'Legacy item', 2, 2, gen_random_uuid(), true, now());
            INSERT INTO approvals.approvals
                ("Id", "SubjectType", "SubjectId", "Type", "RequestedByUserId", "Round", "IsActive", "CreatedAtUtc", "Decision")
            VALUES ('{DecidedItemApproval}', 3, '{ItemId}', 2, 'office', 1, true, now() - interval '1 day', 2),
                   ('{OpenItemApproval}', 3, '{ItemId}', 2, 'office', 1, true, now(), NULL);
            """);

        await using (CrmDbContext migrate = CreateContext())
        {
            await migrate.Database.MigrateAsync();
        }

        await using CrmDbContext db = CreateContext();
        CrmRequirementItem item = await db.CrmRequirementItems.AsNoTracking().SingleAsync(i => i.Id == ItemId);
        Assert.Equal(OpenItemApproval, item.PendingApprovalId);
        Assert.Equal(1, item.SendCount);

        var handler = new ApprovalDecidedIntegrationEventHandler(
            db, new CrmApprovalOutcomes(db, new CrmGateReader(db)),
            NullLogger<ApprovalDecidedIntegrationEventHandler>.Instance);
        await handler.HandleAsync(Decided(OpenItemApproval, ApprovalSubjectType.RequirementItem, ItemId), CancellationToken.None);
        await db.SaveChangesAsync();

        await using CrmDbContext read = CreateContext();
        Assert.Equal(ApprovalState.Approved, (await read.CrmRequirementItems.AsNoTracking().SingleAsync(i => i.Id == ItemId)).ApprovalStatus);
    }

    private static ApprovalDecidedIntegrationEvent Decided(Guid approvalId, ApprovalSubjectType subjectType, Guid subjectId) =>
        new(Guid.NewGuid(), approvalId, subjectType, subjectId, ApprovalDecision.Approved, "client", DateTimeOffset.UtcNow);

    /// <summary>Foreign keys suspended for the plant only (same technique as CrmBriefMigrationGuardTests):
    /// the rows' parents are not the point.</summary>
    private async Task PlantAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"SET session_replication_role = replica; {sql} SET session_replication_role = DEFAULT;";
        await command.ExecuteNonQueryAsync();
    }

    private async Task MigrateModuleAsync(string assemblyName, string assemblyQualifiedContextTypeName)
    {
        IModule module = ModuleDiscovery.DiscoverModules(Assembly.Load(assemblyName)).Single();

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        module.Register(services, configuration);

        await using ServiceProvider provider = services.BuildServiceProvider();

        Type contextType = Type.GetType(assemblyQualifiedContextTypeName)
            ?? throw new InvalidOperationException($"Could not load type '{assemblyQualifiedContextTypeName}'.");

        using IServiceScope scope = provider.CreateScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
        await context.Database.MigrateAsync();
    }

    private CrmDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CrmDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
}
