using System.Text.Json;
using BuildingBlocks.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.IntegrationTests;

/// <summary>
/// N1-02 verify. The audit interceptor's mechanics, against a real Postgres, in the project that owns
/// the interceptor.
///
/// <para><b>That last part is why <c>audit_logs</c> lives in the <c>shared</c> schema.</b> This project
/// references only BuildingBlocks and SharedKernel. Had the table been owned by
/// <c>NotificationsDbContext</c> in the <c>notify</c> schema, as the earlier phase brief had it, these
/// tests could not exist here at all — the interceptor's only possible home would have been a
/// full-host integration test, a much blunter instrument for a component whose entire job is
/// per-<c>SaveChanges</c> mechanics.</para>
///
/// <para>Everything goes through <c>AddModuleDbContext</c>, the same registration every module uses,
/// rather than hand-attaching the interceptor — a test that wires it up itself would still pass if the
/// production registration stopped attaching it.</para>
///
/// <para>Audit rows are always read back by <c>EntityId</c>, never by searching the captured JSON: the
/// value columns are <c>jsonb</c>, and Postgres has no implicit <c>jsonb</c>-to-<c>text</c> conversion
/// for the string operators LINQ would translate a <c>Contains</c> into. The collection shares one
/// container, so scoping every read to ids this test created is also what keeps it independent of the
/// others.</para>
/// </summary>
[Collection(OutboxCollection.Name)]
public class AuditInterceptorTests(OutboxFixture fixture)
{
    /// <summary>
    /// <b>B18, closed by mechanism — assertion 1 of 3.</b> The entity name and module on an audit row
    /// are read from EF metadata, never passed as literals.
    ///
    /// <para>Two entity types in two schemas, asserted in one test, is what makes this falsifiable:
    /// with a single auditable type an interceptor that hard-coded <i>any</i> name and <i>any</i>
    /// module would pass. Both expectations are computed (<c>typeof(T).Name</c>), so renaming either
    /// CLR type keeps the test green with no edit — which is the property being claimed — while
    /// hard-coding a literal in the interceptor turns it red.</para>
    /// </summary>
    [Fact]
    public async Task AuditRow_TakesEntityNameAndModule_FromEfMetadata_NotFromLiterals()
    {
        await using ServiceProvider provider = BuildProvider();

        var defaultSchemaThing = new AuditedThing("metadata-probe", "hash", "confidential");
        var otherSchemaThing = new OtherSchemaAuditedThing("metadata-probe");

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            db.AuditedThings.Add(defaultSchemaThing);
            db.OtherSchemaAuditedThings.Add(otherSchemaThing);
            await db.SaveChangesAsync();
        }

        AuditLog defaultSchemaRow = Assert.Single(await ReadAuditRowsAsync(provider, defaultSchemaThing.Id));
        Assert.Equal(typeof(AuditedThing).Name, defaultSchemaRow.EntityName);
        Assert.Equal("test_module", defaultSchemaRow.Module);
        Assert.Equal(AuditAction.Create, defaultSchemaRow.Action);

        AuditLog otherSchemaRow = Assert.Single(await ReadAuditRowsAsync(provider, otherSchemaThing.Id));
        Assert.Equal(typeof(OtherSchemaAuditedThing).Name, otherSchemaRow.EntityName);
        // The falsifying half: a Module read from the DbContext's default schema — or from the
        // backend's hand-maintained entity-to-module dictionary — would say "test_module" here.
        Assert.Equal("test_module_other", otherSchemaRow.Module);
    }

    /// <summary>
    /// <b>The opt-in is <c>IAuditable</c> and nothing else.</b> The backend gated on a hand-maintained
    /// <c>HashSet&lt;string&gt;</c> of entity names next to a separate module dictionary; an entity in
    /// one but not the other wrote nothing at all, silently. That is B18, and this asserts there is no
    /// such second list to fall out of: an otherwise identical entity type differing only in the marker
    /// interface produces no row, while its auditable twin in the same <c>SaveChanges</c> does.
    /// </summary>
    [Fact]
    public async Task EntityWithoutTheAuditableMarker_ProducesNoAuditRow()
    {
        await using ServiceProvider provider = BuildProvider();

        var unaudited = new UnauditedThing("marker-probe");
        var audited = new AuditedThing("marker-probe", "hash", "confidential");

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            db.UnauditedThings.Add(unaudited);
            db.AuditedThings.Add(audited);
            await db.SaveChangesAsync();
        }

        Assert.Empty(await ReadAuditRowsAsync(provider, unaudited.Id));

        // The control: the same save DID audit the marked entity, so "empty" above is the marker's
        // doing and not the interceptor failing to run at all.
        Assert.Single(await ReadAuditRowsAsync(provider, audited.Id));
    }

    /// <summary>
    /// The audit row and the change it audits commit or roll back <b>together</b>. This is the whole
    /// reason the interceptor stages into the saving context instead of writing through one of its own:
    /// a separate transaction means a crash between the two leaves either a change with no trail or a
    /// trail with no change.
    /// </summary>
    [Fact]
    public async Task AuditRow_AndTheChangeItAudits_RollBackTogether()
    {
        await using ServiceProvider provider = BuildProvider();
        var thing = new AuditedThing("rollback-probe", "hash", "confidential");

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();

            db.AuditedThings.Add(thing);
            await db.SaveChangesAsync();

            // Both rows exist inside the transaction...
            Assert.Equal(1, await db.AuditedThings.CountAsync(t => t.Id == thing.Id));
            Assert.Equal(1, await db.Set<AuditLog>().CountAsync(a => a.EntityId == thing.Id.ToString()));

            await transaction.RollbackAsync();
        }

        // ...and neither survives it.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            Assert.Equal(0, await db.AuditedThings.CountAsync(t => t.Id == thing.Id));
        }

        Assert.Empty(await ReadAuditRowsAsync(provider, thing.Id));
    }

    /// <summary>
    /// <b>The test that catches the real security bug.</b> <c>ApplicationUser</c> is
    /// <c>IAuditable</c>, so every login writes an audit row — and its <c>PasswordHash</c>,
    /// <c>SecurityStamp</c> and <c>ConcurrencyStamp</c> come from Microsoft's
    /// <c>IdentityUser&lt;string&gt;</c> and cannot carry <c>[AuditIgnore]</c>. Name-based redaction is
    /// therefore not defence in depth, it is the only thing standing between switching the interceptor
    /// on and writing password hashes into a queryable table. Both paths asserted: by name, and by
    /// attribute.
    /// </summary>
    [Fact]
    public async Task SensitiveValues_AreRedacted_ByNameAndByAttribute()
    {
        await using ServiceProvider provider = BuildProvider();
        var thing = new AuditedThing("redaction-probe", "super-secret-hash", "classified");

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            db.AuditedThings.Add(thing);
            await db.SaveChangesAsync();
        }

        AuditLog row = Assert.Single(await ReadAuditRowsAsync(provider, thing.Id));

        // Redacted by NAME - this is the ApplicationUser.PasswordHash path.
        Assert.Equal("***", ReadString(row.NewValues, nameof(AuditedThing.PasswordHash)));
        Assert.DoesNotContain("super-secret-hash", row.NewValues);

        // Redacted by ATTRIBUTE - the path for properties this codebase owns.
        Assert.Equal("***", ReadString(row.NewValues, nameof(AuditedThing.Confidential)));
        Assert.DoesNotContain("classified", row.NewValues);

        // And a non-sensitive value IS captured, so the two assertions above are not passing because
        // redaction swallowed the whole payload.
        Assert.Equal("redaction-probe", ReadString(row.NewValues, nameof(AuditedThing.Label)));
    }

    [Fact]
    public async Task Update_RecordsOnlyTheChangedColumns_WithBeforeAndAfter()
    {
        await using ServiceProvider provider = BuildProvider();
        var thing = new AuditedThing("before", "hash", "confidential");

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            db.AuditedThings.Add(thing);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            AuditedThing tracked = await db.AuditedThings.SingleAsync(t => t.Id == thing.Id);
            tracked.Rename("after");
            await db.SaveChangesAsync();
        }

        List<AuditLog> rows = await ReadAuditRowsAsync(provider, thing.Id);
        AuditLog updateRow = Assert.Single(rows, r => r.Action == AuditAction.Update);

        Assert.Equal(thing.Id.ToString(), updateRow.EntityId);
        Assert.Equal(
            [nameof(AuditedThing.Label)],
            JsonSerializer.Deserialize<string[]>(updateRow.ChangedColumns!)!);
        Assert.Equal("before", ReadString(updateRow.OldValues, nameof(AuditedThing.Label)));
        Assert.Equal("after", ReadString(updateRow.NewValues, nameof(AuditedThing.Label)));
    }

    [Fact]
    public async Task Delete_RecordsTheOldValues_AndTheAuditRowOutlivesTheEntity()
    {
        await using ServiceProvider provider = BuildProvider();
        var thing = new AuditedThing("delete-probe", "hash", "confidential");

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            db.AuditedThings.Add(thing);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            db.AuditedThings.Remove(await db.AuditedThings.SingleAsync(t => t.Id == thing.Id));
            await db.SaveChangesAsync();
        }

        List<AuditLog> rows = await ReadAuditRowsAsync(provider, thing.Id);
        AuditLog deleteRow = Assert.Single(rows, r => r.Action == AuditAction.Delete);

        Assert.Equal(thing.Id.ToString(), deleteRow.EntityId);
        Assert.Equal("delete-probe", ReadString(deleteRow.OldValues, nameof(AuditedThing.Label)));
        Assert.Null(deleteRow.NewValues);

        // The entity is gone and its trail is not - the property that makes an audit log worth having.
        await using AsyncServiceScope verifyScope = provider.CreateAsyncScope();
        TestModuleDbContext verifyDb = verifyScope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
        Assert.Equal(0, await verifyDb.AuditedThings.CountAsync(t => t.Id == thing.Id));
    }

    /// <summary>
    /// A write with no ambient caller still produces a row, with a null actor. Three real paths hit
    /// this — seeding, the outbox processor's background scope, and the periodic job worker's. The row
    /// keeps what changed and when and loses only who; refusing to write it would put the hole exactly
    /// where the trail matters most. This test is what stops a future reader "fixing" the null by
    /// suppressing the row.
    /// </summary>
    [Fact]
    public async Task WriteWithNoAmbientCaller_IsStillAudited_WithANullActor()
    {
        // No AddApi here, so the TryAdd'd NullAuditContextAccessor is what resolves - exactly the
        // situation of a background scope.
        await using ServiceProvider provider = BuildProvider();
        var thing = new AuditedThing("no-actor-probe", "hash", "confidential");

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            Assert.IsType<NullAuditContextAccessor>(
                scope.ServiceProvider.GetRequiredService<IAuditContextAccessor>());

            TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            db.AuditedThings.Add(thing);
            await db.SaveChangesAsync();
        }

        AuditLog row = Assert.Single(await ReadAuditRowsAsync(provider, thing.Id));

        Assert.Null(row.UserId);
        Assert.Null(row.UserEmail);
        Assert.Null(row.RequestPath);
        // What and when survive - that is the point of writing the row at all.
        Assert.Equal(typeof(AuditedThing).Name, row.EntityName);
        Assert.Equal("test_module", row.Module);
        Assert.NotEqual(default, row.Timestamp);
    }

    /// <summary>
    /// The interceptor must be the SAME instance across scopes. With the <c>(sp, options)</c>
    /// <c>AddDbContext</c> overload, a per-scope interceptor changes the <c>CoreOptionsExtension</c>
    /// service-provider hash on every resolution, making EF Core build and cache a new internal service
    /// provider per scope — a real leak whose only symptom is a warning nobody reads.
    /// </summary>
    [Fact]
    public async Task AuditInterceptor_IsTheSameInstanceAcrossScopes()
    {
        await using ServiceProvider provider = BuildProvider();

        await using AsyncServiceScope first = provider.CreateAsyncScope();
        await using AsyncServiceScope second = provider.CreateAsyncScope();

        Assert.Same(
            first.ServiceProvider.GetRequiredService<AuditSaveChangesInterceptor>(),
            second.ServiceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
    }

    private ServiceProvider BuildProvider()
    {
        IConfigurationRoot cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddModuleDbContext<TestModuleDbContext>(cfg);
        return services.BuildServiceProvider();
    }

    private static async Task<List<AuditLog>> ReadAuditRowsAsync(IServiceProvider provider, Guid entityId)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();

        string key = entityId.ToString();
        return await db.Set<AuditLog>()
            .AsNoTracking()
            .Where(a => a.EntityId == key)
            .ToListAsync();
    }

    /// <summary>Reads one property out of a captured value set. <c>JsonElement</c> rather than a
    /// <c>Dictionary&lt;string, string&gt;</c> because the payload mixes strings with the booleans and
    /// timestamps <c>Entity</c> contributes.</summary>
    private static string? ReadString(string? json, string propertyName)
    {
        Dictionary<string, JsonElement> values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json!)!;
        return values[propertyName].GetString();
    }
}
