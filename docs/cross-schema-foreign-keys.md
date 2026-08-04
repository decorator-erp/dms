# Foreign keys across module schemas

## Short answer

**Keep them.** PostgreSQL supports foreign keys across schemas in the same database with no penalty —
`REFERENCES identity.users(id)` from `hr.employees` works exactly like a same-schema FK. Same planner,
same index usage, same transaction.

What changes is that **EF Core stops knowing about them**. The FK lives in the database; the model has a
plain `Guid`/`string` column. That is the module boundary — and it is the point.

```
┌─ EF model ────────────────────────────────┐   ┌─ database ───────────────────────┐
│ HrDbContext                                │   │ hr.employees                     │
│   Employee.AppUserId  →  string, indexed   │   │   app_user_id text NOT NULL      │
│   (no navigation, no HasOne, no Include)   │   │   FK → identity.users(id)        │
│                                            │   │   ON DELETE RESTRICT             │
│ IdentityDbContext                          │   │ identity.users                   │
│   ApplicationUser  (knows nothing of HR)   │   │   id text PK                     │
└────────────────────────────────────────────┘   └──────────────────────────────────┘
```

## Why keep them

You have **one database and one connection string**. Dropping referential integrity is a cost you pay
in microservices to buy independent deployment and independent failure domains. You are not buying
either. So you would be paying the cost for nothing:

- Orphan rows appear the first time a delete path is missed, and nothing tells you.
- Bad data is silent and cumulative. You find it months later, in a report.
- The DB can no longer help you — no `ON DELETE RESTRICT`, no integrity check on restore.

Dropping the FK also **does not decouple anything**. The coupling is `hr.employees.app_user_id` pointing
at a user id. That coupling exists whether or not a constraint enforces it. Deleting the constraint
removes the safety net, not the dependency.

If a module is ever extracted into a real service, dropping its FKs is a one-line migration at that
point. Keeping them now costs nothing and locks nothing in.

## The rule that makes this safe

> **A foreign key may only point in the same direction as an allowed module dependency.**

`hr.employees → identity.users` is fine: HR depends on Identity.
`identity.users → hr.employees` would be a violation, even though the database would happily accept it.

This is checkable. Add it to `Architecture.Tests` as an integration test against a migrated database:

```sql
SELECT
    src_ns.nspname  AS from_schema,
    tgt_ns.nspname  AS to_schema,
    con.conname     AS constraint_name
FROM pg_constraint con
JOIN pg_class      src    ON src.oid    = con.conrelid
JOIN pg_namespace  src_ns ON src_ns.oid = src.relnamespace
JOIN pg_class      tgt    ON tgt.oid    = con.confrelid
JOIN pg_namespace  tgt_ns ON tgt_ns.oid = tgt.relnamespace
WHERE con.contype = 'f'
  AND src_ns.nspname <> tgt_ns.nspname;
```

Assert every returned `(from_schema → to_schema)` pair is in the allow-list derived from the module
graph. A new FK pointing the wrong way fails the build — which is exactly when you want to hear about it,
not after it is in production.

Allowed pairs for the current design:

```
hr        → identity
approvals → identity
crm       → identity, hr
sites     → projects, crm
projects  → identity, hr, crm, approvals
finance   → projects, crm
documents → projects, hr, approvals
notify    → identity
(shared.outbox / shared.inbox reference nothing)
```

`approvals → identity` (added at G4-03, dms-modular Phase 2): `approvals.approvals."RequestedByUserId" →
identity.users("Id")`, `ON DELETE RESTRICT`. Approvals models the requester as a bare Identity
`ApplicationUser` id rather than an HR `Employee` id (a deliberate divergence from the backend this
module was ported from, which used an `Employee` FK) — Approvals is a schema-isolated sink module with no
reason to depend on HR at all, and the calling module (e.g. Projects) already resolves `Employee →
AppUserId` itself via `Hr.Contracts.IHrDirectory` before requesting an approval. Each eligible approver
(`approval_approvers.ApproverUserId`) is the same kind of id but deliberately carries **no** FK — it's a
row in an EF-owned collection table, not its own aggregate, and a per-row FK would only duplicate the same
constraint the `RequestedByUserId` FK already established for this table's write path.

`projects → hr` and `projects → approvals` (added at G5-04, dms-modular Phase 2): 13 FKs total, all
`ON DELETE RESTRICT` — 11 into `hr.employees`/`hr.departments` (`fk_projects_owner`,
`fk_project_tasks_assigned_to`, `fk_project_tasks_department`, `fk_project_stages_completed_by`,
`fk_project_sub_tasks_completed_by`, `fk_task_comments_employee`, `fk_stage_teams_head`,
`fk_stage_teams_department`, `fk_stage_team_members_employee`, `fk_stage_tasks_department`,
`fk_stage_departments_department` — every bare Guid the domain model documents as "→ hr.employees/
hr.departments, never a navigation" gets the real constraint) and 2 into `approvals.approvals`
(`fk_gate_conditions_resolved_by`, `fk_gate_conditions_approval` — a `GateCondition` links to the
approval it requested and to whoever resolved it). `projects.projects."ClientId"` deliberately has no
FK at all — CRM (the module that would own `clients`) doesn't exist yet; the column is a bare
unvalidated `Guid`, same treatment `Approvals.RequestedByUserId` would need if Approvals ever gained a
client-side actor. Rule 9's own scratch-FK fault-injection (G5-04) proved the check catches a
wrong-direction FK for real, not just by inspection — see that task's PROGRESS.md entry.

## The three things that actually need care

### 1. Delete behaviour — never cascade across a schema boundary

`ON DELETE CASCADE` across modules means one module's delete silently mutates another module's data,
outside its own domain rules, with no event raised. That is worse than an orphan.

```sql
-- correct
ALTER TABLE hr.employees
    ADD CONSTRAINT fk_employees_app_user
    FOREIGN KEY (app_user_id) REFERENCES identity.users(id)
    ON DELETE RESTRICT;
```

`RESTRICT` turns "delete a user who still has an employee record" into an error at the boundary, which is
the correct answer. The owning module then decides: block the delete, soft-delete, or publish a
`UserDeactivated` integration event and let HR react on its own terms.

This is low-friction in practice because the existing codebase already soft-deletes almost everywhere
(`IsActive` plus query filters). Hard deletes are rare, so `RESTRICT` rarely fires.

### 2. Migration ordering

Each module owns its own migrations. `hr`'s initial migration cannot create an FK to `identity.users` if
that table does not exist yet.

Two options:

**(a) Ordered startup migration — simplest, recommended.** Run migrations in module dependency order.
The `IModule` contract already gives you the hook:

```csharp
// Api/Program.cs — topological order, not registration order
string[] order = ["shared", "identity", "hr", "approvals", "crm", "sites",
                  "projects", "documents", "finance", "notify"];
foreach (var name in order)
    await app.Services.MigrateModuleAsync(name);
```

**(b) Constraints-last migration.** Every module creates its tables with plain columns; one final
migration in the host adds all cross-schema constraints. Cleaner ordering, but it puts a module's
constraints outside the module — worse ownership.

Take (a). Add the order list to `Api` with a comment saying it must match the module dependency graph.

### 3. Writing the constraint — EF will not do it for you

EF only emits an FK when both entities are in the same model. They are not. So write it explicitly in
the module's migration:

```csharp
public partial class InitialHr : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ... CreateTable for hr.employees ...

        migrationBuilder.Sql("""
            ALTER TABLE hr.employees
                ADD CONSTRAINT fk_employees_app_user
                FOREIGN KEY (app_user_id) REFERENCES identity.users(id)
                ON DELETE RESTRICT;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE hr.employees DROP CONSTRAINT fk_employees_app_user;");
}
```

And in the EF configuration, the column is just a column:

```csharp
builder.Property(e => e.AppUserId).IsRequired().HasMaxLength(450);
builder.HasIndex(e => e.AppUserId);   // ResolveEmployeeIdAsync filters on this constantly
// NO HasOne(...).WithMany(...) — ApplicationUser is not in this model
```

**Index the FK column yourself.** Postgres indexes the *referenced* side (it is a PK) but not the
*referencing* side. EF would normally add that index as part of `HasOne`; since you are not using
`HasOne`, it is on you. A missing index here is a slow `RESTRICT` check on every delete and a slow lookup
on every access check.

## When to drop an FK instead

Three cases where the constraint genuinely gets in the way:

| Case | Why | Do instead |
|---|---|---|
| The reference is optional and often dangling by design (audit rows referencing deleted entities) | `RESTRICT` blocks legitimate deletes | No FK; keep the id, tolerate dangling |
| The target is high-churn and the source is append-only (event/outbox payload ids) | Constraint adds write cost for no read benefit | No FK |
| You have concretely decided to extract this module to a service | The FK is the last thing to remove | Drop it in the extraction PR, not before |

`shared.outbox_messages` and `shared.inbox_messages` fall in the second row — they reference nothing.

## What this does *not* let you do

Keeping the FK does **not** license a cross-schema join in application code. The constraint is a data
integrity guarantee for the database. The module boundary is still:

- no EF navigation across modules
- no `Include` across schemas
- no `JOIN identity.users` inside an HR query

Cross-module reads go through `IUserDirectory` / `IHrDirectory`. The only place allowed to join across
schemas is the `reporting` module.

Those are two different concerns that happen to touch the same tables. Keeping the FK protects your data;
banning the join protects your architecture. You want both.
