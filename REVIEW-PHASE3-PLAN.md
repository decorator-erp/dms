# Review — Phase 3 execution plan (pre-run)

**Verdict: approved, run it. No corrections.** This is the first plan in five rounds where I found
nothing to fix — and it opens by correcting me, correctly.

---

## 1. The H1 correction is right, and my error was methodological

Every claim verified independently against the host filesystem:

| Claim | Verified |
|---|---|
| Seeding loop commented out | ✓ `Program.cs:55-60` — `//using (var seedScope = ...)` with the `SeedAsync` loop inside, and the doc comment above it left live and now lying |
| `HttpCurrentUser` never moved | ✓ still at `src/Api/HttpCurrentUser.cs`; only `DI.cs` and `PermissionClaim.cs` reference it |
| `OutboxDbMigrationExtension.cs` is dead | ✓ exactly one occurrence in `src/` — its own declaration. No caller. |
| Unreviewed package sweep | ✓ Respawn 7.0.0, Test.Sdk 18.8.1, coverlet 10.0.1, `EntityFrameworkCore.Tools` added — and the stale comment at line 29 still says *"transitive dependencies of Respawn **6.2.1**'s SQL Server support"* |

**Why I got it wrong, precisely:** I diagnosed the tree from a Linux container mount. The repo has
`core.autocrlf=true`, so the Windows working tree holds CRLF while the index holds LF. Git *inside the
container* — without that setting — compared CRLF working-tree files against an LF index and reported
every line of 59 files as changed. My "±6,000 lines of line-ending churn" was real from where I was
standing and false about the repository. The `.git/index.lock` permission warning in that same session
was the tell I should have followed.

The plan's own stated lesson is the right one, and it generalizes past this instance: **a plan's
description of the working tree is a claim to verify, not a fact to build on.** That it lands the same
point `PROGRESS.md` already records twice ("read the actual source method, not just its name") makes it
worth keeping in the phase record, which G8-01 does.

Worth noting what this *doesn't* invalidate: H1's reasoning — these changes must not land as one commit
— was correct, and the plan keeps it while replacing the sequence. Right call. The package sweep,
refactor, and two regressions are four separable things that would have been unreviewable as one blob.

## 2. Judgement calls I'd have made the same way

- **`.gitattributes` kept despite its premise collapsing** (H1-02), on the grounds that `core.autocrlf`
  is per-machine and the next contributor won't have it. Correct, and the plan says to *expect the
  renormalize to produce nearly nothing* — pre-empting exactly the "did this fail?" moment that would
  otherwise burn an attempt.
- **`HttpCurrentUser` stays in `Api`** — one consumer, HTTP-host concern, moving it into
  `BuildingBlocks.Web` risks arch rule 8 for no gain. My plan asserted a move that had never happened
  and shouldn't.
- **Package sweep gated on the gate, with an explicit revert path** if red. Three major-version bumps
  including a Testcontainers breaking change someone already patched around — treating that as
  "verify, then commit alone, or revert and defer" is the only honest handling.
- **Delete `OutboxDbMigrationExtension.cs`** rather than wire it up. Migrate-on-boot is an ops decision
  nobody made; dead code that *looks* load-bearing is worse than no code.

## 3. Details that will save attempts mid-run

The "Already in place — no work needed" section is the highest-value addition: `ConditionType.Attachment`,
`ConditionType.DocumentApproved`, `ApprovalSubjectType.ProjectDocument`, the three `documents → *` rule-9
allow-list pairs, and the three `adding-a-module.md` sections all confirmed present with file:line. Each
is a place the run would otherwise have paused to re-derive or wrongly created a duplicate.

Two specifics I'd have missed:

- **The nudge sites are named exactly** — `InboxDedupeDecorator` after `CommitAsync`, both nested
  `DomainEventDispatchDecorator` classes inside the `IsSuccess` branch only, and explicitly **not**
  `DbContextOutboxExtensions.Enqueue` (stages, doesn't commit). Getting that wrong produces a nudge
  that races an uncommitted row — a heisenbug.
- **The gate handlers are `internal` in `Documents.Application`**, so `ProjectsModule`-style Scrutor
  reflection is required rather than a direct type reference. That's the kind of detail that costs an
  attempt when discovered at compile time.

## 4. One thing to hold to at G7-07

The `UnsupportedConditionType` → success transition is the phase's actual proof, and the plan is right
to name the specific error constant. Assert it **through** `OutboxProcessor.ProcessPendingAsync`, never
a direct handler call — a green test that bypasses delivery proves the handler compiles, not that the
system works. Phase 2's G5-08 established that discipline; keep it.

## 5. Run it

No changes requested. Execute H1-01 → G8-01 unattended per the execution contract.

If the package sweep is red at H1-01, revert and defer as the plan specifies — do not spend the
3-attempt budget chasing a major-version upgrade that isn't this phase's work.
