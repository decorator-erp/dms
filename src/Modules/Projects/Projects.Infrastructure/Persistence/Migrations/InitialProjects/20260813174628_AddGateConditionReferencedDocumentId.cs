using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projects.Infrastructure.Persistence.Migrations.InitialProjects;

/// <inheritdoc />
/// <remarks>
/// G7-02 (A1): adds <c>GateCondition.ReferencedDocumentId</c> for
/// <c>ConditionType.DocumentApproved</c> — the document lineage id (Documents'
/// <c>project_documents.RootDocumentId</c>) this gate waits on.
///
/// Deliberately NO foreign key: it would point at <c>documents.project_documents</c>, and
/// <c>projects -&gt; documents</c> is not in rule 9's allow-list (see
/// docs/cross-schema-foreign-keys.md) and must not be — Documents depends on Projects, so a
/// reverse FK would invert the module graph. Same treatment as <c>projects.projects."ClientId"</c>
/// before CRM: a bare unvalidated column, validated in application code (Documents'
/// <c>DocumentApprovedHandler</c>, G7-07) against Documents' own DbContext, not by the database.
/// </remarks>
public partial class AddGateConditionReferencedDocumentId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<Guid>(
            name: "ReferencedDocumentId",
            schema: "projects",
            table: "gate_conditions",
            type: "uuid",
            nullable: true);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
            name: "ReferencedDocumentId",
            schema: "projects",
            table: "gate_conditions");
}
