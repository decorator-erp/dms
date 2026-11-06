namespace Projects.Infrastructure.Persistence.Migrations.InitialProjects;

/// <summary>
/// The hand-written SQL of the crm-complete Projects migration (<see cref="CrmCompleteProjects"/>), kept
/// apart from the EF-generated file so the migration can be regenerated without losing it.
/// <list type="bullet">
/// <item><see cref="Guard"/> runs first: it refuses to run against a database that already has the
/// tables or columns this migration creates (a half-applied or hand-patched schema) instead of failing
/// halfway through.</item>
/// <item><see cref="ForeignKeys"/>: the cross-schema FKs EF can't emit (two DbContext models), same shape as
/// every other <c>hr.employees</c> FK in this module: <c>ON DELETE RESTRICT</c>, an index on the
/// referencing column (created by EF). It is preceded by an orphan check that raises before the DDL.</item>
/// </list>
/// Additive only: no data is dropped or rewritten.
/// </summary>
internal static class CrmCompleteProjectsSql
{
    public const string Guard =
        """
        DO $$
        BEGIN
            IF to_regclass('projects.project_members') IS NOT NULL THEN
                RAISE EXCEPTION 'crm-complete: projects.project_members already exists; refusing to re-create it';
            END IF;
            IF EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_schema = 'projects' AND table_name = 'task_comments' AND column_name = 'EditedAtUtc') THEN
                RAISE EXCEPTION 'crm-complete: projects.task_comments."EditedAtUtc" already exists';
            END IF;
            IF to_regclass('hr.employees') IS NULL THEN
                RAISE EXCEPTION 'crm-complete: hr.employees is missing; the HR migrations must run first';
            END IF;
        END $$;
        """;

    public const string ForeignKeys =
        """
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM projects.project_members m
                       WHERE NOT EXISTS (SELECT 1 FROM hr.employees e WHERE e."Id" = m."EmployeeId")) THEN
                RAISE EXCEPTION 'crm-complete: project_members has rows with no hr.employees match';
            END IF;
        END $$;

        ALTER TABLE projects.project_members
            ADD CONSTRAINT fk_project_members_employee
            FOREIGN KEY ("EmployeeId") REFERENCES hr.employees("Id")
            ON DELETE RESTRICT;
        """;

    public const string DropForeignKeys =
        """
        ALTER TABLE projects.project_members DROP CONSTRAINT IF EXISTS fk_project_members_employee;
        """;
}
