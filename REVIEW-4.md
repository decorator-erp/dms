# Review 4 — final

**Approved. Run it. No changes to `PLAN.md` this round.**

T1–T4 all landed correctly. Specifically checked:

- **T1** — the outbox routing is right, the handler lives in `Hr.Infrastructure`, and the
  sync-attempt-as-best-effort is correctly marked non-fatal. The double-execution case (sync attempt
  succeeds *and* the outbox handler later runs) is covered by `DeactivateAsync` idempotency — noted so
  it does not look like an oversight when both fire in a test.
- **T4** — the vacuity table is in the G0-07 deliverable and the end-state checklist. The observation
  that `EmployeeDeactivatedIntegrationEvent` graduating to a *used* event leaves
  `UserRegisteredIntegrationEvent` as the only exerciser of R2's zero-handler path is exactly the kind
  of tracking this process is supposed to produce.
- **T2/T3** — full `dotnet ef` invocations present; one migration-order answer everywhere.

Two staleness notes, neither blocking, neither worth a revision cycle:

1. The layout table lists `REVIEW.md · REVIEW-2.md` as read-only inputs — `REVIEW-3.md` and this file
   belong on that line too.
2. The context paragraph still says PLAN.md "carries R1–R6"; it now carries R1–R7, S1–S4 and T1–T4.
   Harmless, since the instruction that governs is "re-read PLAN.md fresh before G0-01".

The distinction between the **seven non-negotiables** (process rules, re-checked at phase boundaries)
and the **nine architecture rules** (enforced in code) is correct as written — they are different lists
and the plan treats them as such.

Nothing else. Four review rounds is the right amount for an unattended 25-task run; a fifth would be
polishing the plan instead of testing the foundation. The remaining risk lives in execution, not in
planning — and the G0-09 checkpoint exists to catch exactly that.

Proceed: `docker info`, then G0-01.
