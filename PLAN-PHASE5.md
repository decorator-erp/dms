# Phase 5 — Sites module (S1-00 → S2-01)

Continues `PLAN.md` (G0–G3), `PLAN-PHASE2.md` (G4–G6), `PLAN-PHASE3.md` (H1/W1/G7–G8) + E1-01,
`PLAN-PHASE4.md` (G9–G10) + E2 + H2/M1. Same execution contract. **Runs unattended start to finish.**

Head at planning: `a7f5a9e`. Six modules (Identity, Hr, Approvals, Projects, Documents, Crm),
531+ tests, 14 architecture rules, clean tree, on `master`.

Sites is the **seventh module** and the smallest remaining: 8 entities, 5 grouped feature files, 2
endpoint files, 6 permission strings. **10 tasks.**

---

# The four M1-03 lessons, applied structurally

M1-03 recorded four lessons for this plan. They are applied as *plan structure*, not as reminders:

**Lesson 1 — every sub-area named in prose gets a task row, checked before execution.** §"Sub-area →
task reconciliation" below is that diff, done now. Nothing named in this document's prose lacks a task.

**Lesson 2 — "already exists" claims verified against the repo when relied on.** Every such claim below
carries its evidence inline. **This lesson already caught a real defect during planning** — see
structural item 1.

**Lesson 3 — verify clauses diffed against the suite at close-out.** S2-01 carries that diff as a
required deliverable, not an optional check.

**Lesson 4 — a characterization test's wording is a promise.** §S1-09 states exactly what the spine
test will and will not prove, before it is written.

---

# Structural item 1 — `CrmDesignBrief.SiteId` is a fabricated column and must go first

**This is my error from `PLAN-PHASE4.md`, found by applying lesson 2 during this planning pass.**

`PLAN-PHASE4.md` deferred *"`CrmDesignBrief.SiteId` — bare nullable Guid, no FK | Sites"* and instructed
G9-04 to add `SiteId`. Verified against the backend: **`CrmDesignBrief` has no `SiteId` field.** The
real link runs the opposite way — `Site.SourceBriefId` (nullable, `Site.cs:66`), documented there as
*"provenance, not a dependency… written only by `BackfillSitesFromBriefs`; there is deliberately no
domain setter."*

Verified in `dms-modular`: the column exists (`CrmDesignBrief.cs:51`, the `InitialCrm` snapshot, and
`ICrmBriefReader.CrmDesignBriefSummary`). G9-04 recorded the divergence honestly —
*"`SiteId` is new in this port, not present in the backend entity"* — so it is a documented invention,
not an accident. And **nothing writes it**: a search of `Crm.Domain` returns only the property
declaration and two doc-comment lines. It is a dead column.

Left in place, Sites would introduce `Site.SourceBriefId` and the model would carry **two fields for one
relationship, pointing in opposite directions**, with the fabricated one unwritten and the real one
authoritative. Someone will eventually populate the wrong one.

**S1-00 removes it, before Sites' real link lands.** Doing it after would mean both exist simultaneously
in a shipped migration.

# Structural item 2 — `SchemaOrder` is a clean append (verified, not assumed)

Sites' outbound references: `Site.ProjectId` → projects · `Site.SourceBriefId` → crm ·
`SiteDocument.DocumentId` → documents · `SiteDocument.AddedById` → hr.

**Nothing points into sites.** `CrmDesignBrief` has no `SiteId` after S1-00, and no other module
references a site. So unlike CRM — which required the project's first `SchemaOrder` *insertion* — Sites
appends at the end:

`["shared","identity","hr","approvals","crm","projects","documents","sites"]`

# Structural item 3 — rule 9's allow-list needs two new pairs

Verified against `docs/cross-schema-foreign-keys.md`. Current entry is `sites → projects, crm`. Sites
also needs:

- **`sites → documents`** — `SiteDocument.DocumentId`. `SiteDocument`'s own doc comment says it *"mirrors
  `CrmArtifactDocument`: this stores no file and introduces no storage path"* — the same
  metadata-links-to-a-Documents-row shape CRM already uses.
- **`sites → hr`** — `SiteDocument.AddedById`.

Both are additions to `AllowedPairs`, with reasoning, in the same commit as the FKs (S1-05) — the
established procedure from G4-03/G5-04/G7-04/G9-08.

**Note the asymmetry with CRM's artifact link:** `crm.crm_artifact_documents.DocumentId` deliberately has
**no** FK, because CRM migrates *before* documents and the ordering cycle forced dropping it
(`PLAN-PHASE4.md` structural item 1). Sites migrates *after* documents, so `SiteDocument.DocumentId`
**can** carry a real FK. Two structurally identical links, one constrained and one not, for a reason
that is entirely about migration order. Record this explicitly in
`docs/cross-schema-foreign-keys.md` — it will otherwise read as an inconsistency.

---

# Sub-area → task reconciliation (lesson 1, done before execution)

Every Sites sub-area in the backend, mapped to the task that owns it. **No row is unowned.**

| Backend sub-area | Files | Owning task |
|---|---|---|
| `Site`, `SiteRecord`, `SiteRecordValue` entities | 3 | S1-02 |
| `SiteDocument` entity | 1 | S1-02 |
| `SiteModule`, `SiteModuleItem`, `SiteItem`, `SiteItemOption` (definition/master data) | 4 | S1-03 |
| `SiteDomainService` | 1 | S1-02/S1-03 (rules into entities + handlers; **no `*DomainService` class**) |
| `ISiteScoped` + `SiteScopeDecorator` | 2 | S1-05 |
| `SiteModuleValidator` | 1 | S1-03 |
| `SiteReadQueries`, `SiteSchemaCache` | 2 | S1-06 |
| `SiteCommands.cs`, `SiteQueries.cs` | 2 | S1-07 |
| `SiteDefinitionCommands.cs`, `SiteDefinitionQueries.cs` | 2 | S1-06 |
| `SiteDocumentCommands.cs` | 1 | S1-07 |
| `SiteEndpoints`, `SiteDefinitionEndpoints` | 2 | S1-08 |
| `tests/Site.UnitTests` (4 files — the backend's **only** tests) | 4 | S1-02/S1-03 (port + extend) |
| 6 `sites.*` permission strings | — | S1-01 |

---

# Pre-resolved facts — each with its evidence (lesson 2)

- **8 entities.** `Site`, `SiteRecord`, `SiteRecordValue`, `SiteDocument`, `SiteModule`,
  `SiteModuleItem`, `SiteItem`, `SiteItemOption`. ✓ verified against `backend/Domain/Entities/`.
- **6 permissions**, verified by grep: `sites.{approve,create,delete,read,submit,update}`. No
  `sites.manage`. Total after Sites: **62**.
- **`SiteDocument` stores no file.** ✓ its own doc comment. Links a `documents.project_documents` row by
  id, exactly as `CrmArtifactDocument` does. **No `IFileStorageService` reference, no upload commands,
  no hardening work** — the same conclusion Phase 4 reached for CRM artifacts.
- **`Site.SourceBriefId` has no domain setter.** ✓ `Site.cs:57-66` — *"provenance, not a dependency…
  written only by `BackfillSitesFromBriefs`."* That backfill is a backend migration utility and is
  **not ported**. The column exists and stays null in this system until something writes it. Record as a
  scope boundary.
- **Sites has its own approval lifecycle, not the Approvals module's.** `sites.submit`/`sites.approve`
  exist as permissions, and `ApprovalSubjectType` has no `Site` value (✓ verified in Phase 2's enum port
  — `GateCondition=1, DesignBrief=2, RequirementItem=3, BriefSpace=4, ProjectDocument=5`). Sites uses
  its own `SiteStatus` (Draft/Submitted/Approved). **Do not route it through `IApprovalRequester`** —
  that would be a design change this plan is not asking for.
- **`ISiteScoped` is the pattern this repo already copied twice.** `ProjectScopeDecorator` (G5-07) and
  `CrmScopeDecorator` (G9-08) are both descendants of the backend's `SiteScopeDecorator`. Sites gets the
  original, mirroring the two existing implementations rather than the backend directly.
- **The backend's only test project is `tests/Site.UnitTests`** (4 files). ✓ verified. This is the one
  module with tests to port rather than write from scratch — audit and extend, per M1-01's precedent
  with `CrmMeetingTests`.

---

# Scope boundaries

| Deferred | Owner |
|---|---|
| `BackfillSitesFromBriefs` — a backend data-migration utility; `SourceBriefId` stays null | not ported; revisit only if a real backfill is needed |
| Site → Approvals-module integration (Sites keeps its own submit/approve lifecycle) | product decision, not this phase |
| `DecideMyPortalApprovalCommand` (still open from M1-02) | Approvals |

---

# Tasks

### S1-00 — remove the fabricated `CrmDesignBrief.SiteId`

Structural item 1. Drop the property, its EF configuration, and `CrmDesignBriefSummary.SiteId` from
`ICrmBriefReader`; add a CRM migration dropping the column. Nothing writes it, so no data path changes.
Correct `PLAN-PHASE4.md`'s deferred row and `PROGRESS.md`'s G9-04 entry to record that the field was an
error in the plan, not a design choice — and that Sites' `SourceBriefId` is the real link.

**Verify:** full gate green; `CrmDbContext` probe migration contains exactly the column drop; no
reference to `CrmDesignBriefSummary.SiteId` remains.

### S1-01 — `Sites.Contracts`

`ISiteReadQueries`, `ISiteSchemaCache`, `ISiteModuleValidator`, `ISiteScoped`, `SitesPermissions`
(6 strings verbatim), `SiteSummary`/`SiteRecordSummary` DTOs, integration events
(`SiteSubmitted`, `SiteApproved` — IDs and primitives only), and commands/queries/DTOs for every feature
in the reconciliation table, grouped per-noun.

**Contracts are validated against handler needs in this task, not later.** M1-01's whole first section
existed because G9-01 wrote twelve records with no handler to check them. Read the backend's five
feature files *before* writing the contract records, and shape them to what the logic actually needs.

**Verify:** references only SharedKernel + BuildingBlocks + `Documents.Contracts` +
`Projects.Contracts`. Rules 5 and 10 green.

### S1-02 — `Sites.Domain` — instances

`Site`, `SiteRecord`, `SiteRecordValue`, `SiteDocument`. `SiteStatus`, `SiteDocumentKind`,
`SiteItemDataType` enums. `ProjectId`/`SourceBriefId`/`DocumentId`/`AddedById` all bare Guids, no
navigations. `SiteErrors`.

Port `tests/Site.UnitTests/SiteLifecycleTests.cs` + `SiteHarness.cs`; audit and extend for illegal
transitions, asserting specific errors (never bare `IsFailure`), per `CrmMeetingTests`' style.

`SiteDomainService`'s rules go into entity methods where pure and handlers where they orchestrate. **No
`*DomainService` class taking a DbContext** — the naming trap from G5-02, now a standing rule.

### S1-03 — `Sites.Domain` — definition (master data) + governance

`SiteModule`, `SiteModuleItem`, `SiteItem`, `SiteItemOption` in a `Definition/` folder — global master
data, deliberately **not** `ISiteScoped` (the backend's `SiteEndpoints` doc comment says so explicitly:
*"the site definition layer… is global master data with no project at all, and is deliberately outside
this"*). `SiteModuleValidator`.

Port `SiteDefinitionGovernanceTests.cs`; extend.

### S1-04 — `Sites.Infrastructure` — persistence

`SitesDbContext`, schema `sites`, `ConfigureOutbox(ownsMigration: false)`, one configuration per entity
— **count them and assert the count against registered `DbSet`s** (the G9-07 precedent that caught a
missing configuration). Initial migration. **Append `"sites"`** to `SchemaOrder` (structural item 2).

Three hand-maintained lists take the new schema — `ModuleDependencyOrder.cs`, `ApiFixture.cs`'s
`MigrateSchemaAsync` switch, `ApiFixture.cs`'s `RespawnerOptions.SchemasToInclude`. Add the four
projects to `DMS.Modular.slnx` **and** `Architecture.Tests.csproj` before writing constrained code;
`Assembly.Load("Sites.Infrastructure")` into `Program.cs`; `Api.csproj` references to both
`Sites.Infrastructure` and `Sites.Application`.

**Verify:** eight schemas apply clean from empty; probe migration empty; rules 1/4/6/8a fault-injected.

### S1-05 — Cross-schema FKs, allow-list, and `SiteScopeDecorator`

FKs via `migrationBuilder.Sql`, `ON DELETE RESTRICT`, each indexed: `sites.sites.ProjectId →
projects.projects`, `sites.site_documents.DocumentId → documents.project_documents`,
`sites.site_documents.AddedById → hr.employees`. `SourceBriefId` → **no FK** (nullable provenance
column, never written; document the absence).

Add `("sites","documents")` and `("sites","hr")` to rule 9's `AllowedPairs` and to
`docs/cross-schema-foreign-keys.md`, **including the CRM-artifact asymmetry note** from structural
item 3. Fault-inject rule 9 once.

`SiteScopeDecorator` mirroring `ProjectScopeDecorator`/`CrmScopeDecorator`: resolve site → project,
call `IProjectAccessGuard`, **404 for unresolvable** (the established authorization order in
`CLAUDE.md`). Registered in Sites' own pipeline only.

### S1-06 — `Sites.Application` — definition features

`SiteDefinitionCommands.cs` + `SiteDefinitionQueries.cs`. `SiteReadQueries`, `SiteSchemaCache`.
The cache's invalidation-on-definition-change is the part to test hardest — a stale schema cache after a
definition edit is the failure mode this component exists to create.

### S1-07 — `Sites.Application` — instances, records, documents

`SiteCommands.cs`, `SiteQueries.cs`, `SiteDocumentCommands.cs`. The submit/approve lifecycle publishes
`SiteSubmitted`/`SiteApproved` via the outbox. Site-document creation validates `DocumentId` against
`Documents.Contracts.IDocumentQueries` **and** carries the FK — belt and braces, unlike CRM artifacts
which have only the application check.

Expect backend bugs; read each handler before porting; record each divergence.

### S1-08 — Endpoints + module registration

`SitesModule : IModule`; `SiteEndpoints`, `SiteDefinitionEndpoints`. Note `MapModuleEndpoints`
auto-discovers `IEndpoint` by assembly scan (✓ verified at M1-02) — no `SitesModule` edit for
registration. Permission-gate each route with the 6 ported strings.

**Verify:** app boots with **seven** modules (assert by name, per M1-02's new test);
`GET /api/permissions` returns **62**; `/scalar` lists site routes (the test M1-02 introduced).

### S1-09 — Integration suite + seven-module smoke

Characterization: project → site created from a definition → records captured → document attached →
submit → approve. Run twice.

**What this test will and will not prove (lesson 4), stated before writing it:** it proves the site
lifecycle end-to-end through real HTTP, and that `SiteDocument` links a real Documents row. It does
**not** prove `SourceBriefId` provenance — nothing writes that column in this system (scope boundary
above). If a later reader expects the spine to cover brief→site provenance, this sentence is why it
does not.

Extend `CrossModuleSmokeTests` to seven modules. Schema-isolation assertion: no `SitesDbContext` SQL
touches another module's schema.

---

# S2-01 — Close-out

`docs/adding-a-module.md`: the append-vs-insert `SchemaOrder` distinction now that both cases exist; the
constrained-vs-unconstrained document-link asymmetry and why; global master data as a deliberate
`ISiteScoped` exemption.

`docs/cross-schema-foreign-keys.md`: the two new pairs, the `SourceBriefId` absent FK, the CRM-artifact
asymmetry.

`PROGRESS.md`: phase boundary — seven non-negotiables re-checked against seven modules; deferred table
updated; the S1-00 correction recorded as a **plan error found by applying lesson 2**, which is the
lesson demonstrating its own value.

**Required deliverables (lessons 1 and 3), not optional checks:**

1. **Sub-area → task reconciliation, re-run.** The table above, checked against what actually shipped.
2. **Verify-clause diff.** Every `**Verify:**` clause in this document, checked against a real test in
   the suite. `PLAN-PHASE4.md:311`'s `/scalar` requirement was silently never written and went unnoticed
   for a full phase; this is the step that catches that class.

Then: what Sites taught that should change the Finance plan.

---

# Execution contract

Per task: restate → implement → task verify → full gate → ≤3 attempts → one commit
`"<TASK-ID>: <what>"` → one `PROGRESS.md` line → next task immediately, no pauses, through S2-01.

```
DOCKER_HOST=tcp://localhost:2375 dotnet build DMS.Modular.slnx -warnaserror
DOCKER_HOST=tcp://localhost:2375 dotnet test DMS.Modular.slnx
DOCKER_HOST=tcp://localhost:2375 dotnet format DMS.Modular.slnx --verify-no-changes
```

**Precondition:** Docker running with the TCP daemon exposed. Phase 4 shipped 32% unverified because
this was skipped — do not start without it.

**Hard stops:** a verify failing 3 attempts; any fix requiring an architecture rule relaxed, a test
deleted, an internal type made public, or a forbidden reference; ambiguity that changes an outcome; any
write under `..\backend`; any module or stub beyond Sites; routing Sites' lifecycle through the
Approvals module; reopening F2 or any closed decision.

**Standing instruction:** when a change alters what an *existing* test proves, correct that test's doc
comment and cover the branch it stopped exercising (E1-01's finding).
