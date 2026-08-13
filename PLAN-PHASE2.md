# Phase 2 — Approvals + Projects modules (G4-01 → G6-02)

Continues `PLAN.md` (G0–G3, complete at `4d360ce` + F1). Same per-task loop, same gate, same
`PROGRESS.md` discipline. **This phase runs start to finish without checkpoints** — hard stop
conditions only (§ Stop conditions).

## Why Approvals is in this phase

Projects keeps four gate-condition handlers. Three are self-contained; the fourth —
`ApprovalHandler` — requests and reads approvals through `Approvals.Contracts.IApprovalRequester`,
and reacts to `ApprovalDecidedIntegrationEvent`. Approvals does not exist yet, and stubs are
forbidden. Approvals is 1 entity + ~6 features: half a day that prevents a fake.

Order: **G4 Approvals (sink) → G5 Projects → G6 close-out.**

## Architecture: as currently built, no changes

- Commands/queries/DTOs live in `*.Contracts` (public), handlers in `*.Application` (internal),
  `*.Application` references its own `*.Infrastructure` — the pattern in `docs/adding-a-module.md`.
  F2 (project collapse) is **not** decided; do not do it here.
- Arch rules 1–10 + meta-test are live. Every new module assembly must be added to
  `Architecture.Tests.csproj` — the meta-test goes red if forgotten; that is by design.
- Rule 10 means Projects may reference `Approvals.Contracts` interfaces/DTOs/events but **never** its
  command/query types. Fault-injection standard applies to every rule that goes newly non-vacuous.
- Outbox as-is (polling). The wake-on-write change from `docs/low-resource-hosting.md` is **out of
  scope** — separate decision, separate task.
- `InboxDedupeDecorator` contract holds for every new integration-event handler: stage changes,
  **never** call your own `SaveChangesAsync`. The employee-deactivation handler (G2-04) is the
  reference example.

## Scope boundaries — read before every G5 task

The old backend's Projects area (~80 feature files) reaches into modules that do not exist here.
**Port only what touches Projects-owned tables.** Specifically NOT ported in this phase, recorded once
in `PROGRESS.md` as deferred-to-owner:

| Deferred | Owner (future) |
|---|---|
| `ProjectDocument*`, `DocumentFolder*`, `ProjectStageDocument*`, `ProjectTaskDocument*` features/endpoints | Documents |
| `ProjectFinancial*`, `ProjectStageFinancial*`, `ProjectTaskFinancial*` endpoints; BudgetLine/BoqSnapshot anything | Finance |
| `Client` validation on project creation (`ClientId` stays an **unvalidated Guid**, no FK — CRM absent) | CRM |
| Gate types `Budget`, `Attachment`, `DocumentApproved`, `CrmBriefApproved`, `CrmItemsResolved`, `CrmPeriodClosed` | Finance / Documents / CRM |
| Notification side effects (events publish with zero handlers — R2 marks them processed) | Notifications |

Gate types not in this phase are **rejected at creation** with an error naming the supported set —
never accepted-then-unresolvable.

---

# G4 — Approvals module

Backend sources: `Domain/Entities/Approval.cs`, `Domain/ValueObjects/Approver.cs`,
`Domain/Services/ApprovalDomainService.cs`, `Application/Features/Approvals/*` (6 files),
`API/Endpoints/ApprovalEndpoints.cs`, enums `Approval*` (5).

### G4-01 — `Approvals.Contracts`

```csharp
public interface IApprovalRequester
{
    /// Creates a pending approval for a subject owned by the calling module. Returns the approval id.
    Task<Result<Guid>> RequestAsync(ApprovalRequest request, CancellationToken ct);
    Task<ApprovalSnapshot?> GetAsync(Guid approvalId, CancellationToken ct);
    Task<IReadOnlyList<ApprovalSnapshot>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
public sealed record ApprovalRequest(
    ApprovalSubjectType SubjectType, Guid SubjectId, ApprovalType Type,
    string RequestedByUserId, IReadOnlyList<ApproverSpec> Approvers, DateTimeOffset? DueAtUtc);
public sealed record ApprovalSnapshot(Guid Id, ApprovalSubjectType SubjectType, Guid SubjectId,
    ApprovalState State, ApprovalDecision? Decision, DateTimeOffset? DecidedAtUtc);

public sealed record ApprovalDecidedIntegrationEvent(
    Guid EventId, Guid ApprovalId, ApprovalSubjectType SubjectType, Guid SubjectId,
    ApprovalDecision Decision, string DecidedByUserId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
public sealed record ApprovalRequestedIntegrationEvent(
    Guid EventId, Guid ApprovalId, ApprovalSubjectType SubjectType, Guid SubjectId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
```

Port `ApprovalSubjectType` **in full** (all 5 values — they are just values; subjects for absent
modules simply are not requested yet). Port `ApprovalState/Type/Decision/ApproverKind` enums.
`ApprovalsPermissions` (`approvals.read`, `approvals.decide`, `approvals.manage` — check the backend's
actual strings and use those). Decide/query commands + DTOs per the current Contracts pattern.

**Verify:** references only SharedKernel + BuildingBlocks. Rule 5 walks the two new events — enums and
primitives only, passes.

### G4-02 — `Approvals.Domain`

`Approval` entity + `Approver` VO + `ApprovalDomainService` logic (quorum/decision rules — port the
actual semantics: read the backend service before rewriting) + `ApprovalErrors`. Raise
`ApprovalDecidedDomainEvent` internally; the integration event is published by the Application layer.

Note: `ICrmApprovalCallbacks` is **not** ported — its job (fan-out on CRM subject decisions) is
replaced by `ApprovalDecidedIntegrationEvent`, which CRM will subscribe to when built. Record this
mapping in `PROGRESS.md`; it is the pattern that kills the old DI-cycle workaround.

**Verify:** unit tests on decision semantics (approve, reject, quorum if present, double-decide
rejected). No EF reference.

### G4-03 — `Approvals.Infrastructure`

`ApprovalsDbContext`, schema `approvals`, `ConfigureOutbox(ownsMigration: false)`, initial migration,
`ApprovalsModule : IModule`, `IApprovalRequester` implementation, endpoints (my pending approvals,
approval by id, decide). `ModuleDependencyOrder.SchemaOrder` gains `"approvals"` (after `"hr"`).
Add the four project references to `Architecture.Tests.csproj` — meta-test red first, then green.

Cross-schema FK: `approvals.approvals.requested_by_user_id → identity.users(id) ON DELETE RESTRICT`
if the backend has that column as a user reference (check first; if it stores employee ids, target
`hr.employees` instead — `("approvals","identity")`/`("approvals","hr")` must then be added to rule 9's
allow-list, which currently lacks approvals-outbound pairs — extend the list AND
`docs/cross-schema-foreign-keys.md` in the same commit, with the reasoning).

**Verify:** migration applies; probe migration empty (full `--context ApprovalsDbContext` invocation);
rule 9 green with the new pair; meta-test green; newly non-vacuous rules for Approvals assemblies
fault-injected (rule 4: temporarily make a Domain type public → red; rule 6: temporary reference to
`HrDbContext` → red; both reverted).

### G4-04 — `Approvals.Application` + integration tests

Decide command (permission-gated, publishes `ApprovalDecidedIntegrationEvent` via outbox in the same
transaction), pending/list/get queries. Integration tests: request (via `IApprovalRequester` directly —
that is its API) → decide via endpoint → outbox row exists → processor delivers → zero-handler mark
processed (no subscriber exists until G5-08 — this exercises R2 on a *real* event).

**Verify:** full flow green; decide without `approvals.decide` → 403; deciding an already-decided
approval → 409/conflict error.

---

# G5 — Projects module

Backend sources: 7 core entities + 5 template entities + `GateCondition`; `Application/Features/`
`{Projects,ProjectStages,ProjectTasks,ProjectSubTasks,TaskComments,StageTeams,Dependencies,Stages,`
`StageTasks,StageSubTasks,StageTemplates,StageGateConditions,TaskGateConditions}`; domain services;
`GateConditions/` handlers (4 of 11); `ProjectAccessService`; ~20 endpoints minus deferred ones.

### G5-01 — `Projects.Contracts`

- `IProjectAccessGuard` — `EnsureProjectAccessAsync(projectId, userId)`,
  `FilterAccessibleProjectsAsync(ids, userId)` (batch, mandatory).
- `IProjectDirectory` — `ProjectSummary`/`StageSummary`/`TaskSummary` by id + batch forms.
- `IGateConditionTypeHandler` + `GateConditionContext` — implemented by Projects itself now, by
  Finance/Documents/CRM later. Shipping it in Contracts **now** is the inversion the whole design
  aims at.
- `ProjectsPermissions` — port the backend's actual `projects.*`/`stages.*`/`tasks.*` strings.
- Integration events: `ProjectStageCompleted` (with `StageName` denormalized), `ProjectStageRejected`,
  `ProjectTask{Assigned,Started,Completed,Rejected,Cancelled,Reopened}` (with `TaskName`,
  `AssigneeEmployeeId`), `GateConditionSatisfied`. IDs + primitives + enums only.
- Commands/queries/DTOs for all in-scope features, current pattern.

**Verify:** references only SharedKernel + BuildingBlocks (+ nothing else — `Approvals.Contracts` is
referenced by Application/Infrastructure, not by Projects.Contracts). Rule 5 green over all new events.

### G5-02 — `Projects.Domain` — core seven

`Project`, `ProjectStage`, `ProjectTask`, `ProjectSubTask`, `TaskComment`, `StageTeam`,
`StageTeamMember`. Cross-module references are bare ids: `Project.ClientId` (Guid, unvalidated — CRM
absent, documented), `Project.OwnerId`/`ProjectTask.AssignedToId`/`StageTeamMember.EmployeeId` (Guids
→ hr), audit/user fields as string user ids. **No navigation property may target a type outside this
module** — that is what G1/G2 established and rule 6 partially enforces.

Domain events ported: the task lifecycle events, stage completed/rejected. Enums:
`ProjectStatus`, `ProjectTaskStatus`, `StageStatus`, `TaskPriority`, `ProjectSector`,
`StageTeamMemberRole`, `WorkflowType`. Errors: `Project*`, `Stage*`, `Task*`, `StageTeam*` families.

**The naming trap (final-report finding, now policy):** the backend's `ProjectDomainService`/
`ProjectTaskDomainService` etc. are repository-bound application logic. Port their *rules* into
entity methods where they are pure (state transitions, guards) and into Application handlers where
they orchestrate. Do not create `*DomainService` classes that take a DbContext, and do not
reintroduce repositories.

**Verify:** no EF reference; unit tests for task/stage state machines (the transitions and their
guards — this is the behavioral core of the whole module).

### G5-03 — `Projects.Domain` — templates + gates

Templates: `Stage`, `StageTask`, `StageSubTask`, `StageTemplate`, `StageDepartment` (in a
`Templates/` folder — future split seam). `StageDepartment.DepartmentId` is a bare Guid (→ hr).
Enums `StageTemplateType`, `StageConditionType`.

Gates: `GateCondition` (in a `Gates/` folder), enums `GateStatus`, `GateTrigger`, `ConditionType`,
`GateOwnerKind`. Gate resolution logic + the four handlers:

| Handler | Depends on |
|---|---|
| `ManualCheckHandler` | nothing |
| `StageReferenceHandler`, `TaskReferenceHandler` | Projects' own data |
| `ApprovalHandler` | `Approvals.Contracts.IApprovalRequester` (injected — so this handler lives where DI reaches it; if Domain stays dependency-free, the handler goes in Application/Infrastructure and Domain holds only the resolution *rules*) |

`ConditionType` values whose handlers are deferred (`Budget`, `Attachment`, `DocumentApproved`,
`CrmBriefApproved`, `CrmItemsResolved`, `CrmPeriodClosed`): creation-time validation rejects them
with an error listing the supported set. A registry (`IReadOnlyDictionary<ConditionType,
IGateConditionTypeHandler>` built from DI) is the single source of "supported".

**Verify:** unit tests — each supported handler's satisfied/unsatisfied logic; creating a gate with
`ConditionType.Budget` → clear rejection error naming supported types.

### G5-04 — `Projects.Infrastructure` — persistence

`ProjectsDbContext`, schema `projects`, `ConfigureOutbox(ownsMigration: false)`, 13 entity
configurations, initial migration. `SchemaOrder` gains `"projects"` (after `"approvals"`).

Cross-schema FKs via `migrationBuilder.Sql`, all `ON DELETE RESTRICT`, each with its index:

| FK | Target |
|---|---|
| `stage_team_members.employee_id`, `project_tasks.assigned_to_id`, `projects.owner_id`, `stage_departments.department_id` | `hr.employees` / `hr.departments` |
| user-id audit/actor columns that the backend constrains | `identity.users` |
| `gate_conditions.approval_id` (nullable) | `approvals.approvals` |
| `projects.client_id` | **no FK** — CRM absent; add when CRM lands. Documented in the migration file itself and `docs/cross-schema-foreign-keys.md`. |

`("projects","hr")`, `("projects","identity")`, `("projects","approvals")` are already in rule 9's
allow-list.

**Verify:** migration applies in `SchemaOrder`; probe migration empty; every FK present in
`pg_constraint` and every pair allowed; rule 9 now **meaningfully** non-vacuous — fault-inject once:
add a scratch FK `identity.users → projects.projects` in a throwaway migration on the test container →
rule 9 red → remove. Rules 1/2/4/6/8a fault-injected for the new assemblies (same one-per-rule
temporary-violation technique as G4-03).

### G5-05 — `Projects.Infrastructure` — access guard

Port `ProjectAccessService` → `ProjectAccessGuard : IProjectAccessGuard` per the long-standing design:
`ICurrentUser.HasPermission(ProjectsPermissions.ReadAll)` short-circuit, then
`IHrDirectory.ResolveEmployeeIdAsync` (per-request memo already exists — use it), then stage-team
membership queries against **projects-owned tables only** (`StageTeamMembers`/`StageTeams`/
`ProjectStages`). `FilterAccessibleProjectsAsync` = one set-based query.

**Verify:** unit tests with substituted `ICurrentUser`/`IHrDirectory`; integration tests — the
security matrix: no permission + not member → denied; `ReadAll` → allowed regardless; member → allowed;
batch filter over 50 projects issues **one** membership query.

### G5-06 — `Projects.Application` — template features

Stage/StageTask/StageSubTask/StageTemplate CRUD + queries (the backend's 24 files, minus anything
touching deferred tables). Template mutation is admin master-data work: permission-gated with the
`stages.*`/`templates.*` strings ported in G5-01.

**Verify:** integration tests — template authoring round-trip; template list/detail queries.

### G5-07 — `Projects.Application` — project core features

The largest task. Split the commit into three if it helps, but it is one task:

- Project CRUD + list (paged per `PagedResult`, filtered).
- **Instantiation**: create-project-from-template — copies template stages/tasks/subtasks into
  `ProjectStage`/`ProjectTask`/`ProjectSubTask` rows, wires gate conditions from
  `StageConditionType` definitions. This is intra-module (Templates is a folder), so it is direct
  reads, no contract hop. Port the backend's instantiation logic faithfully — it is the most
  intricate pure logic in the module.
- Stage lifecycle: start/complete/reject → domain events → integration events on the outbox
  (`StageName` denormalized into the payload).
- Task lifecycle: assign/start/complete/reject/cancel/reopen (+ subtasks, comments, team CRUD).
  Assignment validates the employee via `IHrDirectory.GetAsync` (contract call).
- `SiteScoped`-style row-level enforcement: every project-scoped command/query carries `UserId`
  populated by the endpoint from the principal; a `ProjectScopeDecorator` (modeled on the backend's
  `SiteScopeDecorator`, registered in **Projects' own** pipeline) resolves the target's project and
  calls `IProjectAccessGuard`. NotFound for unresolvable, per the established 404-over-403 reasoning.

**Verify:** integration tests — full instantiate-and-run flow; task lifecycle transitions persist and
publish the right events (assert outbox rows, not delivery); scope decorator: non-member gets 404 on a
project they cannot see, member gets 200; `UserId` never bound from request body (explicit test).

### G5-08 — `Projects.Application` — gates + the Approvals subscription

- Gate CRUD features (`StageGateConditions`, `TaskGateConditions`) with the supported-type validation
  from G5-03.
- Gate resolution flow: stage/task completion domain events trigger gate re-evaluation through the
  handler registry; satisfied gates unlock dependents; cascades stay in-transaction (the backend's
  dispatcher re-loop, already ported at G0-03, is what carries this).
- `ApprovalHandler` end-to-end: creating an approval-type gate calls `IApprovalRequester.RequestAsync`;
  an `IIntegrationEventHandler<ApprovalDecidedIntegrationEvent>` in `Projects.Infrastructure`
  (InboxDedupeDecorator contract: stage, don't save) marks the gate satisfied/failed on decision and
  publishes `GateConditionSatisfiedIntegrationEvent`. Filter: only handle events whose `SubjectType`
  is `GateCondition` — other subject types belong to modules that don't exist yet and must be left
  unhandled (they mark processed via the zero-*matching*-handler path).

**Verify:** integration tests — a 3-level gate cascade resolves in one command's transaction; the
approval flow: create gate → approval requested (row in `approvals.`) → decide via Approvals endpoint →
outbox delivers → gate satisfied → dependent unlocked, all without manual intervention; a
`DesignBrief`-subject decision event does **not** touch any gate.

### G5-09 — `Projects.Infrastructure` — endpoints + module registration

`ProjectsModule : IModule`. Endpoints for everything in scope (project, stage, task, subtask, comment,
team, template, gate routes), each `[HasPermission]`-gated with the ported strings; deferred endpoint
families (documents/financial) are **not** created. `IProjectDirectory` implemented as projections.
Register `IModulePermissions`; permission catalog now shows Identity + HR + Approvals + Projects.

**Verify:** app boots with all four modules; `/scalar` lists the new routes; `GET /api/permissions`
contains the `projects.*` strings; smoke: unauthenticated → 401, authenticated-without-permission →
403 on a mutating route.

### G5-10 — Projects integration test suite (characterization)

One file per flow, Testcontainers, the suite later refactors are measured against:
template → instantiate → team assign → task lifecycle → approval gate → stage completion → next stage
unlocked. Plus the access matrix from G5-05 re-run through real endpoints, and a schema-isolation
assertion: EF logging captured, no SQL from `ProjectsDbContext` touches `identity.`/`hr.`/`approvals.`
schemas (contract calls and FKs are the only crossings; FK enforcement happens inside Postgres, not
in queries).

**Verify:** suite green twice consecutively (flake check); runtime recorded in `PROGRESS.md`.

---

# G6 — Close-out

### G6-01 — Cross-module smoke, four modules

Register user → create employee → grant `projects.*` role → author template → instantiate project →
assign task to that employee → approval gate → decide → cascade completes → deactivate the employee →
login disabled (existing flow still green with four modules loaded). Assert: meta-test covers all four
modules' assemblies; `AddModuleCqrs` isolation now proven with **four** contexts in one root — the
G0 checkpoint's open question closes here for real.

**Verify:** the flow above in one test; all arch rules green; full gate green.

### G6-02 — Docs + final report

- `docs/adding-a-module.md`: append what G4/G5 taught (gate-handler registry pattern, the
  subject-type-filtered subscription pattern, FK allow-list extension procedure).
- `PROGRESS.md`: phase boundary — the seven non-negotiables re-checked against four modules; the
  deferred-items table from Scope boundaries confirmed recorded.
- Final report: task table with SHAs; test counts by suite; every decision this plan did not specify;
  anything thinner than intended; anything in this plan that looks wrong having built it.

---

# Execution contract

Per-task loop: restate → implement → task verify → full gate (`dotnet build DMS.Modular.slnx
-warnaserror` / `dotnet test DMS.Modular.slnx` / `dotnet format DMS.Modular.slnx --verify-no-changes`)
→ ≤3 attempts → one commit `"<TASK-ID>: <what>"` → one `PROGRESS.md` line → **next task immediately,
no pauses, no checkpoints, through G6-02.**

Hard stops (only these): a verify failing 3 attempts; any fix requiring an arch rule relaxed/rescoped,
a test deleted, or a forbidden reference; ambiguity in this plan that changes an outcome; any write
under `..\backend`; any stub or module beyond Approvals and Projects; any change to the
F2/outbox-wake decisions explicitly deferred above.

Final report at G6-02 per that task's spec.
