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
sites     → projects, crm, documents, hr
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

`projects.gate_conditions."ReferencedDocumentId"` (added G7-02, Phase 3) is the same pattern one level
later: it would point at `documents.project_documents`, but `projects → documents` runs the wrong way —
Documents depends on Projects (`documents → projects` is in the allow-list below), so a reverse FK would
invert the module graph. Bare unvalidated `Guid`, validated in application code by Documents' own
`DocumentApprovedHandler` against its own `DbContext`, never by the database. A deliberately-absent FK
gets recorded here for the same reason a present one does — so the next reader finds a decision, not an
oversight.

`documents → projects` and `documents → hr` (added at G7-04, Phase 3): 7 FKs total, all
`ON DELETE RESTRICT` — 3 into `projects.projects`/`projects.project_stages`/`projects.project_tasks`
from `documents.project_documents` (`fk_project_documents_project`,
`fk_project_documents_project_stage`, `fk_project_documents_project_task`), 1 into `hr.employees`
(`fk_project_documents_uploaded_by`), and the identical 3-into-`projects.*` shape again from
`documents.document_folders` (`fk_document_folders_project`, `fk_document_folders_project_stage`,
`fk_document_folders_project_task`) — a folder and a document share the exact same project/stage/task
scoping shape, so their FK sets mirror each other one-for-one. `document_folders.ParentFolderId` and
`project_documents.FolderId` are intra-schema same-schema FKs to `document_folders`, already configured
at G7-03 — EF emits them normally, they were never part of this task.
`project_documents.RootDocumentId` (the lineage self-reference) is intra-schema too, but was actually
*missing* from G7-03's configuration — caught while writing this section, added here as a small
correction rather than a separate task: a real EF-managed FK, `ON DELETE CASCADE`, in its own migration
(`AddProjectDocumentRootDocumentIdForeignKey`).

`crm → identity` and `crm → hr` (added at G9-08, Phase 4): nine FKs total, all `ON DELETE RESTRICT` — 1
into `identity.users` (`fk_clients_app_user`, `crm.clients."AppUserId"`) and 8 into `hr.employees`
(`fk_crm_leads_owner`, `fk_crm_meetings_requested_by_employee`,
`fk_crm_meeting_slots_proposed_by_employee`, `fk_crm_design_briefs_created_by`,
`fk_crm_brief_spaces_created_by`, `fk_crm_requirement_periods_opened_by`,
`fk_crm_requirement_items_created_by_employee`, `fk_crm_client_notes_written_by`,
`fk_crm_artifact_documents_added_by_employee` — every bare Guid the CRM domain model documents as "→
hr.employees, never a navigation" gets the real constraint, mirroring `projects → hr`'s own G5-04
precedent). `crm.crm_contacts`/`crm.crm_meetings`/`crm.crm_meeting_slots`/`crm.crm_requirement_items`
also carry a same-schema `Guid?` alternate-author column (`AddedByContactId`,
`RequestedByContactId`/`ProposedByContactId`, `CreatedByContactId`) — real EF-managed FKs to
`crm.crm_contacts`, configured normally, never part of this cross-schema migration.

`projects → crm` (added at G9-13, Phase 4): one FK, `ON DELETE RESTRICT` —
`fk_projects_client`, `projects.projects."ClientId"` → `crm.clients("Id")`, in its own
`AddClientForeignKey` migration (run after `crm`'s own `InitialCrm`/`CrossSchemaForeignKeys`
migrations, per `ModuleDependencyOrder.SchemaOrder`'s `crm` insertion ahead of `projects`). Closes the
Phase 2 deferral this section already flagged: `Project.ClientId` was a bare unvalidated `Guid` because
CRM did not exist. Application-code validation sits alongside the constraint, not instead of it —
`CreateProjectCommandHandler` calls `Crm.Contracts.IClientDirectory.ExistAsync` before
`Project.Create` is ever invoked, so an unknown client is rejected with a 404-style domain error before
the insert reaches the database; the FK is the backstop for every other write path (migrations,
scripts, future callers) that doesn't go through that handler.

**`crm → projects` does not exist, and cannot, for a structural reason distinct from every other
absent FK recorded in this document.** Every prior deliberate absence (`projects.ClientId` before this
phase, `GateCondition.ReferencedDocumentId`) was a case where the FK direction would have inverted the
module dependency graph — the target module depended on the source, not the reverse. CRM's case is the
opposite: `projects → crm` (the direction `Project.ClientId` finally closes at G9-13) is correct and IS
in the allow-list below. The absence lives entirely on CRM's own outward-pointing columns instead —
`crm.crm_meetings.ProjectId`, `crm.crm_design_briefs.ProjectId`, `crm.crm_spaces.ProjectId`,
`crm.crm_brief_head_snapshots.ProjectId`, `crm.crm_requirement_periods.ProjectId`/`ProjectStageId`,
`crm.crm_requirement_items.ProjectId`/`ProjectTaskId`, `crm.crm_client_notes.ProjectId`,
`crm.crm_brief_spaces.ProjectTaskId` — all bare, unvalidated `Guid`/`Guid?` columns, all deliberately
absent an FK, and none of them will ever gain one through the normal "next migration adds it" path,
because **`crm` precedes `projects` in `ModuleDependencyOrder.SchemaOrder`** (the phase's structural
item 1: `projects.ClientId → crm.clients.Id` requires `crm` before `projects`, and
`documents.project_documents.ProjectId → projects.projects.Id` already requires `projects` before
`documents`, so `crm` cannot come after `projects` without breaking the other constraint). The
`projects.*` tables simply do not exist yet when CRM's own migration runs — this is enforced by
migration order, not merely by rule 9's allow-list (which also never gained a `("crm", "projects")`
pair, for the same reason). Validated where it matters in application code: CRM's gate handlers and
`ICrmGateReader` (G9-13) work in `projectStageId`/`projectTaskId` terms exactly as
`GateCondition.ReferencedDocumentId` does for Documents, trusting the id without a constraint behind
it.

**`sites → projects`, `sites → hr`, `sites → crm`, `sites → documents` (added at S1-05, Phase 5).**
Seven constraints, all raw SQL in `20260816164226_SitesCrossSchemaForeignKeys`, all `ON DELETE
RESTRICT`, all with quoted PascalCase columns:

- `fk_sites_project` — `sites.sites."ProjectId" → projects.projects("Id")`
- `fk_sites_created_by` — `sites.sites."CreatedById" → hr.employees("Id")`
- `fk_sites_decided_by` — `sites.sites."DecidedById" → hr.employees("Id")` (nullable)
- `fk_site_documents_added_by` — `sites.site_documents."AddedById" → hr.employees("Id")`
- `fk_site_documents_document` — `sites.site_documents."DocumentId" → documents.project_documents("Id")`
- `fk_site_records_space` — `sites.site_records."SpaceId" → crm.crm_spaces("Id")` (nullable)
- `fk_site_records_catalog_item` — `sites.site_records."CatalogItemId" → crm.crm_catalog_items("Id")` (nullable)

`("sites","documents")` and `("sites","hr")` are the two new pairs in rule 9's allow-list;
`("sites","projects")` and `("sites","crm")` were already there, declared before the module existed.

**Read `fk_site_documents_document` against `crm.crm_artifact_documents.DocumentId` directly above, or
the pair will read as an inconsistency.** They are the same link — a module-owned row pointing at a
`documents.project_documents` id, storing no file, mirroring the other's shape deliberately. One has a
real foreign key and the other can never have one, and the entire difference is migration order:
`crm` precedes `documents`, so CRM's own migration runs before the table it would reference exists;
`sites` is last in `SchemaOrder`, so every schema it points at is already there. Nothing about the two
relationships differs in importance, ownership, or risk. If a future refactor moves `sites` earlier,
`fk_site_documents_document` has to go the same way CRM's did.

**Two of these diverge from the backend, which uses `CASCADE`.** `sites.sites."ProjectId"` and
`sites.site_documents."DocumentId"` are `ON DELETE CASCADE` in the source system. Rule 9 permits only
`RESTRICT` across a schema boundary — a delete in one module silently destroying another module's rows
is exactly what the rule exists to prevent — so both were changed. The practical consequence is
visible: deleting a project that has surveys is now refused rather than taking the surveys with it, and
the caller has to remove them deliberately.

**`sites.sites."SourceBriefId"` deliberately has no FK, and this absence is different from the two
above.** It is provenance rather than a dependency, nothing in this system writes it
(`BackfillSitesFromBriefs` is a backend data-migration utility that was not ported), and the backend
makes it `ON DELETE SET NULL` precisely so that deleting a brief leaves the survey intact. Rule 9
permits only `RESTRICT`, and `RESTRICT` here would be actively wrong: a project whose design brief was
ever deleted would become permanently undeletable, and the error would surface inside the projects
module against a `sites` table its owner has never heard of. Absent is the correct answer, not a gap to
close. The partial unique index `ux_sites_source_brief` still stands, so if a backfill ever does
populate the column, one brief cannot seed two surveys.

**The `SpaceId` FK does not make `ICrmSpaceReader` redundant.** `fk_site_records_space` proves the
space row exists; it cannot prove the space belongs to the same project as the site, which is the rule
`SiteErrors.SpaceNotInProject` states and the one the backend enforces with an application query. No
constraint Postgres offers can express it without a trigger, and this repo has none — so the FK and the
`Crm.Contracts.ICrmSpaceReader` check at S1-07 are belt and braces on two different failure modes.

**`crm.crm_artifact_documents.DocumentId` has no FK either — the migration-order cycle from structural
item 1, one hop further out.** The three desired FKs (`projects.ClientId → crm.clients.Id`,
`documents.project_documents.ProjectId → projects.projects.Id`, and
`crm.crm_artifact_documents.DocumentId → documents.project_documents.Id`) form an unsatisfiable order:
`crm < projects < documents < crm`. Keeping `projects → crm` (the core business relationship, closing a
Phase 2 deferral) and dropping `crm → documents` (a metadata link on a join table) was the deliberate
trade — see `PLAN-PHASE4.md`'s structural item 1 for the full reasoning. Validated in
`Crm.Application`'s `LinkCrmArtifactDocumentCommandHandler` (G9-12) against
`Documents.Contracts.IDocumentQueries.GetAsync` before the row is created — the entire replacement for
the dropped constraint; a test at G9-12 asserts an unknown `DocumentId` is rejected, since that check
is now the only thing standing where a constraint would have been.

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
// Corrected at S1-05: this list previously placed "sites" before "projects", which contradicts
// the allow-list above -- sites -> projects means projects must migrate FIRST. The shipped order in
// ModuleDependencyOrder.SchemaOrder is authoritative; sites is last.
string[] order = ["shared", "identity", "hr", "approvals", "crm", "projects",
                  "documents", "sites", "finance", "notify"];
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
