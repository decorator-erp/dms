# Review context — handoff for a new session

Not an execution record. `PROGRESS.md` is Claude Code's account of what was built; this is the
**planning-and-review thread's** account of where the project stands, what is in flight, and what has
been learned about how the two sides get things wrong. Read this first, then `PROGRESS.md`'s tail.

---

## The working arrangement

Two roles, deliberately separated:

- **This thread plans and reviews.** Writes phase plans, reviews Claude Code's counter-plans, verifies
  claims against source, reviews execution output, drafts the prompts handed back.
- **Claude Code executes.** Runs unattended, start to finish, per the standing contract below.

Neither side is trusted by default. Claude Code has corrected this thread's plans on substantive
points more than once, and those corrections have been right. That is the arrangement working, not
failing.

**Standing execution contract**, restated in every prompt:

> restate → implement → task verify → full gate → ≤3 attempts → one commit → one `PROGRESS.md` line
> → next task immediately

Acceptable stops: a hard stop condition, or genuine context exhaustion **at a task boundary after the
commit** — never mid-task, since a partial task cannot be gated or committed.

```bash
DOCKER_HOST=tcp://127.0.0.1:2375 dotnet build DMS.Modular.slnx -warnaserror
DOCKER_HOST=tcp://127.0.0.1:2375 dotnet test  DMS.Modular.slnx
DOCKER_HOST=tcp://127.0.0.1:2375 dotnet format DMS.Modular.slnx && git diff --exit-code
```

---

## Where the project stands

**Finance shipped as the ninth and last business module** at `e980c73` — 1106/1106 across 29 suites,
build 0/0, format clean. Phase 7 closed with both reconciliations (`PROGRESS.md:8401`, `:8431`).

Nine modules: Identity, Hr, Approvals, Projects, Documents, Crm, Sites, Notifications(+Audit), Finance.
**Reporting is the tenth and last** (`PLAN.md:696`) and is not started.

Three things open since Phase 2 closed with Finance: `ConditionType.Budget` has a handler (asserted
against the registry, not a list), `InvoiceDueJob` is the eighth job on the one timer, and the
`Project*Financial` families are live.

### Verified facts worth not re-deriving

| Fact | Where |
|---|---|
| Reporting **may join across schemas** — explicit carve-out | `docs/cross-schema-foreign-keys.md:435-436` |
| `NotificationType` 150–179 reserved for Finance, "held rather than reclaimed" | `Notifications.Contracts/Enums.cs:5,9-10` |
| Exactly 22 money columns, all `numeric(19,4)` | `Finance.Infrastructure/Persistence/Configurations/` (8 files) |
| Project-count floor now `>= 36` (projects, **not** modules — 9 × 4) | `tests/Architecture.Tests/MetaSelfEnforcementTests.cs:40` |
| `EnforceCodeStyleInBuild` is `true` | `Directory.Build.props:8` |
| `UserDeactivatedIntegrationEvent` — declared, **never published, never consumed** | `Identity.Contracts/IntegrationEvents.cs:8`, sole occurrence in `src` |
| No count floor on integration events, so deleting some is floor-safe | `tests/Architecture.Tests/` |
| Consolidated deferred-items table | `PROGRESS.md:2176` |

---

## In flight — X-01 → X-03

A three-task housekeeping phase between Finance and Reporting. Claude Code's plan was reviewed and
approved with one change; the revise has been handed over.

- **X-01 — CI.** No `.github/` existed after eight phases. Adds `gate.yml` + `global.json` (SDK pinned
  `10.0.400`, `rollForward: latestPatch`). **No `services: postgres` block** — every fixture builds its
  own Testcontainer, so the runner needs a Docker *daemon*, not a database service.
  **The change I asked for:** pull the `.editorconfig` decision out of the handoff and into the task —
  add `[*.cs] end_of_line = lf`, run `build -warnaserror`, keep it if clean, and if it reddens, that is
  the observed failure, obtained locally instead of via a failed push in the user's hands.
  Also: sequence the handoff as **three pushes** (clean → violation → revert), because push 1 is the
  only thing that proves the harness and a one-push procedure conflates harness failure with the
  injected violation.
- **X-02 — the unconsumed events.** Brief said two; the real count is **eight published-but-unconsumed
  plus one never published**. Finance's `InvoiceIssued` and `PaymentPosted` are **deleted** — the
  carve-out at `:435-436` means Reporting reads live tables, so neither is a read-model feed, and
  `InvoiceIssued` was an incomplete feed anyway (fires only in `CreateInvoiceCommandHandler`).
  Then a `DeclaredUnconsumed` guard with **both `Except` directions**, so an entry that *gains* a
  handler fails too. That direction is what would have caught `SiteApproved`, whose
  "No subscriber exists" comment went stale in Phase 6 and stayed wrong.
- **X-03 — two rules into `docs/adding-a-module.md`:** an injection that fails to go red is a finding
  about the test; cite assertions, not line numbers.

**No remote exists** (`git remote -v` empty, only `master`, no `gh`), so the push half of X-01 is the
user's to run.

---

## What comes after

1. **Phase 8 — Reporting.** Last module. Referenced by nobody, reads every schema, backend footprint is
   three SQL *views* (`project_budget_view`, `v_project`, `V_EmployeeProfile` — the last half-ported,
   with `PLAN.md:529` reserving the rest). Smallest by code, most likely to be mis-planned by counting
   folders. Written by this thread, not Claude Code.
2. **A deferred-items reckoning.** After Reporting there is no future phase to defer into. Every open
   row in `PROGRESS.md:2176` gets closed, done, or formally accepted as won't-do.
3. **Cutover — the real remaining risk.** Nine phases built a new system; nothing has addressed how the
   running backend *becomes* it. Data migration from one schema into nine, deployment against
   `docs/low-resource-hosting.md`, route parity, the switch. Unlike every phase so far, it mostly
   cannot be verified by tests. Worth assessing **before** Reporting, since view parity may be a
   requirement rather than a nicety, and that lands inside Phase 8's scope.

---

## Standing rules, each paid for

1. **A negative assertion against a dead instrument is indistinguishable from a clean system.** Five
   instances: E1-01's stage-gate test, P5F-01's rule-8a injection, three too-narrow greps, N1-08's
   `CapturedSql` (never captured anything for three phases), and the F1-08 vacuous assertions.
2. **A grep returning a clean, plausible result is not evidence** until the pattern is checked against
   a known-positive case. Four false negatives from over-narrow patterns.
3. **A fault injection that fails to go red is a finding about the test, not a pass for the code.** Two
   F1-08 injections passed; both revealed assertions that never reached the clause they claimed to pin.
4. **Cite assertions, not line numbers.** PC-03 edited eight `Architecture.Tests` files; the Phase 7
   plan cited three floors by line and there were seven.
5. **A plan's description of the working tree is a claim to verify, not a fact to build on.** (Phase 3
   CRLF misdiagnosis: reported ±6,000 lines of churn; actual was 32 files / 157 insertions.)
6. **Migration-order FK cycles are resolved by dropping the weaker constraint.** Four instances, now a
   named pattern; the absent FK gets documented as deliberately as a present one.
7. **A contract with no consumer is not shipped early — it is not shipped.**
   (`docs/adding-a-module.md:913`.)

---

## This thread's own error log

Kept because calibration matters more than the appearance of reliability.

| Claim | Reality |
|---|---|
| "59 files, ±6,000 lines of CRLF churn" | 32 files, 157 real insertions — diagnosed from a container against `core.autocrlf=true` |
| "`CrmSchedulerService` is already ported" | It never existed; inherited from `PROGRESS.md` across three review documents |
| "`CrmDesignBrief.SiteId`" | Fabricated; the real link is `Site.SourceBriefId` |
| "`IAuditable` is unused" | 56 implementors; only `AuditIgnoreAttribute` was unused |
| "Append `finance` to `SchemaOrder`" | The doc had already committed to inserting it before `notify` |
| "Project owner via `IProjectDirectory`" | `ProjectSummary` had no `OwnerId` — became F1-00b |
| "Money precision is invisible to the probe" | `HasColumnType` is model metadata, so the probe **does** catch it; the real gap is a coordinated edit |
| "Reporting is the first genuine case for read contracts" | `:435-436` had already licensed it to join across schemas, a phase earlier |
| Three arch-test floors at F1-04 | Seven, cited by stale line numbers |

The pattern: **inherited claims and unverified premises**, not reasoning errors. Verify against source
before asserting, especially when the claim came from a document this thread wrote earlier.

---

## Hard stops (carried into every prompt)

A verify failing 3 attempts · any fix needing an architecture rule relaxed, a test deleted, an internal
type made public, or a forbidden reference · weakening a liveness assertion · any write under
`..\backend` · pushing to a remote without the user naming it · editing another module's Contracts
beyond what the plan names.

**One carve-out, learned at F1-08:** filling a slot that another module *explicitly reserved in writing*
is not a contract extension — it is honoring a prior decision. Reserved ≠ unplanned. Say so in the
plan's wording rather than making the executor guess.
