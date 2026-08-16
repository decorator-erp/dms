# Step 2 completion review — the Docker gate is cleared

**Verdict: mergeable, after the housekeeping in §4.** 531/531 on two consecutive runs, all fifteen Step 1
failures resolved, and three previously-hidden production defects found along the way.

---

## 1. A1 and A2 were both satisfied properly

**A1 — the mechanism is named, and better than required.** E2-01 didn't just re-measure green; it
reproduced red in *true single-process isolation* (fresh container, only test in the run), which ruled
out cross-test pollution before any fix was attempted. Then verbose logging caught the actual cause: two
uncoordinated entry points into the same pending set — the hosted `BackgroundService` poll loop that
`ApiFixture` never disables, and the test's own explicit `ProcessPendingAsync()` — racing for the same
outbox row, with the loser swallowing its `PK_inbox_messages` rollback and returning normally before the
winner had committed.

The decisive evidence is **83 `PK_inbox_messages` violations across a captured run that still ended
green**. That is the difference between "we fixed it" and "we understand it."

**A2 — disposition recorded, correctly not acted on.** `ResetAsync` is named as a structural risk with
both of its costs documented (a diagnosis cycle at E2-01, the direct cause of E2-03's rate-limiter race),
plus the reason not to drive-by fix it. Deferred to Sites as a decision rather than left ambient.

**`EventBus` correctly untouched.** The evidence gate held: `DocumentApprovedIntegrationEvent` has
exactly one handler, so there was no later subscriber for an abort to skip. The risk is recorded with a
specific prediction of when it will bite — an event with a failure-prone third-or-later handler in one
`PublishAsync`, plausibly Sites. That is a far more useful record than "known issue."

## 2. The reframe worth stating explicitly

The E2-01 race was **flaky, not newly broken** — which means it predates Phase 4 entirely. It has existed
since the outbox was built at G0-05, and the 83 violations in a passing run prove it was firing
routinely and being masked by timing.

The implication is uncomfortable and worth writing down: **every outbox-related test result from Phases
1–3 was weaker evidence than it appeared.** Those suites were green partly by luck. Nothing was wrong
with the reasoning at the time — the tests simply could not reach the defect without a real database and
a real background service running concurrently. But it means "Phase 4 broke Phase 3" was never the right
frame; Docker revealed a latent defect that had been quietly present for four phases.

The same applies to the other two: the `.Include` against an EF-ignored navigation and the
`AsNoTracking` stale read in `CrmGateReader` were both unreachable without a live database. Three real
defects, none suspected, each found only because fixing the one in front of it let execution reach the
next. That cascade is the payoff for measuring all fifteen before fixing any.

## 3. One forward-looking caveat on the E2-01 fix

`SemaphoreSlim` single-flights `ProcessPendingAsync` **within one process**. That is correct and
sufficient today — `docs/low-resource-hosting.md` targets a single small instance, and the two racing
entry points were both in-process.

If the app ever runs two or more instances, each gets its own semaphore and the race returns across
processes. Correctness still holds — the inbox dedupe plus the `PK_inbox_messages` constraint prevent
double side-effects — but the stale-read timing window comes back. The standard fix at that point is
`SELECT … FOR UPDATE SKIP LOCKED` on the outbox fetch, which also removes the need for the semaphore.

**Record this as a scale-time note** alongside the wake-on-write decision in
`docs/low-resource-hosting.md`, not as work. It only matters the day someone adds a second instance, and
that day should not begin with rediscovering this.

## 4. Housekeeping before merge

`git status` shows `PLAN-PHASE4.md`, six `REVIEW-*.md`, and `.claude/` untracked. Commit them per the
H1-04 precedent (review documents tracked alongside the record they drive), or add `.claude/`
to `.gitignore` if it is machine-local. "Clean tree" was part of the merge standard — finish it.

## 5. What's next, in order

1. **Housekeeping commit, then merge.** The gate that held this branch back is cleared on its own terms.
2. **Meetings follow-up as its own scoped task.** 12 commands/queries; `MeetingScheduled` already in
   Contracts; `CrmSchedulerService` already ported. Not folded into Sites — the close-out is right that
   a deferred sub-area absorbed into the next module's scope is how it gets lost twice.
3. **Sites planning**, carrying four things forward: the `ResetAsync` restructuring decision, the
   `EventBus` per-handler isolation risk, the multi-instance outbox note above, and the plan-hygiene
   lesson from the Meetings gap — *reconcile scope prose against the task table before finalizing.*

## 6. Standing

Six modules, 531 tests all green against a real Postgres, 14 architecture rules, three latent defects
removed. The Docker gap turned out to be worth every day it cost: it converted four phases of
"probably correct" into "measured," and the measuring found things no amount of review would have.
