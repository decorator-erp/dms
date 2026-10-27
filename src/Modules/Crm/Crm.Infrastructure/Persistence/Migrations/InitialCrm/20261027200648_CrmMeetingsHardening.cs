using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <summary>
/// PR B of the CRM backend completion (meetings). One additive migration:
/// <list type="bullet">
/// <item><description>B-5: <c>crm_meetings.AcceptedByEmployeeId</c> (nullable) — the employee who accepted the slot, so
/// they are notified and reminded too.</description></item>
/// <item><description>B-8: <c>xmin</c> becomes the meetings' concurrency token. No-op in SQL (Postgres' system row
/// version; see <c>CrmLeadConcurrencyToken</c>).</description></item>
/// </list>
/// </summary>
public partial class CrmMeetingsHardening : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "AcceptedByEmployeeId",
            schema: "crm",
            table: "crm_meetings",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            schema: "crm",
            table: "crm_meetings",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AcceptedByEmployeeId",
            schema: "crm",
            table: "crm_meetings");

        migrationBuilder.DropColumn(
            name: "xmin",
            schema: "crm",
            table: "crm_meetings");
    }
}
