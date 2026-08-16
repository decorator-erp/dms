using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Crm.Contracts.Spaces;
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

    internal sealed record Owners(Guid ProjectId, Guid EmployeeId, string AppUserId);

    /// <summary>The app user id the shared project's owning employee is linked to — the identity S1-07's
    /// instance commands act as.</summary>
    public static string OwnerUserId => _owners?.AppUserId
        ?? throw new InvalidOperationException("Call OwnersAsync first.");

    /// <summary>The shared project and employee, created on first use.</summary>
    public static Task<Owners> OwnersAsync(SitesApplicationFixture fixture) => SharedOwnersAsync(fixture);

    /// <summary>
    /// Creates a project, an employee, a site, a record and one captured value against
    /// <paramref name="itemId"/> — the smallest graph that makes an item "in use".
    /// </summary>
    public static async Task<CapturedValue> CaptureValueAsync(SitesApplicationFixture fixture, Guid itemId)
    {
        var moduleId = Guid.CreateVersion7();
        var siteId = Guid.CreateVersion7();
        var recordId = Guid.CreateVersion7();
        var owners = await SharedOwnersAsync(fixture);
        var (projectId, employeeId) = (owners.ProjectId, owners.EmployeeId);
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
    private static Owners? _owners;

    private static async Task<Owners> SharedOwnersAsync(SitesApplicationFixture fixture)
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

            _owners = new Owners(projectId, employeeId, ownerUserId);
            return _owners;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<string> ItemCodeAsync(SitesApplicationFixture fixture, Guid itemId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""SELECT "Code" FROM sites.site_items WHERE "Id" = '{itemId}';""", connection);

        return (string)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// A CRM space on a DIFFERENT project — the case a foreign key cannot catch, and the whole reason
    /// <c>ICrmSpaceReader</c> exists. Written through CRM's own commands so the row is real.
    /// </summary>
    public static async Task<Guid> CreateSpaceInAnotherProjectAsync(SitesApplicationFixture fixture)
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var suffix = Guid.NewGuid().ToString("N");

        var otherProjectId = await CreateAnotherProjectAsync(sp, suffix);

        var spaceTypeId = (await sp
            .GetRequiredService<ICommandHandler<CreateCrmSpaceTypeCommand, Guid>>()
            .HandleAsync(new CreateCrmSpaceTypeCommand($"st{suffix[..6]}", "Bedroom", "غرفة"))).Value;

        var spaceId = (await sp.GetRequiredService<ICommandHandler<CreateCrmSpaceCommand, Guid>>()
            .HandleAsync(new CreateCrmSpaceCommand(otherProjectId, spaceTypeId, "Master bedroom"))).Value;

        return spaceId;
    }

    /// <summary>A document on the shared project — a legitimate link target.</summary>
    public static Task<Guid> CreateDocumentAsync(SitesApplicationFixture fixture) =>
        CreateDocumentAsync(fixture, sameProject: true);

    /// <summary>A document on a DIFFERENT project — defect B5's case.</summary>
    public static Task<Guid> CreateDocumentInAnotherProjectAsync(SitesApplicationFixture fixture) =>
        CreateDocumentAsync(fixture, sameProject: false);

    private static async Task<Guid> CreateDocumentAsync(
        SitesApplicationFixture fixture, bool sameProject)
    {
        var owners = await SharedOwnersAsync(fixture);
        var suffix = Guid.NewGuid().ToString("N");

        Guid projectId;
        if (sameProject)
        {
            projectId = owners.ProjectId;
        }
        else
        {
            await using var scope = fixture.CreateScope();
            projectId = await CreateAnotherProjectAsync(scope.ServiceProvider, suffix);
        }

        // Raw SQL here, unlike the other seeds: uploading a document goes through IFormFile and real file
        // storage, which this module never touches -- SiteDocument links a row by id and stores no file.
        // The row is all that is needed, and standing up multipart upload to obtain one would test
        // Documents rather than Sites.
        var documentId = Guid.CreateVersion7();

        await ExecuteAsync(fixture, $"""
            INSERT INTO documents.project_documents
                ("Id","ProjectId","FileName","StoragePath","ContentType","FileSizeBytes","FileExtension",
                 "Version","RootDocumentId","UploadedById","UploadedOnUtc","IsLocked","IsActive","CreatedAtUtc")
            VALUES ('{documentId}','{projectId}','plan-{suffix[..6]}.pdf','/tmp/plan.pdf','application/pdf',
                    1024,'.pdf',1,'{documentId}','{owners.EmployeeId}', now() AT TIME ZONE 'utc',
                    false,true, now() AT TIME ZONE 'utc');
            """);

        return documentId;
    }

    private static async Task<Guid> CreateAnotherProjectAsync(IServiceProvider sp, string suffix)
    {
        // CreateProjectCommand resolves its owner from the acting user, so the caller must be a real
        // employee -- the shared owner is the one this fixture has.
        sp.GetRequiredService<TestCurrentUser>().UserId = OwnerUserId;

        var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();

        var clientUserId = (await register.HandleAsync(new RegisterCommand(new RegisterRequest(
            $"other-client-{suffix}@example.com", "P@ssw0rd123!", "Other", "Client", null, "employee")))).Value!;

        var clientId = (await sp.GetRequiredService<ICommandHandler<CreateClientCommand, Guid>>()
            .HandleAsync(new CreateClientCommand(clientUserId, "2 Other Way"))).Value;

        return (await sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>()
            .HandleAsync(new CreateProjectCommand(
                $"O-{suffix}"[..12], "Other Project", "9 Other St", WorkflowType.Stage, clientId))).Value;
    }

    private static async Task ExecuteAsync(SitesApplicationFixture fixture, string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
