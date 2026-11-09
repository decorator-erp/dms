using System.Collections.Concurrent;
using System.Reflection;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Respawn;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Boots the real Api host (<c>WebApplicationFactory&lt;Program&gt;</c>) against a Testcontainers
/// Postgres, migrating every module schema in <see cref="ModuleDependencyOrder.SchemaOrder"/> — today
/// that's just <c>shared</c>; add each module's <c>*.Infrastructure</c> DbContext here (and its
/// ProjectReference to this test project) as Identity (G1-03) and HR (G2-03) are built.
///
/// The JWT signing key and the seed admin's password are generated per fixture and injected via
/// configuration. <c>appsettings.json</c> commits neither (PF-S1), and tests must not depend on (or leak
/// into) a developer's local secrets.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string JwtKey { get; } = $"{Guid.NewGuid():N}{Guid.NewGuid():N}";

    /// <summary>PF-S1: the password <c>AdminUserSeeder</c> seeds the admin with. The fixed suffix meets the
    /// password policy (upper, lower, digit, symbol) whatever the random part holds.</summary>
    public string AdminPassword { get; } = $"{Guid.NewGuid():N}aA1!";

    public const string JwtIssuer = "DMS.Modular.Api.Tests";
    public const string JwtAudience = "DMS.Modular.Client.Tests";

    /// <summary>One real, active employee, seeded once in <see cref="InitializeAsync"/> for tests that need
    /// a valid owner id (assigning a lead). It used to be the public lead endpoint's configured intake owner
    /// (<c>Crm:PublicLeadIntakeOwnerId</c>); since LI-1 website leads are stored unassigned and the host
    /// reads no such setting. <see cref="ResetAsync"/> is never actually called anywhere in this test
    /// collection today, so this row persists for the fixture's whole lifetime; if that ever changes,
    /// re-seed it there too.</summary>
    public static readonly Guid SeededEmployeeId = Guid.Parse("00000000-0000-0000-0000-00000000c12e");

    private Testcontainers.PostgreSql.PostgreSqlContainer _container = null!;
    private Respawner _respawner = null!;

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Every SQL statement EF Core has executed across every DbContext in this host, captured via a
    /// custom <see cref="ILoggerProvider"/> registered in <see cref="ConfigureWebHost"/> — for G3-01's
    /// "enable EF query logging in the test and assert no SQL touches the identity schema" requirement.
    /// Call <see cref="ClearCapturedSql"/> before the operation under test so earlier noise (migrations,
    /// seeding, an unrelated request) doesn't produce a false positive.
    ///
    /// <para><b>Excludes exactly one statement: N2's per-request session check.</b> Since Phase 1B the
    /// host's JwtBearer <c>OnTokenValidated</c> reads the caller's security stamp from
    /// <c>identity.users</c> on every authenticated request, through <c>IUserSessionState</c>. That is the
    /// host authenticating the request before any module runs, not a module reaching into Identity's
    /// tables, which is what the schema-isolation tests guard against. It is recognised by the query tag
    /// <c>identity:session-state</c> (<c>UserSessionState.SessionStateQueryTag</c>), not by its shape, so
    /// any other <c>identity.</c> statement still counts. <see cref="CapturedSessionChecks"/> keeps the
    /// excluded statements visible, so a test can prove the check really ran.</para>
    /// </summary>
    public IReadOnlyList<string> CapturedSql => [.. _capturedSql.Where(sql => !IsSessionCheck(sql))];

    /// <summary>The session-check statements <see cref="CapturedSql"/> leaves out.</summary>
    public IReadOnlyList<string> CapturedSessionChecks => [.. _capturedSql.Where(IsSessionCheck)];

    private static bool IsSessionCheck(string sql) =>
        sql.Contains("-- identity:session-state", StringComparison.Ordinal);

    public void ClearCapturedSql() => _capturedSql.Clear();

    private readonly ConcurrentQueue<string> _capturedSql = new();

    /// <summary>
    /// A signed token claiming <paramref name="permissions"/> for <paramref name="userId"/>. It tests
    /// route claims and nothing else; for a real login token use <see cref="TestUsers"/>.
    ///
    /// <para><b>Since N2 the host also checks every token's session:</b> its <c>sstamp</c> must equal
    /// the user's stored security stamp, and the user must exist and be active. So this carries the
    /// user's current stamp. An id nobody provisioned gets a bare, active login row first (raw SQL, like
    /// <see cref="SeedEmployeeAsync"/>), so the many tests that only need "some caller with
    /// these permissions" keep working. A token issued before a change to its user (deactivation, a role
    /// change) is stale afterwards, exactly as in production.</para>
    /// </summary>
    public string IssueTestToken(string userId, params string[] permissions) =>
        TestJwt.Issue(JwtKey, JwtIssuer, JwtAudience, userId, CurrentSecurityStamp(userId), permissions);

    private string CurrentSecurityStamp(string userId)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();

        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO identity.users
                ("Id", "UserName", "NormalizedUserName", "FirstName", "LastName", "IsActive", "UserType", "CreatedAt",
                 "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount",
                 "SecurityStamp")
            VALUES (@id, 'token-' || @id, upper('token-' || @id), 'Test', 'Token', true, 0, now(),
                    true, false, false, true, 0, gen_random_uuid()::text)
            ON CONFLICT ("Id") DO NOTHING;
            UPDATE identity.users SET "SecurityStamp" = gen_random_uuid()::text
            WHERE "Id" = @id AND "SecurityStamp" IS NULL;
            SELECT "SecurityStamp" FROM identity.users WHERE "Id" = @id;
            """;
        command.Parameters.AddWithValue("id", userId);
        return (string)command.ExecuteScalar()!;
    }

    public async Task InitializeAsync()
    {
        await StartAndMigrateAsync();

        await SeedEmployeeAsync();

        // The string-connection overload of CreateAsync only supports SQL Server (it constructs a
        // SqlConnection internally) - Postgres needs the DbConnection overload with an explicit
        // NpgsqlConnection.
        await using var respawnConnection = new NpgsqlConnection(ConnectionString);
        await respawnConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(respawnConnection, new RespawnerOptions
        {
            // __EFMigrationsHistory lives in the default "public" schema regardless of a context's
            // HasDefaultSchema (confirmed by inspection), so restricting to these three schemas already
            // leaves it untouched — no explicit TablesToIgnore needed.
            // "notify" added at N1-03. Omitting it would leave notification and activity rows alive
            // across every reset in this collection - and because the dedupe index is unique per
            // recipient, a leaked row makes the NEXT test's identical notification silently skip.
            // "finance" added at F1-04, in the same commit as the migration case above. Omitting it
            // would leave budget lines, ledger entries and invoices alive across every reset in this
            // collection - and unlike a stale read, that leaks in a way the tests would enforce: the
            // unique index on invoices."Number" is global, so a leaked invoice makes the NEXT test that
            // issues the same number fail with a database error rather than a clear assertion.
            SchemasToInclude =
                ["identity", "hr", "approvals", "crm", "projects", "documents", "sites", "finance", "notify", "shared"],
            DbAdapter = DbAdapter.Postgres,
        });

        // Force the host to build now (rather than lazily on first request) so a configuration or
        // startup failure surfaces here, in InitializeAsync, not confusingly inside the first test.
        _ = Services;
    }

    private const int StartAttempts = 3;
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// F5: a failure here fails every test in the collection at once (~1 ms each), so start-up is hardened
    /// rather than left to a single try. Each attempt gets a fresh container with a start timeout, waits until
    /// Postgres actually accepts a query (the container's readiness check can pass while the server is still
    /// restarting after init), then migrates. A failed attempt's container is disposed and the next one starts
    /// after a short backoff; the third failure is rethrown with the earlier ones attached.
    /// </summary>
    private async Task StartAndMigrateAsync()
    {
        var failures = new List<Exception>();
        for (int attempt = 1; attempt <= StartAttempts; attempt++)
        {
            _container = new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:17").Build();
            try
            {
                using (var startTimeout = new CancellationTokenSource(StartTimeout))
                {
                    await _container.StartAsync(startTimeout.Token);
                }

                await WaitUntilAcceptingQueriesAsync();

                foreach (string schema in ModuleDependencyOrder.SchemaOrder)
                {
                    await MigrateSchemaAsync(schema);
                }

                return;
            }
            catch (Exception ex) when (attempt < StartAttempts)
            {
                failures.Add(ex);
                await _container.DisposeAsync();
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
            catch (Exception ex)
            {
                failures.Add(ex);
                throw new AggregateException(
                    $"The Api fixture's Postgres failed to start and migrate {StartAttempts} times.", failures);
            }
        }
    }

    private async Task WaitUntilAcceptingQueriesAsync()
    {
        DateTime deadline = DateTime.UtcNow + ReadyTimeout;
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();
                await using NpgsqlCommand command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync();
                return;
            }
            catch (NpgsqlException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting, not ConfigureAppConfiguration + AddInMemoryCollection: settings applied this way
        // are guaranteed highest precedence, added directly to the host's own configuration rather than
        // as one more provider whose ordering relative to Program.cs's own setup (appsettings.json,
        // user secrets, env vars) would otherwise have to be reasoned about.
        builder.UseEnvironment("Development");
        builder.UseSetting("JwtSettings:Key", JwtKey);
        builder.UseSetting("JwtSettings:Issuer", JwtIssuer);
        builder.UseSetting("JwtSettings:Audience", JwtAudience);
        builder.UseSetting("DMS_SEED_ADMIN_PASSWORD", AdminPassword);
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        // Small on purpose (default is 50 MB) - keeps DocumentsSmokeTests' oversized-upload test's payload
        // small and fast rather than needing a genuinely 50+ MB request; every other test's upload content
        // is a handful of bytes, well under this cap.
        builder.UseSetting("FileStorage:MaxUploadBytes", "5242880");
        // Phase 1B, three policies since PF-D2. Every test request comes from one IP, and the suite logs in
        // hundreds of times (TestUsers), so the auth rate limits are raised far out of reach here. The 429s
        // are asserted by AuthRateLimitTests on derived hosts, each setting every limit it relies on. The public
        // lead form keeps its real limit (five a minute per IP): its tests each send from their own client IP
        // instead (CreatePublicClient), so they never share a window.
        builder.UseSetting("RateLimiting:AuthLogin:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:AuthRefresh:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:AuthSession:PermitLimit", "100000");
        // N2: no session-state cache, so a security-stamp change is visible on the very next request.
        builder.UseSetting("Identity:SessionStateCacheSeconds", "0");
        // F1-10. Stands down OutboxProcessor's and PeriodicJobWorker's TIMER loops -- both remain
        // registered and resolvable, and the ten call sites that drive them explicitly are unaffected.
        //
        // CapturedSql below is a process-wide sink with no per-request correlation, so a background
        // sweep landing between a schema-isolation test's ClearCapturedSql() and its assertion reads as
        // the module under test having touched another module's schema. That race was diagnosed at
        // F1-00, deferred here, and is fixed by removing the interleave rather than by widening the
        // forbidden-schema list -- which would hide the leaks those tests exist to catch. See
        // BuildingBlocks.BackgroundLoops.
        builder.UseSetting(BuildingBlocks.BackgroundLoops.EnabledKey, "false");
        builder.ConfigureLogging(logging => logging.AddProvider(new SqlCapturingLoggerProvider(_capturedSql)));
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new TestClientIpStartupFilter()));
    }

    /// <summary>The test-only request header <see cref="TestClientIpStartupFilter"/> reads.</summary>
    public const string TestClientIpHeader = "X-Test-Client-Ip";

    /// <summary>
    /// LI-1: an anonymous client whose requests come from their own client IP, so the public lead form's
    /// per-IP rate limit (five a minute) gives each test its own window. Every other request in the suite
    /// shares TestServer's one (null) remote address.
    ///
    /// <para>Not a derived host on purpose: a derived host that runs a module's first EF query leaves its own
    /// logger factory - disposed with that host - in EF Core's static internal-service-provider cache, and
    /// <see cref="CapturedSql"/> then sees nothing from that module for the rest of the run.</para>
    /// </summary>
    public HttpClient CreatePublicClient()
    {
        HttpClient client = CreateClient();
        byte[] ip = Guid.NewGuid().ToByteArray()[..4];
        ip[0] = 198; // 198.18.0.0/15, the benchmarking range: never a real client, never a configured proxy.
        ip[1] = (byte)(18 + (ip[1] & 1));
        client.DefaultRequestHeaders.Add(TestClientIpHeader, new System.Net.IPAddress(ip).ToString());
        return client;
    }

    /// <summary>Sets the connection's remote address from <see cref="TestClientIpHeader"/> when a test sends
    /// it; otherwise does nothing. First in the pipeline, so the rate limiter partitions on it.</summary>
    private sealed class TestClientIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(TestClientIpHeader, out Microsoft.Extensions.Primitives.StringValues value)
                    && System.Net.IPAddress.TryParse(value.ToString(), out System.Net.IPAddress? address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }

    /// <summary>Captures every message logged under EF Core's "Executed DbCommand" event — the SQL text
    /// itself, schema-qualified table names included — across every DbContext in the host.</summary>
    private sealed class SqlCapturingLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new SqlCapturingLogger(categoryName, sink);

        public void Dispose()
        {
        }

        private sealed class SqlCapturingLogger(string categoryName, ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) =>
                categoryName == "Microsoft.EntityFrameworkCore.Database.Command" && logLevel >= LogLevel.Information;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel) && eventId.Id == Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted.Id)
                {
                    sink.Enqueue(formatter(state, exception));
                }
            }
        }
    }

    /// <summary>Raw SQL, before the host (and its <c>hr</c>/<c>identity</c> FKs) is even built — a real
    /// row for <see cref="SeededEmployeeId"/> to satisfy, matching
    /// <c>ProjectAccessGuardTests.SeedEmployeeAsync</c>'s own precedent for seeding a cross-module
    /// dependency outside any module's own command surface.</summary>
    private async Task SeedEmployeeAsync()
    {
        const string appUserId = "public-lead-intake-owner";

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using (NpgsqlCommand userCommand = connection.CreateCommand())
        {
            userCommand.CommandText = """
                INSERT INTO identity.users
                    ("Id", "FirstName", "LastName", "IsActive", "UserType", "CreatedAt",
                     "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                VALUES (@appUserId, 'Intake', 'Owner', true, 0, now(), true, false, false, false, 0);
                """;
            userCommand.Parameters.AddWithValue("appUserId", appUserId);
            await userCommand.ExecuteNonQueryAsync();
        }

        await using NpgsqlCommand employeeCommand = connection.CreateCommand();
        employeeCommand.CommandText = """
                INSERT INTO hr.employees
                    ("Id", "JobTitle", "HireDate", "ContractType", "AppUserId", "IsActive", "CreatedAtUtc")
                VALUES (@id, 'Sales', CURRENT_DATE, 1, @appUserId, true, now());
                """;
        employeeCommand.Parameters.AddWithValue("id", SeededEmployeeId);
        employeeCommand.Parameters.AddWithValue("appUserId", appUserId);
        await employeeCommand.ExecuteNonQueryAsync();
    }

    private async Task MigrateSchemaAsync(string schema)
    {
        switch (schema)
        {
            case "shared":
                await using (var db = new SharedDbContext(
                    new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(ConnectionString).Options))
                {
                    await db.Database.MigrateAsync();
                }
                break;

            case "identity":
                await MigrateModuleSchemaAsync(
                    "Identity.Infrastructure",
                    "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
                break;

            case "hr":
                await MigrateModuleSchemaAsync(
                    "Hr.Infrastructure",
                    "Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure");
                break;

            case "approvals":
                await MigrateModuleSchemaAsync(
                    "Approvals.Infrastructure",
                    "Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure");
                break;

            case "crm":
                await MigrateModuleSchemaAsync(
                    "Crm.Infrastructure",
                    "Crm.Infrastructure.Persistence.CrmDbContext, Crm.Infrastructure");
                break;

            case "projects":
                await MigrateModuleSchemaAsync(
                    "Projects.Infrastructure",
                    "Projects.Infrastructure.Persistence.ProjectsDbContext, Projects.Infrastructure");
                break;

            case "documents":
                await MigrateModuleSchemaAsync(
                    "Documents.Infrastructure",
                    "Documents.Infrastructure.Persistence.DocumentsDbContext, Documents.Infrastructure");
                break;

            case "sites":
                await MigrateModuleSchemaAsync(
                    "Sites.Infrastructure",
                    "Sites.Infrastructure.Persistence.SitesDbContext, Sites.Infrastructure");
                break;

            case "finance":
                await MigrateModuleSchemaAsync(
                    "Finance.Infrastructure",
                    "Finance.Infrastructure.Persistence.FinanceDbContext, Finance.Infrastructure");
                break;

            case "notify":
                await MigrateModuleSchemaAsync(
                    "Notifications.Infrastructure",
                    "Notifications.Infrastructure.Persistence.NotificationsDbContext, Notifications.Infrastructure");
                break;

            // F1-04. There was no `default` here, and the plan for this task named the resulting hazard
            // precisely: a schema added to ModuleDependencyOrder.SchemaOrder without a case here is
            // simply never migrated, and the first symptom is a "relation does not exist" error from
            // whichever test happens to touch that module first - a message that points at the module,
            // not at this switch. Turning it into an immediate, named failure costs nothing: every
            // schema in SchemaOrder has a case today, so this line can only fire for the next module
            // someone adds, which is exactly who needs to read it.
            default:
                throw new InvalidOperationException(
                    $"ModuleDependencyOrder.SchemaOrder contains \"{schema}\" but MigrateSchemaAsync has no "
                    + "case for it, so that schema is never migrated. Add one here.");
        }
    }

    /// <summary>
    /// Resolves a module's DbContext by assembly-qualified name and migrates it, without the test
    /// project ever naming the internal concrete type — every module DbContext is internal (arch rule
    /// 4), so there is no compile-time way to reference it from outside the module, and there
    /// shouldn't be.
    ///
    /// Deliberately does NOT go through <see cref="WebApplicationFactory{TEntryPoint}.Services"/> (the
    /// real host's container) — since G1-07, <c>Program.cs</c> runs every <c>IModule.SeedAsync</c> as
    /// part of building that host, and seeding queries tables that don't exist yet on a fresh
    /// Testcontainers database. Building a throwaway <see cref="ServiceCollection"/> via the module's
    /// own (public) <see cref="IModule.Register"/> — the same discovery mechanism
    /// <c>Program.cs</c> uses, just not <c>Program.cs</c>'s own container — resolves and migrates the
    /// DbContext with no seeding side effect, so migration always completes before the real host (and
    /// its seed step) ever builds.
    /// </summary>
    private async Task MigrateModuleSchemaAsync(string moduleAssemblyName, string assemblyQualifiedContextTypeName)
    {
        IModule module = ModuleDiscovery.DiscoverModules(Assembly.Load(moduleAssemblyName)).Single();

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
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
}
