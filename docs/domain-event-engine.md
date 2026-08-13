# Domain Event Engine

## What it is

A way for code inside **one module** to react to something that happened, **without the writer knowing who reacts**.

`DocumentApproved` → the approval handler doesn't call the notification code. It just raises an event. Whoever cares subscribes.

**Rule:** domain events are *in-transaction* and *intra-module*. Cross-module → integration events on the outbox instead.

## The 4 pieces

| Piece | File | Job |
|---|---|---|
| `IDomainEvent` | `SharedKernel/IDomainEvent.cs` | Empty marker. "I am an event." |
| `Entity._domainEvents` | `SharedKernel/Entity.cs` | Every entity carries a list of events it raised |
| `IDomainEventQueue` | `SharedKernel/IDomainEventQueue.cs` | Scoped bucket for events with **no entity instance** |
| `DomainEventDispatcher<TContext>` | `BuildingBlocks/DomainEvents/` | Collects + runs handlers |
| `IDomainEventHandler<T>` | `SharedKernel/IDomainEventHandler.cs` | The subscriber |

## The flow

```
Command handler runs
  └─ document.Approve()          →  AddDomainEvent(new DocumentApproved(id))
  └─ SaveChangesAsync()          →  row written, event still in memory
       ↓
DomainEventDispatchDecorator     →  only if Result.IsSuccess
       ↓
DomainEventDispatcher.DispatchAsync()
  1. scan ChangeTracker for entities with events
  2. drain IDomainEventQueue
  3. clear the entities' event lists
  4. resolve IDomainEventHandler<DocumentApproved> from DI, invoke all
  5. if handlers changed anything → SaveChangesAsync()
  6. loop (handlers can raise new events) — max 10 iterations
```

## Why two sources of events?

**ChangeTracker** — normal case. You loaded the aggregate, called a method, it added an event to its own list.

```csharp
public void Approve(Guid approverId)
{
    Status = DocumentStatus.Approved;
    AddDomainEvent(new DocumentApproved(Id, approverId));
}
```

**Queue** — set-based writes. `ExecuteUpdate` / `ExecuteDelete` never load an entity, so the ChangeTracker is empty and there is nothing to hang the event on.

```csharp
await db.Documents.Where(d => d.FolderId == id)
        .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsArchived, true), ct);

eventQueue.Enqueue(new FolderArchived(id));   // ← otherwise this event is lost
```

## Why the dispatcher is generic over `TContext`

One dispatcher per module, bound to that module's own DbContext.

If it were a single shared non-generic dispatcher, Identity's command would drain Documents' ChangeTracker and call `SaveChanges` on Documents' context — constructing a DbContext for a request that never touched that module. That silently breaks the "one DbContext per module" rule.

Same reason `ModuleCqrsRegistration` decorates each **closed** service type (`ICommandHandler<LoginCommand, AuthResponse>`) instead of using Scrutor's open-generic `Decorate`. Open-generic decoration rewraps *everything already registered*, so module #2's registration would re-wrap module #1's handlers with the wrong dispatcher.

## Why the decorator, not the DbContext

Dispatch is hooked at the **command pipeline** boundary:

```
DomainEventDispatch  ← outermost
  └─ Logging
      └─ Validation
          └─ YourHandler   ← innermost
```

- Validation fails → handler never runs → no events.
- Handler returns `Result.Failure` → **no dispatch**. Failed commands don't fire events.
- Works whether your handler used `SaveChanges` or an explicit transaction.
- Idempotent: if nothing was queued, `DispatchAsync` returns immediately.

## Two details worth knowing

**Reflection, not `dynamic`.** Handlers are usually `internal` in a module's Application assembly. The `dynamic` binder runs from BuildingBlocks and can't see internal members. Calling `handleMethod.Invoke` on the public interface method dodges accessibility entirely. `TargetInvocationException` is unwrapped so the real exception propagates and the transaction rolls back.

**The 10-iteration cap.** Handler A raises event B, whose handler raises event C… The loop keeps draining until empty. If it hits 10, that's a cascade cycle — logs a warning and stops rather than hanging.

## Domain vs Integration — the one-line version

| | Domain event | Integration event |
|---|---|---|
| When | in the transaction | after commit |
| Scope | same module | cross-module |
| Transport | in-memory, DI | outbox table → `EventBus` |
| Payload | can hold entities | IDs and primitives only |
| Failure | rolls back the command | retried by `OutboxProcessor` |

## Adding one

```csharp
// 1. Event (module's Domain layer)
public sealed record DocumentApproved(Guid DocumentId, Guid ApproverId) : IDomainEvent;

// 2. Raise it (inside the aggregate)
AddDomainEvent(new DocumentApproved(Id, approverId));

// 3. Handle it (module's Application layer — internal is fine)
internal sealed class NotifyOnDocumentApproved(DocumentsDbContext db)
    : IDomainEventHandler<DocumentApproved>
{
    public Task Handle(DocumentApproved e, CancellationToken ct) { ... }
}
```

No registration needed — `AddModuleCqrs<TContext>` scans the assembly and picks it up.
