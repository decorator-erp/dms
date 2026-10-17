using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hr.Infrastructure.Persistence.Migrations.InitialHr;

/// <inheritdoc />
public partial class UpdateEmployeeEntity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
            name: "EmergencyContactName",
            schema: "hr",
            table: "employees");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
            name: "EmergencyContactName",
            schema: "hr",
            table: "employees",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);
}
