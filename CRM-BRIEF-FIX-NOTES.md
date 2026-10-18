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
