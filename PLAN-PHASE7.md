# Phase 7 — Finance (F1-00 → F2-01)

Continues Phases 1–6 + E1/E2/H2/M1/P5F/PC. Same execution contract.

Head at planning: `3d5dd01` (+ the PC sweep). Eight modules, 846 tests across 26 suites, 19
architecture facts, clean tree.

**Scale (verified, dot-tolerant patterns, no `head` truncation):** 9 entities · **39 feature files**
(Variations 11 · Invoices 6 · LedgerEntries 6 · CostCategories 6 · BudgetLines 5 · BoqSnapshots 4 ·
Finance 1) · 10 endpoint files · **12 permission strings** · 6 domain services. **12 tasks.**

**This is the last business module.** After it, only Reporting remains — and Finance closes three
things that have been open since Phase 2.

---

# What Finance closes

| Open since | Item |
|---|---|
| Phase 2 | **`ConditionType.Budget`** — the last unimplemented gate type. After F1-08, all ten have handlers. |
| Phase 6 | **`InvoiceDueJob`** — deferred because it reads `Invoices`. Becomes the 8th `IPeriodicJob` on the one timer. |
| Phase 2 | The `Project*FinancialEndpoints` families, deferred from Projects because Finance owned the data. |

The Budget closure gets a **milestone verify**: no `ConditionType` value returns
`UnsupportedConditionType`. That assertion can only be written once, and this is the phase.

---

# Four structural items — resolve before code

## 1. `GateCondition.VariationId` — a Projects change, budgeted explicitly

Phase 4's lesson 1 says budget a `GateCondition` extension as its own item rather than a footnote,
because it crosses two modules and carries a migration. **It applies here.** Verified:

- `backend/Domain/Entities/GateCondition.cs:28-29` has `Guid? VariationId` + a `Variation?`
  navigation, with `LinkToVariation(Guid)` at `:78`.
- `dms-modular`'s `Projects.Domain/Gates/GateCondition.cs` has **no** `VariationId` — Phase 2 built it
  correctly, since Finance did not exist.

**But it is not a `GateConditionContext` change.** `BudgetHandler.ValidateAsync` returns
`Result.Success()` unconditionally — its own doc comment says *"Creation needs no references;
satisfaction is driven by the ledger and variation domain services as entries and variations post."*
So the context record is untouched; only the **persisted column** is needed, for satisfaction linkage.
Exactly the `ReferencedDocumentId` shape from Phase 3 (A1), and the distinction matters: validation
needs nothing, satisfaction needs to remember.

**F1-00 adds it, before Finance exists** — property, factory, EF configuration, Projects migration.

**No FK.** It would point at `finance.variations`, and Finance depends on Projects
(`BudgetLine.ProjectId`, `Invoice.ProjectId`, …), so a `projects → finance` constraint makes the
ordering unsatisfiable. This is the **fourth** instance of the same treatment — `projects.ClientId`
(pre-Phase 4), `GateCondition.ReferencedDocumentId` (Phase 3), `crm_artifact_documents.DocumentId`
(Phase 4), and now this. Document the deliberate absence in the migration and in
`docs/cross-schema-foreign-keys.md`; by now the pattern deserves a named entry there rather than a
fourth ad-hoc paragraph.

## 2. `SchemaOrder` — append, and reconcile the doc

Finance references `projects` and `crm`, both earlier. Nothing points into `finance` once
`VariationId` carries no FK. **Append `"finance"`.**

`docs/cross-schema-foreign-keys.md:271`'s illustrative order lists `finance` *before* `notify`, and
Phase 6's plan noted *"Finance inserts before it later."* There is no `finance ↔ notify` FK, so the
order between them is functionally irrelevant — but the doc and `SchemaOrder` must not disagree.
**Append, then fix the doc's illustrative order to match**, and say in the commit that the choice was
arbitrary and made for consistency rather than correctness.

## 3. The bidirectional Contracts pair — Finance ↔ Projects

`Finance.Infrastructure → Projects.Contracts` (implements `IGateConditionTypeHandler` for Budget).
`Projects.Infrastructure → Finance.Contracts` (Budget-gate satisfaction is *"driven by the ledger and
variation domain services as entries and variations post"* — so Finance publishes and Projects
subscribes, or Projects reads through a Finance contract).

**Decide which at F1-01, not mid-run.** The Documents precedent (Phase 3, structural item 2) is
publish-and-subscribe; the CRM precedent (Phase 4) is a read contract (`ICrmGateReader`) *plus* events.
Budget satisfaction is a **computation over ledger state**, which is closer to `ICrmGateReader`'s shape
than to an event — but the trigger (a payment posting, a variation being signed) is an event. Likely
both, as CRM did. Name both directions in F1-01 and record the reasoning.

## 4. Money — the one domain risk no prior module had

Every entity here carries currency amounts. Three things to settle at F1-02/F1-03 and assert:

- **`decimal` precision and scale on every money column.** Port the backend's `HasPrecision(...)`
  exactly; an unspecified scale silently truncates. This is a raw-SQL-adjacent concern — the R3
  named-list guard should cover money column definitions the same way it covers constraints.
- **Ledger invariants.** `LedgerEntry`/`LedgerAllocation` is double-entry-adjacent; allocation totals
  must never exceed the entry, and reversal must be exact. Unit-test the invariants, not just the
  happy path.
- **Rounding.** Wherever the backend rounds (BOQ generation, variation application, invoice totals),
  port the rule verbatim and record it. Silent rounding divergence is the class of bug that surfaces
  as a one-cent discrepancy six months in.

---

# Standing lessons applied

- **Positive controls from the start (PC sweep).** Every schema-isolation and schema-object assertion
  in this phase carries a liveness check in the same test — `Assert.NotEmpty(captured)` before
  `Assert.DoesNotContain(...)`. `CapturedSql` was dead for three phases; Finance's tests do not get to
  repeat that.
- **Create `FinanceModule` minimal early (F1-04).** Phase 6's R1 measured the cost of growing a module
  across tasks: 4 tests broken at N1-06, 131 at N1-07. Create it at F1-04 with DbContext + permissions,
  then add `AddModuleCqrs`, `AddModuleIntegrationEvents`, the `IPeriodicJob` scan and endpoints in
  their own tasks — but **create it once, early**.
- **Grep before you claim, with the pattern checked against a known positive.** This plan's own
  permission count is 12, not the 8 an earlier inventory recorded — three `finance.variations.*`
  strings carry a second dot. Recount at F1-01 rather than trusting this number.
- **Fault injections must match the rule's mechanism** (P5F). Rule 8a needs a real member access.
- **When a change alters what an existing test proves, rename it and cover the branch it stopped
  exercising** (E1-01).

---

# Scope boundaries

| Deferred | Owner |
|---|---|
| Reporting's financial read models (`ProjectBudgetView`) | Reporting — the last module |
| Multi-currency / FX | product decision; single currency this phase, and **say so in the record** |
| Tax/VAT computation beyond what the backend does | product decision |
| Payment-gateway integration | out of scope entirely; `LedgerEntry` records payments, it does not take them |

---

# Tasks

### F1-00 — `GateCondition.VariationId` (Projects change, before Finance exists)

Property + factory threading + EF configuration + a Projects migration, **no FK** (structural item 1).
Projects' existing tests must pass **unchanged** — that is what proves the addition is source- and
behaviour-compatible.

**Verify:** Projects' probe migration contains **exactly** the one added column and nothing else — the
check that catches accidental model drift in a seven-module-old context (the G7-02 precedent). Full
gate.

### F1-01 — `Finance.Contracts`

`IFinanceReader` (whatever Budget-gate satisfaction needs — settle structural item 3 here), permission
constants (**recount**, expect 12), integration events (`PaymentPosted`, `VariationSigned`,
`InvoiceIssued`, `BudgetExceeded` — IDs and primitives only), commands/queries/DTOs for all 39
features grouped per-noun.

**Read the backend's feature files before writing the records** — the requirement that exists because
G9-01 wrote twelve contract records with no handler to validate them and three could not express what
the logic needed.

**Name every Contracts type shipped without a handler**, and which task builds it (G9-14 lesson).

**Verify:** references only SharedKernel + BuildingBlocks + `Projects.Contracts` (+ `Crm.Contracts` if
invoices need `IClientDirectory`). Rules 5 and 10 green — and **say whether they are vacuous** at this
point rather than claiming they prove something (N1-01 precedent).

### F1-02 — `Finance.Domain` — cost, budget, ledger

`CostCategory`, `BudgetLine`, `LedgerEntry`, `LedgerAllocation`. Enums `LedgerDirection`,
`LedgerEntryType`, `PaymentMethod`, `ChangeCategory`. Money precision per structural item 4.

Cross-module ids stay bare Guids. Ledger invariants unit-tested — allocation never exceeds the entry,
reversal is exact, direction is respected.

### F1-03 — `Finance.Domain` — invoices, variations, BOQ

`Invoice`, `InvoiceLine`, `Variation`, `BoqSnapshot`, `BoqSnapshotLine`. Enums `InvoiceStatus`,
`InvoiceType`, `VariationStatus`, `VariationKind`, `BoqSnapshotType`.

**Variation is the largest sub-area (11 files) and has the richest state machine** — Raise → Submit →
Approve/Reject → Sign → Apply, plus Cancel. Enumerate every legal and illegal transition, asserting
specific errors, never bare `IsFailure`.

### F1-04 — `Finance.Infrastructure` persistence + **`FinanceModule` minimal**

`FinanceDbContext`, schema `finance`, `ConfigureOutbox(false)`, `ConfigureAudit(false)`, configuration
count asserted against registered `DbSet`s. Initial migration. **Append `"finance"` to `SchemaOrder`**
and fix the doc's illustrative order (structural item 2).

**`FinanceModule : IModule` created here, minimal** — `AddModuleDbContext<FinanceDbContext>` +
`IModulePermissions`. Nothing else (R1 precedent).

Three hand-maintained lists take the new schema: `ModuleDependencyOrder`, `ApiFixture`'s
`MigrateSchemaAsync` switch, `ApiFixture`'s Respawn `SchemasToInclude`. Four projects into
`DMS.Modular.slnx` **and** `Architecture.Tests.csproj` before writing constrained code.

**R3 named-list guard** for every raw-SQL object, **including money column precision** — the
probe-migration check is blind to raw SQL, and `HasPrecision` drift is exactly as invisible.

**Verify:** ten schemas apply clean from empty; probe empty; rules 1/4/6/8a fault-injected with each
injection matching the rule's mechanism; **liveness on every gathered assertion**.

### F1-05 — Cross-schema FKs + allow-list

FKs via `migrationBuilder.Sql`, `ON DELETE RESTRICT`, each indexed: `finance.*.ProjectId →
projects.projects`, stage/task references → `projects.*`, `Invoice.ClientId → crm.clients`, actor ids
→ `hr.employees` / `identity.users`. Enumerate them from the entities rather than from this list.

`finance → projects` and `finance → crm` are already in rule 9's allow-list; add any others with
reasoning. Fault-inject rule 9 once, for real.

Add the **named entry** for the deliberately-absent-FK pattern to
`docs/cross-schema-foreign-keys.md` — four instances now, it should not be a fourth paragraph.

### F1-06 — `Finance.Application` — cost categories, budget lines, BOQ

CostCategories 6 + BudgetLines 5 + BoqSnapshots 4. BOQ generation's rounding rule ported verbatim and
recorded.

### F1-07 — `Finance.Application` — ledger and invoices

LedgerEntries 6 + Invoices 6 + the Finance dashboard query. Payment posting, allocation, reversal.
Invoice lifecycle including cancel.

Expect backend bugs — every phase has found some (Phase 5 found 19). Read each handler before porting;
fix rather than port; record each divergence.

### F1-08 — Variations, `BudgetHandler`, gate satisfaction, `InvoiceDueJob`

The phase's integration core:

- Variations 11 files — the state machine from F1-03 driven through real handlers.
- **`BudgetHandler`** implementing `Projects.Contracts.IGateConditionTypeHandler`, registered by
  `FinanceModule`'s own Scrutor scan. `ValidateAsync` is a no-op returning success (verified).
- **Budget-gate satisfaction** per structural item 3 — ledger posts and variation signings drive it.
- **`InvoiceDueJob : IPeriodicJob`**, registered in `FinanceModule`. Idempotent via notification dedupe
  keys; run twice, assert one notification per recipient.

**Verify — the milestone assertion:** every `ConditionType` value now has a registered handler, and
**none returns `UnsupportedConditionType`**. Plus: a Budget gate on a task is satisfied when a signed
variation covers its overrun, driven through `OutboxProcessor.ProcessPendingAsync`, never a direct
call. `PeriodicJobWorker` executes all **8** jobs in one tick.

### F1-09 — Endpoints + nine-module registration

`FinanceEndpoints`, `CostCategoryEndpoints`, `BudgetLineEndpoints`, `LedgerEntryEndpoints`,
`InvoiceEndpoints`, `VariationEndpoints`, `BoqSnapshotEndpoints`, and the three
`Project*FinancialEndpoints`.

**The three `Project*Financial` families are the point worth stating:** their routes live under
`/api/projects/...` but the handlers are Finance's. Route shape and module ownership are independent —
this is the first place in the system where they visibly diverge, and it belongs in
`docs/adding-a-module.md`.

**Modified in the same commit** so the tree is never red between commits: `Program.cs` module list,
and the smoke tests asserting module count and permission total. **Rename** them per the standing rule;
**recount** the permission total additively via `FinancePermissions.All.Count`.

**Verify:** nine modules asserted by name; permission total recounted; `/openapi/v1.json` lists the
routes.

### F1-10 — Integration suite + nine-module spine

Spine: project → budget lines → variation raised, approved, signed, applied → invoice issued → payment
posted and allocated → Budget gate satisfied → BOQ snapshot generated. Run twice.

Extend `CrossModuleSmokeTests` to nine modules. Schema isolation for `FinanceDbContext` **with
liveness**.

**State before writing what it will and will not prove.**

---

# F2-01 — Close-out

`docs/adding-a-module.md`: route-shape vs module-ownership; the deliberately-absent-FK pattern as a
named entry; money precision under the R3 guard.

`docs/cross-schema-foreign-keys.md`: Finance's pairs, the absent `VariationId` FK, the illustrative
order fix.

`docs/low-resource-hosting.md`: eight jobs on one timer, still one loop.

`PROGRESS.md`: nine-module phase boundary; **all ten `ConditionType` values now handled** — the Phase 2
inversion complete across five modules; deferred table.

**Required deliverables:** sub-area → task reconciliation; verify-clause diff. Then: **what Finance
teaches the Reporting plan** — Reporting reads every schema and is referenced by none, which inverts
every assumption the last seven phases were built on.

---

# Execution contract

Per task: restate → implement → task verify → full gate → ≤3 attempts → one commit → one `PROGRESS.md`
line → next task immediately.

```bash
DOCKER_HOST=tcp://127.0.0.1:2375 dotnet build DMS.Modular.slnx -warnaserror
DOCKER_HOST=tcp://127.0.0.1:2375 dotnet test DMS.Modular.slnx
DOCKER_HOST=tcp://127.0.0.1:2375 dotnet format DMS.Modular.slnx && git diff --exit-code
```

**Where to spend the attempt budget:** on the **port**. Backend defect fixes are discretionary and can
become a follow-up; the port cannot. Say so in the `PROGRESS.md` line if you defer one.

**Hard stops:** a verify failing 3 attempts; any fix needing an architecture rule relaxed, a test
deleted, an internal type made public, or a forbidden reference; **weakening a liveness assertion to
make a test pass**; ambiguity that changes an outcome; any write under `..\backend`; any module or stub
beyond Finance; extending another module's `Contracts` beyond `GateCondition.VariationId`, which F1-00
mandates; reopening F2 or any closed decision.
