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
