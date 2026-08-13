# Phase 2 completion review — verified against code at `961c815`

**Verdict: accepted. No corrective prompt needed — every claim checked out in source.**

## Verified

| Claim | Check | Result |
|---|---|---|
| 17 commits, one per task (+F1 folded into G4-04) | `git log` | ✓ G4-01 → G6-02, all present, messages match |
| F1 in `InboxDedupeDecorator` | read the file | ✓ exact shape from REVIEW-PHASE2-REVISED: dedupe check → `BeginTransactionAsync` → handler → `DispatchAsync` → inbox row → single commit. Doc comment records the broken first draft honestly and includes the execution-strategy warning verbatim. |
| Command-path atomicity fix | read `DomainEventDispatchDecorator` + G5-08 commit | ✓ same transaction shape applied to the command path; rollback on `Result` failure. |
| Scope-decorator authorization hole | read `ProjectScopeRegistration` + `ProjectsModule.cs:36` | ✓ now scans `typeof(IProjectScoped).Assembly` — i.e. **Projects.Contracts** — with a doc comment explaining why. |
| `SubjectType == GateCondition` filter | `ApprovalDecidedIntegrationEventHandler:23` | ✓ |
| `DecideAny` | present across Contracts/Domain/Application/Endpoints | ✓ |
| Phase boundary + deferred table | `PROGRESS.md:2088` region | ✓ |
| 185/185 vs. 159 `[Fact]`/`[Theory]` attributes counted | — | consistent — theories expand to multiple executed cases. |

## Two observations, neither requiring action

### 1. The authorization hole was my plan's fault — and the test regime caught it

The Phase 2 plan text said: *"reflect `Projects.Application`'s assembly for command types implementing
`IProjectScoped`."* In this architecture commands are declared in **Contracts** (the G1-07
circular-reference resolution); Application holds only handlers. Scanning Application finds zero
`IProjectScoped` types, decorates nothing, and every project-scoped command runs with no row-level
access control — silently, because nothing fails.

Three review rounds — mine included — read that sentence and did not catch it. The **verify block
written for the same task** did: the scope-decorator test (non-member → 404) failed against the
undecorated pipeline. That is the whole thesis of this process working as intended — prescriptive
review catches design errors, verify blocks catch the errors review misses, and neither alone would
have been enough. The fix (scan `typeof(IProjectScoped).Assembly`, which can never name the wrong
assembly) is better than the plan's wording deserved.

### 2. The command-path fix is a semantic change to Phase 1 behavior — a correct one

Wrapping every command in an explicit transaction means a handler that calls `SaveChangesAsync` and
*then* returns `Result.Failure` now rolls back, where before its write survived. No Phase 1 handler
does this (failure paths return before saving), which is why 185/185 stayed green — but it is a
behavioral guarantee change, correctly stronger, and the doc comment says so. Also noted: the
`GateCondition.MarkSatisfied` nullable-resolver fix was surfaced by the **cross-schema FK to
`hr.employees` rejecting `Guid.Empty`** — the R6 constraint catching a domain-modeling bug at test
time is precisely the argument for keeping DB-level FKs.

## State of the system

Six modules of ten (Identity, HR, Approvals, Projects + BuildingBlocks/SharedKernel foundation),
four-module cross-flow proven over real HTTP, arch rules 1–10 all non-vacuous or scheduled, and a
deferred-items table naming an owner for everything cut. Remaining from the original design: CRM
(+Clients), Sites, Documents, Finance, Notifications & Audit, Reporting — and the two standing
deferred decisions (F2 project-collapse, outbox wake-on-write from `docs/low-resource-hosting.md`).

Recommended next phase when ready: **Documents** (small, unblocks the two deferred gate types and the
`ProjectDocument` approval subject) or **CRM** (largest remaining, unblocks `ClientId` validation and
Sites). Same planning procedure — it has now caught real bugs before the run three times in a row.
