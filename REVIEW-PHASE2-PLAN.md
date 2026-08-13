# Review — Phase 2 execution plan (pre-run)

**Verdict: approved to run after the two required changes in §2.** The research quality is the best of
any plan so far — of the nine resolved decisions, every one I could check against the backend held up,
including three that correct *my* plan.

---

## 1. Fact-check results

| Claim | Checked | Result |
|---|---|---|
| #5 `StageConditionType` is dead code | grep across all four backend projects | ✓ referenced only by its own file. My G5-03 list was wrong; drop it. |
| #6 `ProjectSector` is CRM-only | entity + feature grep | ✓ only `CrmBriefHeadSnapshot`/`CrmDesignBrief`/`CrmLeadIntake`. My G5-02 list was wrong; drop it. |
| #7 instantiation creates zero gates | grep `GateCondition` in `ProjectStageRepository` | ✓ no matches. My "wires gate conditions" sentence described behavior that does not exist. Skeleton-only is correct. |
| #8 no `approvals.manage` | permission-string dump | ✓ only `approvals.read`, `approvals.decide`. |
| #9 singular `project.*`, non-uniform groups | permission-string dump | ✓ `project.read/read_all/create/update/set_active/change_status/delete`, `stages.*`, `stage_tasks.*`, `stage_teams.manage` — verbatim as the plan tabulates. Porting as-is is right. |
| #2 `isAdminOverride` exists in the backend | `ApprovalDomainService:109`, `DecideApprovalCommand:66` | ✓ exists, employee-path only — but see R-B below on *dropping* it. |
| `ApprovalDecision` no-Pending semantics | enum source | ✓ exactly as stated, including the `ChangesRequested` doc comment the plan quotes. |
| #1 backend is one-assigned-approver-per-row | `Approval.cs` | ✓ `AssignedEmployeeId`/`AssignedClientId`, exactly one set. So `ApproverSpec` *set-of-eligible* is a **redesign**, not a port — the evidence given (the `ManagerApproval` owner-or-team-head query) is real and the redesign is defensible, but label it as a redesign in `PROGRESS.md`, not a resolution of an ambiguity. |

Decisions #3 (computed `ApprovalState`) and #4 (inert `Round`, no rework loop) are sound scoping.

## 2. Required changes before the run starts

### R-A — Domain-event dispatch is missing from the integration-event delivery path

The sharpest gap, and it is in *my* plan's G5-08 text, which the execution plan inherited uncritically.

The claim "cascades stay in-transaction — the G0-03 dispatcher re-loop carries this" is only true on
the **command** path: `DomainEventDispatchDecorator` wraps command handlers. The approval-decided flow
runs on the **delivery** path: `OutboxProcessor → EventBus → InboxDedupeDecorator → handler →
SaveChangesAsync`. Nothing in that chain drains domain events. So when
`ApprovalDecidedIntegrationEventHandler` stages `gate.MarkSatisfied()` and that raises a domain event
to unlock dependents, the event sits in the tracker, the save commits the gate flip, and **the cascade
silently never runs** — the same silent-gap class as Phase 1's outbox wiring hole, except this one
would pass G5-08's happy-path test if the test only checks the directly-satisfied gate.

**Fix (small, in BuildingBlocks, additive):** the decoration chain built by
`AddModuleIntegrationEvents<TContext>` gains a `DomainEventDispatchDecorator`-equivalent for
integration handlers — after the inner handler runs and before/around the dedupe save, drain via the
existing `DomainEventDispatcher<TContext>` (which already re-loops on cascade). Order: inner handler →
domain-event dispatch (re-loop) → inbox row → single save path. This preserves the R1 contract — one
commit point — because the dispatcher stages, it does not save, when invoked inside this chain
(mirror how the command decorator sequences it).

**Verify addition to G5-08:** a *dependent* gate/stage unlocked by the cascade — asserted through the
delivery path (decide via endpoint → processor delivers → dependent unlocked), not through a direct
command. If only the directly-referenced gate flips, the test must fail.

### R-B — Dropping `isAdminOverride` with no replacement creates a deadlock

The reasoning for dropping it is correct — Approvals hard-coding Projects' `project.update` string
would break the one-module-one-permission-namespace rule. But the backend override is the escape hatch
for a real failure mode: **an approval assigned to an employee who leaves is undecidable forever.**
The plan removes the hatch and does not replace it; there is also no reassignment command. First time
this happens in production, someone edits the database by hand.

**Fix (keeps the boundary argument intact):** add `approvals.decide_any` to `ApprovalsPermissions` —
Approvals-owned, so no cross-module string — allowing a holder to decide regardless of the
`Approvers` set. Not seeded to any default role except admin. One extra branch in the decide command,
one extra integration test (non-approver without the permission → 403; with it → decided). This is
strictly less machinery than the backend's override and strictly more than nothing.

## 3. Recorded, not blocking

- **`ChangeItems` is silently dropped.** Backend `Approval.ChangeItems` (owned JSON, populated on
  `ChangesRequested`) appears nowhere in the execution plan. Consistent with decision #4 — but it is a
  data-model omission, not just a behavior omission, and CRM's client-review flow will want it later.
  Add one line to the deferred table: *ChangeItems — owner: CRM/client-review phase.*
- **Decision #1's id type**: `RequestedByUserId` as a user id (not employee Guid) follows my G4-01
  block and keeps Approvals HR-free — right call, and the FK to `identity.users` matches. Note the
  backend's `RequestedById` is an ambiguous Guid; the new model is cleaner than the source here.
- The plumbing section (ApiFixture switch, `Api.csproj` references, `Assembly.Load` list, meta-test
  ordering) is exactly the Phase-1 lesson applied forward. No notes.
- `ProjectScopeDecorator` as module-owned decoration wired after `AddModuleCqrs` (outermost) with a
  contracts-level `IProjectScoped` marker — correct, and correctly kept out of the shared
  `ModuleCqrsRegistration`.
- G4-04's trimming of the backend's six approval features to the contract surface + subject-trail is
  right; the manager/client inbox queries are Projects/CRM-shaped and belong there.

## 4. Corrections to my own plan, absorbed

G5-02's enum list loses `ProjectSector`; G5-03 loses `StageConditionType`; G5-07's "wires gate
conditions from `StageConditionType` definitions" sentence is wrong and superseded by decision #7's
skeleton-only instantiation. The execution plan caught all three by reading the source instead of
trusting the plan — that is the standard.

## 5. Run instruction

Apply R-A (BuildingBlocks decorator + the G5-08 verify addition) and R-B (`approvals.decide_any`)
as amendments to the relevant tasks — R-A lands with G5-08 but the BuildingBlocks change may be built
at G4-04 where the first integration handler test exists to prove it; R-B lands in G4-01/G4-04.
Then run G4-01 → G6-02 unattended per the execution contract. Both amendments get `PROGRESS.md`
entries citing this review.
