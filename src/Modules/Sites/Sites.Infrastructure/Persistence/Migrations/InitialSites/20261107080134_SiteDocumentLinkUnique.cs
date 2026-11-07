using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sites.Infrastructure.Persistence.Migrations.InitialSites;

/// <summary>
/// NS2: one active link per (document, subject). Raw SQL because EF cannot express the partial index
/// with <c>NULLS NOT DISTINCT</c> — exactly one of <c>SiteId</c>/<c>RecordId</c> is null on every row
/// (<c>ck_site_documents_one_subject</c>), so with default NULL semantics the index would never bite.
/// Partial on <c>IsActive</c> so an unlink frees the subject. Listed in <c>SitesSchemaObjectTests</c>.
/// </summary>
public partial class SiteDocumentLinkUnique : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // C3 guard: refuse, never silently clean up, if existing data already holds duplicates.
        migrationBuilder.Sql("""
            DO $$
            DECLARE duplicates bigint;
            BEGIN
                SELECT count(*) INTO duplicates FROM (
                    SELECT 1 FROM sites.site_documents
                    WHERE "IsActive"
                    GROUP BY "DocumentId", "SiteId", "RecordId"
                    HAVING count(*) > 1) d;

                IF duplicates > 0 THEN
                    RAISE EXCEPTION '% invalid rows: duplicate active site_documents links per (DocumentId, SiteId, RecordId); resolve them before migrating', duplicates;
                END IF;
            END $$;
            """);

        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX ux_site_documents_link
                ON sites.site_documents ("DocumentId", "SiteId", "RecordId") NULLS NOT DISTINCT
                WHERE "IsActive";
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP INDEX IF EXISTS sites.ux_site_documents_link;");
}
