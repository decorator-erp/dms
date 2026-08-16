# Review — Phase 4 CRM execution plan

**Verdict: approved. Run it.** One ±1 count to settle during execution (§3), nothing blocking.

All five corrections verified. The ripple I missed is real and correctly scoped.

---

## 1. Corrections verified — four of five were my errors

| Correction | Verified |
|---|---|
| 54 permissions, not 59 | ✓ `grep -o '"crm\.[a-z_.]*"' \| sort -u \| wc -l` → **48**, plus 6 `clients.*`. My "53 crm.*" was a miscount of my own earlier grep output. 56 with Portal (below). |
| `requirements.view_own` / `requirements.submit_own` exist | ✓ both present in `DefaultPermissions.cs` — my permission survey used a `"crm\.*"`/`"clients\.*"` pattern that structurally could not see a third prefix. Same class of error as the E1 test search. |
| No rate limiter anywhere | ✓ zero `RateLimit` matches in `src/`. My brief said "built-in limiter, no new dependency" — true about the dependency, misleading about the work. It is a **shared-host change** (`DI.cs` + `Program.cs`), not module-local, and correctly re-scoped. |
| Rule 9 allow-list already has the CRM pairs | ✓ consistent with `docs/cross-schema-foreign-keys.md`, which lists `crm → identity, hr` and `projects → … crm`. G9-08 adds only a `Modules`-dictionary entry. |
| `ConditionType` already reserves 7/8/9 | ✓ consistent with the Phase 3 record confirming `Attachment=5`/`DocumentApproved=10` already present. Handlers only. |

**Lesson 1 confirmed correct, and the follow-through is better than mine.** I recorded "no extension
needed." The plan goes further and corrects the *standing expectation* at `PROGRESS.md:3136-3139`
("expect CRM to need one more such extension"). Leaving a wrong expectation in the record because the
current phase happened not to trip on it is how the next phase inherits it as fact. Right call.

## 2. The `ClientId` ripple — the finding that matters

My plan closed the Phase 2 deferral and said nothing about what that costs. Verified: **five test files
create projects** and would all break on the new FK —
`Documents.Application.Tests/FolderFlowTests.cs`, `Projects.Infrastructure.Tests/ProjectAccessGuardTests.cs`,
`Projects.Application.Tests/{GateFlowTests,ProjectFlowTests}.cs`, plus the four `Integration.Tests`
files that name `ClientId` directly.

Note my own grep for `ClientId` found only the four integration files — the module tests construct
projects without naming the field, so a literal-string search misses them. The plan found them by
searching for *project creation*, which is the right question. Third time this project that a search
answered a narrower question than intended; the plan's method was better than mine.

`Projects.UnitTests/ProjectTests.cs` correctly excluded — pure domain, no DB, and validation belongs in
`CreateProjectCommandHandler`, never in `Project.Create`. That distinction is exactly right: a domain
constructor that reaches for a cross-module directory would be a boundary violation dressed as
validation.

Budgeting three fixtures' `crm`-schema migration + client seeding as an explicit G9-13 sub-task rather
than incidental fallout is the correct handling, and the generalized lesson for Sites — *a deferred FK
closed later costs every test that inserted a fake id* — is worth more than the fix itself.

## 3. One thing to settle during execution

Entity count: the plan says 24; I count 23 `Crm*` files + `Client` + `ClientChild` = **25** (with
`CatalogAttributeValueRules` correctly excluded as a static rule class, not an entity). One of us is off
by one or two, and it only matters because it sets the EF-configuration count at G9-07.

**Don't take either number.** Count the configurations at G9-07 and assert the count in that task's
verify against the `DbSet`s actually registered. A mismatch there is a missing configuration, which is
exactly the bug an assumed number hides.

## 4. Three scope decisions — all sound

- **Vertical slices over breadth** — same trade as G5/G7, and the plan is honest that the 98-file count
  describes the backend, not this phase's output. Deferred rows per sub-area, as before.
- **Gate seam is push *and* pull** — this is the sharpest addition. My plan had only the push
  (events → Projects subscribes). The pull (`ICrmGateReader` backing `SatisfyGateConditionCommandHandler`)
  covers the case my design silently dropped: **a gate created after its CRM condition already became
  true**, which no event will ever re-fire for. That gate would have sat pending forever. Correct catch.
- **Portal is in** — justified on the strongest possible grounds: it is the only consumer of
  `CrmAccessGuard`'s client-actor branch, which would otherwise ship with no caller and no test. Dead
  code with a security role is worse than absent code.

## 5. Details worth keeping

- **`CrmCatalogV2Endpoints` only** — V1 and V2 both map `/api/crm/catalog`; porting both risks duplicate
  route registration. I had "17 endpoint files" with no such caveat.
- **Three hand-maintained places take the new schema** (`ModuleDependencyOrder`, `ApiFixture`'s `switch`,
  `RespawnerOptions.SchemasToInclude`) — the kind of list that costs an attempt when discovered at
  runtime.
- **Prove the subject-type filters with a real row's id under the wrong subject type**, per
  `adding-a-module.md:280-289`. A random-guid test passes with the filter deleted. This is E1's lesson
  applied before the fact rather than after.
- **Three backend bugs fixed on port** (`CrmLead.Convert` accepting any Guid,
  `HandleBriefSpaceDecisionAsync`'s `?? Guid.Empty` publishing a garbage project id,
  `CrmSchedulerService`'s uncaught `FindSystemTimeZoneById`) — the third is a config-value crash at
  request time, worth the startup validation.

## 6. Run it

Execute G9-01 → G10-01 unattended per the execution contract. Settle the entity count at G9-07 by
counting rather than trusting; everything else stands as written.
