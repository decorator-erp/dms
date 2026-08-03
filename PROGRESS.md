# Progress log

One line per task: ID, status, files touched, anything decided that PLAN.md did not specify.

---

**G0-01** — done — `.gitignore`, `DMS.Modular.slnx`, `Directory.Build.props`, `Directory.Packages.props`,
`docker-compose.yml`. Decided: Postgres host port 5433 (avoids a local 5432); central package versions
resolve the backend's Microsoft-package 10.0.9/10.0.10 drift to 10.0.10;
`ManagePackageVersionsCentrally=true` added to `Directory.Packages.props` (plan text omitted it).
`dotnet build` on the project-less solution emits a harmless "nothing to restore" warning, not an
error — treated as pass.

**G0-02** — done — `src/SharedKernel/` (9 files: `Entity`, `Result`, `Error`, `IDomainEvent`,
`IDomainEventHandler`, `IDomainEventDispatcher`, `IDomainEventQueue`, `IAuditable`,
`AuditIgnoreAttribute`), ported byte-for-byte from `../backend/SharedKernel/`. csproj has no
`PropertyGroup` beyond the SDK default — everything (target framework, nullable, implicit usings,
warnings-as-errors) comes from `Directory.Build.props`. Zero package references, zero project
references, confirmed by `dotnet build -warnaserror`. **Known gap recorded, not fixed:** `IAuditable`
and `AuditIgnoreAttribute` come across, but the EF audit interceptor that acts on them belongs to the
out-of-scope Notifications & Audit module. `Employee` (G2-02) will implement `IAuditable` and no audit
rows will ever be written for it in this scope. Owner of the gap: the Notifications & Audit module,
when built.

**G0-03** — done — `src/BuildingBlocks/` (`Messaging/{ICommand,IQuery,ICommandHandler,IQueryHandler}`,
`Behaviors/{ValidationDecorator,LoggingDecorator,DomainEventDispatchDecorator,LogMessages}`,
`DomainEvents/{DomainEventQueue,DomainEventDispatcher<TContext>}`, `Pagination/PagedResult`,
`Validation/ValidationExtensions`, `ModuleCqrsRegistration.AddModuleCqrs<TContext>`) plus
`tests/BuildingBlocks.UnitTests/` (new test project, not in PLAN.md's file tree — needed to make the
task's verify block runnable; xunit + EF Core InMemory, no Testcontainers/Docker dependency).

**Real design problem solved, not just ported:** copying the backend's single global
`services.Decorate(typeof(ICommandHandler<,>), ...)` calls verbatim would be wrong in a multi-module
composition root — Scrutor's open-generic `Decorate` rewraps *every* currently-registered closed
generic matching that pattern, not just the ones from the assembly being scanned. A second module
calling `AddModuleCqrs` would silently re-wrap the first module's already-decorated handlers with the
*second* module's `DomainEventDispatcher<TContext>`, forcing construction of the wrong module's
DbContext on requests that never touch it. `AddModuleCqrs<TContext>` instead reflects the target
assembly for each handler's own closed `(TCommand[, TResponse])` pair and decorates that exact closed
service type — since two modules never declare the same command type, this can't collide. Documented
in the XML doc on `ModuleCqrsRegistration` itself.

`LogMessages` ported as the CQRS-pipeline subset only (`Command_Handling/Succeeded/Failed`,
`Query_Handling/Succeeded/Failed`) — the ~250 lines of module-specific templates (`Auth_*`, `User_*`,
`Dept_*`, `Finance_*`, etc.) belong to the modules that raise them, not to shared infrastructure.
`PagedResult<T>` rewritten per plan decision D6: immutable record, `PageSize` exposed, `Empty(...)`
instead of `Default => default!`. Verified: failing validator returns `Result.Failure` without invoking
the inner handler; a command raising a domain event dispatches it after success and not after failure
— both proven through the real `AddModuleCqrs` pipeline (DI-resolved `ICommandHandler<T>`), not by
constructing decorators directly, and the failure-path assertion is not vacuous (removing the
`result.IsSuccess` guard would flip it).

**G0-04** — done — `src/BuildingBlocks/ICurrentUser.cs`. Interface only, as specified; HTTP
implementation deferred to G0-08. Verify done by inspection (`grep -i AspNetCore
BuildingBlocks.csproj` → no match) since `Architecture.Tests` doesn't exist until G0-07 — this becomes
a permanently code-enforced rule there (rule 8), not just a one-time check.

**G0-05** — done — `src/BuildingBlocks/Events/{IIntegrationEvent,IIntegrationEventHandler,IEventBus,
EventBus}`, `src/BuildingBlocks/Outbox/{OutboxMessage,InboxMessage,OutboxModelConfiguration,
SharedDbContext,SharedDbContextFactory,DbContextOutboxExtensions,InboxDedupeDecorator,
ModuleIntegrationEventRegistration,OutboxProcessor,OutboxServiceCollectionExtensions,Migrations/*}`.
`tests/BuildingBlocks.IntegrationTests/` (new project, same reasoning as G0-03's UnitTests project —
this task's verify block needs real transaction/commit boundaries, which the InMemory EF provider
can't demonstrate, so Testcontainers.PostgreSql is used here ahead of G0-09's formal harness).

**R1 (inbox atomicity) implemented as:** every module `DbContext` maps `OutboxMessage` *and*
`InboxMessage` via `OutboxModelConfiguration.ConfigureOutbox(ownsMigration: false)`;
`SharedDbContext` owns the migration for both (`ownsMigration: true`) and nothing else.
`IOutboxWriter` is deliberately an **extension method on `DbContext`** (`db.Outbox().Enqueue(event)`),
not a DI-registered service — a shared `IOutboxWriter` registration would collide across modules the
same way a non-generic domain-event dispatcher would. `InboxDedupeDecorator<TContext,TEvent>` wraps
each module's `IIntegrationEventHandler<T>` (registered via the new
`AddModuleIntegrationEvents<TContext>`, using the same per-closed-type decoration technique as G0-03's
`AddModuleCqrs`) and writes the dedupe row through the SAME `TContext` instance the handler uses, in
ONE `SaveChangesAsync` call — the handler's own business write and the inbox marker can never commit
separately. **Contract this relies on:** handlers stage changes but do not call their own
`SaveChangesAsync`; the decorator's save is the one commit point. Documented on the decorator's XML
doc, will need to be documented again wherever the first real handler is written (G2-04).

**R2 (zero-handler events)** falls out of the design rather than needing special-casing: `EventBus`
resolving zero handlers for an event type is a silent, successful no-op (not an error), so
`OutboxProcessor` marks the message processed on the first pass regardless of handler count.

**IEventBus vs IOutboxWriter, a distinction PLAN.md's text doesn't spell out:** `IOutboxWriter` is the
*write* path (stage an event transactionally with an aggregate write); `IEventBus.PublishAsync` is the
*delivery* path (resolve and invoke whatever's subscribed, right now) — `OutboxProcessor` is just:
pop pending rows → deserialize → `IEventBus.PublishAsync` → mark processed or record failure. This
split let the same delivery mechanism be called directly from tests (immediate) or from the poller
(deferred) without duplicating the handler-resolution logic.

**Honest limit of the "process killed before returning" test:** a real process kill can't be scripted
in an automated test. The test instead proves the guarantee the design rests on directly — the
handler's write and its inbox marker commit through one `SaveChangesAsync` call, so there is no window
where one exists without the other — by invoking the decorated handler twice via `IEventBus` without
ever advancing `outbox_messages.processed_utc` between the two calls (exactly what a real processor
would do on retry after a crash at that boundary) and asserting the business effect happened once.
This is mechanically the same assertion as the "publish twice" test; the two are kept as separate
tests because they document the guarantee from different angles (black-box idempotency vs. the
specific crash-recovery scenario R1 names), not because they exercise different code paths — flagged
honestly rather than silently, per the checkpoint's question about non-vacuous tests.

Generated migration classes (`InitialShared`, `SharedDbContextModelSnapshot`, and the equivalent for
the test module) stay `public partial class` — **not** hand-edited to `internal`, per the plan's R3
correction (arch rule 4 will exclude `Migration`/`ModelSnapshot` subclasses at G0-07, not the other
way around).
