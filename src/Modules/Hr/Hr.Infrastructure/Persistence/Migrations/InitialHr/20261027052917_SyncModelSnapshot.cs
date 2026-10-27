using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hr.Infrastructure.Persistence.Migrations.InitialHr;

// Intentional no-op (F4): brings the model snapshot up to date with the shared tables this context maps but
// excludes from migrations (outbox_messages' dead_lettered_utc / next_attempt_utc and its index, audit_logs),
// plus the explicit name of IX_departments_Name, which equals the default and so renames nothing — so this
// module's next real migration doesn't carry that noise. Emits no DDL (checked with
// `dotnet ef migrations script`).
/// <inheritdoc />
public partial class SyncModelSnapshot : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
