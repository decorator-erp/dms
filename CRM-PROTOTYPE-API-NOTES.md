# CRM prototype API — scope of work + CRM gaps (notes)

Plan: `dms-docs` `backend/dms-modular/plans/PLAN-CRM-PROTOTYPE-API.md` §3 (scope of work) and §4 (CRM gaps), read
from `dms-docs` `origin/main` @ `1c1c72e`. Temporary file: the owner moves it to `../docs/backend/dms-modular/` after
merging.

Branch `feat/scope-of-work`, stacked on PR #12's head `e3730bf` (DEVIATION-1). PR: https://github.com/decorator-erp/dms/pull/13 (ready for review, stacked on #12).

## Status

| Row | Status | Commit | Proving tests |
|---|---|---|---|
| SOW-1 — `StageInstantiator` refactor | **fixed** | `c217e94` | refactor; the existing instantiation and project tests are the proof (`ProjectFlowTests.*`, `GateFlowTests.*`, 21/21 unchanged), plus `ScopeOfWorkConfirmTests.CreateProject_from_a_template_still_creates_the_same_skeleton` |
| SOW-2 — template `DefaultFee` + `TaskCount` | **fixed** | `b53521d` (HTTP binding in `99850ea`, DEVIATION-8) | `StageFeeTests.Create_and_update_with_a_default_fee_round_trip`, `A_negative_default_fee_is_400_on_create_and_on_update`, `TaskCount_counts_each_stages_template_tasks` |
| SOW-3 — `ScopeOfWork` domain, persistence, migration | **fixed** | `b0a28e0` | `Projects.UnitTests.ScopeOfWorkTests` (13 tests: every rule, total, renumbering, reorder, confirm rules, every mutation after confirm, estimated end); all Projects fixtures migrate a fresh DB |
| SOW-4 — routes, handlers, permissions | **fixed** | `99850ea`, `1008f3c` (permission total, DEVIATION-20) | `ScopeOfWorkFlowTests` (10 tests) — picking from two templates, duplicate → 409, whole template skips picked, missing source, edit/remove/reorder, GET empty Draft writes nothing + header round-trip, outsider → 404 on every route, 400s, concurrent edit → 409, first-save race → 409 (R8), missing project → 404; `ProjectsSmokeTests.ScopeOfWork_routes_are_403_for_a_client_user`; `SeedingTests.Full_access_roles_hold_the_scope_permissions_and_the_client_role_holds_none` (R4); `EnumMappingParityTests` (+1 pair) |
| SOW-5 — confirm | **fixed** | `5f8658a` | `ScopeOfWorkConfirmTests` (12 tests) — core three-items-from-two-templates, dates at 00:00Z (R1), dates untouched without a planned start, `ProjectHasStages`, `SourceStageNotFound`, `MissingHead` creates nothing, each 400 rule, every mutation after confirm → 409, two parallel confirms (3 rounds), event payload primitives (R9), FreeStyle mirrors the template path (R2), `CreateProject(StageTemplateId)` regression; `Architecture.Tests` (event declared unconsumed) |
| GAP-1 — lead status counts | **fixed** | `1201145` | `LeadIntakeReviewTests.StatusCounts_count_a_seeded_set_per_status`, `StatusCounts_respect_the_owner_and_search_filters`, `StatusCounts_agree_with_the_list`; M4 list tests unchanged and green |
| GAP-2 — brief rounds list | **fixed** | `e54ac9d` | `BriefRoundFlowTests.Rounds_list_changes_requested_voided_and_approved_and_the_portal_hides_the_voided_round`, `Rounds_are_404_for_another_client_and_for_an_employee_outside_the_project`; `CrmBriefHeadSnapshotTests.RecordOutcome_is_set_once_and_a_second_call_is_a_no_op` (R3d) |
| GAP-3 — `ProjectResponse.StageCount` | **fixed** | `f975c9f` | `ProjectStageCountTests.StageCount_is_0_without_stages_and_3_after_a_three_stage_template_in_both_queries`, `The_stage_count_is_a_subquery_of_one_statement_not_a_query_per_project` (R7, `ToQueryString`) |

Blocked: **none**. Skipped-invalid: **none**.

## Red-before-green

Red was run on the code before each row's implementation (for rows that add handlers, the new handler file was
moved out of the tree for the red run; for SOW-2 and GAP-3 the contract field existed with a placeholder value, so
the red is behavioural).

| Row | Test | Observed failure before the fix |
|---|---|---|
| SOW-1 | — | refactor; existing tests are the proof (21/21 Projects app tests green on the refactor, unchanged) |
| SOW-2 | `Create_and_update_with_a_default_fee_round_trip` | `Assert.Equal() Failure` — expected `1500.50`, fee not stored |
| SOW-2 | `A_negative_default_fee_is_400_on_create_and_on_update` | `Assert.True() Failure` — a negative fee was accepted |
| SOW-2 | `TaskCount_counts_each_stages_template_tasks` | `Assert.Equal() Failure` — expected `3`, actual `0` |
| SOW-3 | `ScopeOfWorkTests.*` (13) | compile error CS0103 — `ScopeOfWorkErrors` does not exist (DEVIATION-18) |
| SOW-4 | `ScopeOfWorkFlowTests.*` (10) | `InvalidOperationException: No service for type ICommandHandler<AddScopeOfWorkItemsCommand, …>` (no handlers) |
| SOW-4 | `ScopeOfWork_routes_are_403_for_a_client_user` | 404 on all 7 routes (no routes) |
| SOW-4 | `Full_access_roles_hold_the_scope_permissions_…` | `Assert.Contains() Failure` — `scope.read` not on admin |
| SOW-5 | `ScopeOfWorkConfirmTests.*` (11 of 12) | `No service for type ICommandHandler<ConfirmScopeOfWorkCommand, …>` |
| SOW-5 | `CreateProject_from_a_template_still_creates_the_same_skeleton` | passed on old code — a regression guard by design |
| GAP-1 | `StatusCounts_*` (3) | `No service for type IQueryHandler<GetCrmLeadStatusCountsQuery, …>` |
| GAP-2 | `Rounds_list_changes_requested_voided_and_approved_…` | with outcome recording stashed: `Collections differ` — expected `[ChangesRequested, Voided, Pending]`, actual `[Unknown, Unknown, Pending]` |
| GAP-2 | `Rounds_are_404_for_another_client_…` | passed with the recording stashed (the read side and its scoping were in place) — a guard |
| GAP-2 | `RecordOutcome_is_set_once_…` | written with the method; red not observed separately (DEVIATION-17) |
| GAP-3 | `StageCount_is_0_without_stages_and_3_…` | `Assert.Equal() Failure` — expected `3`, actual `0` |
| GAP-3 | `The_stage_count_is_a_subquery_of_one_statement…` | passed on the placeholder — it guards the query shape, not the value |

## Tests

| Run | Total | Passed | Failed | Skipped |
|---|---|---|---|---|
| Baseline (`e3730bf`, full solution) | 1517 | 1517 | 0 | 0 |
| First final run (`f975c9f`) | 1566 | 1565 | 1 (`GetPermissions_TotalsAcrossAllNineModules`, DEVIATION-20) | 0 |
| Final (`1008f3c`, full solution) | 1566 | 1566 | 0 | 0 |

New tests by project: `Projects.UnitTests` 61 → 74, `Projects.Application.Tests` 21 → 49, `Crm.UnitTests` 233 → 234,
`Crm.Application.Tests` 186 → 191, `Integration.Tests` 196 → 198. Docker ran: nothing is "written, not run". No test
was deleted, skipped or ignored; no existing assertion was changed in this PR.

Gates on `1008f3c` (plus the format commit):
- `dotnet build DMS.Modular.slnx -warnaserror`: 0 warnings, 0 errors.
- `dotnet test DMS.Modular.slnx`: **1566 passed, 0 failed, 0 skipped** (baseline 1517; +49).
- `dotnet test tests/Architecture.Tests`: 35/35.
- `dotnet format`: two real changes (a using order, an initializer layout), committed; its CRLF rewrites of other
  files are normalised by `.gitattributes` and were discarded.
- `git diff --stat`: empty after the notes commit.
- `dotnet ef migrations has-pending-model-changes`: none for `ProjectsDbContext` and `CrmDbContext`.

**Self-audit:**
- every row is `fixed`, each with a red-before-green entry;
- migrations: one Projects (`ScopeOfWork`) and one CRM (`CrmBriefRoundOutcome`), both additive;
- `CreateProject(StageTemplateId)` is unchanged, with a regression test;
- no test was deleted or skipped;
- no secret is committed: test fixtures generate their keys, and no key or password value is in the diff.

## Migrations

Two, both additive.

**Projects — `20261027122430_ScopeOfWork`** (two tables + `stages.DefaultFee`):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<decimal>(
        name: "DefaultFee", schema: "projects", table: "stages",
        type: "numeric(18,2)", precision: 18, scale: 2, nullable: true);

    migrationBuilder.CreateTable(
        name: "scopes_of_work", schema: "projects",
        columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
            Status = table.Column<int>(type: "integer", nullable: false),
            DurationDays = table.Column<int>(type: "integer", nullable: true),
            PlannedStartDate = table.Column<DateOnly>(type: "date", nullable: true),
            AdjustmentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
            AdjustmentNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
            ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            ConfirmedById = table.Column<Guid>(type: "uuid", nullable: true),
            xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
            IsActive = table.Column<bool>(type: "boolean", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_scopes_of_work", x => x.Id);
            table.ForeignKey("FK_scopes_of_work_projects_ProjectId", x => x.ProjectId,
                principalSchema: "projects", principalTable: "projects", principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        });

    migrationBuilder.CreateTable(
        name: "scope_of_work_items", schema: "projects",
        columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            ScopeOfWorkId = table.Column<Guid>(type: "uuid", nullable: false),
            SourceStageId = table.Column<Guid>(type: "uuid", nullable: false),
            Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
            Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
            Order = table.Column<int>(type: "integer", nullable: false),
            Fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
            CreatedStageId = table.Column<Guid>(type: "uuid", nullable: true),
            IsActive = table.Column<bool>(type: "boolean", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_scope_of_work_items", x => x.Id);
            table.ForeignKey("FK_scope_of_work_items_scopes_of_work_ScopeOfWorkId", x => x.ScopeOfWorkId,
                principalSchema: "projects", principalTable: "scopes_of_work", principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        });

    migrationBuilder.CreateIndex("IX_scope_of_work_items_ScopeOfWorkId", schema: "projects",
        table: "scope_of_work_items", column: "ScopeOfWorkId");
    migrationBuilder.CreateIndex("IX_scope_of_work_items_ScopeOfWorkId_Order", schema: "projects",
        table: "scope_of_work_items", columns: new[] { "ScopeOfWorkId", "Order" });
    migrationBuilder.CreateIndex("IX_scope_of_work_items_ScopeOfWorkId_SourceStageId", schema: "projects",
        table: "scope_of_work_items", columns: new[] { "ScopeOfWorkId", "SourceStageId" }, unique: true);
    migrationBuilder.CreateIndex("IX_scopes_of_work_ProjectId", schema: "projects",
        table: "scopes_of_work", column: "ProjectId", unique: true);
}
```

(Argument lists condensed here; the file has EF's generated layout.) `dotnet ef migrations script` shows `xmin` left
out of `CREATE TABLE` — Npgsql skips the system column, as on CRM leads and briefs.

**CRM — `20261027092002_CrmBriefRoundOutcome`** (GAP-2):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<DateTime>(
        name: "DecidedAtUtc", schema: "crm", table: "crm_brief_head_snapshots",
        type: "timestamp with time zone", nullable: true);

    migrationBuilder.AddColumn<int>(
        name: "Outcome", schema: "crm", table: "crm_brief_head_snapshots",
        type: "integer", nullable: true);
}
```

**Empty-second-migration proof**, for each context right after its migration: `dotnet ef migrations add
ThrowawayEmptyCheck` generated

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }
```

for both Projects and CRM; both throwaways were deleted and each model snapshot compared byte-identical to before.
Both migrations apply to a fresh database: every Projects and CRM fixture migrates from empty and passes.

## Step 0 facts (names used)

- **Permission seeding:** `ProjectsModule.Register`'s `ModulePermissionsSet` (Projects' `IModulePermissions`).
  Identity's `RoleClaimSeeder` adds every catalog permission a full-access role lacks, on every start, to existing
  roles too (`RoleClaimSeeder.cs`, the `Except` diff). `DefaultRoles.FullAccessRoles = [Admin, Manager]`, so both get
  `scope.*` with no seeder change; the client role gets none.
- **Tokens (R4):** `JwtProvider` puts one permission claim per resolved effective permission into each token, and
  `RefreshTokenCommandHandler` mints through `IJwtProvider.GenerateTokenAsync(user, roles)`. A signed-in admin's
  current access token keeps its old claims; the first refresh (or a new login) carries `scope.*`. The UI's
  permission poll (`dms-docs` `backend/19-stale-permission-claims`) sees the new codes up to ~10 minutes earlier
  than the token; the API trusts the token, so until the refresh the routes answer 403.
- **Projects scope marker:** `Projects.Contracts.IProjectScoped` (`ProjectId`, `UserId`), enforced by
  `ProjectScopeDecorator` (any failure → 404 `projects.NotFound`). `projects.read_all` bypasses membership; the
  office roles hold it.
- **Department heads:** `IHrDirectory.GetDepartmentHeadsAsync(departmentIds)`, resolved before anything is staged;
  a missing head is `TeamErrors.MissingHead` (decision #7) — unchanged in `StageInstantiator`.
- **Instantiation:** `CreateProjectCommandHandler.InstantiateAsync` → `StageInstantiator.InstantiateAsync(project,
  IReadOnlyList<StagePick>)`, the only caller of `ProjectStage.Create` (checked by grep).
- **FreeStyle (R2):** `ProjectTask.Create` returns `TaskErrors.StageNotAllowed` for a staged task in a FreeStyle
  project; `CreateProject(StageTemplateId)` returns that error. Confirm goes through the same instantiator and
  returns the same error (stages without tasks would instantiate in both).
- **GAP-2 outcome source:** a past round's outcome was **not** stored in CRM: only the brief's current
  `Status`/`PendingApprovalId` existed, and `crm_brief_head_snapshots` was write-once. Chosen: nullable `Outcome`
  and `DecidedAtUtc` on the snapshot, set once —
  - `CrmApprovalOutcomes.ApplyToBriefAsync` records `Approved` / `ChangesRequested` on the brief's highest round
    (snapshots are created only on send, so that is the round being decided), with the event's `OccurredAtUtc`;
    in the send path's missed-decision branch the time is "now" (approximate; `ApprovalRequestOutcome` carries no
    decision time, and no call to Approvals was added);
  - `SendCrmBriefCommandHandler`'s void branch records `Voided` on round `nextRound - 1`, before the `nextRound`
    snapshot is added;
  - reading: a stored outcome wins; otherwise the current round follows the brief (PendingApproval → `Pending`,
    Approved → `Approved`, Rejected → `ChangesRequested`), and anything else is `Unknown`. No backfill.

## DEVIATIONs

- **DEVIATION-1 — stacked PR.** PR #12 wasn't merged when this session ran, and this work builds on it (the
  sector chip). The session's harness branch is PR #12's own branch, so pushing here would have mixed the two PRs.
  Branch `feat/scope-of-work` was cut from PR #12's head and the PR targets `claude/gifted-planck-x3lvtg`. Merge #12
  first, with a merge commit or rebase; if #12 is squash-merged, rebase this branch onto master and drop #12's
  commits before review.
- **DEVIATION-2 — the plan was read from dms-docs** (`origin/main` @ `1c1c72e`, read-only). Its §3.2 codes and §3.3
  routes are used as written. Extra codes kept: `projects.scope.TooManyItems` (400), `projects.scope.ItemNotFound`
  (404), `projects.scope.NegativeFee` (400).
- **DEVIATION-3 — enums are integers on the wire.** The API has no `JsonStringEnumConverter`, so
  `CrmBriefRoundOutcome` and `ScopeOfWorkStatus` serialize as numbers (values in the frontend contract).
- **DEVIATION-4 — event shape.** `ScopeOfWorkConfirmedIntegrationEvent(EventId, ScopeOfWorkId, ProjectId, TotalFees,
  DurationDays, OccurredAtUtc)`: every `IIntegrationEvent` here carries `EventId` and `OccurredAtUtc`. `TotalFees`
  is the total (fees + adjustment).
- **DEVIATION-5 — the 403 for a client user is an HTTP test** (`Integration.Tests`); route claims only exist there.
  The token carries exactly the seeded client role's permissions, read from `identity.role_claims`.
- **DEVIATION-6 — plan mode.** At the owner's request ("plan first"), the session planned before coding; the plan was
  approved with the owner's amendments R0–R9.
- **DEVIATION-7 — one Projects migration, generated twice.** SOW-2's `DefaultFee` column needs a migration for its
  tests to pass (EF refuses to migrate a model with pending changes), so `ScopeOfWork` was generated in SOW-2 with
  that column only, then regenerated in SOW-3 with the two tables (old files deleted, snapshot restored from SOW-1,
  `migrations add` again). The PR holds one Projects migration.
- **DEVIATION-8 — SOW-2's HTTP binding landed in SOW-4.** The template stage endpoints bind private request DTOs
  that SOW-2 didn't touch; `defaultFee` was added to `CreateStageRequest`/`UpdateStageRequest` in `99850ea`.
- **DEVIATION-9 — R8 is deterministic.** Instead of racing two handler calls (timing-dependent), the first save is
  held uncommitted in a transaction and the second runs through the handler: it blocks on the unique `ProjectId`
  index, then gets `ConcurrentUpdate` when the first commits. One row remains.
- **DEVIATION-10 — confirm with no scope row is `Empty` (400)**: nothing has been picked.
- **DEVIATION-11 — dates without a planned start.** The plan says the end is "left empty"; the prompt says the
  project's dates are untouched. The prompt wins; the test asserts both dates unchanged.
- **DEVIATION-12 — header body.** The design doc's `PUT` omits `plannedStartDate`; the plan and prompt include it.
  Included.
- **DEVIATION-13 — `(ScopeOfWorkId, Order)` is non-unique.** The design doc says unique; the plan and prompt say
  non-unique (a reorder rewrites every order in one save). Non-unique; 1..n is the aggregate's rule.
- **DEVIATION-14 — every scope save is `xmin`-checked.** `xmin` sits on the scope row, so an edit touching only item
  rows would skip it; `ScopeWrites` marks the root modified on every save.
- **DEVIATION-15 — a missing project is 404 on scope reads and first writes.** A project-wide reader passes the scope
  decorator without the row being checked; the GET and the first write check it, instead of a 500 on the FK.
- **DEVIATION-16 — the 50-item cap is checked twice:** the request validator (at most 50 source ids) and the domain
  (`TooManyItems` when the scope would exceed 50).
- **DEVIATION-17 — `RecordOutcome`'s set-once unit test** was written with the method; its red wasn't observed
  separately.
- **DEVIATION-18 — SOW-3's red is a compile error** (the aggregate is new); its behaviour is then covered by 13 unit
  tests, every one green.
- **DEVIATION-20 — the first final gate run had 1 failure, caused by this PR.**
  `SitesSmokeTests.GetPermissions_TotalsAcrossAllNineModules` pins the system-wide permission total (expected 130,
  actual 133: the three `scope.*` codes). Fixed by that test's own rule (add each module's contribution from its
  registered constant; never retype the total): `ProjectsPermissions.Scope.All` is the list `ProjectsModule`
  registers, and the test adds its count (`1008f3c`). Not a weakened test: the total is still exact.
- **DEVIATION-19 — GAP-3 red placeholder.** The analyzers reject an unused parameter, so the red run's placeholder
  was `stages.Count(s => s.Id == p.Id)` (always 0). Never committed.

## Frontend contract

Enums are integers (DEVIATION-3). `ScopeOfWorkStatus`: `Draft = 1`, `Confirmed = 2`. `CrmBriefRoundOutcome`:
`Pending = 1`, `Approved = 2`, `ChangesRequested = 3`, `Voided = 4`, `Unknown = 5`.

### Scope of work (office only) — `/api/projects/{projectId}/scope-of-work`

| Method | Route | Permission | Body → result |
|---|---|---|---|
| GET | `/` | `scope.read` | → `ScopeOfWorkResponse`; **200 with an empty Draft** (`id: null`, no items) when none exists yet; nothing is written |
| PUT | `/` | `scope.manage` | `{ durationDays?, plannedStartDate? ("yyyy-MM-dd"), adjustmentAmount, adjustmentNote?, notes? }` → 204; replaces the whole header; creates the Draft on the first save |
| POST | `/items` | `scope.manage` | `{ sourceStageIds: [guid…] }` (1–50, distinct) → 200 `ScopeOfWorkItemResponse[]` (the added items, appended in the order given) |
| POST | `/items/from-template/{stageTemplateId}` | `scope.manage` | → 200 `ScopeOfWorkItemResponse[]`: every stage of the template not yet picked, in template order (empty when all were picked) |
| PUT | `/items/order` | `scope.manage` | `{ itemIds: [guid…] }` — the full new order → 204 |
| PUT | `/items/{itemId}` | `scope.manage` | `{ name, description?, fee }` → 204 |
| DELETE | `/items/{itemId}` | `scope.manage` | → 204; the rest are renumbered 1..n |
| POST | `/confirm` | `scope.confirm` | → 200 `{ createdStageIds: [guid…] }` in item order |

```
ScopeOfWorkResponse {
  id: guid | null, projectId: guid, status: 1|2, durationDays: int | null, plannedStartDate: "yyyy-MM-dd" | null,
  estimatedEndDate: "yyyy-MM-dd" | null,            // plannedStartDate + durationDays
  subtotal: decimal, adjustmentAmount: decimal, adjustmentNote: string | null, total: decimal,   // total = subtotal + adjustment
  notes: string | null, confirmedAtUtc: datetime | null, confirmedById: guid | null,
  items: ScopeOfWorkItemResponse[]                  // ordered by `order`
}
ScopeOfWorkItemResponse {
  id: guid, sourceStageId: guid, sourceTemplateName: string | null,  // null once the template stage is deleted
  name: string, description: string | null, order: int, fee: decimal, createdStageId: guid | null
}
```

- Picking copies the template stage's name, description and `defaultFee` (0 when it has none); later template
  edits don't change the scope.
- An adjustment ≠ 0 needs a note to confirm. Negative = discount. The total must be ≥ 0.
- Confirm creates only the picked stages, with each item's name, description and fee as the stage `cost`, plus
  tasks, subtasks and teams copied from the source. With a planned start, the project's `estimatedStartDate` becomes
  that date at 00:00Z and `estimatedEndDate` = start + `durationDays`; without one, the project's dates are left
  alone. After confirm, every change is 409.
- Limits: name 200 (the stage name it becomes), description 2000, note 500, notes 2000; `durationDays` 1–3650; `plannedStartDate` 2000-01-01..2100-12-31; fee ≥ 0; adjustment ±1e12;
  at most 50 items.

### Changed shapes

- `StageResponse` (`GET /api/stage-templates/{id}/stages`) gains `defaultFee: decimal | null` and
  `taskCount: int`.
- `POST /api/stage-templates/{id}/stages` and `PUT /api/stage-templates/stages/{stageId}` accept `defaultFee`
  (≥ 0). **On update, omitting `defaultFee` keeps the stored fee; a default fee can be changed but not cleared**
  (same rule as the other optional stage fields). Intended.
- `ProjectResponse` gains `stageCount: int` (single and list queries).

### CRM

| Method | Route | Permission | Result |
|---|---|---|---|
| GET | `/api/crm/leads/status-counts?ownerId=&search=` | `crm.leads.read` | `{ total, new, contacted, qualified, unqualified, converted }`, under the same owner and search filters as the lead list |
| GET | `/api/crm/briefs/{briefId}/rounds` | `crm.briefs.read` (project-scoped, 404 outside) | `[{ round, sentAtUtc, outcome, decidedAtUtc? }]` by round, voided rounds included |
| GET | `/api/crm/portal/briefs/{briefId}/rounds` | `crm.briefs.view_own` | the same shape for the caller's own brief (another client → 404), voided rounds left out |

Rounds sent before this change show `Unknown` unless they are the brief's current round. A round whose decision
reached CRM through the send path's recovery branch has an approximate `decidedAtUtc`.

### New error codes

| Code | Status |
|---|---|
| `projects.scope.Confirmed` | 409 |
| `projects.scope.ProjectHasStages` | 409 |
| `projects.scope.DuplicateItem` | 409 |
| `projects.scope.ConcurrentUpdate` | 409 — reload and retry |
| `projects.scope.Empty` | 400 |
| `projects.scope.DurationRequired` | 400 |
| `projects.scope.AdjustmentNoteRequired` | 400 |
| `projects.scope.NegativeTotal` | 400 |
| `projects.scope.SourceStageNotFound` | 400 |
| `projects.scope.InvalidOrder` | 400 |
| `projects.scope.TooManyItems` | 400 (extra) |
| `projects.scope.NegativeFee` | 400 (extra) |
| `projects.scope.ItemNotFound` | 404 (extra) |

Confirm can also return `projects.NotFound` (404; a missing or inactive project), `teams.MissingHead` (409; a picked
stage's department has no head, nothing is created) and `tasks.StageNotAllowed` (400; a FreeStyle project with a
picked stage that has tasks), exactly as `CreateProject(StageTemplateId)` does.

### Permissions

`scope.read`, `scope.manage`, `scope.confirm` — admin and manager (full-access roles, seeded on start, existing
databases included); the client role holds none (403). A signed-in user gets them on the next token refresh or
login (see Step 0, Tokens).

### Converting a lead into a new client

No API change: `POST /api/clients` to create the client, then `POST /api/crm/leads/{id}/convert` with its id.

## Review fixes (before merge)

A code review of PR 13 found two real 500s and some smaller issues. Fixed on the branch before merging:
- **Item name vs stage name.** A scope item's name allowed 256 characters, but confirm copies it into
  `ProjectStage.Name` (`varchar(200)`), so a 201–256 character name made every confirm a 500 (Postgres 22001). The
  validator and the `scope_of_work_items.Name` column are now 200. The unmerged `ScopeOfWork` migration was
  regenerated (`20261027122430`), so the PR still has one Projects migration. Tests: the validation test now uses
  a 201-character name; `An_item_name_at_the_longest_allowed_length_confirms_into_a_stage` confirms a 200-character
  name.
- **Planned start overflow.** An unbounded `plannedStartDate` (e.g. 9999-12-01) plus `durationDays` overflowed
  `DateOnly` on every later GET and on confirm (500). The header validator bounds it to 2000-01-01..2100-12-31.
  Red on the old validator: the validation test failed.
- **CRM `/rounds` route** now uses `CrmEndpointActors.RequireUserId` like its siblings (401 instead of a null
  user id), and sits after the round-diff route so the diff's comment is back on its own route.
- `CrmApprovalOutcomes`' doc reference points at `ApplyToBriefAsync`; `GetStagesByTemplate` projects in SQL again.

Kept as is, by decision:
- The notes file at the repo root: the session prompt put it there, as PRs 10–12 did.
- The confirm handler's own-transaction fallback, which mirrors `SaveCrmLeadIntakeCommandHandler`.
- `ConfirmedById` from `ICurrentUser`, as the prompt asked ("as `CreateProject` resolves its owner").
- `ScopeOfWorkStore`, a static helper over the module's `DbContext`, not a repository abstraction.

After the fixes: **1567 passed, 0 failed, 0 skipped**; Architecture 35/35; `build -warnaserror` clean; no pending
model changes for Projects or Crm.

## Follow-up review fixes (PR after 13)

A second review of PR 13, after it merged, found three small issues. Fixed on `fix/scope-of-work-review`, one
commit each:

| Fix | Commit | Red before green |
|---|---|---|
| A template add that adds nothing changes nothing: no Draft created, no save. Before, the save bumped `xmin`, so another user's open edit got a false `ConcurrentUpdate`. A confirmed scope still answers `Confirmed`; a missing project is 404. | `dad846b` | `Adding_a_template_whose_stages_are_all_picked_changes_nothing`: `projects.scope.ConcurrentUpdate` on the other user's save. `Adding_an_empty_template_to_a_project_without_a_scope_creates_no_scope`: expected 0 scope rows, actual 1. |
| A template stage's `DefaultFee` is capped at 1e12 on create and update, the same cap as a scope item's fee. Before, there was no upper bound: a value past `numeric(18,2)` overflowed at save (500), and values over 1e12 were copied into items. | `14283d5` | `A_default_fee_above_the_cap_is_400_and_the_cap_itself_is_accepted`: a 1e13 fee was accepted. |
| The task count filters by the stage ids already loaded instead of a correlated re-filter, and is skipped for an empty template. Pure refactor. | `1bdec0a` | Refactor; `TaskCount_counts_each_stages_template_tasks` stays green. |

Tests: 1567 → **1570 passed, 0 failed, 0 skipped**. Architecture 35/35; `build -warnaserror` clean; no migration
and no pending model changes; `dotnet format` made no content change.

Frontend: adding a template whose stages are all already picked returns `[]` and changes nothing. A stage's
`defaultFee` must be between 0 and 1e12.
