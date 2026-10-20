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
/// Follow-up H1: a brief or entry that was already <c>PendingApproval</c> when <c>CrmBriefApprovalRounds</c>
/// added <c>PendingApprovalId</c> (as NULL) must not be stranded. <c>CrmBriefBackfillPendingApprovalIds</c>
/// copies the id of its open <c>approvals.approvals</c> row, so the client's decision binds again. The
/// rows are planted in the pre-backfill schema exactly as the old code left them, then the real decided
/// handler applies the decision.
/// </summary>
public sealed class CrmBriefPendingBackfillTests : IAsyncLifetime
{
    private const string MigrationBefore = "20261018074122_CrmBriefStageRequired";

    private static readonly Guid BriefId = new("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid EntryId = new("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid OpenBriefApproval = new("00000000-0000-0000-0000-00000000a0b1");
    private static readonly Guid DecidedBriefApproval = new("00000000-0000-0000-0000-00000000d0b1");
    private static readonly Guid OpenEntryApproval = new("00000000-0000-0000-0000-00000000a0e1");

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
    public async Task LegacyPendingBriefAndEntry_GetTheirOpenApprovalBound_AndTheDecisionThenApplies()
    {
        // As the pre-fix code left them: Pending, PendingApprovalId NULL, one open approval each (plus an
        // older, decided approval on the brief that must not be picked).
        await PlantAsync($"""
            INSERT INTO crm.crm_design_briefs
                ("Id", "ProjectId", "ProjectStageId", "CreatedById", "Status", "UnitType", "UnitCondition",
                 "HiringReasons", "IsActive", "CreatedAtUtc", "PendingApprovalId")
            VALUES ('{BriefId}', gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 2, 1, 1, 0, true, now(), NULL);
            INSERT INTO crm.crm_brief_spaces
                ("Id", "BriefId", "ApprovalStatus", "CreatedById", "IsActive", "CreatedAtUtc", "SendCount", "PendingApprovalId")
            VALUES ('{EntryId}', '{BriefId}', 2, gen_random_uuid(), true, now(), 0, NULL);
            INSERT INTO approvals.approvals
                ("Id", "SubjectType", "SubjectId", "Type", "RequestedByUserId", "Round", "IsActive", "CreatedAtUtc", "Decision")
            VALUES ('{DecidedBriefApproval}', 2, '{BriefId}', 2, 'office', 1, true, now() - interval '1 day', 2),
                   ('{OpenBriefApproval}', 2, '{BriefId}', 2, 'office', 1, true, now(), NULL),
                   ('{OpenEntryApproval}', 4, '{EntryId}', 2, 'office', 1, true, now(), NULL);
            """);

        await using (CrmDbContext migrate = CreateContext())
        {
            await migrate.Database.MigrateAsync();
        }

        await using CrmDbContext db = CreateContext();
        CrmDesignBrief brief = await db.CrmDesignBriefs.AsNoTracking().SingleAsync(b => b.Id == BriefId);
        CrmBriefSpace entry = await db.CrmBriefSpaces.AsNoTracking().SingleAsync(e => e.Id == EntryId);
        Assert.Equal(OpenBriefApproval, brief.PendingApprovalId);
        Assert.Equal(OpenEntryApproval, entry.PendingApprovalId);

        var handler = new ApprovalDecidedIntegrationEventHandler(
            db, new CrmApprovalOutcomes(db, new CrmGateReader(db)),
            NullLogger<ApprovalDecidedIntegrationEventHandler>.Instance);
        await handler.HandleAsync(Decided(OpenBriefApproval, ApprovalSubjectType.DesignBrief, BriefId), CancellationToken.None);
        await handler.HandleAsync(Decided(OpenEntryApproval, ApprovalSubjectType.BriefSpace, EntryId), CancellationToken.None);
        await db.SaveChangesAsync();

        await using CrmDbContext read = CreateContext();
        Assert.Equal(ApprovalState.Approved, (await read.CrmDesignBriefs.AsNoTracking().SingleAsync(b => b.Id == BriefId)).Status);
        Assert.Equal(ApprovalState.Approved, (await read.CrmBriefSpaces.AsNoTracking().SingleAsync(e => e.Id == EntryId)).ApprovalStatus);
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
