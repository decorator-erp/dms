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

---

## Run D — hardening (M4, L1–L4)

**M4 — validators that match the columns.**
- `CrmBriefHeadDataValidator` is wired into the create and update-head validators through `SetValidator`. Its rules:
  - **Lengths, from `CrmDesignBriefConfiguration`:** Address 512; VisitorsNote and DesignerExperienceReason 1000; PreferredStyle and PreferredColors 256; ArchitecturalRequests and OtherNeeds 2000.
  - **Ranges:** `AreaSqm > 0`, `FloorNo >= 0`, `UnitsPerFloor >= 1`, `ExpectedBudget >= 0`.
  - **Enums:** `IsInEnum` for every one. `HiringReasons` is a `[Flags]` enum, so it is checked against the mask of its defined bits; `IsInEnum` would reject valid combinations.
- **Entries** (create, update, supersede): `Requirements` ≤ 2000 and `CountOrPieces >= 1` when set.
- All of these are now 400 `ValidationError`s instead of a 500 from `DbUpdateException` (or silently accepted negatives).
- The create handler also saves through `BriefWrites`. A race on the unique `(ProjectId, ProjectStageId)` index therefore maps to `AlreadyExists`.

**Low items**
- **L1:** a duplicate create → 409 `crm.briefs.AlreadyExists`, replacing the misleading `NotDraft`. An unknown project → 404 `crm.briefs.ProjectNotFound`.
- **L2:** the `Created` Location is `/api/crm/stages/{stageId}/brief`, the brief's real GET.
- **L3:**
  - The spaces list for an unknown brief → 404. Both the scope decorator and the handler enforce it.
  - `CrmBriefSpaceResponse.IsCurrent` is computed per list: false once another entry in the same list supersedes it. The portal list hides Drafts, so a Draft correction the client cannot see does not mark the original as superseded for them.
- **L4:** `Crm.Contracts/ICrmBriefReader.cs` (`ICrmBriefReader` + `CrmDesignBriefSummary`) was deleted; Run 0 found no users. Sites' historical comment now notes the deletion.
- `PUT …/send` is unchanged.

**Tests** (`BriefHardeningTests`, **run, green**)
- One case each for an over-long Address, OtherNeeds or PreferredStyle; a zero area; a negative floor or budget; zero units per floor; an out-of-range enum; an undefined flag bit. Each is a 400 `ValidationError` naming the field.
- An over-long VisitorsNote on update → 400.
- `Requirements` of 2001 and `CountOrPieces` of 0 → 400, while 2000 and 1 are accepted.
- `AlreadyExists` 409 and `ProjectNotFound` 404.
- An unknown brief's list → 404, and `IsCurrent` false on the superseded entry and true on the correction.
- HTTP: `CrmBriefRouteTests` asserts the new Location.

---

# Closure report

Commits on `fix/crm-brief-review`, one per run, each gated and pushed:

| Run | Commit | Gate |
|---|---|---|
| A | `c2f5cbe` | green |
| B | `5798c71` | green |
| C | `1b17fdd` | green |
| D | `0ea52e8` | green |

"Green" here means `dotnet build DMS.Modular.slnx -warnaserror` passed, `dotnet test DMS.Modular.slnx` ran all 29 test projects with 0 failures (Docker available, Testcontainers suites included), and `dotnet test tests/Architecture.Tests` passed 25/25.

## 1. Findings

| Id | Status | Commit | Proving test(s) |
|---|---|---|---|
| H1 | fixed | `c2f5cbe` | `CrmDesignBriefTests.UpdateHead_WhilePending_IsLocked_AndChangesNothing`, `…MarkApproved_WithAnotherApprovalsId_IsIgnored`, `…ADecisionForAnEarlierRound_DoesNotApplyToTheCurrentOne`, `…UpdateHead_WhileApproved_ReturnsToDraft`; `BriefRoundFlowTests.EditWhilePending_IsRejectedWith409`, `…Reject_…_ThenEditGoesToDraft_ThenResendOpensRoundTwo` |
| H2 | fixed | `5798c71` | `BriefScopeAndReferenceTests.CreateBrief_WithAnotherProjectsStage_Is400`, `…Entry_WithForeignSpaceTaskOrMeeting_Is400_OnCreateUpdateAndSupersede` |
| H3 | fixed (design adjusted, see Run 0) | `c2f5cbe` | `BriefRoundFlowTests.FailedApprovalRequest_LeavesNoSnapshot_AndThePriorStatus`, `…Resend_AfterCrmCommitWasLost_ReusesTheSameApproval`, `…Send_WhenOrphanApprovalAlreadyDecided_AppliesDecision`, `…Send_WhilePendingOnAMissingApproval_ReRequestsTheSameId`, `…ConcurrentSends_ExactlyOneWins_TheOtherGets409`; `ApprovalFlowTests.RequestingTwice_WithTheSameApprovalId_…`; `CrmApprovalIdsTests` |
| H4 | fixed (briefs, spaces, requirement items) | `c2f5cbe` | `BriefRoundFlowTests.Send_ThenApprove_…_ApprovesEnqueuesAndSatisfiesTheGate` (office sender's notification) |
| H5 | fixed | `5798c71` | `BriefScopeAndReferenceTests.EmployeeOutsideTheProject_Gets404_OnEveryBriefMessage`, `…StageTeamMember_WithoutReadAll_CanWorkTheBrief_ButGets404OnAnotherProject`; `BriefClientLoopTests.RoundDiff_OnAnotherProjectsBrief_Is404_ForBothAudiences` |
| M1 | fixed | `5798c71` | `BriefScopeAndReferenceTests.StageRoute_ReturnsThatStagesBrief_AndProjectRoute_ReturnsTheLatest`; `CrmBriefMigrationGuardTests.StageRequired_RefusesToRun_WhileABriefHasNoStage`; `CrmBriefRouteTests` |
| M2 | fixed | `5798c71` | `BriefScopeAndReferenceTests.SupersedingTheSameEntryTwice_Is409_…`, `…ConcurrentSupersedes_ExactlyOneWins`; `CrmBriefSpaceTests.CreateSuperseding_OnADifferentSpace_Fails`, `…Update_OfACorrection_CannotMoveItToAnotherSpace`; `CrmBriefMigrationGuardTests.…SupersededTwice` |
| M3 | fixed | `c2f5cbe` | `BriefRoundFlowTests.StaleWrite_OnTheBrief_IsAConcurrencyConflict`, `…ConcurrentSends_…` |
| M4 | fixed | `0ea52e8` | `BriefHardeningTests.CreateBrief_WithAnInvalidHeadField_Is400_NotA500` (9 cases), `…UpdateHead_WithAnOverLongField_Is400`, `…Entry_WithOverLongRequirements_OrNoPieces_Is400` |
| M5 | fixed | `1b17fdd` | `CrmBriefHeadSnapshotTests.ClientDiff_NeverContainsExpectedBudget_OrLastMeetingId`, `…RoundOne_…`, `…Values_AreRawInvariantStrings`; `BriefClientLoopTests` (3); `CrmBriefRouteTests` |
| M6 | **blocked** | — | Client role holds no `approvals.decide` (Run 0 step 3); see §4 |
| M7 | question + doc change | `1b17fdd` (notes) | — (no code change by instruction) |
| L1 | fixed | `0ea52e8` | `BriefHardeningTests.DuplicateCreate_IsAlreadyExists_AndAnUnknownProject_IsProjectNotFound` |
| L2 | fixed | `0ea52e8` | `CrmBriefRouteTests` (Location assertion) |
| L3 | fixed | `0ea52e8` | `BriefHardeningTests.SpacesList_ForAnUnknownBrief_Is404_AndFlagsTheCurrentLedgerLine` |
| L4 | fixed (deleted) | `0ea52e8` | build + `Architecture.Tests` (no references remain) |
| L5 | fixed | `c2f5cbe` | `BriefRoundFlowTests.Send_WhenOrphanApprovalAlreadyDecided_AppliesDecision` (the decision for an unbound approval is ignored and logged, not applied) |

No finding was invalid. Every finding was confirmed against the code before it was fixed.

## 2. Tests not run

**None.** `dockerd` could be started in the container, so every Testcontainers suite ran on every gate, including all new application, infrastructure and HTTP tests. CI should still run them as usual.

## 3. Frontend contract changes

**New routes**
- `GET /api/crm/stages/{stageId}/brief` (`crm.briefs.read`) → `CrmDesignBriefResponse`. This is now *the* way to read a brief.
- `GET /api/crm/briefs/{briefId}/rounds/{round}/diff` → `BriefHeadDiffResponse { briefId, round, previousRound?, changes: [{ field, previousValue?, currentValue? }] }`.
  - With `crm.briefs.read`: the office audience, budget included.
  - With `crm.briefs.view_own`: the client audience, no `ExpectedBudget` or `LastMeetingId`, and only the client's own projects (else 404).
  - Neither permission → 403.
  - Values are raw strings: enums as integers, bools as `"true"`/`"false"`.

**Changed routes and shapes**
- `POST /api/crm/projects/{projectId}/brief`:
  - **`projectStageId` is required** in the body.
  - The stage must belong to the project, else 400 `crm.briefs.StageProjectMismatch`.
  - `Location` is now `/api/crm/stages/{stageId}/brief`.
- `GET /api/crm/projects/{projectId}/brief`: **obsolete**. It returns the latest brief (`CreatedAtUtc desc`); switch to the stage route.
- `CrmDesignBriefResponse.projectStageId` is non-null.
- `CrmBriefSpaceResponse` gains **`isCurrent`**.
- `GET /api/crm/portal/projects/{projectId}/brief[?stageId=]`:
  - The response is now **`{ head: BriefHeadBlock | null, spaces: CrmBriefSpaceResponse[] }`**. The old `{ id, projectId, status }` is gone.
  - `head` is the **last sent** round, with `briefId`, `projectId`, `projectStageId`, live `status`, `round` and the head fields. It has **no `expectedBudget` and no `lastMeetingId`**, and it is null before the first send.
  - `spaces` has no Draft entries.
- **Head edit semantics:**
  - Editing an Approved or Rejected head returns it to **Draft**, not PendingApproval. The UI must send it again.
  - Editing while **PendingApproval** → **409** `crm.briefs.HeadLockedPendingApproval`.
  - Sending a pending or approved brief → 409 `crm.briefs.NotSendable`.
- After a client **ChangesRequested**, a CRM subject shows **Rejected**, not Pending (M7 doc change).
- No request body gains a user field. The new command fields `UserId` (office brief messages) and `SentByUserId` (the three sends) are **server-filled from the token**; a request without a user → 401 `crm.access.Unauthenticated`.
- **Scope:** every office brief route now 404s (`crm.briefs.NotFound`) for an employee who has neither a stage-team seat on the project nor `project.read_all`.

**New error codes**

| Status | Codes |
|---|---|
| 409 | `crm.briefs.HeadLockedPendingApproval`, `NotSendable`, `ConcurrentSend`, `ConcurrencyConflict`, `AlreadySuperseded`, `AlreadyExists` |
| 400 | `crm.briefs.StageProjectMismatch`, `SpaceProjectMismatch`, `TaskProjectMismatch`, `TaskStageMismatch`, `MeetingProjectMismatch`, `SupersedeSpaceMismatch`; validation errors (`Validation.General`) for head and entry fields |
| 404 | `crm.briefs.ProjectNotFound`, `RoundNotFound` |
| 401 | `crm.access.Unauthenticated` |
| 409 | `approvals.ApprovalIdInUse` |

**Internal module contract (owner amendment 9)**
- `Approvals.Contracts.ApprovalRequest` gains a leading **`Guid ApprovalId`**.
- `IApprovalRequester.RequestAsync` returns **`Result<ApprovalRequestOutcome(ApprovalId, Decision?)>`** instead of `Result<Guid>`, and is idempotent per id.
- Projects (gates) and Documents (review) callers were updated and pass `Guid.NewGuid()`.

## 4. Blockers and open questions

1. **M6 — BLOCKED.** The Client role has no `approvals.decide`, so a client cannot decide over HTTP without an admin grant. `DecideMyPortalApprovalCommand` (`crm.approvals.decide_own`) is still unmapped. Decide between:
   - seeding `approvals.decide` + `approvals.read` on the Client role, which is safe because `Approval.Decide` only accepts listed approvers;
   - mapping the portal decide command.

   Tests that decide as the client use the handler and say so in their names.
2. **M7 — question.** Should a **Rejected** brief-space entry or requirement item keep `CrmItemsResolved` closed? Today `CrmGateReader` counts Rejected as resolved. The doc change for `04-Approvals.md` is in Run C.
3. **Owner amendment 3 — question.** When an **approved head is edited**, it returns to Draft, but `CrmBriefApprovedIntegrationEvent` has already satisfied the stage gate, and nothing un-satisfies it. Should editing an approved brief re-open the gate, or is the gate a one-time milestone? Behaviour was not changed.
4. **Owner amendment 4 — flag.** `project.read_all` passes `IProjectAccessGuard`, so under the new decorator it grants **write** scope on every office brief route. Accept it, or split read and write in `CrmProjectScope`.
5. **H3 design note.** `DomainEventDispatchDecorator` wraps each command in a CRM transaction, so CRM *flushes* before requesting and *commits* after. The remaining "approval committed, CRM commit lost" window is closed by deterministic per-round approval ids plus the idempotent `RequestAsync`, which also applies a decision made in between (amendment 1). No cross-context transaction exists to use instead (Run 0 step 1).
6. **Out of scope, observed in passing.**
   - Requirement items are not round-bound the way briefs and entries now are. Their decided handler only checks the mark result.
   - Approvals' open-approval check has no DB backstop: its `(SubjectType, SubjectId)` index is not unique. For CRM subjects, the deterministic id and the CRM-side unique `(BriefId, Round)` index cover it.
7. **Decision 20 respected.** None of the 10 unit columns, nor their snapshot fields, were moved or changed. The new validator only bounds them: Address length, AreaSqm/FloorNo/UnitsPerFloor ranges, and enum ranges.

## 5. PR body

See the draft PR on `fix/crm-brief-review`. If opening it failed, the body is below.

> **CRM brief review fixes (H1–L5)**
>
> Fixes the review of CRM's design-brief slice in four runs (one commit each): approval-round integrity (H1, H3, H4, M3, L5), project scope and references (H2, H5, M1, M2), the client loop (M5; M6 blocked, M7 recorded), and hardening (M4, L1–L4).
>
> - Each round is bound to its approval; the head locks while pending and returns to Draft when a decided head is edited.
> - Sends flush CRM before requesting, with deterministic per-round approval ids. `RequestAsync` is idempotent and reports a decision CRM missed.
> - The office sender is now the requester.
> - `xmin` concurrency; races are 409s.
> - Office brief routes are project-scoped (404). Stage, space, task and meeting references must belong to the brief's project.
> - `ProjectStageId` is required, behind a data-guarded migration. New stage route; one supersede per entry.
> - Money-free client diff and portal view; validators match the columns.
>
> Migrations: `CrmBriefApprovalRounds`, `CrmBriefStageRequired`. The latter **refuses to run** if any brief has a NULL stage or any entry is superseded twice; resolve those rows first.
>
> Frontend contract changes, blockers (M6) and open questions: `CRM-BRIEF-FIX-NOTES.md` §3–§4.
>
> Gate: build `-warnaserror`, full `dotnet test` (Testcontainers included), arch tests — green on every run.
