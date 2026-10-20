using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <summary>
/// Follow-up H1 to the CRM brief fixes: a one-off backfill of <c>PendingApprovalId</c> for briefs and
/// brief-space entries that were already <c>PendingApproval</c> when <c>CrmBriefApprovalRounds</c> added
/// the column (as NULL). Without it those rows are stranded for good: the decision no longer binds
/// (<c>MarkApproved(id)</c> needs a matching id), Send refuses a pending row, and the head is locked.
///
/// <para><b>This reads another module's table — once, on purpose.</b> CLAUDE.md's rule is that cross-module
/// reads go through <c>*.Contracts</c>, never another module's tables. A migration has no contract to
/// call, and the only record of which approval a legacy pending row is waiting on is
/// <c>approvals.approvals</c> (the undecided row for that subject). So this migration reads it directly,
/// a single time, and writes only CRM's own columns. No CRM code reads the approvals schema; nothing
/// after this migration depends on it. <c>approvals</c> precedes <c>crm</c> in
/// <c>ModuleDependencyOrder.SchemaOrder</c>, so the table exists wherever CRM migrates in order; the
/// <c>to_regclass</c> guard keeps CRM migratable alone (tests that migrate only identity/hr/crm), where
/// there are no approvals to copy.</para>
///
/// <para>Matching: <c>SubjectType</c> 2 = DesignBrief, 4 = BriefSpace (<c>Approvals.Domain.ApprovalSubjectType</c>);
/// CRM status 2 = PendingApproval (<c>Crm.Domain.ApprovalState</c>); <c>Decision IS NULL</c> = still open.
/// Approvals has no unique backstop on open approvals per subject, so the newest open one wins. Rows
/// with no open approval stay NULL — count them before deploying (see CRM-BRIEF-FIX-NOTES.md).</para>
/// </summary>
public partial class CrmBriefBackfillPendingApprovalIds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$ BEGIN
          IF to_regclass('approvals.approvals') IS NOT NULL THEN
            UPDATE crm.crm_design_briefs b
            SET "PendingApprovalId" = a."Id"
            FROM (
              SELECT DISTINCT ON ("SubjectId") "Id", "SubjectId"
              FROM approvals.approvals
              WHERE "SubjectType" = 2 AND "Decision" IS NULL
              ORDER BY "SubjectId", "CreatedAtUtc" DESC) a
            WHERE a."SubjectId" = b."Id" AND b."Status" = 2 AND b."PendingApprovalId" IS NULL;

            UPDATE crm.crm_brief_spaces e
            SET "PendingApprovalId" = a."Id"
            FROM (
              SELECT DISTINCT ON ("SubjectId") "Id", "SubjectId"
              FROM approvals.approvals
              WHERE "SubjectType" = 4 AND "Decision" IS NULL
              ORDER BY "SubjectId", "CreatedAtUtc" DESC) a
            WHERE a."SubjectId" = e."Id" AND e."ApprovalStatus" = 2 AND e."PendingApprovalId" IS NULL;
          END IF;
        END $$;
        """);

    /// <inheritdoc />
    /// <remarks>Deliberately empty: clearing the ids again would re-strand the rows.</remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
