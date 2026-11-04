using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <summary>
/// L-4 (CRM production review): a document is linked to a subject once. The subject is four nullable
/// columns, so one partial unique index per column on <c>("DocumentId", &lt;column&gt;)</c> where the
/// column is set. Each index is preceded by a guard that refuses to migrate over existing duplicates.
/// </summary>
public partial class CrmArtifactLinkUnique : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Guards first, before any DDL (CrmItemsPeriodsHardening's pattern): an existing duplicate link
        // would fail its unique index halfway through, so each subject column is checked up front and
        // a clear error names what to resolve by hand.
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (
                SELECT 1 FROM crm.crm_artifact_documents
                WHERE "BriefId" IS NOT NULL
                GROUP BY "DocumentId", "BriefId" HAVING COUNT(*) > 1)
              THEN RAISE EXCEPTION 'crm_artifact_documents links one document to the same BriefId more than once (duplicates) — remove the extra links before migrating';
              END IF;
            END $$;
            """);

        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (
                SELECT 1 FROM crm.crm_artifact_documents
                WHERE "SpaceId" IS NOT NULL
                GROUP BY "DocumentId", "SpaceId" HAVING COUNT(*) > 1)
              THEN RAISE EXCEPTION 'crm_artifact_documents links one document to the same SpaceId more than once (duplicates) — remove the extra links before migrating';
              END IF;
            END $$;
            """);

        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (
                SELECT 1 FROM crm.crm_artifact_documents
                WHERE "RequirementItemId" IS NOT NULL
                GROUP BY "DocumentId", "RequirementItemId" HAVING COUNT(*) > 1)
              THEN RAISE EXCEPTION 'crm_artifact_documents links one document to the same RequirementItemId more than once (duplicates) — remove the extra links before migrating';
              END IF;
            END $$;
            """);

        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (
                SELECT 1 FROM crm.crm_artifact_documents
                WHERE "MeetingId" IS NOT NULL
                GROUP BY "DocumentId", "MeetingId" HAVING COUNT(*) > 1)
              THEN RAISE EXCEPTION 'crm_artifact_documents links one document to the same MeetingId more than once (duplicates) — remove the extra links before migrating';
              END IF;
            END $$;
            """);

        migrationBuilder.CreateIndex(
            name: "ux_crm_artifact_documents_document_brief",
            schema: "crm",
            table: "crm_artifact_documents",
            columns: new[] { "DocumentId", "BriefId" },
            unique: true,
            filter: "\"BriefId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ux_crm_artifact_documents_document_meeting",
            schema: "crm",
            table: "crm_artifact_documents",
            columns: new[] { "DocumentId", "MeetingId" },
            unique: true,
            filter: "\"MeetingId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ux_crm_artifact_documents_document_requirement_item",
            schema: "crm",
            table: "crm_artifact_documents",
            columns: new[] { "DocumentId", "RequirementItemId" },
            unique: true,
            filter: "\"RequirementItemId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ux_crm_artifact_documents_document_space",
            schema: "crm",
            table: "crm_artifact_documents",
            columns: new[] { "DocumentId", "SpaceId" },
            unique: true,
            filter: "\"SpaceId\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_crm_artifact_documents_document_brief",
            schema: "crm",
            table: "crm_artifact_documents");

        migrationBuilder.DropIndex(
            name: "ux_crm_artifact_documents_document_meeting",
            schema: "crm",
            table: "crm_artifact_documents");

        migrationBuilder.DropIndex(
            name: "ux_crm_artifact_documents_document_requirement_item",
            schema: "crm",
            table: "crm_artifact_documents");

        migrationBuilder.DropIndex(
            name: "ux_crm_artifact_documents_document_space",
            schema: "crm",
            table: "crm_artifact_documents");
    }
}
