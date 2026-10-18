# CRM Brief fixes — notes

Branch: `fix/crm-brief-review`. This file stands in for `../docs/backend/dms-modular/` (absent in the cloud clone).
It holds the PROGRESS entries, the `04-Approvals.md` doc change and the closure report. The owner moves it out after merging.

Environment: .NET SDK 10.0.400 is at `~/.dotnet`. `dockerd` was started inside the container, and `postgres:17` was pulled.
**Testcontainers suites ran.** Baseline before any change: every test project green (`dotnet test DMS.Modular.slnx`).

---

## Run 0 — read-only checks

1. **Project access** (`Projects.Contracts.IProjectAccessGuard`, `Projects.Infrastructure/Services/ProjectAccessGuard.cs`)
   - `EnsureProjectAccessAsync(projectId, userId)` passes in two cases:
     - `ICurrentUser` (the HTTP caller, not `userId`) holds `project.read_all`;
     - `userId` resolves via `IHrDirectory` to an employee with a `StageTeamMember` row on any stage of that project.
   - Otherwise it returns `projects.AccessDenied` (403). It never checks whether the project exists.
   - `ProjectScopeDecorator` maps any failure to 404 `projects.NotFound`.
   - `ProjectScopeRegistration` decorates every `IProjectScoped` message through Scrutor, after `AddModuleCqrs`, so the decorator is outermost.
   - **CRM can call the guard directly.** `Crm.Application` and `Crm.Infrastructure` already reference `Projects.Contracts`, the guard is registered app-wide, and Sites already does this (`SiteScopeDecorator`).
   - **No mechanism shares one transaction across two module DbContexts.** There is no `TransactionScope`, `UseTransaction` or shared connection, so H3 uses the ordering + idempotent-request design. See also the transaction finding below.
2. **409 mapping:** BuildingBlocks does **not** map `DbUpdateConcurrencyException` or unique-violation `DbUpdateException`; `GlobalExceptionHandler` turns both into 500. Sites translates them locally (`SiteWrites.SaveAsync`). CRM now does the same (`BriefWrites.SaveAsync`).
3. **Identity seeds:** the **Client role holds no seeded permissions**. `DefaultRoles.FullAccessRoles = [Admin, Manager]`, and `RoleClaimSeeder` seeds only those two. So Client does **not** have `approvals.decide` → **M6 is blocked** (see Blockers).
4. **H4 on requirement items:** `SendCrmRequirementItemCommandHandler` had the bug (`RequestedByUserId: client.AppUserId`). The one-field fix is applied in Run A.
5. **`ICrmBriefReader` / `CrmDesignBriefSummary`:** no implementation, registration, injection or test anywhere. The only mention is a comment in `Sites.Contracts/IntegrationEvents.cs:14`. Delete it in Run D.
6. **Approval ids:** `Approval.Create` always generated its id (`Entity()` → `Guid.CreateVersion7()`, `Id` is `private init`). `Entity(Guid id)` exists but is protected and `Approval` did not expose it, so a caller-supplied id was **not** possible. Run A adds `Approval.Create(Guid id, …)`.

**Extra finding (changes H3).** `DomainEventDispatchDecorator` wraps every command handler in an explicit CRM transaction.
- A handler's `SaveChangesAsync` only flushes. The commit happens after the handler returns. "CRM commits first" therefore means "CRM flushes first".
- That is still what H3 needs. The `(BriefId, Round)` unique violation and the new `xmin` conflict surface at the flush, *before* `RequestAsync`.
- A failed request returns a failure, and the decorator rolls the whole CRM transaction back. That removes the snapshot and restores the prior status by itself.
- **Deviation from the prompt:** step 8's manual "revert, clear, remove snapshot, save" is therefore not written. The rollback is the revert, and `FailedApprovalRequest_LeavesNoSnapshot_AndThePriorStatus` proves it.
- The one window left is: the approval commits, then CRM's commit fails. It is closed by **deterministic approval ids** (Run A): the retry recomputes the same round, hence the same id, and the idempotent request returns the existing approval.

**Finding verification.** Every finding H1–L5 was checked against the code. All are accurate. M6 and M7 are questions by nature.

---

## Run A — approval-round integrity (H1, H3, H4, M3, L5)

**Approvals**
- `ApprovalRequest` gains `Guid ApprovalId`, as its first positional field.
- `RequestAsync` now returns `Result<ApprovalRequestOutcome(Guid ApprovalId, ApprovalDecision? Decision)>`:
  - An existing id for the **same subject** → success, carrying that approval's decision if it has one. This is owner amendment 1.
  - An existing id for a **different subject** → 409 `approvals.ApprovalIdInUse`.
  - Otherwise the old `OpenApprovalExists` check, then create the approval with the supplied id.
- `Approval.Create(Guid id, …)` was added; the old overload delegates to it.
- The other callers pass `Guid.NewGuid()` and read `.Value.ApprovalId`: Documents `RequestDocumentReviewCommandHandler`, Projects `GateApprovalRequests`, and tests `PeriodicJobTests` and `ApprovalFlowTests`.

**Domain — H1**
- **`CrmDesignBrief`:**
  - `PendingApprovalId` added.
  - `UpdateHead`: while pending → 409 `crm.briefs.HeadLockedPendingApproval`; Approved or Rejected → Draft.
  - `BeginRound(approvalId)`: only from Draft or Rejected, else 409 `crm.briefs.NotSendable`. It replaces `MarkPendingApproval`.
  - `MarkApproved(id)` / `MarkRejected(id)`: require pending **and** a matching id, else `NotPending`. They clear the id on success.
- **`CrmBriefSpace`:** the same binding — `PendingApprovalId`, `Send(approvalId)`, `MarkApproved(id)`, `MarkRejected(id)` — plus `SendCount`, the entry's round number.

**Decision path — H1, L5**
- `ApprovalDecidedIntegrationEventHandler` passes `@event.ApprovalId` into `CrmApprovalOutcomes` (new, `Crm.Infrastructure/Services`).
- That class applies the decision and stages `CrmBriefApprovedIntegrationEvent` **only on a successful approve**.
- Every mark result is checked. A mismatch is logged at Warning and dropped.
- Requirement items are not round-bound (out of scope), but their result is now checked too.
- The send handlers use the same applier for amendment 1.

**Send handlers — H3**
- Order:
  1. Load the subject.
  2. Resolve the project and client.
  3. Compute the round and `approvalId = CrmApprovalIds.For(subjectType, subjectId, round)`.
  4. Snapshot (brief only), then `BeginRound` / `Send`.
  5. **Flush** through `BriefWrites.SaveAsync`.
  6. `RequestAsync(approvalId)`.
  7. If the outcome carries a decision (the orphan was decided before CRM knew), apply it in the same transaction.
- `CrmApprovalIds` follows owner amendment 7:
  - a fixed namespace GUID constant;
  - SHA-256 over namespace + subject type + subject id + round;
  - 16 bytes;
  - the version nibble set to 5 and the variant bits set to `10xx`.
- **Recovery:** send while pending under an id that `GetAsync` cannot find → re-request with the same id. Otherwise → `NotSendable` (brief) or `EntryPending` (entry).
- **Concurrency:** `BriefWrites` maps the unique index `IX_crm_brief_head_snapshots_BriefId_Round` → 409 `crm.briefs.ConcurrentSend`, and `DbUpdateConcurrencyException` → 409 `crm.briefs.ConcurrencyConflict`.
  - Two truly concurrent sends: exactly one wins, and the other gets one of those two 409s or `NotSendable` (`ConcurrentSends_ExactlyOneWins_TheOtherGets409`).

**H4.** `SendCrmBriefCommand`, `SendCrmBriefSpaceCommand` and `SendCrmRequirementItemCommand` each gain `string SentByUserId`.
- The endpoints fill it from `ICurrentUser.UserId`; no user → 401 `crm.access.Unauthenticated`.
- It is the approval's `RequestedByUserId`, so `ApprovalDecidedNotificationHandler` now notifies the office sender.

**M3.** `xmin` is mapped as the row version on `crm_design_briefs` and `crm_brief_spaces`, in the same style as `ProjectDocumentConfiguration`.
- The update-head and update-entry handlers save through `BriefWrites`.
- Migration `20261018072206_CrmBriefApprovalRounds` adds `PendingApprovalId` (both tables) and `SendCount`.
- `dotnet ef migrations script` confirms Npgsql emits **no** DDL for `xmin`, since it is a system column:

  ```sql
  ALTER TABLE crm.crm_design_briefs ADD "PendingApprovalId" uuid;
  ALTER TABLE crm.crm_brief_spaces ADD "PendingApprovalId" uuid;
  ALTER TABLE crm.crm_brief_spaces ADD "SendCount" integer NOT NULL DEFAULT 0;
  ```

**Amendment 8 (concurrency in the decided handler).** A `DbUpdateConcurrencyException` from the handler is **retried, not dead-lettered**:
- `InboxDedupeDecorator` lets the exception escape and rolls the delivery back, inbox row included.
- `OutboxProcessor.ProcessOneAsync` catches it, calls `RecordFailure` (attempts + error) and leaves `ProcessedUtc` null.
- The next poll re-delivers it against the reloaded row.
- There is no attempt cap, no dead-letter and no backoff beyond the poll interval. The handler therefore lets it propagate; this is documented on the handler.

**Tests**
- **Unit:**
  - `CrmDesignBriefTests`: `UpdateHead_WhileApproved_ReturnsToDraft` (renamed), rejected → Draft, pending → locked, each BeginRound state, and wrong-id approve/reject ignored. Also `ADecisionForAnEarlierRound_DoesNotApplyToTheCurrentOne`.
  - `CrmBriefSpaceTests`: the binding, the round counter, wrong-id ignored, and a resend after rejection.
  - `ApprovalTests.Create_WithCallerSuppliedId_KeepsThatId`.
  - `CrmApprovalIdsTests`: determinism, sensitivity to each input, and the version/variant bits.
- **Application** (`BriefRoundFlowTests`, all **run and green**):
  - send → approve → Approved, `CrmBriefApprovedIntegrationEvent` in the outbox, gate reader true, and the **office sender's** decision notification present;
  - reject → edit (Draft) → resend → round 2 under the round-2 id;
  - edit while pending → 409;
  - second send while pending → 409;
  - a failed request leaves no snapshot and Draft;
  - a lost CRM commit → the resend reuses the same approval;
  - `Send_WhenOrphanApprovalAlreadyDecided_AppliesDecision`, Approved and Rejected cases;
  - recovery re-request;
  - concurrent sends;
  - a stale write → `ConcurrencyConflict`;
  - space send → approve.
- `ApprovalFlowTests`: a same-id request is idempotent and reports its decision; an id for a different subject → `ApprovalIdInUse`.
- Tests that decide as the client are named `…ClientDecidesViaHandler_NotPortalRoute…` (amendment 6). They call the `DecideApprovalCommand` handler directly, so they do **not** show M6 is solved.

---

## Run B — scoping and references (H2, H5, M1, M2)

**Caller audit, before H5/M1 (owner amendment 2).**
- `CrmDesignBrief.SeedFromIntake` has **no callers**. The `ConvertCrmLeadCommandHandler` comment says seeding happens at period-open, but nothing calls it.
- `CrmDesignBrief.Create` is called only from `CreateCrmDesignBriefCommandHandler`.
- Every brief or brief-space command and query is dispatched only from `CrmBriefEndpoints`. No job, event handler or other module dispatches one.
- So there are **no null-stage creators to fix and no system caller needs a scope bypass**, and none was built.
- If a future system caller appears, the plan's `CrmProjectScope.SystemUserId` bypass is the intended shape.

**H5 — project scope.**
- `Crm.Contracts.ICrmProjectScoped { string UserId; CrmScopeTarget ScopeTarget }`. `CrmScopeTarget` is a `Project`, `Stage`, `Brief` or `BriefSpaceEntry` id.
- `CrmProjectScopeDecorator` and `CrmProjectScopeRegistration` live in `Crm.Infrastructure/Decorators`:
  - The decorator resolves the `ProjectId`: a brief from `crm_design_briefs`, an entry by joining to its brief, a stage via `IProjectDirectory.GetStageAsync`.
  - It then calls `IProjectAccessGuard.EnsureProjectAccessAsync`.
  - An unresolvable subject and an access failure are both 404 `crm.briefs.NotFound`.
  - It is registered after `AddCrmScopeDecorator`, so it wraps validation and the transaction and is outermost. The order stays route 403 → scope 404 → state 409.
- It applies to all 10 office messages: create, update-head, send, entry create/update/send/supersede, get-by-project, get-by-stage, and the spaces list.
- Each gains `string UserId`, which the endpoint fills from `ICurrentUser.UserId`; null → 401. On the two send commands, `UserId` is `SentByUserId`.
- The portal keeps its `ICrmScoped` client check.

**H2 — references** (`Crm.Application/Features/Briefs/BriefReferenceValidator.cs`). Each mismatch is a 400 with a field-specific code; an unknown id counts as a mismatch.

| Reference | Error code | Checked on |
|---|---|---|
| Stage | `crm.briefs.StageProjectMismatch` | brief create |
| Space | `SpaceProjectMismatch` | entry create, update, supersede |
| Task, other project | `TaskProjectMismatch` | entry create, update, supersede |
| Task, other stage (both stages set) | `TaskStageMismatch` | entry create, update, supersede |
| Meeting | `MeetingProjectMismatch` | entry create, update, supersede |

- **Beyond the prompt:** the head's `LastMeetingId` on update-head gets the same meeting check. It is the same class of cross-project reference.

**M1 — one brief per stage.**
- `ProjectStageId` is `Guid` (required) in the command, the request body, the entity, `CrmDesignBriefResponse` and the column.
- Migration `20261018074122_CrmBriefStageRequired` opens with the prompt's `DO $$` guard on `crm.crm_design_briefs`. A second guard then checks for any entry superseded more than once. Only after both comes the DDL: NOT NULL, the unfiltered stage index, and the M2 unique index. Script output:

  ```sql
  DO $$ BEGIN IF EXISTS (SELECT 1 FROM crm.crm_design_briefs WHERE "ProjectStageId" IS NULL) THEN RAISE EXCEPTION '…'; END IF; END $$;
  DO $$ BEGIN IF EXISTS (SELECT 1 FROM crm.crm_brief_spaces WHERE "SupersedesSpaceId" IS NOT NULL GROUP BY "SupersedesSpaceId" HAVING COUNT(*) > 1) THEN RAISE EXCEPTION '…'; END IF; END $$;
  DROP INDEX crm."IX_crm_design_briefs_ProjectStageId";
  DROP INDEX crm."IX_crm_brief_spaces_SupersedesSpaceId";
  ALTER TABLE crm.crm_design_briefs ALTER COLUMN "ProjectStageId" SET NOT NULL;
  CREATE INDEX "IX_crm_design_briefs_ProjectStageId" ON crm.crm_design_briefs ("ProjectStageId");
  CREATE UNIQUE INDEX "IX_crm_brief_spaces_SupersedesSpaceId" ON crm.crm_brief_spaces ("SupersedesSpaceId") WHERE "SupersedesSpaceId" IS NOT NULL;
  ```

  - The generated `defaultValue: Guid.Empty` on the NOT NULL alter was removed. The guard guarantees there are no NULLs, and an empty-GUID default would be wrong data.
- New route `GET /api/crm/stages/{stageId}/brief` (`crm.briefs.read`, scoped through the Stage target).
- `GET /api/crm/projects/{projectId}/brief` stays but is **obsolete**. It returns the latest brief by `CreatedAtUtc desc`.
- The portal brief takes an optional `?stageId=` and follows the same rule.

**M2 — the ledger cannot fork.**
- A partial unique index on `SupersedesSpaceId WHERE NOT NULL`.
- The supersede handler pre-checks → 409 `crm.briefs.AlreadySuperseded`, and `BriefWrites` maps the index violation to the same error. Two concurrent supersedes: exactly one wins.
- A correction must keep the original's `SpaceId`, at supersede and on later updates of the correction → 400 `crm.briefs.SupersedeSpaceMismatch`.

**Tests**
- Unit: `CreateSuperseding_OnADifferentSpace_Fails` and `Update_OfACorrection_CannotMoveItToAnotherSpace`.
- Application (`BriefScopeAndReferenceTests`, all **run and green**):
  - an outsider employee gets 404 on all 10 messages;
  - **a stage-team member without `read_all`** works the brief, but gets 404 on another project's brief, entries and stage (owner amendment 5; the cross-project diff check comes with the route in Run C);
  - a foreign stage → 400;
  - a foreign space, task (other project and other stage) or meeting → 400, on create, update and supersede, while the project's own references are accepted;
  - a double supersede → 409, and a correction on another space → 400;
  - concurrent supersedes;
  - the stage route, and the project route returning the latest brief.
- Infrastructure (`CrmBriefMigrationGuardTests`, **run and green**): migrate to the previous migration, plant a NULL-stage brief or a double supersede, and the migration throws the guard's message and leaves the schema unchanged.
- `BriefSpaceFlowTests`, `PeriodItemArtifactFlowTests` and `CrossModuleSmokeTests` now create real stages, and the HTTP smoke token carries `project.read_all`.
- The test helper `BriefTestSeeding` builds multi-stage projects and stage-team membership.

**Owner flag (amendment 4).** `IProjectAccessGuard` passes on `project.read_all`, read off `ICurrentUser`. Under the new decorator that permission therefore grants **write** scope on every office brief route — create, edit, send, supersede — not just reads.
- That is the guard's existing semantics, so it was left alone, but it is broader than the permission's name suggests.
- Options: accept it, or have `CrmProjectScope` require stage-team membership for commands and honour `read_all` only for queries.

---

## Run C — client loop (M5, M6, M7)

**M5 — money-free diff and portal view.**
- **Domain:** `CrmBriefHeadSnapshot.DiffFrom(CrmBriefHeadSnapshot? previous, BriefDiffAudience audience)` now returns `BriefFieldChange(Field, PreviousValue, CurrentValue)`.
  - Values are raw invariant strings: an enum as its integer (flags included), a bool as `"true"`/`"false"`, a decimal invariant.
  - With no previous snapshot (round 1), every field that has a value is a change.
  - The **Client** audience skips `ExpectedBudget` and `LastMeetingId` entirely — not even the field name appears.
- **`GET /api/crm/briefs/{briefId}/rounds/{round}/diff`** → `BriefHeadDiffResponse { briefId, round, previousRound?, changes: [{ field, previousValue?, currentValue? }] }`.
  - With `crm.briefs.read`: office audience, project-scoped by the Run B decorator (`GetCrmBriefRoundDiffQuery`).
  - Otherwise with `crm.briefs.view_own`: client audience. The brief's project must belong to the caller's client, else 404 (`GetMyPortalBriefRoundDiffQuery`, `ICrmScoped`).
  - Neither permission → 403. There is no any-of permission helper, so the route uses `RequireAuthorization()` plus explicit `HasPermission` checks.
  - An unknown round → 404 `crm.briefs.RoundNotFound`.
- **Portal brief** `GET /api/crm/portal/projects/{projectId}/brief[?stageId=]` now returns `PortalBriefViewResponse { head: BriefHeadBlock | null, spaces: CrmBriefSpaceResponse[] }`.
  - `head` is the **latest snapshot**, i.e. what the client was actually sent, never the office's unsent edits. It carries the brief's id, project, stage, live status and round, and is null until the first send.
  - `BriefHeadBlock` has **no** `ExpectedBudget` and no `LastMeetingId`.
  - `spaces` excludes Draft entries.
  - This replaces `PortalBriefResponse {Id, ProjectId, Status}`.
- **Tests:**
  - Unit: `ClientDiff_NeverContainsExpectedBudget_OrLastMeetingId`, `RoundOne_WithNoPrevious_ReportsEveryFieldThatHasAValue`, `Values_AreRawInvariantStrings`.
  - Application (`BriefClientLoopTests`, **run, green**):
    - the portal head is null until the first send, then shows that round without budget fields, and Draft entries are hidden;
    - in the office round-2 diff the budget goes 50000 → 80000, while the client diff shows only the address;
    - the client's round-1 diff has no budget;
    - an unknown round → 404;
    - another project's brief → 404, for another client and for an outsider employee. This is amendment 5's cross-project diff check.
  - HTTP (`Integration.Tests/CrmBriefRouteTests`, **run, green**): the stage route; office diff 200 with the budget; client diff 200 with no `ExpectedBudget` anywhere in the JSON; neither permission → 403; the portal view JSON has no `expectedBudget`.

**M6 — client decision path: BLOCKED, no code change.**
- Run 0 step 3 found that the seeded Client role holds **no** permissions, so it lacks `approvals.decide`. The client cannot reach `PUT /api/approvals/{id}/decide` unless an admin grants it through the role/permission admin API.
- `DecideMyPortalApprovalCommand` (`crm.approvals.decide_own`) is still declared but unmapped.
- The data model **does** support a client decider: `approval_approvers` stores `ApproverKind.Client` rows, and `Approval.Decide` matches on `ApproverUserId`. The `GetMyPortalBriefQueryHandler` remark calling the ledger "employee-scoped" is out of date.
- Every test that has the client decide calls the `DecideApprovalCommand` handler directly, and is named `…ClientDecidesViaHandler_NotPortalRoute…`.
- Owner decision needed. Either seed `approvals.decide` (+ `approvals.read`) on the Client role — the `Approval.Decide` eligibility check already confines a client to approvals naming them — or map `DecideMyPortalApprovalCommand` under `crm.approvals.decide_own`.

**M7 — `04-Approvals.md` doc change (to apply in `../docs/backend/dms-modular/`).** Add under the subject-type mapping:

> **CRM subjects (DesignBrief, BriefSpace, RequirementItem): `ChangesRequested` → `Rejected`.**
> Approvals itself treats `ChangesRequested` as non-terminal (`Approval.State` = PendingApproval).
> CRM does not: its ledger, ported from the backend, has only Draft / PendingApproval / Approved / Rejected, and Rejected already *is* the "send it back for revision" state.
> A Rejected head or entry is editable and resendable; editing a Rejected head returns it to Draft.
> The approval row itself is decided (`Decision` set), so it is no longer "open", and the resend opens a new round with a new approval.
> The frontend must therefore show a CRM subject as **Rejected (changes requested)** after a ChangesRequested decision, not Pending.

**M7 question (no code change).** `CrmGateReader.CountUnresolvedFor{Stage,Task}Async` counts only Draft and PendingApproval as unresolved. A **Rejected** brief-space entry or requirement item therefore counts as resolved, and can let `CrmItemsResolved` close a stage or task gate while the client's "no" is still unaddressed. **Should a Rejected entry keep `CrmItemsResolved` closed?**
