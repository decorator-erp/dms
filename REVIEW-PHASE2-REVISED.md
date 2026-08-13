# Review — Phase 2 execution plan, revised (post R-A/R-B absorption)

**Verdict: not ready to run as written. One required fix (F1 below) before G4-04, which is where
decision #10/R-A is scheduled to land.** Everything else — the redesign-vs-port labeling, R-B's
`decide_any`, the enum/permission corrections — is right and confirmed against source.

---

## The one problem: R-A's fix is built on a false premise, verified in code

Decision #10 claims the new integration-event decorator ordering works because
`dispatcher.DispatchAsync(ct)` *"stages the cascade, does not save — same contract the command-path
decorator already relies on."*

I read `DomainEventDispatcher<TContext>.DispatchAsync()` directly:

```csharp
// Handlers may resolve gates purely set-based (no tracked changes); skip the empty save.
if (dbContext.ChangeTracker.HasChanges())
    await dbContext.SaveChangesAsync(ct);
```

This runs **inside the loop**, once per iteration, up to `MaxIterations = 10`. It does not stage-only.
It saves — potentially several times for one cascade. The premise is false, and it is not a minor
wording slip: the entire correctness argument for decision #10 rests on it.

### What actually happens with the proposed ordering

`InboxDedupe (outermost) → domain-event-dispatch wrapper → raw handler`. Execution order:

1. Raw handler stages `gate.MarkSatisfied()`.
2. Dispatch wrapper calls `dispatcher.DispatchAsync(ct)` → iteration 1 drains the domain event, invokes
   its handler (which may stage a *dependent* gate's satisfaction), and — because
   `ChangeTracker.HasChanges()` is true — **calls `SaveChangesAsync` right there.** That is commit #1,
   an implicit transaction of its own, already closed.
3. If the dependent gate's satisfaction raised its own event, iteration 2 repeats: drain, invoke, save.
   Commit #2.
4. Control returns to `InboxDedupe`, which adds the inbox row and calls `SaveChangesAsync` again.
   Commit #3.

Three separate transactions where the design calls for one. A crash after commit #1 but before commit
#3 leaves the cascade **partially applied and the inbox row absent**. `OutboxProcessor` retries, the
handler runs again, `gate.MarkSatisfied()` fires again — and whether that is safe depends entirely on
whether every downstream step is independently idempotent, which is precisely the assumption R1 was
built to remove. Decision #10 introduces the bug it was written to prevent, one layer downstream, in
code that exists specifically to guarantee that class of bug cannot happen.

This is also not hypothetical for this phase: G5-08's own new verify — *"assert a dependent gate gets
unlocked by the cascade, exercised through the real delivery path"* — is exactly the scenario that
walks straight through the multi-commit window above. The task that proves the fix works is the task
most exposed to the fix being wrong.

### Why "stage but never save" was true in Phase 1 and stops being true here

`InboxDedupeDecorator`'s doc comment and Phase 1's `EmployeeDeactivatedIntegrationEventHandler` establish
the contract correctly for their case: a handler that stages one write and raises no further domain
events, with nothing between it and the inbox-row save. One handler, one save, one commit — correct.

Decision #10 inserts a *second saving component* (the dispatcher) between the handler and the inbox
row. The single-save invariant was never designed to survive that insertion, and nothing in the plan's
text notices the seam.

## F1 (required) — wrap the chain in one explicit transaction, don't chase "one save"

Stop trying to guarantee exactly one `SaveChangesAsync` call. Guarantee one **transaction** instead —
strictly more robust, and it doesn't care how many internal saves the dispatcher performs:

```csharp
internal sealed class InboxDedupeDecorator<TContext, TEvent>(
    IIntegrationEventHandler<TEvent> innerHandler,
    DomainEventDispatcher<TContext> dispatcher,
    TContext dbContext)
    : IIntegrationEventHandler<TEvent>
    where TContext : DbContext
    where TEvent : IIntegrationEvent
{
    public async Task HandleAsync(TEvent @event, CancellationToken ct)
    {
        var handlerName = innerHandler.GetType().FullName ?? innerHandler.GetType().Name;

        if (await dbContext.Set<InboxMessage>()
                .AnyAsync(m => m.EventId == @event.EventId && m.HandlerName == handlerName, ct))
            return;

        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        await innerHandler.HandleAsync(@event, ct);
        await dispatcher.DispatchAsync(ct);          // may save 0..N times — all inside tx now

        dbContext.Set<InboxMessage>().Add(new InboxMessage(@event.EventId, handlerName, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }
}
```

This is a change to `InboxDedupeDecorator` itself, not a new nested class in
`DomainEventDispatchDecorator<TContext>` — the domain-event dispatch does not need its own decorator on
the integration path at all; `InboxDedupeDecorator` just needs `DomainEventDispatcher<TContext>` injected
and called between the handler and the inbox write, inside an explicit transaction. This is *simpler*
than decision #10's two-decorator ordering scheme, not more complex, and it does not touch the command
path or `DomainEventDispatchDecorator` at all — zero risk of regressing Phase 1.

If a crash happens anywhere inside the `await using var tx` block, nothing commits: not the handler's
write, not the cascade, not the inbox row. `processed_utc` stays null, `OutboxProcessor` retries, and
the whole chain reruns cleanly from zero — which is exactly R1's original guarantee, now correctly
covering the cascade too.

**Verify (replaces decision #10's verify, same task, G4-04):** construct a handler that raises a domain
event whose own handler raises a *second* domain event (two-level cascade, forces
`DomainEventDispatcher` through 2 loop iterations = 2 internal saves) — assert all three writes (handler,
level-1 cascade, level-2 cascade) commit together. Then the crash-simulation shape already used for R1
originally: invoke the decorated handler twice with the outbox row never marked processed between calls,
assert the side effect — including the cascade — happened exactly once. G5-08 depends on this test
existing and passing before it builds the approval-gate cascade on top of it.

## Everything else in the plan: confirmed correct

- R-B (`approvals.decide_any`) — sound, no changes needed.
- Decision #1 (`ApproverSpec` as a redesign, correctly labeled as such, not as a port) — confirmed
  against `Approval.cs`'s XOR-single-approver shape; the labeling discipline is exactly right.
- Decisions #3/#4 (`ApprovalState` computed, `Round` inert, `ChangeItems` deferred to CRM with an
  explicit table row) — sound scoping, nothing missing this time.
- Decisions #5/#6/#7 (`StageConditionType` and `ProjectSector` dropped, skeleton-only instantiation) —
  all three previously verified against source and correctly carried forward unchanged.
- Decisions #8/#9 (permission strings) — verbatim match confirmed again in this pass.
- The plumbing section, reference patterns, and task-by-task notes are unchanged from the prior review
  and still correct.

## Instruction

Apply F1 in place of decision #10's original text, at G4-04, before G5-08. Everything else in this
plan runs as written. Re-verify F1's own test non-vacuously (construct the two-level cascade, confirm
it fails without the transaction wrap by temporarily removing `BeginTransactionAsync`/`CommitAsync` and
observing partial commit on a simulated mid-chain failure, then restore) before trusting it — same
fault-injection discipline as every other rule in this codebase.
