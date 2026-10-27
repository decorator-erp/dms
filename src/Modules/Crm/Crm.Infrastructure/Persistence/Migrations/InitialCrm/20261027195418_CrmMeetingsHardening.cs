using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <summary>
/// PR B of the CRM backend completion (meetings). One additive migration:
/// <list type="bullet">
/// <item><description>B-5: <c>crm_meetings.AcceptedByEmployeeId</c> (nullable) — the employee who accepted the slot, so
/// they are notified and reminded too.</description></item>
/// </list>
/// </summary>
public partial class CrmMeetingsHardening : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<Guid>(
            name: "AcceptedByEmployeeId",
            schema: "crm",
            table: "crm_meetings",
            type: "uuid",
            nullable: true);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(
            name: "AcceptedByEmployeeId",
            schema: "crm",
            table: "crm_meetings");
}
