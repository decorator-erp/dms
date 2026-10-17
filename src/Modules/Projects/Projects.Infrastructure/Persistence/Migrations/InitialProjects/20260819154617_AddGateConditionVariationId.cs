using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projects.Infrastructure.Persistence.Migrations.InitialProjects;

/// <inheritdoc />
/// <remarks>
/// F1-00: adds <c>GateCondition.VariationId</c> for <c>ConditionType.Budget</c> — the Finance
/// variation whose signed amount a Budget gate's satisfaction is linked to. Added BEFORE the Finance
/// module exists, as its own task, because it crosses two modules and carries a migration.
///
/// Deliberately NO foreign key. It would point at <c>finance.variations</c>, and Finance depends on
/// Projects (<c>BudgetLine.ProjectId</c>, <c>Invoice.ProjectId</c>, …), so a
/// <c>projects -&gt; finance</c> constraint would make the migration order unsatisfiable — projects
/// migrates first, and finance's tables do not exist yet. This is the FOURTH instance of the
/// deliberately-absent-FK pattern in this codebase, after <c>projects.projects."ClientId"</c>
/// (pre-Phase 4), <c>gate_conditions."ReferencedDocumentId"</c> (Phase 3) and
/// <c>crm.crm_artifact_documents."DocumentId"</c> (Phase 4). See
/// docs/cross-schema-foreign-keys.md, where F1-05 gives the pattern a named entry rather than a
/// fourth ad-hoc paragraph.
///
/// The column IS indexed: satisfaction looks a gate up by variation id when a variation is
/// approved, which is a keyed read, not a scan. The index is intended and is the only thing in this
/// migration besides the column itself.
///
/// Validation of the value is Finance's job, in its own application code against its own DbContext —
/// the same division of labour <c>ReferencedDocumentId</c> has with Documents.
/// </remarks>
public partial class AddGateConditionVariationId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "VariationId",
            schema: "projects",
            table: "gate_conditions",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_gate_conditions_VariationId",
            schema: "projects",
            table: "gate_conditions",
            column: "VariationId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_gate_conditions_VariationId",
            schema: "projects",
            table: "gate_conditions");

        migrationBuilder.DropColumn(
            name: "VariationId",
            schema: "projects",
            table: "gate_conditions");
    }
}
