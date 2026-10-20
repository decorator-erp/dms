using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Identity.Application.Tests;

/// <summary>
/// B2: makes a race deterministic. A test arms it with SQL; the next <c>SaveChanges</c> on any
/// <c>IdentityModuleDbContext</c> first runs that SQL on a separate connection (committed), then
/// continues. The SQL plants the conflicting row after the handler's pre-check and before its insert,
/// which is exactly the window a concurrent request would have used, so the handler's unique-violation
/// catch is the only thing between it and a 500. One shot: it disarms itself when it fires.
/// </summary>
public sealed class PlantBeforeSave : SaveChangesInterceptor
{
    private string? _connectionString;
    private string? _sql;

    public void Arm(string connectionString, string sql)
    {
        _connectionString = connectionString;
        _sql = sql;
    }

    public void Disarm() => _sql = null;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _sql, null) is { } sql)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
