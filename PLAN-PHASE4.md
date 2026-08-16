# Phase 4 — CRM module (G9-01 → G10-01)

Continues `PLAN.md` (G0–G3), `PLAN-PHASE2.md` (G4–G6), `PLAN-PHASE3.md` (H1/W1/G7–G8) + E1-01.
Same per-task loop, gate, commit format, and `PROGRESS.md` discipline. **Runs unattended start to
finish** — hard stop conditions only.

Head at planning time: five modules (Identity, Hr, Approvals, Projects, Documents), 266 tests,
14 architecture rules, clean tree.

**Scale:** 28 entities (26 CRM + `Client`, `ClientChild`), ~98 feature files (85 CRM + 13 Clients),
17 endpoint files, 59 permission strings (53 `crm.*` + 6 `clients.*`), 20 domain-service files.
The largest and most connected module in the system. **16 tasks.**

---

# The four Phase 3 lessons, applied honestly

`PROGRESS.md:3270` names four things Documents taught that should change this plan. Applied — and one
of them turns out **not to apply**, which is worth stating rather than performing compliance with:

**Lesson 1 — budget the `GateConditionContext`/`GateCondition` extension as its own sub-task.**
**Does not apply.** Verified all three CRM gate handlers in the backend: `CrmBriefApprovedHandler`,
`CrmItemsResolvedHandler`, `CrmPeriodClosedHandler` read **only `OwnerKind`**, which
`GateConditionContext` already carries. No new context member, no new `GateCondition` column, no
Projects migration. The lesson was correct to demand the check; the check's answer is "nothing needed."
Recording that explicitly so a future reader doesn't assume it was overlooked.

**Lesson 2 — flag the `AddModuleIntegrationEvents` fix as a prerequisite, not incidental.**
Applies. CRM becomes the **third** subscriber to `ApprovalDecidedIntegrationEvent` (after Projects on
`GateCondition` and Documents on `ProjectDocument`), filtering to `DesignBrief`, `RequirementItem`,
`BriefSpace`. The G7-07 registration fix is what makes that safe. **G9-13 must assert it explicitly** —
three subscribers, three disjoint subject-type filters, no handler wrapped in another's
`InboxDedupeDecorator`. Do not assume it holds because it held for two.

**Lesson 3 — plan the bidirectional Contracts pair up front.** Applies, and it is larger than
Documents': see "Dependency shape" below. Both directions named here so no task rediscovers them.

**Lesson 4 — reuse `IFileStorageService`, don't re-invent upload hardening.**
Applies in a **stronger** form than anticipated. `CrmArtifactDocument`'s own doc comment says it links
*"an existing project_documents row (same upload pipeline, no new storage)"* — CRM does not upload at
all. It stores a `DocumentId` pointing at a Documents row. So CRM needs **no** `IFileStorageService`
reference, no upload commands, and no hardening work. It needs `Documents.Contracts` to validate the
id. Even less work than the lesson budgeted.

---

# Two structural items — resolve before writing code

## 1. The migration-order cycle (the one that would have blocked the run)

Three desired FKs form an unsatisfiable ordering:

| FK | Requires |
|---|---|
| `projects.projects.ClientId → crm.clients.Id` (the Phase 2 deferral this phase closes) | crm **before** projects |
| `documents.project_documents.ProjectId → projects.projects.Id` (shipped, G7-04) | projects **before** documents |
| `crm.crm_artifact_documents.DocumentId → documents.project_documents.Id` | documents **before** crm |

`crm < projects < documents < crm`. A cycle. At least one FK must go.

**Decision: keep `projects → crm`, drop `crm → documents`.**

`projects.ClientId` is a core business relationship — every project has a client, it has been an
unvalidated Guid since Phase 2 explicitly waiting for this module, and validating it is one of the
reasons CRM was scheduled. `CrmArtifactDocument.DocumentId` is a metadata link on a join table. Trading
the join-table constraint to gain the core one is the right side of the trade.

So:
- **`SchemaOrder` inserts `"crm"` between `"approvals"` and `"projects"`** — an insertion, not the
  append every module since G0-08 has used. Legal: the rule protects *existing* dependencies, and
  nothing currently migrated points outward from `crm`. Record the reasoning; the append-only phrasing
  alone would mislead the next module. Final order:
  `["shared", "identity", "hr", "approvals", "crm", "projects", "documents"]`.
- **`CrmArtifactDocument.DocumentId` gets no FK** — a bare unvalidated Guid, validated in application
  code against `Documents.Contracts.IDocumentQueries` at artifact-creation time. Same treatment and same
  precedent as `projects.ClientId` before this phase (`ProjectConfiguration.cs:26`) and
  `GateCondition.ReferencedDocumentId` (G7-02/A1). **Document the deliberate absence** in the migration
  and in `docs/cross-schema-foreign-keys.md` — an absent FK needs a record as much as a present one,
  and this one has a non-obvious reason (ordering, not module-graph direction).
- `crm → documents` therefore does **not** enter rule 9's allow-list. `crm → identity` and `crm → hr`
  already do; `projects → crm` already does.

## 2. Dependency shape — three inbound, one outbound

Larger than Documents' pair. All Contracts-only, all acyclic (no `*.Contracts` references its own
implementation):

```
Crm.Infrastructure   → Projects.Contracts    (implements IGateConditionTypeHandler ×3)
Crm.Infrastructure   → Documents.Contracts   (validates artifact DocumentId)
Crm.Application      → Identity.Contracts    (client accounts), Hr.Contracts (employee resolution),
                                              Approvals.Contracts (IApprovalRequester)
Projects.Infrastructure → Crm.Contracts      (ICrmGateReader + CRM event subscriptions)
```

The outbound direction is the one to plan deliberately. `ICrmGateReader` is the satisfaction seam —
verified in the backend, keyed on `projectStageId`/`projectTaskId`:

```csharp
// Crm.Contracts — read-only, called by Projects at gate-satisfy time
public interface ICrmGateReader
{
    Task<bool> HasClosedGatheringAsync(Guid projectStageId, CancellationToken ct);
    Task<bool> IsBriefApprovedForStageAsync(Guid projectStageId, CancellationToken ct);
    Task<int>  CountUnresolvedForStageAsync(Guid projectStageId, CancellationToken ct);
    Task<int>  CountUnresolvedForTaskAsync(Guid projectTaskId, CancellationToken ct);
}
```

**Do not port the backend's two document methods** (`GetDocumentLineageProjectAsync`,
`IsLatestDocumentApprovedAsync`). They are Documents' data, not CRM's; the backend bundled them into
`ICrmGateReader` for convenience. Phase 3's deferred table entry naming CRM as owner of
*"`ICrmGateReader`-style CRM lookup for `DocumentApprovedHandler`"* refers only to the
is-this-the-client's-own-document check — not these two. Leave `DocumentApprovedHandler`'s same-project
approximation alone this phase; revisit if a real requirement appears.

---

# Pre-resolved facts (verified against `../backend` — do not re-derive)

**Sub-area file counts:** Catalog 20 · Meetings 11 · Briefs 9 · Leads 8 · Portal 6 · SpaceTypes 5 ·
Items 5 · Artifacts 4 · Contacts 4 · Notes 4 · Periods 4 · Spaces 3 · DesignProfile 1 · Workspace 1 ·
Clients 13.

**`Client._projects` (backend `Client.cs:18`) is NOT ported.** A client does not navigate to projects;
projects reference a client. This is the cycle prerequisite from the original architecture review and
dropping it is what makes CRM ownership of `Client` legal.

**`CrmSchedulerService` is NOT a background job.** Office-hours options in, free meeting slots out — no
timer, no `IHostedService`. Port as a plain scoped service. Do not introduce a scheduler; nothing in CRM
needs one, and the hosting target (`docs/low-resource-hosting.md`) specifically avoids them.

**`CrmAccessService` resolves two actor kinds** — Employee (backend: `context.Employees`) or Client
(`context.Clients` → `AppUserId`). In the new module the employee branch goes through
`IHrDirectory.ResolveEmployeeIdAsync`; the client branch is CRM-owned data and stays local. Mirrors
`ProjectAccessGuard`: read-all bypass, 404 for missing, 403 for exists-but-unrelated.

**`CrmPublicEndpoints` is deliberately anonymous** — its own doc comment says it omits
`RequireAuthorization()` on purpose (`POST /leads`, public lead submission). This is the only
unauthenticated write surface in the system. See G9-14.

**`CrmArtifactDocument`** links to an existing `project_documents` row; author is employee XOR contact
(`AddedByEmployeeId`/`AddedByContactId`), because `project_documents.uploaded_by_id` is employee-only
and the true author of a client-supplied artifact lives on the CRM row. Exactly one of
brief/space/item/meeting is set — validated in `Create`.

**Permission strings** — all 59 ported verbatim, including the non-uniform naming (`crm.leads.*`,
`crm.meetings.*`, `crm.briefs.*`, `crm.catalog.*`, `crm.items.*`, `crm.spaces.*`, `crm.space_types.*`,
`crm.contacts.*`, `crm.notes.*`, `crm.periods.*`, `crm.artifacts.*`, `crm.design_profile.read`,
`crm.approvals.view_own`, `crm.approvals.decide_own`, `crm.portal`, `clients.*`). Same rule as Phases
2–3: port, don't tidy.

**Expect backend bugs.** Every phase has found some (Phase 2: two; Phase 3: four). Read each handler
before porting it; fix rather than port, and record each as a deliberate divergence in `PROGRESS.md`.
The backend has no tests for CRM — every test here is new work with nothing to port.

---

# Scope boundaries

| Deferred | Owner |
|---|---|
| `CrmDesignBrief.SiteId` — bare nullable Guid, no FK | Sites |
| Finance-facing client billing views | Finance |
| Notification side effects (meeting reminders, period-close warnings) — events publish, zero handlers | Notifications |
| `DocumentApprovedHandler`'s client-ownership check (stays same-project) | revisit only on a real requirement |
| Full CRUD breadth beyond the vertical slices below — same trade Projects (G5) and Documents (G7) made | later, if needed |

---

# Tasks

### G9-01 — `Crm.Contracts`

`IClientDirectory` (`GetAsync`/`GetManyAsync` → `ClientSummary`, batch mandatory), `ICrmAccessGuard`
(`ResolveActorAsync` → `CrmActor`), `ICrmGateReader` (the four methods above, **not** the two document
ones), `ICrmBriefReader` (brief status — the Sites seam, declare now), `CrmPermissions` (all 59),
integration events (`ClientCreated`, `LeadConverted`, `CrmBriefApproved`, `CrmPeriodClosed`,
`CrmItemsResolved`, `MeetingScheduled` — IDs and primitives only), commands/queries/DTOs for every
in-scope feature, grouped per-noun like `Hr.Contracts`.

The three gate-satisfaction events must carry **`ProjectStageId`** (and `ProjectTaskId` where the gate
can be task-owned — `CrmItemsResolved` only) so Projects can match gates without a callback.

**Verify:** references only SharedKernel + BuildingBlocks + `Projects.Contracts` + `Documents.Contracts`.
Rules 5 and 10 green.

### G9-02 — `Crm.Domain` — Clients & Contacts

`Client`, `ClientChild`, `CrmContact`. **No `_projects` collection.** `Client.AppUserId` is a bare
`string` (→ identity.users, FK at G9-08, no navigation). Primary-contact rules.

**Verify:** no EF reference; unit tests for client/child/contact invariants and primary-contact
transitions.

### G9-03 — `Crm.Domain` — Leads, Intake, Meetings

`CrmLead`, `CrmLeadIntake`, `CrmLeadIntakeResidential`, `CrmMeeting`, `CrmMeetingSlot`. Enums:
`LeadSource`, `LeadStatus`, `MeetingStatus`, `MeetingSlotStatus`, `MeetingType`, **`HiringReason`,
`PastExperience`** (Phase 2 correctly refused these for HR — this is the module that uses them),
`TimelinePressure`, `UnitType`, `UnitCondition`, `UnitPosition`, `BuildingKind`, `ElevatorStatus`,
`FurnitureStatus`, `FinishingPurpose`.

Lead → Client conversion is the key transition; port its rules faithfully.

**Verify:** unit tests on the lead status machine, conversion, and meeting/slot lifecycle.

### G9-04 — `Crm.Domain` — Briefs & Spaces

`CrmDesignBrief`, `CrmBriefSpace`, `CrmBriefHeadSnapshot`, `CrmSpace`, `CrmSpaceType`, `CrmSpaceItem`,
`CrmSpaceItemAttributeValue`. `SiteId` bare nullable Guid. The head-snapshot/diff logic is intricate —
port faithfully and unit-test the diff specifically.

### G9-05 — `Crm.Domain` — Catalog

`CrmCatalogCategory`, `CrmCatalogItem`, `CrmCatalogAttribute`, `CrmCatalogAttributeOption`,
`CrmCatalogItemAttributeValue`, `CrmCatalogCategorySpaceType`, `CatalogAttributeValueRules`. Enums
`CatalogAttributeType`, `CatalogSelectionMode`. Largest sub-area — the attribute-value rule engine is
the part to unit-test hardest.

### G9-06 — `Crm.Domain` — Periods, Items, Notes, Artifacts

`CrmRequirementPeriod`, `CrmRequirementItem`, `CrmClientNote`, `CrmArtifactDocument`. Enums
`RequirementCategory`, `RequirementPeriodStatus`, `RequirementSource`, `CrmProvisionStatus`.
`CrmRequirementItem.EmployeeId` → bare Guid (hr). `CrmArtifactDocument` per the facts section:
`DocumentId` bare Guid, employee-XOR-contact author, exactly-one-subject validation in `Create`.

### G9-07 — `Crm.Infrastructure` — persistence

`CrmDbContext`, schema `crm`, `ConfigureOutbox(ownsMigration: false)`, 28 configurations, initial
migration. **Insert `"crm"` before `"projects"` in `SchemaOrder`** per structural item 1 — record the
insertion-vs-append reasoning in `PROGRESS.md`. Add the four projects to `DMS.Modular.slnx` and
`Architecture.Tests.csproj` (meta-test red → green).

**Verify:** migration applies in the new order; existing modules' migrations still apply (the insertion
must not disturb them); probe migration empty (full `--context CrmDbContext` invocation); rules
1/4/6/8a fault-injected for the new assemblies.

### G9-08 — Cross-schema FKs + access guard

FKs via `migrationBuilder.Sql`, all `ON DELETE RESTRICT`, each with its index:
`crm.clients.AppUserId → identity.users(Id)`; `crm.crm_requirement_items.EmployeeId → hr.employees(Id)`;
any other bare Guid the domain documents as pointing at hr/identity.
**`crm.crm_artifact_documents.DocumentId` gets none** — structural item 1; document the absence.

Extend `docs/cross-schema-foreign-keys.md` in the same commit, same format as G4-03/G5-04/G7-04, and
**include the ordering-cycle reasoning** — this is the first absent FK caused by ordering rather than
module-graph direction.

`CrmAccessGuard : ICrmAccessGuard` — employee branch via `IHrDirectory`, client branch local.

**Verify:** rule 9 green (and `crm → documents` absent from it); both actor kinds resolve; access-guard
unit tests with substituted `ICurrentUser`/`IHrDirectory`.

### G9-09 — `Crm.Application` — Clients, Contacts, Leads, Notes

Clients 13 + Contacts 4 + Leads 8 + Notes 4. Lead conversion publishes `LeadConverted` + `ClientCreated`.

### G9-10 — `Crm.Application` — Briefs, Spaces, SpaceTypes

Briefs 9 + Spaces 3 + SpaceTypes 5 + DesignProfile 1 + Workspace 1.

### G9-11 — `Crm.Application` — Catalog

Catalog 20 — the single largest sub-area. Split the commit if useful; still one task.

### G9-12 — `Crm.Application` — Periods, Items, Artifacts

Periods 4 + Items 5 + Artifacts 4. Artifact creation validates `DocumentId` via
`Documents.Contracts.IDocumentQueries` — the replacement for the FK dropped in structural item 1. **A
test must assert an artifact with an unknown `DocumentId` is rejected**, because that validation is now
the only thing standing where a constraint would have been.

### G9-13 — Gate handlers, `ICrmGateReader`, the Approvals subscription, and the Projects `ClientId` FK

The phase's integration core:

- The three `IGateConditionTypeHandler` implementations (`CrmBriefApproved`, `CrmItemsResolved`,
  `CrmPeriodClosed`), registered by `CrmModule`. Validation is `OwnerKind`-only per lesson 1 —
  `CrmBriefApproved`/`CrmPeriodClosed` are stage-owner-only; `CrmItemsResolved` supports both.
- `ICrmGateReader` implementation over `CrmDbContext`.
- CRM publishes the three satisfaction events; **`Projects.Infrastructure` subscribes** and satisfies
  matching gates by `ProjectStageId`/`ProjectTaskId`.
- `ApprovalDecidedIntegrationEventHandler` in `Crm.Infrastructure`, filtered to `DesignBrief`,
  `RequirementItem`, `BriefSpace`. **Replaces the backend's `ICrmApprovalCallbacks`** — the DI-cycle
  workaround whose own doc comment explains it exists only to avoid that cycle. Record the replacement.
- **In `Projects`:** the `projects.projects.ClientId → crm.clients.Id` FK migration, plus `ClientId`
  validation via `IClientDirectory` on project creation. Closes the Phase 2 deferral.

**Verify (lesson 2, explicitly):** three subscribers to `ApprovalDecidedIntegrationEvent` with disjoint
filters — a `DesignBrief` decision reaches CRM only; a `GateCondition` decision reaches Projects only; a
`ProjectDocument` decision reaches Documents only. Assert all three, and assert no handler is wrapped in
another module's `InboxDedupeDecorator` (the G7-07 bug, now with three subscribers).
Plus: the three CRM gate types **create successfully** where Phase 2 returned `UnsupportedConditionType`
— assert that transition. Gate satisfaction driven through `OutboxProcessor.ProcessPendingAsync`, never
a direct handler call. Project creation with an unknown `ClientId` now fails validation. Rule 9 green
with `projects → crm`.

### G9-14 — Endpoints, module registration, and the anonymous surface

`CrmModule : IModule` (mirror `ProjectsModule`/`DocumentsModule` registration order exactly); the 17
endpoint files; `IClientDirectory`/`ICrmBriefReader`/`ICrmGateReader` as projections.

**The anonymous endpoint needs explicit treatment.** `CrmPublicEndpoints` (`POST /leads`) is the only
unauthenticated write in the system:

- **Rate limiting** — ASP.NET's built-in fixed-window limiter keyed by IP. No new dependency, no Redis.
- Payload size cap and strict validation — untrusted input path.
- A test asserting it is reachable anonymously **and** that no other CRM route is.

**Verify:** app boots with six modules; `/scalar` lists CRM routes; `GET /api/permissions` contains all
59 strings; anonymous `POST /leads` succeeds while anonymous GET on any other CRM route → 401; the rate
limiter rejects a burst.

### G9-15 — CRM integration suite + six-module smoke

Characterization suite: lead → convert to client → meeting scheduled → design brief → spaces → catalog
items → requirement period → brief approval via Approvals → CRM gate satisfied → a Projects stage gated
on `CrmBriefApproved` unlocks. Run twice for flake check.

Extend `CrossModuleSmokeTests` to six modules. Schema-isolation assertion: no SQL from `CrmDbContext`
touches `identity.`/`hr.`/`projects.`/`approvals.`/`documents.`.

**Verify:** suite green twice; all 14 arch rules green; full gate green.

---

# G10-01 — Close-out

Append to `docs/adding-a-module.md`: the `SchemaOrder` **insertion-vs-append** rule and when an ordering
cycle forces dropping an FK; the three-subscriber confirmation of the shared-event registration fix; the
"validate in application code where a constraint was dropped" pattern. Update
`docs/cross-schema-foreign-keys.md` with the CRM pairs and the deliberately-absent `crm → documents`
entry.

`PROGRESS.md` phase boundary — seven non-negotiables re-checked against six modules; deferred table
updated (Sites/Finance rows now name CRM's seams).

Final report: task table with SHAs, test counts by suite, every decision this plan did not specify,
anything thinner than intended, anything in this plan that looks wrong having built it, and **what CRM
taught that should change the Sites plan**.

Additionally record: **lesson 1 was checked and did not apply** — no `GateConditionContext` extension
was needed, because all three CRM gate handlers read only `OwnerKind`. A future reader should be able to
tell that from the record rather than assuming it was missed.

---

# Execution contract

Per task: restate → implement → task verify → full gate (`dotnet build DMS.Modular.slnx -warnaserror` /
`dotnet test DMS.Modular.slnx` / `dotnet format DMS.Modular.slnx --verify-no-changes`) → ≤3 attempts →
one commit `"<TASK-ID>: <what>"` → one `PROGRESS.md` line → next task immediately, no pauses, through
G10-01.

**Hard stops:** a verify failing 3 attempts; any fix requiring an arch rule relaxed/rescoped, a test
deleted, an internal type made public, or a forbidden reference; ambiguity that changes an outcome; any
write under `..\backend`; any module or stub beyond CRM; reopening F2 or any closed decision.

**One standing instruction, from E1-01's best finding:** when a change alters what an *existing* test
proves, correct that test's doc comment and add coverage for the branch it stopped exercising. A test
that stays green while its stated coverage evaporates is the failure mode that let E1 ship, and CRM
touches Projects' gate paths — the same class of silent drift is available here.
