using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hr.Infrastructure.Persistence.Migrations.InitialHr;

/// <inheritdoc />
public partial class InitialHr : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "hr");

        migrationBuilder.CreateTable(
            name: "departments",
            schema: "hr",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                DepartmentHeadId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_departments", x => x.Id));

        migrationBuilder.CreateTable(
            name: "employees",
            schema: "hr",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                JobTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                HireDate = table.Column<DateOnly>(type: "date", nullable: false),
                EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ContractType = table.Column<int>(type: "integer", nullable: false),
                PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                EmergencyContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                EmergencyContactPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                AppUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_employees", x => x.Id));

        migrationBuilder.CreateTable(
            name: "employee_departments",
            schema: "hr",
            columns: table => new
            {
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_employee_departments", x => new { x.EmployeeId, x.DepartmentId });
                table.ForeignKey(
                    name: "FK_employee_departments_departments_DepartmentId",
                    column: x => x.DepartmentId,
                    principalSchema: "hr",
                    principalTable: "departments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_employee_departments_employees_EmployeeId",
                    column: x => x.EmployeeId,
                    principalSchema: "hr",
                    principalTable: "employees",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_departments_DepartmentHeadId",
            schema: "hr",
            table: "departments",
            column: "DepartmentHeadId");

        migrationBuilder.CreateIndex(
            name: "IX_departments_Name",
            schema: "hr",
            table: "departments",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_employee_departments_DepartmentId",
            schema: "hr",
            table: "employee_departments",
            column: "DepartmentId");

        migrationBuilder.CreateIndex(
            name: "IX_employees_AppUserId",
            schema: "hr",
            table: "employees",
            column: "AppUserId",
            unique: true);

        // Cross-schema FK - EF cannot emit this itself (identity.users and hr.employees are in two
        // different EF models), so it's written by hand. ON DELETE RESTRICT, never CASCADE, across
        // a module boundary (see docs/cross-schema-foreign-keys.md): a delete blocked here is a
        // deliberate error at the boundary, not a silent cross-module mutation. This requires
        // identity.users to already exist, which is why ModuleDependencyOrder.SchemaOrder migrates
        // "identity" before "hr".
        // Column names are quoted and PascalCase ("Id", "AppUserId") to match this solution's
        // convention of leaving EF's default column naming alone (no snake_case naming convention
        // is configured anywhere in this solution) - unquoted identifiers would fold to lowercase
        // in Postgres and silently reference the wrong (nonexistent) column names.
        migrationBuilder.Sql(
            """
            ALTER TABLE hr.employees
                ADD CONSTRAINT fk_employees_app_user
                FOREIGN KEY ("AppUserId") REFERENCES identity.users("Id")
                ON DELETE RESTRICT;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE hr.employees DROP CONSTRAINT fk_employees_app_user;");

        migrationBuilder.DropTable(
            name: "employee_departments",
            schema: "hr");

        migrationBuilder.DropTable(
            name: "departments",
            schema: "hr");

        migrationBuilder.DropTable(
            name: "employees",
            schema: "hr");
    }
}
