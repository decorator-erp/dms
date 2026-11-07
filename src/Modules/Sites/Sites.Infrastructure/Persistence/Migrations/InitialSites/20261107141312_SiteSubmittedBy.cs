using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sites.Infrastructure.Persistence.Migrations.InitialSites;

/// <summary>
/// R1-9: the survey's submitter, so neither its creator nor its submitter can approve it. Nullable: a survey that
/// is a draft has none, and one submitted before this migration keeps none (its creator check still applies). No
/// guard: adding a nullable column cannot fail on existing rows.
/// </summary>
public partial class SiteSubmittedBy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""ALTER TABLE sites.sites ADD COLUMN "SubmittedById" uuid NULL;""");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""ALTER TABLE sites.sites DROP COLUMN "SubmittedById";""");
}
