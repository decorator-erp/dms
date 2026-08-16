# Review — Step 2 plan (E2-00 → E2-05)

**Verdict: approved with two additions (A1, A2), neither blocking the start.** Begin at E2-00.

---

## What's right, and worth naming

**E2-00 first.** Taken, and for the stated reason: the instrument comes before the investigation.

**E2-01 as a diagnosis task with a decision point, not a predetermined fix.** This is the correct shape
for the only failure that might be a production defect. The probe order — isolation run, then outbox
row, then handler-abort path — is genuinely cheapest-and-most-decisive first, and clearing the obvious
suspects statically (CRM filters first; `AddModuleIntegrationEvents` decorates only its own scan;
E1-01 touched neither path) means no cycles get spent re-deriving what's already known.

The settled decision on `EventBus` is exactly right: **the aggregate-failure rewrite lands only if probe
3 shows a handler actually throwing.** I raised that rewrite as a possibility; gating it on evidence
rather than plausibility is better than how I framed it. *"Nothing in shared infrastructure changes
without a failing test pointing at it"* is the rule that has kept this codebase honest for four phases.

**The `ResetAsync` finding.** Verified — `ApiFixture.cs:37`'s own doc comment says it outright. Reading
it and drawing the consequence (accumulating DB ⇒ cross-test interference is a first-class suspect for
#1) is what turns E2-01 from "debug a regression" into "first, find out whether there is one."

**#13 re-diagnosed as a 403, not a JSON bug** — and the fix includes asserting status before
deserializing, so the next missing permission reports as itself. Fixing the diagnostic alongside the
defect is the right instinct.

**E2-04's assert against `DefaultRoles.All.Length`** rather than a new literal — the number can't drift
again. Same shape as `CrmPermissions.All.Count`.

---

## A1 — If E2-01 goes green in isolation, do not accept a green re-measure as the answer

The plan says: *green in isolation → cross-test interference → "E2-01 may resolve itself once
E2-02/E2-03 land — re-measure after those."*

The risk in that path: E2-02 and E2-03 change what data exists and in what order tests run. A green
full-suite result afterward may mean the interference was fixed, **or** that it moved — different
ordering, different accumulated state, same latent fragility. Accepting green without distinguishing
those leaves an order-dependent test that will fail again at random, and this project has spent two
phases on exactly the "green for the wrong reason" failure mode (the E1 stage-gate test; the
three-subscriber assertion that died before asserting).

**Required if that branch is taken:** name the interfering state before closing E2-01 — which test
leaves what behind, and why it prevents the gate from reaching `Satisfied`. A one-line `PROGRESS.md`
statement of the mechanism is enough. If the mechanism can't be identified, E2-01 stays open rather
than closing on a green number.

## A2 — Give `ResetAsync` an explicit disposition

Right now it's background context that explains two failures (#14 directly, #1 possibly). It should
leave this step with a decision recorded, not remain ambient.

Calling it between tests is **not** a safe drive-by fix — the collection's tests depend on data seeded
by earlier ones, so resetting would break more than it fixes. That's precisely why it needs a written
disposition rather than an implicit "someone will notice."

Minimum: an E2-05 entry naming it as a known structural risk — *the `Integration.Tests` collection shares
one accumulating database; `ResetAsync` exists but is never called; tests are order-coupled by
construction* — with the observation that it has now cost one diagnosis cycle and caused one failure
outright. Whether to restructure toward per-test isolation is a Sites-phase decision, not this step's.

---

## One note on sequencing

If E2-01's isolation probe comes back **green**, consider running E2-02 and E2-03 before returning to
it — the plan already allows this ("re-measure after those"), and it is the cheaper order, since those
ten FK fixtures are changing the very state suspected of interfering. Just don't let the re-measure
substitute for the mechanism (A1).

## Exit

532/532, twice, plus `dotnet format --verify-no-changes` — correctly flagged as not yet run this round
and part of every gate from here. Meetings and Sites stay out.
