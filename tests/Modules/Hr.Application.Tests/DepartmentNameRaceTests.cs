using System.Globalization;
using BuildingBlocks.Messaging;
using Hr.Contracts.Departments;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel;
using Xunit;

namespace Hr.Application.Tests;

/// <summary>
/// Aug 3.5 (B2): a department create or rename that passes the duplicate check and then loses to the
/// partial unique index answers the check's own 400 <c>departments.DuplicatedName</c>, not a 500.
/// The race is made deterministic with a trigger: just before the handler's row is written, the
/// trigger inserts an active department with the same name, in the same statement's transaction, which
/// is the window a concurrent request would have used. It only fires for this test's names, and
/// <c>pg_trigger_depth()</c> keeps its own insert from firing it again.
/// </summary>
[Collection(HrApplicationCollection.Name)]
public sealed class DepartmentNameRaceTests(HrApplicationFixture fixture) : IAsyncLifetime
{
    private readonly string _createName = Corrected($"Racecreate{Guid.NewGuid():N}");
    private readonly string _renameName = Corrected($"Racerename{Guid.NewGuid():N}");

    public async Task InitializeAsync() => await ExecuteAsync($"""
        CREATE OR REPLACE FUNCTION hr.plant_department_name_race() RETURNS trigger AS $$
        BEGIN
          IF pg_trigger_depth() = 1 AND NEW."Name" IN ('{_createName}', '{_renameName}') THEN
            INSERT INTO hr.departments ("Id", "Name", "IsActive", "CreatedAtUtc")
            VALUES (gen_random_uuid(), NEW."Name", true, now());
          END IF;
          RETURN NEW;
        END $$ LANGUAGE plpgsql;
        CREATE TRIGGER plant_department_name_race BEFORE INSERT OR UPDATE OF "Name" ON hr.departments
          FOR EACH ROW EXECUTE FUNCTION hr.plant_department_name_race();
        """);

    public async Task DisposeAsync() => await ExecuteAsync("""
        DROP TRIGGER IF EXISTS plant_department_name_race ON hr.departments;
        DROP FUNCTION IF EXISTS hr.plant_department_name_race();
        """);

    [Fact]
    public async Task A_create_and_a_rename_that_lose_to_the_unique_index_are_DuplicatedName()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();

        Result<Guid> created = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>()
            .HandleAsync(new CreateDepartmentCommand(_createName));

        Assert.True(created.IsFailure);
        Assert.Equal("departments.DuplicatedName", created.Error.Code);
        Assert.Equal(400, created.Error.Status);

        // A scope per command, as per request in the host: the failed create's entity is still tracked
        // by the first scope's context.
        await using AsyncServiceScope otherScope = fixture.CreateScope();
        Result<Guid> other = await otherScope.ServiceProvider.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>()
            .HandleAsync(new CreateDepartmentCommand(Corrected($"Raceother{Guid.NewGuid():N}")));
        Assert.True(other.IsSuccess, other.IsFailure ? other.Error.ToString() : null);

        await using AsyncServiceScope renameScope = fixture.CreateScope();
        Result renamed = await renameScope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateDepartmentCommand>>()
            .HandleAsync(new UpdateDepartmentCommand(other.Value, _renameName, null));

        Assert.True(renamed.IsFailure);
        Assert.Equal("departments.DuplicatedName", renamed.Error.Code);
    }

    /// <summary>The handlers title-case names; the trigger must see the stored form.</summary>
    private static string Corrected(string name) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower());

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
