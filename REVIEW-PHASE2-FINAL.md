# Review — Phase 2 execution plan, final pass

**Verdict: approved. Run G4-01 → G6-02 unattended, no checkpoints, as specified.**

F1 correctly supersedes the broken decision #10 draft — transaction-wraps the handler/cascade/inbox-row
chain instead of chasing a "does not save" premise that the actual `DomainEventDispatcher<TContext>`
source disproves. The write-up is honest about the false start rather than editing history, which is
the right way to record it.

Two things checked in this pass that weren't checked before, both clear:

**Two-transaction structure is fine, not a gap.** F1 wraps the module's `TContext` (handler + cascade +
inbox row) in one transaction; `OutboxProcessor` separately marks `processed_utc` via `SharedDbContext`
in its own save afterward. A crash between the two leaves the inbox row committed but the outbox flag
still null — on retry, `InboxDedupeDecorator`'s dedupe check at the top finds the `(event_id,
handler_name)` pair already present and returns immediately, no re-execution. The inbox table, not the
outbox flag, is the actual correctness backstop; the flag is only an optimization to skip
already-succeeded work. This is the existing Phase 1 design, F1 doesn't need to (and shouldn't) extend
its transaction across both contexts.

**No `EnableRetryOnFailure` anywhere** — checked `HrModule.cs`, `SharedDbContextFactory.cs`,
`Program.cs`. `BeginTransactionAsync`/`CommitAsync` will work as written; EF only requires an execution
strategy wrapper around user-initiated transactions when retry-on-failure is configured. **Flag for
later, not now:** if resiliency policies are ever added to any `UseNpgsql(...)` call, F1's transaction
code must move inside `dbContext.Database.CreateExecutionStrategy().ExecuteAsync(...)` or it will throw
at the first retry attempt. Cheap to note now, easy to miss later — add one line to
`InboxDedupeDecorator`'s doc comment: "if retry-on-failure is ever enabled on TContext, this method must
be wrapped in an execution strategy." That's the whole ask; not a blocking change.

Everything else stands from the prior two reviews: R-B (`decide_any`), the redesign-vs-port labeling on
`ApproverSpec`, the enum/permission corrections (#5/#6/#8/#9), the skeleton-only instantiation (#7), and
the full plumbing/reference-pattern sections. Nothing further to add.

Proceed.
