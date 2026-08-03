# Review 3 — final

**Approved. Start at G0-01 after T1.** `PLAN.md` updated for T1–T4; re-read it once more, then run.

S1–S4 landed correctly. Your observation about the vacuity count is right and I have fixed the plan
rather than leaving you to work around it — see T4.

---

## T1 — R4's retry does not exist *(the one real finding)*

> *"Order them employee-first-then-user, so a failure leaves an active login on an inactive employee
> rather than a locked-out active employee, **and let the retry close the gap**."*

That sentence is mine, and it is wrong. There is no retry. `IUserAdministration.DeactivateAsync` is a
synchronous contract call, not an outbox event. If it fails, the employee is inactive, the login still
works, and **nothing will ever try again**. The command has already committed, so returning a failure
tells the caller something untrue.

For a security-relevant action that is the wrong failure mode, and it is precisely the shape the outbox
exists to fix. Route it through the outbox instead:

1. `DeactivateEmployeeCommandHandler` writes `Employee.IsActive = false` **and** the
   `EmployeeDeactivatedIntegrationEvent` outbox row in **one transaction**.
2. An `IIntegrationEventHandler<EmployeeDeactivatedIntegrationEvent>` **in `Hr.Infrastructure`** calls
   `IUserAdministration.DeactivateAsync`. The handler lives in HR, so Identity still references nothing —
   R4's constraint is preserved.
3. `OutboxProcessor` retries until it succeeds. `DeactivateAsync` is idempotent, so retries are safe.

Optionally call it synchronously as well for immediacy, treating failure as non-fatal — the outbox is
the mechanism of record either way. Never fail the command after the employee write has committed.

The gap is now one poll interval instead of permanent. This also makes
`EmployeeDeactivatedIntegrationEvent` a *used* event rather than a second zero-handler case.

**Add to G2-04's verify:** the Identity call fails on first attempt; the outbox retries; the login is
disabled without operator intervention.

## T2 — `dotnet ef` needs `--context` and the project flags

`dotnet ef migrations add Probe && dotnet ef migrations remove` fails with *"More than one DbContext was
found"* from G2-03 onward, and arguably from G1-03 given `SharedDbContext` already exists. It will burn
an attempt against the 3-try budget on a task that is otherwise fine.

Full invocations are now written out in `PLAN.md` at G1-03. Mirror them at G2-03.

## T3 — Your own doc contradicts itself on the migration order

The **R6** row still says *"list lives in `Api` with a comment tying it to the module graph"*; the **S3**
row says `BuildingBlocks.Web`. S3 supersedes it — the R6 row predates review 2 and was not updated.
Only worth mentioning because a plan you are executing unattended should not contain two answers to the
same question.

## T4 — The vacuity count, confirmed and fixed

You are right, and it undercounts by more than you said. At G0-07 rules 1–6 and 8 have no module
projects to constrain, and rule 9 has no cross-schema FK until G2-03. **Only rule 7** (`SharedKernel` has
zero project references) is live.

`PLAN.md` now says so, and asks for a table recording which task makes each rule non-vacuous. Reporting
"nine green" at G0-07 without that table would be technically true and substantively empty — the exact
failure mode arch tests are supposed to prevent.

---

## Everything else

D1–D8 stand. The `IdentityModuleDbContext` / `HrDbContext` asymmetry is justified and noted — leave it.
Respawn config, `ManagePackageVersionsCentrally`, the `dotnet format` ordering: all correct.

The layout table distinguishing your files from mine is a good addition. `PLAN.md` is the exception —
I have edited it four times now, including during this review, so **read it fresh before G0-01** rather
than working from any earlier reading.

---

## At the G0-09 checkpoint

The four questions from review 2 stand. Question 2 is the one that matters: **did the R1
kill-before-return test genuinely fail before the fix?** If it passes both ways, the inbox atomicity work
is unverified and everything downstream inherits that. Say so plainly if that is what you find — an
honest "this test proves nothing" is worth more than a green tick.

Add a fifth: **which G0 decisions would be expensive to reverse at G2, and which would be cheap?**
That is the question the checkpoint exists to answer, and it is easier to answer having just built the
foundation than it will be from anywhere else.
