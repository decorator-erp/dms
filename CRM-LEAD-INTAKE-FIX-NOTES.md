# CRM lead intake review — fix notes

Branch `fix/crm-lead-intake-review` off `master` at `b8cbae5` (the commit the review was taken at).
Temporary file: the owner moves it to `../docs/backend/dms-modular/` after merging.

## 0. Status report — everything worked on

| Step | Status | Commit | Outcome |
|---|---|---|---|
| Step 0 — SDK | done | — | .NET SDK 10.0.400 (pinned in `global.json`) was already in `/root/.dotnet`, just not on `PATH`. |
| Step 0 — Docker | done | — | Daemon not running at start; started `dockerd` by hand (overlay worked first try). **Every Testcontainers test ran** — nothing is "written, not run". |
| Step 0 — baseline build | done | — | `dotnet build DMS.Modular.slnx -warnaserror`: 0 warnings, 0 errors. |
| Step 0 — baseline tests | done | — | 1419 total: 1223 passed, **196 failed** (all `Integration.Tests`, each in 1 ms), 0 skipped. Pre-existing, see DEVIATION-1. |
| Step 0 — verify findings | done | — | All 9 findings reproduced at `b8cbae5`. None `skipped-invalid`. Real names recorded in §1. |
| M1 | **fixed** | `5749dd6` | Intake rules on every save. |
| H1 | **fixed** | `e20bef5`, `c2ef773` (comment tidy) | Residential block persisted. |
| M2 | **fixed** | `8f17eaa` | Intake frozen after conversion. |
| H2 | **fixed** | `a2acef0`, `b70de8e` (format) | First brief seeded from the intake (D1 option A). |
| M3 | **fixed** | `898b29f`, `2be5ef3` (arch-rule list) | Validators + active-owner check. |
| M4 | **fixed** | `30f0db1` | Paged, filtered, searchable lead list. |
| M5 | **fixed** | `4fd9cca` | No conversion into an inactive client. |
| L1 | **fixed** | `1c9e458` | `xmin` token, `LeadWrites`, one additive (SQL no-op) migration. |
| L2 | **fixed** | `e9e40c3` | 200-empty intake for a lead without one. |
| L3 | left as-is (product decision) | — | See §6. |
| I1 | left as-is (product decision) | — | See §6. |
| Gate — build `-warnaserror` | pass | — | 0 warnings, 0 errors. |
| Gate — `dotnet test DMS.Modular.slnx` | pass | — | **1486 passed, 0 failed, 0 skipped** (baseline failures gone; see DEVIATION-1). |
| Gate — `dotnet test tests/Architecture.Tests` | pass (after fix) | `2be5ef3` | First run: 1 failure (rule 11, new child validators); fixed via the rule's documented exemption list → 35/35. |
| Gate — `dotnet format` | pass | `b70de8e` | One real change (using order). See DEVIATION-12 for the line-ending noise. |
| Gate — `git diff --stat` | pass | — | Empty. |
| Notes file | done | this commit | — |
| PR | done | — | Opened ready for review; link in the session's final message. |

Blocked rows: **none**.

## 1. Findings

Names found in the code and used throughout: `CrmLead.ConvertedClientId`, `CrmLead.ConvertedAtUtc`,
`CrmLead.Status` / `LeadStatus.Converted`, `Client.IsActive` (via `Deactivate()`), HR read contract
`Hr.Contracts.IHrDirectory.GetAsync` (active employees only, K3), Projects read contract
`IProjectDirectory.GetProjectAsync` → `ProjectSummary.ClientId`, paged type
`BuildingBlocks.Pagination.PagedResult<T>`, unique index `IX_crm_lead_intakes_LeadId`.

| Id | Status | Commit(s) | Proving test(s) |
|---|---|---|---|
| H1 | fixed | `e20bef5` | `LeadIntakeReviewTests.IntakeSave_with_residential_round_trips_every_residential_field`, `…updating_residential_returns_the_new_values`, `…without_residential_on_a_residential_lead_leaves_the_child_unchanged`, `…switching_sector_away_from_residential_removes_the_child`, `…with_residential_on_a_non_residential_sector_is_rejected` |
| H2 | fixed | `a2acef0` | `LeadIntakeReviewTests.FirstBrief_is_seeded_from_the_converted_leads_intake`, `FirstBrief_keeps_the_office_entered_values_over_the_intake`, `A_brief_for_a_second_stage_is_not_seeded`, `A_brief_for_a_client_without_a_converted_lead_is_unchanged`, `A_first_brief_with_omitted_unit_details_and_no_intake_to_seed_from_is_400`; `BriefCreateValidatorTests.*` |
| M1 | fixed | `5749dd6` | `CrmLeadIntakeTests.Create_WithExperienceButNoPriorUse_Fails`, `Create_WithReasonButNoExperience_Fails`; `LeadIntakeReviewTests.FirstIntakeSave_with_experience_but_no_prior_designer_is_rejected_and_writes_nothing` |
| M2 | fixed | `8f17eaa` | `LeadIntakeReviewTests.IntakeSave_on_a_converted_lead_is_refused_and_leaves_the_intake_unchanged` |
| M3 | fixed | `898b29f`, `2be5ef3` | `LeadValidatorTests.*` (27 cases); `LeadIntakeReviewTests.Create_with_an_over_long_name_is_400_not_500`, `Create_with_an_undefined_source_or_bad_email_is_400`, `Update_with_an_over_long_phone_or_undefined_source_is_400`, `ChangeStatus_to_an_undefined_status_or_to_Converted_is_400`, `Intake_with_an_over_long_field_an_undefined_enum_or_a_zero_area_is_400`, `Create_and_Update_with_an_unknown_owner_are_OwnerNotFound`, `An_inactive_owner_is_refused_on_create_and_on_reassignment_but_keeps_existing_leads_editable` |
| M4 | fixed | `30f0db1` | `LeadIntakeReviewTests.LeadList_pages_newest_first_with_a_total_count`, `LeadList_filters_by_status_and_by_owner`, `LeadList_searches_first_name_last_name_email_and_phone_case_insensitively`, `LeadList_search_treats_like_wildcards_literally`, `LeadList_with_a_page_size_over_200_is_400`, `LeadList_validator_rejects_out_of_range_paging` (×3) |
| M5 | fixed | `4fd9cca` | `LeadIntakeReviewTests.Convert_into_a_deactivated_client_is_refused_without_publishing` |
| L1 | fixed | `1c9e458` | `CrmLeadConcurrencyModelTests.CrmLead_maps_xmin_as_its_concurrency_token`; `LeadIntakeReviewTests.A_lost_update_on_a_lead_is_ConcurrentUpdate`, `A_second_first_intake_for_one_lead_is_ConcurrentUpdate_not_a_unique_violation_500`, `Two_parallel_converts_convert_once_and_publish_once`, `Two_parallel_first_intake_saves_leave_one_intake_and_never_a_500` |
| L2 | fixed | `e9e40c3` | `LeadIntakeReviewTests.GetIntake_for_a_lead_without_intake_returns_empty_data`, `GetIntake_for_a_missing_lead_is_not_found` |

New error codes (all tested): `crm.leads.ResidentialWithoutSector` (400), `crm.leads.OwnerNotFound` (400),
`crm.leads.ConcurrentUpdate` (409), and `crm.briefs.UnitDetailsRequired` (400, see DEVIATION-4).

## 2. Red-before-green

Each test was run on the code as it stood before its row was implemented (for M1, the source change was
stashed and the tests run against the old code).

| Row | Test | Observed failure before the fix |
|---|---|---|
| M1 | `Crm.UnitTests` `CrmLeadIntakeTests.Create_WithExperienceButNoPriorUse_Fails` / `Create_WithReasonButNoExperience_Fails` | compile error CS1061 (`CrmLeadIntake` has no `Value`): `Create` returned the entity, not a `Result` |
| M1 | `FirstIntakeSave_with_experience_but_no_prior_designer_is_rejected_and_writes_nothing` | `Assert.True() Failure` — the bad first save succeeded |
| H1 | `IntakeSave_with_residential_round_trips_every_residential_field` | `Assert.Equal() Failure` — expected `{Villa, SemiFinished, …}`, actual `null` |
| H1 | `IntakeSave_updating_residential_returns_the_new_values` | `Assert.Equal() Failure` — actual `null` |
| H1 | `IntakeSave_without_residential_on_a_residential_lead_leaves_the_child_unchanged` | `Assert.Equal() Failure` — actual `null` |
| H1 | `IntakeSave_with_residential_on_a_non_residential_sector_is_rejected` | `Assert.True() Failure` — 204 for discarded data |
| H1 | `IntakeSave_switching_sector_away_from_residential_removes_the_child` | passed on old code — the stale-child delete already existed; kept as a regression guard |
| M2 | `IntakeSave_on_a_converted_lead_is_refused_and_leaves_the_intake_unchanged` | `Assert.True() Failure` — the save on a converted lead succeeded |
| H2 | `FirstBrief_is_seeded_from_the_converted_leads_intake` | 400 `Validation.General` (IsInEnum on the omitted unit type) — nothing seeds |
| H2 | `FirstBrief_keeps_the_office_entered_values_over_the_intake` | `Assert.Equal() Failure` — expected `Residential`, actual `null` (Sector never seeded) |
| H2 | `A_brief_for_a_second_stage_is_not_seeded` | `Assert.True() Failure` — the first brief with omitted unit details was refused |
| H2 | `A_first_brief_with_omitted_unit_details_and_no_intake_to_seed_from_is_400` | `Assert.Contains() Failure` — `crm.briefs.UnitDetailsRequired` not found |
| H2 | `A_brief_for_a_client_without_a_converted_lead_is_unchanged` | passed on old code (regression guard) |
| H2 | `BriefCreateValidatorTests.Create_accepts_omitted_unit_details_so_the_first_brief_can_seed_them` (no Docker) | failed — IsInEnum rejected `UnitType = 0` |
| M3 | `LeadValidatorTests.*` (no Docker; over-length per request type, undefined enum per request type incl. ChangeStatus, ChangeStatus→Converted, bad email, AreaSqm 0 / −5) | `Assert.Contains() Failure` on every case — validators only checked NotEmpty |
| M3 | `Create_with_an_over_long_name_is_400_not_500` | `DbUpdateException` (Postgres 22001) — a 500 |
| M3 | `Create_and_Update_with_an_unknown_owner_are_OwnerNotFound` | `DbUpdateException` (FK `fk_crm_leads_owner`) — a 500 |
| M3 | `ChangeStatus_to_an_undefined_status_or_to_Converted_is_400` | `Assert.Equal() Failure` — expected 400 (undefined status was stored) |
| M3 | `Update_with_an_over_long_phone_or_undefined_source_is_400`, `Create_with_an_undefined_source_or_bad_email_is_400`, `Intake_with_an_over_long_field_an_undefined_enum_or_a_zero_area_is_400` | failed — 500 or the value was stored |
| M3 | `An_inactive_owner_is_refused_on_create_and_on_reassignment_but_keeps_existing_leads_editable` | failed — the inactive owner was accepted |
| M4 | `LeadList_*` | compile error CS1729 / CS0246 — `GetCrmLeadsQuery` took no paging or filter args and had no validator; the list was an unbounded `IReadOnlyList` |
| M5 | `Convert_into_a_deactivated_client_is_refused_without_publishing` | `Assert.True() Failure` — conversion into the soft-deleted client succeeded |
| L1 | `CrmLeadConcurrencyModelTests.CrmLead_maps_xmin_as_its_concurrency_token` (no Docker) | `Assert.NotNull() Failure` — no `xmin` property |
| L1 | `A_lost_update_on_a_lead_is_ConcurrentUpdate`, `A_second_first_intake_…`, `Two_parallel_converts_…`, `Two_parallel_first_intake_saves_…` | compile error CS0103 / CS0117 — no `LeadWrites`, no `CrmLeadErrors.ConcurrentUpdate` |
| L2 | `GetIntake_for_a_lead_without_intake_returns_empty_data` | failed — 404 `crm.leads.NotFound` |
| L2 | `GetIntake_for_a_missing_lead_is_not_found` | passed on old code (regression guard) |

## 3. Tests

| | Total | Passed | Failed | Skipped |
|---|---|---|---|---|
| Baseline (`b8cbae5`, full solution) | 1419 | 1223 | 196 (`Integration.Tests`, pre-existing, DEVIATION-1) | 0 |
| Final (`b70de8e`, full solution) | 1486 | 1486 | 0 | 0 |

New tests: +67 (`Crm.UnitTests` 220 → 222, `Crm.Infrastructure.Tests` 18 → 19, `Crm.Application.Tests` 102 → 166).
`Architecture.Tests` 35 → 35. **"Written, not run": none** — Docker was available, every test ran.
No test was deleted, skipped or ignored.

## 4. Migration

One migration, additive: `Crm.Infrastructure/Persistence/Migrations/InitialCrm/20261024143316_CrmLeadConcurrencyToken`.

```csharp
// No-op in SQL: xmin is Postgres' system row-version column, which every table already has.
// Npgsql's migration SQL generator skips system columns, so this emits no DDL (checked with
// `dotnet ef migrations script`); EF records it only so the model snapshot knows the token.
/// <inheritdoc />
protected override void Up(MigrationBuilder migrationBuilder) =>
    migrationBuilder.AddColumn<uint>(
        name: "xmin",
        schema: "crm",
        table: "crm_leads",
        type: "xid",
        rowVersion: true,
        nullable: false,
        defaultValue: 0u);
```

Same shape as the existing `CrmBriefApprovalRounds` xmin migration. `dotnet ef migrations script
20261018192940_CrmBriefBackfillPendingApprovalIds CrmLeadConcurrencyToken` emits **no DDL**:

```sql
START TRANSACTION;
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261024143316_CrmLeadConcurrencyToken', '10.0.13');
COMMIT;
```

It applies on a fresh database: both test fixtures (`Crm.Application.Tests`, `Crm.Infrastructure.Tests`) migrate
from empty and pass.

**Empty-second-migration proof:** `dotnet ef migrations add ThrowawayEmptyCheck` right afterwards generated

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }
```

The throwaway was deleted (DEVIATION-11). The snapshot also caught up the shared `outbox_messages` model
(DEVIATION-10).

## 5. D1 as implemented (option A, copy-once)

`CreateCrmDesignBriefCommandHandler`, after the existing project, stage and duplicate checks:

1. If **no brief exists on the `ProjectId`** (any stage), it is the project's first brief. Otherwise nothing is seeded.
2. Read `ProjectSummary.ClientId` through `IProjectDirectory` (the call the handler already makes).
3. Find the intake of a lead with `Status == Converted && ConvertedClientId == clientId`. **Several converted
   leads: the newest by `ConvertedAtUtc`, then by `Id` descending** (deterministic tie-break). In this
   session's tests each client has at most one converted lead, so the rule wasn't hit in practice.
4. Load that intake's residential child and call `brief.SeedFromIntake(...)` before the save.
5. **The request wins on every field.** `SeedFromIntake` fills blanks for every nullable field. It overwrites
   `UnitType`/`UnitCondition`, so the handler resolves those two first (request value, else the intake's
   residential value, else 400 `crm.briefs.UnitDetailsRequired`) and passes `null` for them to `SeedFromIntake`
   (DEVIATION-4). `Sector` isn't part of the head request, so the intake's sector is always stamped.
6. Copy-once: the intake is never read again for later briefs; the brief head is the live truth from then on.

## 6. Product decisions left as-is

- **L3 — open status workflow.** `ChangeStatus` still allows any transition between non-converted statuses
  (New ↔ Contacted ↔ Qualified ↔ Unqualified). The only rules are unchanged: a converted lead is final, and
  Converted is reached only through convert (now also a 400 at the validator, see §8).
- **I1 — no duplicate check, no delete/archive, no owner scoping.** Two leads with the same email or phone are
  still allowed; leads cannot be deleted or archived; any holder of `crm.leads.read` sees every lead (the new
  `ownerId` filter is a filter, not a scope).

## 7. Deviations and blocked items

Blocked: **none**.

- **DEVIATION-1 — pre-existing `Integration.Tests` failures.** At baseline the full parallel run failed all 196
  `Integration.Tests` in ~1 ms each (fixture start-up under load, not test bodies). The project passed 196/196
  when run on its own at baseline, and passed in both full final runs. Recorded, not fixed; it isn't in code
  this PR touches.
- **DEVIATION-2 — plan mode.** The session opened in plan mode, despite the prompt's "no plan mode". The plan
  was written and approved once (with the owner's revisions); after that no step stopped for approval.
- **DEVIATION-3 — no `Commercial` sector.** `ProjectSector` has no `Commercial`. The "residential with a
  non-residential sector" tests use `Medical`.
- **DEVIATION-4 — `UnitType`/`UnitCondition` on brief create.** The revised plan said to make them nullable on
  the brief-create request only. The request type is the shared `Crm.Contracts.Briefs.CrmBriefHeadData`, also
  used by `UpdateCrmBriefHeadCommand` and in `CrmDesignBriefResponse.Head`. Making it nullable would have
  changed the update contract and the response shape too. Chosen instead: the record is unchanged, and **on
  create only** an omitted value counts as blank. Omitted means the JSON field is absent, which binds to the
  enum default `0`; that value is outside the enum, so it was already a 400. Only the create validator
  (`new CrmBriefHeadDataValidator(unitDetailsMayBeOmitted: true)`) accepts it; the handler fills it from the
  intake or returns the new 400 `crm.briefs.UnitDetailsRequired`. Update still requires both fields.
- **DEVIATION-5 — `ResidentialWithoutSector` lives in the handler only.** The plan said to also put it in the
  intake validator for a Docker-free test. A validator failure surfaces as `Validation.General`, which would
  hide the named code the prompt asks for. Docker ran, so the app tests are the red/green evidence.
- **DEVIATION-6 — "IsInEnum on Source and Status (Create/Update)".** `CreateCrmLeadCommand` and
  `UpdateCrmLeadCommand` carry no `Status`, so only `Source` is checked there. `Status` is checked on
  `ChangeCrmLeadStatusCommand` and on the list query's filter.
- **DEVIATION-7 — owner check on update only when the owner changes.** Otherwise a lead whose owner has left
  could never be edited again. Covered by `An_inactive_owner_is_refused_on_create_and_on_reassignment_but_keeps_existing_leads_editable`.
- **DEVIATION-8 — `IHrDirectory` has no separate "inactive" answer.** `GetAsync` sees active employees only
  (K3), so `null` covers both unknown and inactive, and both map to `OwnerNotFound`. No existence-only
  fallback was needed.
- **DEVIATION-9 — parallel first-intake test assertion.** If the two saves serialize, the second one
  legitimately updates the first one's row, so both succeed. The test asserts at least one success, any
  failure is `ConcurrentUpdate`, and exactly one intake row. Observed in 5/5 rounds: one `ok` plus one
  `crm.leads.ConcurrentUpdate`. Parallel converts: 5/5 rounds gave one success plus `ConcurrentUpdate`, and
  exactly one `LeadConverted` outbox row each time. `AlreadyConverted` is also accepted, in case the two
  serialize.
- **DEVIATION-10 — snapshot catch-up.** Regenerating `CrmDbContextModelSnapshot` also brought in the current
  shared `outbox_messages` model (`dead_lettered_utc`, `next_attempt_utc`, the widened index filter), which
  earlier migrations never refreshed. CRM migrations exclude that table (the generated `Up()` has no outbox
  DDL), so this is snapshot-only.
- **DEVIATION-11 — throwaway migration removed by deleting its files.** `dotnet ef migrations remove` couldn't
  build: the generated throwaway breaks the repo's analyzers (IDE0022 on its empty method bodies). Its two
  files were deleted by hand. The snapshot was unaffected; an empty migration doesn't change it.
- **DEVIATION-12 — `dotnet format` line endings.** It rewrote ~1000 files to CRLF (`.editorconfig` says
  `end_of_line = crlf`), but `.gitattributes` (`* text=auto eol=lf`) normalizes them back on add. The only
  real content change (using order in one test file) was committed as `b70de8e`, and `git diff --stat` is
  empty.
- **DEVIATION-13 — arch rule 11.** The two new child validators (`CrmLeadIntakeDataValidator`,
  `CrmLeadIntakeResidentialDataValidator`) were added to the rule's documented `ChildValidators` exemption
  list, next to `CrmBriefHeadDataValidator`. They are reachable only through `SetValidator`. No test was
  weakened.
- **DEVIATION-14 — red observed as compile errors for some rows.** M1's unit tests, M4 and L1's app tests use
  API the old code didn't have, so their red is a compile error. Each of those rows also has a behavioural red
  (M1's app test, L1's model test; M4's list had no paging at all).
- **DEVIATION-15 — PR tooling.** `gh` isn't available in this environment, so the PR was opened through the
  GitHub connector. It's ready for review, not a draft, as the prompt asked.

## 8. Frontend impact

- **`GET /api/crm/leads` is paged.** Response is `PagedResult`: `{ items, pageNumber, pageSize, totalCount }`
  (was a bare array). Query params: `pageNumber` (default 1), `pageSize` (default 50, max 200), `status`,
  `ownerId`, `search` (case-insensitive contains on first name, last name, email and phone; `%` and `_` are
  literal). Order: newest first.
- **Residential intake round-trips.** `PUT /leads/{id}/intake` with `residential` now stores it and
  `GET …/intake` returns it. Omitting `residential` on a residential lead keeps what's stored. Switching
  sector away from Residential deletes it.
- **`GET /leads/{id}/intake` → 200 with all-null `data`** (including `residential: null`) for a lead that has no
  intake yet. A missing lead is still 404 `crm.leads.NotFound`.
- **409 `crm.leads.AlreadyConverted`** when saving a converted lead's intake.
- **ChangeStatus to `Converted` → 400** validation error (was 409 `crm.leads.InvalidStatusTransition`).
- **Brief create:** `unitType` / `unitCondition` may be omitted on create. On the project's first brief they
  come from the converted lead's residential intake; otherwise 400 `crm.briefs.UnitDetailsRequired`. The
  first brief also gets sector, area, unit address, designer history and the residential flags wherever the
  request leaves them blank.
- **New codes:** `crm.leads.ResidentialWithoutSector` (400), `crm.leads.OwnerNotFound` (400, unknown or
  inactive owner on create, or on reassignment), `crm.leads.ConcurrentUpdate` (409, reload and retry),
  `crm.briefs.UnitDetailsRequired` (400).
- **400s that used to be 500s:** over-long names, email, phone, notes, job title, addresses and reason; an
  unknown owner (was an FK 500); a duplicate first intake save (now 409). **400s for values that used to be
  stored:** undefined enums, a malformed email, `areaSqm ≤ 0`, an inactive owner.

## 9. PR body

The PR was opened with the GitHub connector; its body is the summary above (§0, §1, §8).

## Follow-ups F1, F2 (PR 11)

Branch `fix/crm-lead-followups` off `master` at `d942e71` (the PR #10 merge). PR:
https://github.com/decorator-erp/dms/pull/11 (ready for review).

### Status

| Item | Status | Commit | Proving tests |
|---|---|---|---|
| F1 — blank email/phone means none | **fixed** | `5cff9ee` | `CrmLeadTests.Create_WithBlankEmailOrPhone_StoresNull` (×2), `Update_WithBlankEmailOrPhone_StoresNull` (×2), `Create_And_Update_TrimEmailAndPhone`; `LeadValidatorTests.Blank_emails_are_accepted_on_create_and_update` (×2), `Malformed_emails_are_still_rejected` (×2), `A_257_character_email_is_still_rejected`; `LeadIntakeReviewTests.Create_with_an_empty_email_saves_and_stores_null`, `Update_with_a_whitespace_email_saves_and_stores_null`, `Create_with_a_malformed_email_is_still_400` |
| F2 — intake save can't race a convert | **fixed** | `40f1274` | `LeadIntakeReviewTests.An_intake_save_waits_for_an_uncommitted_convert_then_is_refused` (main proof), `A_convert_waits_for_a_lock_held_the_way_the_intake_save_takes_it_then_succeeds`, `A_convert_and_a_first_intake_save_in_parallel_never_leave_an_intake_written_after_conversion` (10 rounds); M2, H1 and L1 tests unchanged and green |

Both findings still held on `master` at `d942e71`: the validators ran `LeadEmail()` whenever `Email is not
null`, and the intake save read the lead with `FindAsync` without writing or locking it. Blocked: none. No
migration, no data rewrite: existing rows that already hold `""` for email or phone stay as they are.

### Red-before-green

| Item | Test | Observed failure on unchanged code |
|---|---|---|
| F1 | `CrmLeadTests.Create_WithBlankEmailOrPhone_StoresNull("" / "   ")`, `Update_WithBlankEmailOrPhone_StoresNull("" / "   ")` | `Assert.Null() Failure: Value is not null` |
| F1 | `CrmLeadTests.Create_And_Update_TrimEmailAndPhone` | `Assert.Equal() Failure: Strings differ` (not trimmed) |
| F1 | `LeadValidatorTests.Blank_emails_are_accepted_on_create_and_update("" / "   ")` | `Assert.True() Failure`: `EmailAddress` rejected the blank |
| F1 | `LeadIntakeReviewTests.Create_with_an_empty_email_saves_and_stores_null`, `Update_with_a_whitespace_email_saves_and_stores_null` | failed: 400 `Validation.General` on `Email` |
| F1 | `Malformed_emails_are_still_rejected`, `A_257_character_email_is_still_rejected`, `Create_with_a_malformed_email_is_still_400` | passed on old code (regression guards) |
| F2 | `An_intake_save_waits_for_an_uncommitted_convert_then_is_refused` | `Assert.NotSame() Failure`: the save completed at once and wrote the intake instead of waiting on the lead row |
| F2 | `A_convert_waits_for_a_lock_held_the_way_the_intake_save_takes_it_then_succeeds` | passed on old code. Expected: the test takes the lock itself, so this guards the convert side. |
| F2 | `A_convert_and_a_first_intake_save_in_parallel_never_leave_an_intake_written_after_conversion` | passed on old code: 10/10 rounds `convert ok, save ok, intake written`. Timing alone doesn't hit the race window, which is why the deterministic test is the proof. |

### Tests

| | Total | Passed | Failed | Skipped |
|---|---|---|---|---|
| Baseline (`master` `d942e71`) | 1486 | 1486 | 0 | 0 |
| Final (`40f1274`) | 1502 | 1502 | 0 | 0 |

+16 tests: `Crm.UnitTests` 222 → 227, `Crm.Application.Tests` 166 → 177. Docker ran, so nothing is "written, not
run". `Integration.Tests` passed 196/196 in both full runs; the known parallel start-up flake didn't occur.
Gates: `build -warnaserror` clean; `Architecture.Tests` 35/35; `dotnet format` left no real change (only CRLF
noise, normalized away); `git diff --stat` empty.

### F2 transaction path

**Ambient decorator transaction; no execution strategy; no fallback needed.**
- `AddModuleDbContext` configures plain `UseNpgsql` with no `EnableRetryOnFailure`, so
  `CreateExecutionStrategy().ExecuteAsync` doesn't apply.
- Every CRM command handler is already wrapped by `DomainEventDispatchDecorator`, which opens a transaction
  before the handler and commits or rolls it back after. The `FOR UPDATE` lock is held by that transaction until
  it ends.
- The handler opens its own transaction only when `CurrentTransaction is null` (a caller running it
  undecorated). The lock approach worked on the first attempt; the `xmin` fallback wasn't used.

Parallel-race outcomes observed with the fix: 10/10 rounds `convert ok, save ok, intake written` (the save won
the lock each time). The `AlreadyConverted` path is proved deterministically by the main test.

### DEVIATIONs

- **DEVIATION-16:** the prompt asked for an explicit `BeginTransactionAsync` in the handler. Every CRM command
  already runs inside `DomainEventDispatchDecorator`'s transaction, and a second `BeginTransactionAsync` on the
  same connection throws. The handler uses the ambient transaction and opens one only when none exists.
- **DEVIATION-17:** the prompt's lock query `SELECT * … FOR UPDATE` fails with `42703: column c.xmin does not
  exist`: `SELECT *` omits system columns, and `CrmLead` maps `xmin` (added in L1). The query is
  `SELECT *, xmin FROM crm.crm_leads WHERE "Id" = {leadId} FOR UPDATE`.
- **DEVIATION-18:** "never an intake written after `ConvertedAtUtc`" isn't checked by comparing timestamps.
  `Convert()` stamps `ConvertedAtUtc` before its `UPDATE` waits on the lock, so a legitimate save-first round can
  show `intake.CreatedAtUtc > ConvertedAtUtc`. The parallel test asserts the two allowed end states (save ok and
  intake exists, or `AlreadyConverted` and no intake); the ordering itself is proved by the two deterministic
  tests.
- **DEVIATION-19:** the baseline build ran while the new test files were being written, so the first baseline run
  already included 10 of them (7 failing, i.e. red). The baseline above is `master`'s own suite (1486, all
  passing, same as PR #10's final run). The red table comes from a separate run of all new tests on unchanged
  source.
- **DEVIATION-20 (behaviour note):** L1's `Two_parallel_first_intake_saves_leave_one_intake_and_never_a_500` now
  logs `ok, ok`. The lock serializes the two first saves, so the second one updates the first one's row instead of
  hitting the unique index (409). The test allows both outcomes and is unchanged.
- **DEVIATION-21:** the session opened in plan mode, despite "no plan mode". The plan was approved once; after
  that no step stopped for approval.

### Frontend impact

- `""` or whitespace for `email` or `phone` on lead create and update is accepted and stored as `null`. Other
  values are stored trimmed. A malformed or over-long email is still a 400.
- Saving an intake while the lead is being converted may wait briefly, then returns 409
  `crm.leads.AlreadyConverted` if the convert won.
- Two simultaneous first saves of one intake now both succeed (last write wins) instead of one getting 409
  `crm.leads.ConcurrentUpdate`.

## Open items F3, F4, F5, LC-L5, LC-L6 (PR 12)

Branch `claude/gifted-planck-x3lvtg` off `master` at `2e48b80` (the PR #11 merge); see DEVIATION-22. PR:
https://github.com/decorator-erp/dms/pull/12 (ready for review).

All five items still held on `master` at `2e48b80`; none was `skipped-invalid`. Blocked: **none**.

### Status

| Item | Status | Commit | Proving tests |
|---|---|---|---|
| LC-L6 — convert takes the lead lock | **fixed** | `a397050` | `LeadIntakeReviewTests.A_convert_waits_for_an_uncommitted_intake_save_then_fills_the_client_from_the_new_intake`; L1, M5 and F2 convert tests unchanged and green |
| LC-L5 — whitespace counts as blank | **fixed** | `f8798c3` | `ClientTests.FillBlanksFromIntake_FillsAWhitespaceJobTitle_WithTheTrimmedIntakeValue`, `FillBlanksFromIntake_KeepsARealJobTitle`, `FillBlanksFromIntake_NeverCopiesAWhitespaceIntakeValue`; `CrmLeadIntakeTests.Create_And_Update_StoreWhitespaceOnlyTextAsNull`, `Create_And_Update_StorePaddedTextTrimmed`, `Create_WithAWhitespaceReasonAndNoExperience_IsNotAReasonWithoutExperience` |
| F3 — brief `Sector` | **fixed** | `d12e80a` | `LeadIntakeReviewTests.FirstBrief_with_a_sector_in_the_request_keeps_it_over_the_intakes`, `FirstBrief_without_a_sector_takes_the_intakes`, `A_later_stage_brief_without_a_sector_inherits_the_projects_latest_brief_sector`, `A_brief_for_a_client_without_a_converted_lead_stores_the_requested_sector`, `Update_with_a_sector_replaces_it_and_without_one_keeps_it`, `An_undefined_sector_is_400_on_create_and_on_update`, `A_brief_for_a_second_stage_is_not_seeded` (assertion updated, DEVIATION-24); `BriefCreateValidatorTests.An_undefined_sector_is_rejected_on_create_and_on_update`; `BriefRoundFlowTests.RoundDiff_ShowsASectorChange` |
| F4 — module snapshots match the model | **fixed** | `eb5da3a` | `has-pending-model-changes` clean for all 10 contexts; `migrations script` emits no DDL for each of the 8 kept migrations; every fixture migrates a fresh DB (all `*.Infrastructure.Tests`, `*.Application.Tests` and `Integration.Tests` green) |
| F5 — `Integration.Tests` start-up | **fixed (not reproduced)** | `66102b2` | 3 baseline runs plus 1 forced-parallel run with 0 failures; 3 final runs with 0 failures |

### Red-before-green

Red was run on unchanged source. For F3, only the trailing `Sector` contract parameter had been added at that
point, so the tests compiled and failed on behaviour rather than at compile time.

| Item | Test | Observed failure on unchanged code |
|---|---|---|
| LC-L6 | `A_convert_waits_for_an_uncommitted_intake_save_then_fills_the_client_from_the_new_intake` | `Assert.Equal() Failure: Strings differ`. Expected `"New title"`, got the stale `"Old title"`. The convert did wait (its UPDATE blocked on the lock), but it had already read the old intake. |
| LC-L5 | `FillBlanksFromIntake_FillsAWhitespaceJobTitle_WithTheTrimmedIntakeValue` | `Assert.Equal() Failure: Strings differ` (the whitespace title counted as filled) |
| LC-L5 | `FillBlanksFromIntake_NeverCopiesAWhitespaceIntakeValue` | `Assert.Null() Failure: Value is not null` |
| LC-L5 | `Create_And_Update_StoreWhitespaceOnlyTextAsNull`, `Create_And_Update_StorePaddedTextTrimmed` | `Assert.Null()` / `Assert.Equal()` failures (stored as sent) |
| LC-L5 | `Create_WithAWhitespaceReasonAndNoExperience_IsNotAReasonWithoutExperience` | `crm.leads.ExperienceReasonWithoutExperience`: a blank reason counted as a reason |
| LC-L5 | `FillBlanksFromIntake_KeepsARealJobTitle` | passed on old code (regression guard) |
| F3 | `FirstBrief_with_a_sector_in_the_request_keeps_it_over_the_intakes` | expected `Medical`, actual `Residential` (`SeedFromIntake` overwrote it) |
| F3 | `A_later_stage_brief_without_a_sector_inherits_…`, `A_brief_for_a_second_stage_is_not_seeded` | expected `Residential`, actual `null` |
| F3 | `A_brief_for_a_client_without_a_converted_lead_stores_the_requested_sector` | expected `Medical`, actual `null` |
| F3 | `Update_with_a_sector_replaces_it_and_without_one_keeps_it` | expected `Residential`, actual `null` |
| F3 | `An_undefined_sector_is_400_on_create_and_on_update`, `BriefCreateValidatorTests.An_undefined_sector_is_rejected_…` | `Assert.True() Failure` / no validation error (accepted) |
| F3 | `BriefRoundFlowTests.RoundDiff_ShowsASectorChange` | `Assert.Single() Failure: The collection was empty` (sector never reached the head) |
| F3 | `FirstBrief_without_a_sector_takes_the_intakes` | passed on old code (H2 regression guard) |

F4 and F5 have no red test; their proof is below.

### Tests

| Run | Total | Passed | Failed | Skipped | `Integration.Tests` |
|---|---|---|---|---|---|
| Baseline 1 (`2e48b80`) | 1502 | 1502 | 0 | 0 | 196/196 |
| Baseline 2 | 1502 | 1502 | 0 | 0 | 196/196 |
| Baseline 3 | 1502 | 1502 | 0 | 0 | 196/196 |
| Forced parallel (full run + a second `Integration.Tests` run at once, before the F5 change) | 1517 | 1517 | 0 | 0 | 196/196 and 196/196 |
| Final 1 (`66102b2`) | 1517 | 1517 | 0 | 0 | 196/196 |
| Final 2 (`66102b2`) | 1517 | 1517 | 0 | 0 | 196/196 |
| Final 3 (`66102b2`) | 1517 | 1517 | 0 | 0 | 196/196 |

+15 tests: `Crm.UnitTests` 227 → 233, `Crm.Application.Tests` 177 → 186. Docker ran, so nothing is "written, not
run". No test was deleted, skipped or ignored.
Gates on `66102b2`: `build -warnaserror` gave 0 warnings and 0 errors. `dotnet test DMS.Modular.slnx` passed 3
consecutive runs with 0 failures, and `Architecture.Tests` passed 35/35. `dotnet format` changed only line endings
(the CRLF noise from DEVIATION-12, normalised by `.gitattributes`): `git diff --ignore-cr-at-eol` showed no content
change, so nothing was committed for it. `git diff --stat` is empty after the notes commit.

### F3 as implemented

`CreateCrmDesignBriefCommandHandler` resolves the effective sector in this order:
1. the request's `Sector`, when set (validated with `IsInEnum`);
2. on the project's first brief, the converted lead's intake sector. `SeedFromIntake` now does `Sector ??= sector`,
   so it only fills a blank, like every other field;
3. on a later brief, the `Sector` of the project's most recent brief, ordered by `CreatedAtUtc desc`, then `Id desc`;
4. otherwise null.

On update, `Apply` sets `Sector` only when the request carries one. Null means keep, the same rule as
`LastMeetingId`. **A sector can be changed but never cleared back to null. This is intended:** a project keeps its
sector once it has one.

Snapshot, diff and portal: `CrmBriefHeadSnapshot` already captured `Sector` field by field, `DiffFrom` already
compared it, and the portal `BriefHeadBlock` already exposed it (it was always null before). Its column
`crm_brief_head_snapshots.Sector` already exists, so **F3 needs no migration**. `has-pending-model-changes` for
`CrmDbContext` stayed clean after F3 landed.

### F4 per module

`has-pending-model-changes` (`--project`/`--startup-project` = the module's Infrastructure project, which carries its
design-time factory) reported **no changes for all 10 contexts, both before and after F3.** That check can't see the
problem: the stale part is the shared `outbox_messages` and `audit_logs` model, which every module maps with
`ExcludeFromMigrations`, so the differ produces no operations for it (DEVIATION-25). A `grep` of the snapshots showed
8 of them still lacked `dead_lettered_utc`, `next_attempt_utc` and the widened unprocessed index.
`migrations add SyncModelSnapshot` regenerated each one; every generated `Up()` was empty.

| Context | Snapshot stale? | `Up()` | Result |
|---|---|---|---|
| `SharedDbContext` (owns `outbox_messages`) | no | — | no migration needed |
| `CrmDbContext` | no (caught up in L1, DEVIATION-10) | — | no migration needed |
| `IdentityModuleDbContext` | yes (outbox) | empty | kept `20261027052914_SyncModelSnapshot` |
| `HrDbContext` | yes (outbox, plus an explicit `IX_departments_Name` name equal to the default) | empty | kept `20261027052917_SyncModelSnapshot` |
| `ProjectsDbContext` | yes (outbox) | empty | kept `20261027052907_SyncModelSnapshot` |
| `ApprovalsDbContext` | yes (outbox, `audit_logs`) | empty | kept `20261027052920_SyncModelSnapshot` |
| `DocumentsDbContext` | yes (outbox, `audit_logs`) | empty | kept `20261027052900_SyncModelSnapshot` |
| `NotificationsDbContext` | yes (outbox) | empty | kept `20261027052910_SyncModelSnapshot` |
| `SitesDbContext` | yes (outbox, `audit_logs`) | empty | kept `20261027052924_SyncModelSnapshot` |
| `FinanceDbContext` | yes (outbox) | empty | kept `20261027052904_SyncModelSnapshot` |

Each kept migration has empty `Up()`/`Down()` bodies in the repo's file-scoped style, plus a comment saying it's an
intentional no-op. The generated code failed `-warnaserror` on IDE0161 (file-scoped namespace). Empty block bodies
pass the analyzers. Afterwards `has-pending-model-changes` is clean for all 10 contexts, and `migrations script
<previous> SyncModelSnapshot` emits only the history row for all 8. For example, HR
(`20261020045733_HrDataIntegrity` → `SyncModelSnapshot`):

```sql
START TRANSACTION;
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261027052917_SyncModelSnapshot', '10.0.13');

COMMIT;
```

### F5

**The root-cause exception was not captured: the flake did not reproduce.** All 3 baseline full runs passed
`Integration.Tests` 196/196. So did one forced-parallel attempt, a full solution run with a second
`Integration.Tests` run started at the same moment. Per the amended instructions it was not retried further.

The fixture hardening is low-risk and stays in. `ApiFixture.InitializeAsync` now calls `StartAndMigrateAsync`, which
makes up to 3 attempts. Each attempt uses a fresh `postgres:17` container with a 3-minute start timeout, waits until
Postgres actually answers `SELECT 1` (up to 30 s; the container's readiness check can pass while the server restarts
after init), then migrates every schema. A failed attempt's container is disposed, and the next one starts after a
2 s or 4 s backoff. The third failure is rethrown as an `AggregateException` holding all three, so if the flake comes
back its cause is in the first failing test's output. No test is skipped, the project stays in the solution run, and
parallelization is unchanged. Run evidence is in the Tests table above.

### Self-audit

- Every item is `fixed` (F5 as `fixed (not reproduced)`). Each code item has its red-before-green entry above.
- Migrations: only F4's 8 `SyncModelSnapshot` no-ops. F3 needed none.
- No test deleted, skipped or ignored. One assertion changed on purpose (DEVIATION-24).
- No secret committed: the fixture still generates its JWT key and admin password per run, and the diff contains no
  key or password value.
- Gates: recorded in the Tests section.

### DEVIATIONs

- **DEVIATION-22 — branch.** The session's harness pins all pushes to `claude/gifted-planck-x3lvtg` (already at
  `master` `2e48b80`), so work went there instead of `fix/crm-lead-open-items`. One commit per item with the
  `crm-leads: <id>` prefix, as asked.
- **DEVIATION-23 — plan mode.** The session switched into plan mode despite "no plan mode". The plan was approved
  once, with the owner's amendments A1–A3, and nothing stopped for approval after that.
- **DEVIATION-24 — H2 assertion.** `A_brief_for_a_second_stage_is_not_seeded` asserted `Sector == null` on a stage-2
  brief. F3's decided rule makes it inherit the first brief's sector, so that one assertion now expects `Residential`
  (with a comment). The test's other assertions, which check that nothing else is seeded, are unchanged.
- **DEVIATION-25 — F4 detection.** `has-pending-model-changes` reported no changes for every context, which read
  literally would make F4 `skipped-invalid`. The problem described does hold: 8 snapshots were stale on excluded
  shared tables, which that command can't see. The safer reading was to sync those 8 with empty, no-DDL migrations,
  the outcome the item asks for.
- **DEVIATION-26 — LC-L5 placement.** Intake text is cleaned in `CrmLeadIntake.Create`/`Update`, before the field
  rules run, rather than inside `Apply`. The stored values are the same, and a whitespace-only
  `DesignerExperienceReason` no longer trips `ExperienceReasonWithoutExperience`.
- **DEVIATION-27 — `dotnet-ef` needed `DOTNET_ROOT`.** The global tool couldn't find the runtime in `~/.dotnet`
  until `DOTNET_ROOT` was set. Environment only, no repo change.

### Frontend impact

- The brief head has an optional `sector`. Send it on create or update; omitting it keeps the stored value, and a
  sector can't be cleared back to null. An undefined value is a 400.
- On create without a `sector`, the project's first brief takes the converted lead's intake sector, and later-stage
  briefs inherit the sector from the project's latest brief.
- The round diff and the portal head may now show a `Sector` change or value.
- Lead intake text (`jobTitle`, `clientAddress`, `unitAddress`, `designerExperienceReason`) comes back trimmed, and
  whitespace-only is stored as null. A client's blank job title is now filled from the intake on convert.
- A convert may wait briefly while an intake save of the same lead is in progress. It then fills the client from the
  saved intake.
