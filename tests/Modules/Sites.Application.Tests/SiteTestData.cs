using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Hr.Contracts.Employees;
using Identity.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects.Contracts;
using Projects.Contracts.Projects;

namespace Sites.Application.Tests;

/// <summary>
/// Writes rows the definition-layer commands cannot reach. Records, values and sites belong to S1-07's
/// handlers, which do not exist yet — but the definition layer's governance rules are all "is this in
/// use?" probes, so they cannot be tested at all without captured data. Raw SQL against the same
/// container, deliberately: it keeps this file from depending on handlers written in a later task, and
/// it makes what is being set up explicit rather than routed through five commands.
/// </summary>
internal static class SiteTestData
{
    internal sealed record CapturedValue(Guid ModuleId, Guid RecordId, Guid SiteId);

    /// <summary>
    /// Creates a project, an employee, a site, a record and one captured value against
    /// <paramref name="itemId"/> — the smallest graph that makes an item "in use".
    /// </summary>
    public static async Task<CapturedValue> CaptureValueAsync(SitesApplicationFixture fixture, Guid itemId)
    {
        var moduleId = Guid.CreateVersion7();
        var siteId = Guid.CreateVersion7();
        var recordId = Guid.CreateVersion7();
        var (projectId, employeeId) = await SharedOwnersAsync(fixture);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await ExecuteAsync(fixture, $"""
            INSERT INTO sites.site_modules
                ("Id","Code","NameEn","NameAr","IsSystem","SortOrder","IsActive","CreatedAtUtc")
            VALUES ('{moduleId}','mod_{suffix}','M','م',false,0,true, now() AT TIME ZONE 'utc');

            INSERT INTO sites.sites
                ("Id","ProjectId","Code","Status","Address","CreatedById","IsActive","CreatedAtUtc")
            VALUES ('{siteId}','{projectId}','SITE-{DateTime.UtcNow.Ticks % 100000:D5}',1,'12 Nasr Road',
                    '{employeeId}',true, now() AT TIME ZONE 'utc');

            INSERT INTO sites.site_records
                ("Id","SiteId","ModuleId","Label","SortOrder","IsActive","CreatedAtUtc")
            VALUES ('{recordId}','{siteId}','{moduleId}','Front door',0,true, now() AT TIME ZONE 'utc');

            INSERT INTO sites.site_record_values ("RecordId","ItemId","ValueNumber")
            VALUES ('{recordId}','{itemId}',90);
            """);

        return new CapturedValue(moduleId, recordId, siteId);
    }

    public static Task SoftDeleteRecordAsync(SitesApplicationFixture fixture, Guid recordId) =>
        ExecuteAsync(fixture, $"""
            UPDATE sites.site_records SET "IsActive" = false WHERE "Id" = '{recordId}';
            """);

    public static async Task<string> ModuleCodeAsync(SitesApplicationFixture fixture, Guid moduleId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""SELECT "Code" FROM sites.site_modules WHERE "Id" = '{moduleId}';""", connection);

        return (string)(await command.ExecuteScalarAsync())!;
    }

    // One shared project + employee per fixture, created on first use through the REAL commands of the
    // modules that own those rows -- Identity's RegisterCommand, Hr's CreateEmployeeCommand, Crm's
    // CreateClientCommand, Projects' CreateProjectCommand -- exactly as Crm.Application.Tests does.
    //
    // Not raw SQL: seeding a site reaches four schemas deep (sites.sites -> projects.projects, whose
    // ClientId is NOT NULL -> crm.clients -> identity.users, plus hr.employees -> identity.users), and
    // identity.users is ASP.NET Identity's own table. Hand-writing a valid row there means reproducing a
    // dozen NOT NULL columns that are not this module's business and would rot the moment Identity
    // changes. The chain itself is the FK graph working as designed.
    //
    // Shared rather than per-call because hr.employees has a UNIQUE index on AppUserId. Tests only care
    // about site/record/module identity, and none asserts project membership -- which is what the scope
    // decorator's outsider case relies on.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static (Guid ProjectId, Guid EmployeeId)? _owners;

    private static async Task<(Guid ProjectId, Guid EmployeeId)> SharedOwnersAsync(
        SitesApplicationFixture fixture)
    {
        await Gate.WaitAsync();
        try
        {
            if (_owners is { } existing) return existing;

            await using var scope = fixture.CreateScope();
            var sp = scope.ServiceProvider;
            var suffix = Guid.NewGuid().ToString("N");

            var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();

            var clientUserId = (await register.HandleAsync(new RegisterCommand(new RegisterRequest(
                $"site-client-{suffix}@example.com", "P@ssw0rd123!", "Test", "Client", null, "employee")))).Value!;

            var clientId = (await sp.GetRequiredService<ICommandHandler<CreateClientCommand, Guid>>()
                .HandleAsync(new CreateClientCommand(clientUserId, "1 Test Way"))).Value;

            var ownerUserId = (await register.HandleAsync(new RegisterCommand(new RegisterRequest(
                $"site-owner-{suffix}@example.com", "P@ssw0rd123!", "Owner", "Test", null, "employee")))).Value!;

            var employeeId = (await sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>()
                .HandleAsync(new CreateEmployeeCommand(
                    ownerUserId, "Surveyor", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"))).Value;

            sp.GetRequiredService<TestCurrentUser>().UserId = ownerUserId;

            var projectId = (await sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>()
                .HandleAsync(new CreateProjectCommand(
                    $"P-{suffix}"[..12], "Test Project", "12 Nasr Road", WorkflowType.Stage, clientId))).Value;

            _owners = (projectId, employeeId);
            return _owners.Value;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task ExecuteAsync(SitesApplicationFixture fixture, string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
