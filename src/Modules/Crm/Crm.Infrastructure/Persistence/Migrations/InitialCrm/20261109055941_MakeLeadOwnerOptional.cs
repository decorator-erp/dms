using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <summary>LI-1: a lead's owner is optional - the public intake form stores leads unassigned. The FK to
/// <c>hr.employees</c> (<c>fk_crm_leads_owner</c>) stays and still applies whenever an owner is set.</summary>
public partial class MakeLeadOwnerOptional : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AlterColumn<Guid>(
            name: "OwnerId",
            schema: "crm",
            table: "crm_leads",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Not EF's generated default (an empty Guid), which no employee row matches: the FK would refuse
        // it. Unassigned leads have no owner to restore, so the rollback stops and says so; assign them
        // (or remove them) first.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM crm.crm_leads WHERE "OwnerId" IS NULL) THEN
                    RAISE EXCEPTION 'crm.crm_leads has unassigned leads; assign an owner to each before rolling back MakeLeadOwnerOptional';
                END IF;
            END $$;
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "OwnerId",
            schema: "crm",
            table: "crm_leads",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }
}
