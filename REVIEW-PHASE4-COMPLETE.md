# Phase 4 completion review

**Verdict: do not merge yet.** The Docker gap is correctly identified as the blocker and I endorse it
without reservation. The Meeting gap is **my plan's error**, not an execution failure — details below.

---

## 1. The Meeting gap is a hole in `PLAN-PHASE4.md`

Verified. `PLAN-PHASE4.md` mentions Meetings exactly three times:

- **line 121** — backend sub-area inventory ("Meetings 11")
- **line 179** — `MeetingScheduled` integration event in `Crm.Contracts`
- **lines 196-199** — **G9-03**, `Crm.Domain` entities (`CrmMeeting`, `CrmMeetingSlot`, the enums)

And the Application tasks allocate: G9-09 Clients/Contacts/Leads/Notes · G9-10 Briefs/Spaces/SpaceTypes ·
G9-11 Catalog · G9-12 Periods/Items/Artifacts.

**No task owns the Meetings Application layer.** The domain entities were planned, the integration event
was planned, and the scope-decision prose named "meeting" in the vertical-slice spine — which is exactly
what made it read as covered.

The provenance is specific: in the earlier draft, G9-13 was titled *"Meetings, the Approvals
subscription, and the Projects `ClientId` FK."* When I rewrote that plan as `PLAN-PHASE4.md`, I retitled
G9-13 to lead with the gate handlers, and Meetings dropped out of the task list while surviving in the
scope sentence. A sub-area named in prose, allocated to no task, in a plan whose task table is the only
thing an executor works from.

**The failure mode worth generalizing** (and the new `docs/adding-a-module.md` section is the right
place): *scope prose and the task table are two lists, and nothing checks them against each other.* A
plan that names N sub-areas in its scope decision and allocates M task slots should reconcile the two
explicitly. Cheap to check, invisible when skipped.

**Leaving it unimplemented was right.** Twelve handlers improvised during an endpoints task, unreviewed,
against a test suite that cannot run, would have been the worse outcome by a wide margin. Same for the
two Portal operations needing cross-module design calls. Deferring with reasoning recorded is the
correct call and matches how this project has handled every other mid-run discovery.

## 2. Docker: the blocker, and it is bigger than a test-count statistic

**168 of 532 tests (32%) have never executed**, and 14 of 21 test projects are Testcontainers-dependent.
What did not run is not a random third of the suite — it is *the specific evidence this phase was
designed to produce*:

| Never run | Why it matters |
|---|---|
| Six-module characterization flow | The phase's headline claim that CRM integrates end-to-end |
| Three-subscriber assertion | The G7-07 registration bug's **third** exercise — the whole reason lesson 2 was written into the plan |
| Migration order with the `"crm"` **insertion** | The first insertion (not append) in the project's history. If wrong, migrations fail from empty — and nothing has applied them to a real database |
| `projects.ClientId` FK + the three-fixture ripple | A new cross-schema FK plus rewrites to how every existing project-inserting test supplies a client |
| Rule 9 (cross-schema FK direction) | Needs a live DB — so the FK-direction guarantee is **unverified** for CRM's two new raw-SQL constraints |
| Rate limiter burst behaviour | New shared-host middleware |

The two migration probes are worth exactly what the report says they are worth and no more: they prove
the EF model matches the migration. They do not prove the SQL applies, that FK targets exist when the
constraint is created, or that inserting `"crm"` before `"projects"` leaves the existing chains intact.

Being blunt about the standing: this phase's evidence is currently *"it compiles and the non-DB tests
pass."* For a module whose entire purpose is cross-module integration — three gate handlers, a third
event subscriber, a schema-order insertion, and a cross-schema FK closing a two-phase-old deferral —
that is close to no evidence at all. The work may well be correct. "May well be" has not been this
project's standard for four phases and shouldn't become it now.

Reporting this prominently rather than burying it under a green build number is the right instinct and
the reason it is catchable at all.

## 3. What to do, in order

1. **Get Docker running and execute the full suite.** Nothing else — not Sites, not the Meeting
   follow-up — until 532/532 is a real number. Expect failures; a 32% unrun fraction across a schema
   insertion, a new FK, and three fixture rewrites would be unusual to land clean first try.
2. **Fix whatever that surfaces** as a numbered follow-up task under the standing execution contract,
   the way E1-01 was handled.
3. **Then** the Meeting follow-up as its own scoped task (`E2-01`-style): 12 commands/queries, the
   `MeetingScheduled` event already exists in Contracts, `CrmSchedulerService` already ported. Do not
   fold it into the Sites plan — a deferred sub-area absorbed into the next module's scope is how it
   gets lost twice.
4. **Then** Sites planning, with the four lessons from the final report.

## 4. Standing

Six modules, 532 tests written, the Phase 2 `ClientId` deferral closed, the first anonymous rate-limited
endpoint. Real progress — held back from mergeable by an environment gap, not a design one.

Two disclosures that a less careful report would have omitted: a scope gap traceable to the plan, and a
third of the suite never executed. Both were surfaced without prompting. That is the process working
exactly as intended, and it is why the Docker gate is trustworthy as a gate rather than a formality.
