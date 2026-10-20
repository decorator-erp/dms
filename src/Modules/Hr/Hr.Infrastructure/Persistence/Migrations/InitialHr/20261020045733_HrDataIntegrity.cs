using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hr.Infrastructure.Persistence.Migrations.InitialHr;

/// <summary>
/// HR Phase 2 (K1, K9, N8, Aug 2.7): the constraints behind HR's data rules, each preceded by a guard.
/// <list type="bullet">
/// <item>one active membership per employee (partial unique index);</item>
/// <item>contract type 1..4 (CHECK);</item>
/// <item>a department head is a real employee (RESTRICT FK);</item>
/// <item>department names unique among active departments (partial unique index, replacing the
/// plain one that counted deleted departments forever);</item>
/// <item>the membership FKs become RESTRICT instead of CASCADE;</item>
/// <item><c>employee_departments."StartedAtUtc"</c>, the start of the current stint, backfilled from
/// <c>"CreatedAtUtc"</c>.</item>
/// </list>
/// <para><b>The guards refuse to migrate over data that breaks a new rule, rather than fixing it.</b>
/// Which membership to keep, which contract type was meant, which duplicate name to rename: each is
/// a decision for a person, made with a reviewed script before deploying (see
/// HR-IDENTITY-FIX-NOTES.md, Phase 2 §4). They run first, before any DDL, so a refused migration
/// changes nothing.</para>
/// </summary>
public partial class HrDataIntegrity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // K9 guards: all four before any DDL.
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (
                SELECT 1 FROM hr.employee_departments WHERE "IsActive"
                GROUP BY "EmployeeId" HAVING COUNT(*) > 1)
              THEN RAISE EXCEPTION 'hr.employee_departments has employees with more than one active membership — resolve before migrating';
              END IF;
            END $$;
            """);
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM hr.employees WHERE "ContractType" NOT BETWEEN 1 AND 4)
              THEN RAISE EXCEPTION 'hr.employees has a ContractType outside 1..4 — resolve before migrating';
              END IF;
            END $$;
            """);
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (
                SELECT 1 FROM hr.departments d
                WHERE d."DepartmentHeadId" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM hr.employees e WHERE e."Id" = d."DepartmentHeadId"))
              THEN RAISE EXCEPTION 'hr.departments has a DepartmentHeadId that is not an employee — resolve before migrating';
              END IF;
            END $$;
            """);
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (
                SELECT 1 FROM hr.departments WHERE "IsActive"
                GROUP BY "Name" HAVING COUNT(*) > 1)
              THEN RAISE EXCEPTION 'hr.departments has two active departments with the same name — resolve before migrating';
              END IF;
            END $$;
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_employee_departments_departments_DepartmentId",
            schema: "hr",
            table: "employee_departments");

        migrationBuilder.DropForeignKey(
            name: "FK_employee_departments_employees_EmployeeId",
            schema: "hr",
            table: "employee_departments");

        migrationBuilder.DropIndex(
            name: "IX_departments_Name",
            schema: "hr",
            table: "departments");

        // Added nullable, backfilled from CreatedAtUtc (the best estimate of when the current stint
        // started: every existing row has had exactly one), then made required. The scaffold's
        // default of year 1 would have been a lie in every row.
        migrationBuilder.AddColumn<DateTime>(
            name: "StartedAtUtc",
            schema: "hr",
            table: "employee_departments",
            type: "timestamp with time zone",
            nullable: true);
        migrationBuilder.Sql("""UPDATE hr.employee_departments SET "StartedAtUtc" = "CreatedAtUtc";""");
        migrationBuilder.AlterColumn<DateTime>(
            name: "StartedAtUtc",
            schema: "hr",
            table: "employee_departments",
            type: "timestamp with time zone",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "timestamp with time zone",
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_employees_contract_type",
            schema: "hr",
            table: "employees",
            sql: "\"ContractType\" BETWEEN 1 AND 4");

        migrationBuilder.CreateIndex(
            name: "IX_employee_departments_EmployeeId",
            schema: "hr",
            table: "employee_departments",
            column: "EmployeeId",
            unique: true,
            filter: "\"IsActive\"");

        migrationBuilder.CreateIndex(
            name: "IX_departments_Name",
            schema: "hr",
            table: "departments",
            column: "Name",
            unique: true,
            filter: "\"IsActive\"");

        migrationBuilder.AddForeignKey(
            name: "FK_departments_employees_DepartmentHeadId",
            schema: "hr",
            table: "departments",
            column: "DepartmentHeadId",
            principalSchema: "hr",
            principalTable: "employees",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_employee_departments_departments_DepartmentId",
            schema: "hr",
            table: "employee_departments",
            column: "DepartmentId",
            principalSchema: "hr",
            principalTable: "departments",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_employee_departments_employees_EmployeeId",
            schema: "hr",
            table: "employee_departments",
            column: "EmployeeId",
            principalSchema: "hr",
            principalTable: "employees",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_departments_employees_DepartmentHeadId",
            schema: "hr",
            table: "departments");

        migrationBuilder.DropForeignKey(
            name: "FK_employee_departments_departments_DepartmentId",
            schema: "hr",
            table: "employee_departments");

        migrationBuilder.DropForeignKey(
            name: "FK_employee_departments_employees_EmployeeId",
            schema: "hr",
            table: "employee_departments");

        migrationBuilder.DropCheckConstraint(
            name: "ck_employees_contract_type",
            schema: "hr",
            table: "employees");

        migrationBuilder.DropIndex(
            name: "IX_employee_departments_EmployeeId",
            schema: "hr",
            table: "employee_departments");

        migrationBuilder.DropIndex(
            name: "IX_departments_Name",
            schema: "hr",
            table: "departments");

        migrationBuilder.DropColumn(
            name: "StartedAtUtc",
            schema: "hr",
            table: "employee_departments");

        migrationBuilder.CreateIndex(
            name: "IX_departments_Name",
            schema: "hr",
            table: "departments",
            column: "Name",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_employee_departments_departments_DepartmentId",
            schema: "hr",
            table: "employee_departments",
            column: "DepartmentId",
            principalSchema: "hr",
            principalTable: "departments",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_employee_departments_employees_EmployeeId",
            schema: "hr",
            table: "employee_departments",
            column: "EmployeeId",
            principalSchema: "hr",
            principalTable: "employees",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }
}
