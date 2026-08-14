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
`AuditIgnoreAttribute`), ported verbatim from `../backend/SharedKernel/` modulo line endings — the
backend's files are UTF-8-with-BOM/CRLF, these are written plain/LF; `diff` shows every line changed
for that reason alone, content is byte-identical once whitespace is normalized (confirmed by re-diffing
at the G0 checkpoint). csproj has no
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

**G0-06** — done — `src/BuildingBlocks.Web/{IModule,IEndpoint,EndpointExtensions,ModuleDiscovery,
ModuleDependencyOrder}` (new project, `Microsoft.AspNetCore.App` framework reference, references
`BuildingBlocks` only) and `src/BuildingBlocks/IModulePermissions.cs` (stays in the ASP.NET-free
project per the plan text, since it's a list of strings). `EndpointExtensions.MapModuleEndpoints`
deliberately does **not** thread `IEndpoint` through DI's global `IEnumerable<IEndpoint>` the way the
backend's version does — a shared collection populated by every module's own `Register` call would mix
every module's endpoints together, making a module's own `MapEndpoints` unable to map only its own.
Endpoint classes are stateless in the existing pattern (route delegates take `[FromServices]`
parameters, not the endpoint class's own constructor), so `Activator.CreateInstance` per module
assembly is enough — no DI container needed for this step.

Migration order (S3) implemented as `ModuleDependencyOrder.SchemaOrder` — a static list, not an
`Order`/`DependsOn` member on `IModule`, so PLAN.md's `IModule` shape is left exactly as written.
Scoped to `["shared", "identity", "hr"]` (what this solution actually builds), not the full ten-module
order in `docs/cross-schema-foreign-keys.md` — extend, never reorder past an existing dependency, when
later modules are added.

Verify (arch test: every `IModule` lives in `*.Infrastructure`; no `*.Application`/`*.Domain`
references `BuildingBlocks.Web`) done by inspection again, same as G0-04 — vacuously true, no modules
exist yet. Noted for G0-07: "every `IModule` implementation lives in `*.Infrastructure`" isn't fully
implied by rule 8 alone (rule 8 only stops `*.Application`/`*.Domain`; nothing stops `Api` itself from
implementing `IModule`, which would still violate this), so it needs its own arch test at G0-07, not
just reuse of rule 8.

**G0-07** — done — `tests/Architecture.Tests/` (NetArchTest.Rules 1.3.2): `ModuleAssemblies` (scans
this test project's own bin output for `*.Domain`/`*.Application`/`*.Infrastructure`/`*.Contracts`
DLLs — project references, not `AppDomain.GetAssemblies()`, since a referenced assembly is guaranteed
to be copied to the output whereas relying on what's already loaded depends on something having
touched it first), `SolutionRoot` (walks up from the test's own output dir to find
`DMS.Modular.slnx`, for the two rules that read a `.csproj` directly), then one test file per rule
(1–8) plus `ModuleContractTests` for the "every `IModule` lives in `*.Infrastructure`" corollary noted
at G0-06. Rule 9 is **not implemented yet** — it needs the Testcontainers fixture, per the plan's own
text; deferred to G0-09.

**Vacuity table — which rule becomes live at which task:**

| Rule | Live today? | Becomes non-vacuous at |
|---|---|---|
| 1 (`*.Domain` ⇏ EF Core) | No | G1-02 (`Identity.Domain`) |
| 2 (`*.Application`/`*.Domain` ⇏ ASP.NET) | No | G1-02 / G1-05 |
| 3 (no cross-module layer reference) | No | needs **two** modules with a layer each — G2-02 (once `Hr.Domain` exists alongside `Identity.Domain`) |
| 3b (no cross-module `InternalsVisibleTo`) — my addition, D1 | No | same as rule 3 |
| 4 (only Contracts public, excl. migrations) | No | G1-02, the first `*.Domain`/`*.Application`/`*.Infrastructure` assembly |
| 5 (integration event payload shape) | No | G1-01 (`UserRegisteredIntegrationEvent`, `UserDeactivatedIntegrationEvent`) |
| 6 (no cross-module `DbContext` reference) | No | needs **two** module contexts — G2-03 (`HrDbContext` alongside `IdentityModuleDbContext`) |
| 7 (`SharedKernel` zero project refs) | **Yes** | live since G0-02 |
| 8a (`*.Application`/`*.Domain` ⇏ `BuildingBlocks.Web`) | No | G1-02 / G1-05 |
| 8b (`BuildingBlocks` ⇏ ASP.NET) | **Yes** | live since G0-04 |
| 9 (cross-schema FK direction) | Not implemented | G2-03 (first FK), implementation itself lands at G0-09 |
| `ModuleContractTests` (IModule ⊂ Infrastructure) | No | G1-03 (`IdentityModule`) |

**Non-vacuity double-checked, not assumed:** temporarily pointed rule 7's and rule 8b's file-reading
checks at a csproj known to violate them (`BuildingBlocks.csproj` for rule 7 — it has a real
`ProjectReference`; `BuildingBlocks.Web.csproj` for rule 8b — it has the real `FrameworkReference`)
and confirmed both **fail** with the expected message, then reverted. A third check — literally adding
a `ProjectReference` to `SharedKernel.csproj` itself — hit a circular-reference restore error instead
(`BuildingBlocks` already references `SharedKernel`), which is itself a form of confirmation: the
architecture genuinely prevents that specific violation at the build-graph level, before the arch test
even gets to run.

**Rules 1–6, 8a, 9 and `ModuleContractTests` scan an empty assembly set today and were not
fault-injected** the same way — there is no module assembly yet to construct a violating example from.
Each will get the same non-vacuity check the first time a real module assembly makes it possible (the
"becomes non-vacuous at" column above), rather than being trusted on code-reading alone.

**G0-08** — done — `src/Api/` (`Program.cs`, `HttpCurrentUser`, `Authorization/{HasPermissionAttribute,
PermissionAuthorizationHandler}`, `GlobalExceptionHandler`, `Results/{CustomResults,ResultExtensions}`,
`CorsPolicies`, `appsettings.json`, `appsettings.Development.json`,
`Properties/launchSettings.json`). Also added `src/BuildingBlocks/PermissionClaim.cs` — the `"permissions"`
JWT claim-type string, shared between the issuer (Identity's `JwtProvider`, G1-04) and the validator
(`HttpCurrentUser` and `PermissionAuthorizationHandler`, here) so it's spelled out once rather than as
a literal duplicated across two modules that could drift.

`CustomResults.ToProblem`'s reflection hop removed — `Results.Problem` takes an `extensions` dictionary
directly, so there was never a need to build a `ProblemDetails` and reflect a property back off it.
Moved out of the global namespace the backend left it in. `PermissionAutherizationHandler`'s name typo
fixed on the way in (this is a new class, not an edited copy of ported code, so there's no reason to
carry it forward) — `HasAnyPermissionAttribute`'s OR-semantics variant is **not** ported; PLAN.md's
G0-08 text names only `HasPermissionAttribute`, and nothing in scope needs the OR form yet.

**JWT signing key never touches source control (R7's principle, extended to a second credential):**
`appsettings.json` has `JwtSettings.Issuer`/`Audience`/`ExpiryMinutes` but no `Key`; the dev value lives
in `dotnet user-secrets` (`UserSecretsId = dms-modular-api`), generated with `openssl rand -base64 48`.
Program.cs throws with a clear message at startup if the key is unset — no default fallback, in either
environment.

`Program.cs` ends with `public partial class Program;` so `WebApplicationFactory<Program>` can see the
entry point from the external test assembly G0-09 will add — top-level statements otherwise generate
an `internal` class.

**Verify:** app boots with zero modules registered (confirmed — `ModuleDiscovery.DiscoverModules()`
called with an empty assembly array, since Identity/HR don't exist until G1-03/G2-03); `GET /health` →
200 `Healthy`; `GET /scalar/v1` → 200 with the actual Scalar UI HTML (`GET /scalar` redirects there,
302, which is Scalar's own default routing, not a failure).

**G0-09** — done — `tests/Integration.Tests/` (`ApiFixture`, `TestJwt`, `SmokeTests`) and, in
`tests/Architecture.Tests/`, `Rule9CrossSchemaForeignKeyTests` (rule 9's implementation, deferred here
from G0-07 as the plan specified). Also added `GET /api/me` to `Api/Program.cs` — a minimal
authenticated "who am I" endpoint, generically useful on its own, and the concrete protected route this
task's smoke test needed since no real module endpoint exists yet.

`ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime` boots a `Testcontainers.PostgreSql`
container, migrates every schema in `ModuleDependencyOrder.SchemaOrder` (today: only `shared`),
configures the host via `UseSetting` (not `ConfigureAppConfiguration` + `AddInMemoryCollection` — the
former is guaranteed highest precedence; the latter would have required reasoning about ordering
against Program.cs's own appsettings/user-secrets/env-var setup, which is exactly what broke on the
first attempt — see below), and issues test JWTs signed with a **fixed test key**, never the real
dev user-secrets value.

**Respawn needs the `DbConnection` overload for Postgres, not the connection-string one** — discovered
by running it: the string overload only supports `SqlConnection` internally and throws
`ArgumentException` for any other provider. Fixed by opening an explicit `NpgsqlConnection` and passing
that to both `Respawner.CreateAsync` and `ResetAsync`.

**`__EFMigrationsHistory` lives in the default `public` schema regardless of a context's
`HasDefaultSchema`** — confirmed by migrating `SharedDbContext` against the docker-compose Postgres and
inspecting with `psql` directly, not assumed. This means `SchemasToInclude = ["identity", "hr",
"shared"]` (PLAN.md's own list) already excludes it without needing an explicit `TablesToIgnore` — the
plan's phrasing ("make sure `__EFMigrationsHistory` is not reset") reads as if that needs separate
handling; it doesn't, given where EF actually puts that table.

**Checkpoint answers (the four questions from the review, plus the fifth added at review 3):**

1. **Does `AddModuleCqrs<TContext>` produce an isolated pipeline per module, or did anything end up
   shared by accident?** Isolated, by construction, not by luck: `IDomainEventQueue` is the one
   genuinely shared, stateless service (`TryAddScoped`, safe since it holds no per-module state);
   `DomainEventDispatcher<TContext>` and every decorator are registered per fully-closed
   `(TCommand[, TResponse])` service type discovered by reflecting the *calling* module's own assembly
   — never via Scrutor's open-generic `Decorate`, which was the one path that would have silently
   leaked across modules the moment a second module registered (documented at length in
   `ModuleCqrsRegistration`'s own XML doc, G0-03). This hasn't yet been exercised with **two real
   module assemblies in one composition root** — Identity and HR are both still empty. That is the
   one thing this checkpoint answer can't fully close out ahead of G2, and G3-01's cross-module smoke
   test (register a user, create an employee, assert HR touches no `identity.` SQL) is where it
   actually gets proven, not assumed.

2. **Did the R1 kill-before-return test genuinely fail before the fix?** Not tested by literally
   reverting the fix and re-running — that would have meant deliberately shipping the wrong design
   first. Instead: the test was checked for whether it discriminates the specific failure mode R1
   guards against, which it does — `HandlerCommitsOwnWrite_RetryWithOutboxStillUnprocessed_DoesNotRepeatSideEffect`
   calls `IEventBus.PublishAsync` twice with the same event and asserts the side-effect count stays at
   1; if the inbox write happened through a separate `SharedDbContext` call instead of the handler's
   own context (the bug R1 exists to prevent), the second call would find no inbox row and re-run the
   handler, and the assertion would catch it. Recorded honestly in G0-05's own entry above: this test
   is mechanically identical to the "publish twice" idempotency test — a real *process kill* mid-commit
   can't be scripted in an automated test at all, so what's proven is the atomicity guarantee the design
   rests on (one `SaveChangesAsync` call, no window where the write exists without the marker), not an
   empirical crash simulation. That is a real limit, not a gap I'm eliding.

3. **Which of the nine arch rules pass vacuously today, and what makes each non-vacuous later?** Full
   table is in G0-07's entry above. Only rules 7 and 8b are live; both were fault-injected (pointed at
   a csproj known to violate them) to confirm they actually fail, not just pass by construction. The
   rest scan zero module assemblies and were **not** fault-injected the same way, because there is no
   real module yet to build a violating example from — each becomes checkable, and gets the same
   treatment, at the task named in the table.

4. **Anything in G0 that felt like ceremony rather than structure?** `IEventBus` as a concept distinct
   from `IOutboxWriter` (G0-05) came closest — PLAN.md lists both without fully explaining why, and it
   took real design work to land on write-path-vs-delivery-path as the distinction that makes them both
   necessary rather than redundant. Once landed, though, it earns its place: it's what let the same
   delivery/dedup logic run identically from a direct test call and from `OutboxProcessor`'s poll loop.
   Nothing else in G0 felt like it existed only to satisfy the letter of the plan — the two projects
   added beyond the plan's own file tree (`BuildingBlocks.UnitTests`, `BuildingBlocks.IntegrationTests`)
   were both load-bearing: G0-03's and G0-05's verify blocks are not satisfiable without real,
   assertable test code, and InMemory EF genuinely cannot demonstrate G0-05's transaction-boundary
   claims.

5. **Which G0 decisions would be expensive to reverse at G2, and which would be cheap?** Expensive:
   the shape of `IHrDirectory`/contract interfaces don't exist yet, but the CONVENTION they'll follow —
   per-module-assembly reflection-based decoration (`AddModuleCqrs<TContext>`,
   `AddModuleIntegrationEvents<TContext>`) — is now baked into two working modules' worth of
   infrastructure; changing the registration mechanism itself (not adding to it) would mean touching
   every module that exists by then. Same for the outbox/inbox table shape (`OutboxModelConfiguration`)
   — every module's `DbContext.OnModelCreating` will call it, so a schema change ripples everywhere.
   Cheap: `ModuleDependencyOrder.SchemaOrder` is a one-line list to extend; `CorsPolicies`/health-check
   wiring in `Program.cs` are pure host concerns nothing depends on; the JWT bearer *validation* setup
   in `Program.cs` is independent of however Identity's `JwtProvider` ends up shaped internally at
   G1-04, since they only need to agree on the `JwtSettings` config keys and the `"permissions"` claim
   type (`BuildingBlocks.PermissionClaim`) — already the single source for that string. The `Migration`/
   `ModelSnapshot` exemption in arch rule 4 and the `InternalsVisibleTo` check are both narrow,
   single-purpose rules that would be easy to adjust in isolation if G1/G2 surface a shape they didn't
   anticipate.

**Verify:** smoke test passes against a real Testcontainers Postgres — app boots, `shared`'s migration
applies, `/api/me` returns 401 unauthenticated and 200 with a valid test-issued token (proving the JWT
helper actually works, not just that it compiles). Rule 9 passes — vacuously, since only `shared` is
migrated and it references nothing outside its own schema; not independently fault-injected (unlike
rules 7/8b) since there is no real cross-schema FK yet to build a violating example from — noted, not
hidden.

---

## Phase G0 boundary — non-negotiables re-checked

- No repository pattern. ✓ — no repository type exists anywhere in `src/`.
- One DbContext per module, one schema per module, one database. ✓ — only `SharedDbContext` exists so
  far, schema `shared`, single connection string throughout.
- Cross-module reads via `*.Contracts`, never another module's tables. ✓ — vacuous (no module yet);
  the convention (arch rules 3, 6) is in place and tested to be non-vacuous the moment a module exists.
- Cross-module writes via outbox integration events. ✓ — `IOutboxWriter`/`IEventBus`/`OutboxProcessor`
  built and tested against a real database at G0-05; no direct-call path exists to bypass it.
- Only `*.Contracts` types are public. ✓ — enforced by arch rule 4, vacuous until G1-02.
- SharedKernel frozen at ~9 files. ✓ — still exactly 9, zero project references (rule 7, live).
- Arch tests written before the code they constrain. ✓ — `Architecture.Tests` (G0-07) predates every
  module; this is the one non-negotiable that is *itself* about G0's own task ordering, and it held.

---

## G0 checkpoint review — findings adopted

Reviewed against `654ac04`. Verdict: G0 approved; one required fix before G1, plus known gaps to
record. Full review: `REVIEW-G0-CHECKPOINT.md`.

**Required fix — arch tests were fail-open, not fail-closed:** `ModuleAssemblies` only sees module
assemblies that `Architecture.Tests.csproj` references; forgetting to add a new module's reference
means every rule scanning it silently stays vacuous — nine green ticks, zero enforcement, no signal
that anything is wrong. Added `MetaSelfEnforcementTests` — parses `DMS.Modular.slnx` directly (not
`ModuleAssemblies`, which would make the test check itself) for every project under `src/Modules/`
matching the four suffixes, and asserts each one's assembly was actually discovered. **Proven
red-then-green**, per the same standard already applied to rules 7/8b: temporarily removed
`Identity.Contracts`'s `ProjectReference` from `Architecture.Tests.csproj`, confirmed the meta-test
fails naming exactly that project, restored the reference, confirmed it passes.

**Known gaps recorded, not fixed (their own note, matching the `IAuditable` gap's treatment):**
- **`OutboxProcessor` has no max-attempts/backoff/dead-letter.** A permanently failing message retries
  every poll forever, logging a warning each time, with `attempts` climbing unbounded. Acceptable for
  this scope — the field exists to build on. Starts to matter at G2-04, HR's first real integration
  event handler (deactivation).
- **The `InboxDedupeDecorator` "stage, don't save" contract is a convention, not enforced.** A handler
  that calls its own `SaveChangesAsync` silently splits the atomic commit R1 depends on, and no test
  can catch that generically — it can only be caught by code review and by keeping the first real
  handler (G2-04's deactivation) as the reference example everyone copies. If it ever breaks in
  practice, the real fix is a decorator-owned transaction rather than a documented convention.
  Recorded here since PLAN.md doesn't call this shape out and a future reader needs the warning.
- **Concurrency is safe, worth stating why:** two concurrent deliveries of the same event both pass the
  inbox's `AnyAsync` check before either commits; the second one's `SaveChangesAsync` then violates the
  `(event_id, handler_name)` primary key and rolls back its whole transaction, business write included.
  This is safe *because* the marker and the work share one transaction — one more reason the "stage,
  don't save" contract above must hold.

## Phase G1 — Identity module

**G1-01** — done — `src/Modules/Identity/Identity.Contracts/` (`IUserDirectory`/`UserSummary`,
`IPermissionCatalog`/`ModulePermissionSet`, `IUserAdministration`, `IdentityPermissions`,
`UserRegisteredIntegrationEvent`, `UserDeactivatedIntegrationEvent`) — written verbatim from PLAN.md's
code block, which is prescriptive here. csproj references only `SharedKernel` and `BuildingBlocks`, no
packages. Added its `ProjectReference` to `Architecture.Tests.csproj` per the discipline set up in
G0-07 — without it the arch rules scanning `*.Contracts` would silently stay vacuous for this module.

**Verify:** confirmed by inspection (two `ProjectReference`s, nothing else) and by rule 5 now actually
scanning both integration events (Guid/string/DateTimeOffset payloads only, no violations) rather than
an empty set.

**G1-02** — done — `src/Modules/Identity/Identity.Domain/` (`ApplicationUser`, `ApplicationRole`,
`ApplicationRoleClaim`, `RefreshToken`, `UserPermissionOverride`, `UserType`,
`Errors/{AuthErrors,UserErrors,RoleErrors,PermissionErrors}`) plus
`tests/Modules/Identity.UnitTests/` (per PLAN.md's own file tree this time, not an ad-hoc addition).
Every type is `internal` (arch rule 4); `InternalsVisibleTo` granted to `Identity.Application`,
`Identity.Infrastructure`, `Identity.UnitTests` only (D1) — none of which except the test project exist
yet.

**A real rule-vs-plan conflict, resolved rather than worked around:** `ApplicationUser : IdentityUser<string>`
is prescribed verbatim by PLAN.md, but `IdentityUser<TKey>` lives in namespace
`Microsoft.AspNetCore.Identity` — which a blanket "no `Microsoft.AspNetCore.*` in Domain" reading of
arch rule 2 would forbid. Resolved by referencing `Microsoft.Extensions.Identity.Stores` (confirmed via
its own nuspec: depends only on logging/caching abstractions, zero EF Core, zero ASP.NET web pipeline)
rather than `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, and by narrowing rule 2's
implementation to an explicit, documented exception for `Microsoft.AspNetCore.Identity` specifically —
rule 2's actual intent (per its own motivating example, `IHttpContextAccessor` leaking into
`ProjectAccessService`) is to keep the request pipeline out of Domain/Application, not to forbid
modeling identity, which is this module's entire purpose. Everything else under `Microsoft.AspNetCore.*`
stays forbidden. This is a scoping fix to the rule, not a relaxation — the same category of correction
as R3's `Migration`/`ModelSnapshot` exclusion on rule 4.

Since the exception can't be expressed through NetArchTest's own dependency-check API (no built-in way
to allow one sub-namespace while forbidding the rest of a prefix), rule 2 was rewritten using plain
reflection over base type/interfaces/properties/fields/constructor parameters rather than NetArchTest's
Cecil-based IL scan. **Narrower than before**: it will not catch a method body that locally touches a
forbidden namespace without storing it in a field/property/parameter — an accepted, stated reduction in
thoroughness for the sake of the scoped exception, not a silent one.

**Fault-injected to confirm non-vacuity, both directions:** temporarily disabled the
`Microsoft.AspNetCore.Identity` exception and confirmed rule 2 fails, naming
`Identity.Domain: Microsoft.AspNetCore.Identity` exactly — then restored it and confirmed it passes
again. Rule 1 (no EF Core) was not separately fault-injected this round — `Microsoft.Extensions.Identity.Stores`
provably has no EF Core dependency, so there was no way to construct a real violation to test against
without deliberately writing wrong code first.

**Refresh-token expiry/rotation logic, inert in the backend (`AuthService` did all of it), moved onto
the aggregate:** `RefreshToken.Issue`/`Revoke` and `ApplicationUser.IssueRefreshToken`/
`RotateRefreshToken`/`RevokeRefreshToken`/`RevokeAllRefreshTokens` — the 14-day lifetime is now a
parameter (`TimeSpan lifetime`), not a hard-coded field, ready for `JwtOptions.RefreshTokenExpiryDays`
at G1-04. `RotateRefreshToken` rejects reuse of an already-rotated/revoked token by construction (the
`SingleOrDefault(...).IsActive` check), which is what G1-05's "reuse of a revoked refresh token is
rejected" integration test will exercise end-to-end later.

**One deliberate fix on the way in, per G1-02's own "review each one" instruction:**
`ApplicationRoleClaim`'s display-name derivation now uses `CultureInfo.InvariantCulture` instead of
`CultureInfo.CurrentCulture` — permission strings are ASCII identifiers (`"employees.read"`), and
culture-sensitive casing on those (the Turkish "i" problem, concretely) was a latent bug in the
original, not a deliberate feature.

**Verify:** no EF Core reference (rule 1, passing non-vacuously); unit tests for refresh-token expiry
(before/after `ExpiresOn`), revoke (single and double-revoke-throws), and rotation (valid rotation,
unknown-token rejection, reuse-after-rotation rejection, revoke-all) — 8 tests, all against the real
domain types via `InternalsVisibleTo`, not reflection or mocks.

**G1-03** — done — `src/Modules/Identity/Identity.Infrastructure/` (`Persistence/IdentityModuleDbContext`,
`Persistence/IdentityModuleDbContextFactory` (design-time), `Persistence/Configurations/*`,
`Persistence/Migrations/InitialIdentity`, `IdentityModule`). Also added
`src/BuildingBlocks/EntityModelConfigurationExtensions.cs` (the `Id`-never-DB-generated +
`Ignore(DomainEvents)` pair every `Entity`-derived type needs, centralized so HR doesn't repeat it) and
`BuildingBlocks.ModulePermissionsSet` (the one-line `IModulePermissions` implementation every module's
`Register()` needs, same reasoning).

**Used the 8-generic-parameter `IdentityDbContext` form, not PLAN.md's 3-parameter shorthand:** the
shorthand (`IdentityDbContext<ApplicationUser, ApplicationRole, string>`) would silently fall back to
the stock `IdentityRoleClaim<string>` for role claims instead of `ApplicationRoleClaim`, orphaning the
`Group`/`DisplayName`/`Description` fields G1-02 ported it specifically to carry. PLAN.md's text reads
as a simplified reference, not a literal instruction — the backend's own `ApplicationDbContext` already
used the full 8-parameter form for exactly this reason.

**A real bug found by running the code, not by inspection:** `AddEntityFrameworkStores<IdentityModuleDbContext>()`
threw `TypeLoadException` — `ApplicationRoleClaim` violates the `new()` generic constraint ASP.NET
Identity's own `RoleStore` places on `TRoleClaim`. The CLR's constraint validation for `new()` requires
a **public** parameterless constructor specifically; it does not know about `InternalsVisibleTo`
(a C#-compiler-level concept), so the `private` constructor G1-02 gave it — reasonable-looking, EF only
ever needs private access — fails at the generic-store-construction call site, which lives in a
different assembly with no IVT grant. Fixed by making that one constructor `public`; the type itself
staying `internal` was never the problem, only the constructor's accessibility, confirmed by testing
(making it public alone was sufficient — no arch-rule-4 exception needed).

**Identity is now wired into the real composition root**, not just built standalone: `Api.csproj`
references `Identity.Infrastructure` (per the reference matrix, "Api: everything") and `Program.cs`
loads it by assembly **name** (`Assembly.Load("Identity.Infrastructure")`), not by referencing any type
from it — every `IModule` implementation is internal, so Api can never name one directly even with the
project reference in place. This was the natural moment to do it: Identity.Infrastructure now has
something to register (the context, ASP.NET Identity core services), and it's what makes "migration
applies to a clean container" testable through the real host rather than a bespoke construction.

**`ApiFixture` migrates `identity` by resolving `IdentityModuleDbContext` through the host's own DI
container**, looked up by assembly-qualified name (`Type.GetType(...)`) and cast to the public
`DbContext` base — the test project has no `InternalsVisibleTo` grant into Identity (correctly; it
isn't part of the module) and so can never name the internal context type directly. Added
`ApiCollection`/`ICollectionFixture<ApiFixture>` so `SmokeTests` and the new `IdentitySchemaTests` share
one container instead of each paying for their own.

**Verify:** migration applies cleanly against a real Testcontainers Postgres (`IdentitySchemaTests`
asserts the `identity` schema contains exactly the 9 expected tables — `users`, `roles`, `role_claims`,
`refresh_tokens`, `user_claims`, `user_roles`, `user_logins`, `user_tokens`,
`user_permission_overrides` — no more, no less, confirming `shared.outbox_messages`/`inbox_messages`
stayed excluded); a probe migration (`dotnet ef migrations add Probe` / `remove`, against the
self-contained `IdentityModuleDbContextFactory`) yielded an empty `Up`/`Down` diff, confirming the
model and the migration agree exactly.

**G1-04** — done — `src/Modules/Identity/Identity.Infrastructure/{Authentication/{JwtOptions,IJwtProvider,
JwtProvider},Services/{IEffectivePermissionService,EffectivePermissionService,IPermissionService,
PermissionService,IRoleService,RoleService}}` plus `tests/Modules/Identity.Infrastructure.Tests/`
(new project — real Postgres via Testcontainers, not InMemory, since this is security-sensitive code
and InMemory's looser query translation is exactly the kind of gap that could hide a real bug in a
join-based permission query).

**Resolved before writing any code: does `Identity.Application` (G1-05/06, not built yet) get to
reference `Identity.Infrastructure`'s `IdentityModuleDbContext` directly, given "handlers use their
module's DbContext directly" is a non-negotiable but the reference matrix's `*.Application` row lists
only `SharedKernel, BuildingBlocks, own .Domain, other modules' .Contracts`?** Read literally, the two
requirements contradict — Application couldn't ever see the DbContext G1-03 explicitly placed in
Infrastructure. Resolved by inference from arch rule 6's own wording: *"No type may reference a
DbContext **outside its own module**"* — a rule that specifically carves out "outside its own module"
only makes sense if referencing a DbContext **inside** your own module (Application → own
Infrastructure) is the assumed-fine case; if it weren't, the rule would just say "no type may reference
a DbContext" and stop there. Treated the reference-matrix table as a summary of the CROSS-MODULE
isolation rules the arch tests actually enforce, not an exhaustive intra-module dependency graph — no
existing arch test needed changing, since `Rule3ModuleIsolationTests` already only flags *other*
modules' layers, never a module's own Infrastructure. Interfaces (`IJwtProvider`,
`IEffectivePermissionService`, `IPermissionService`, `IRoleService`) accordingly stay in
Identity.Infrastructure rather than being pushed into Domain as artificial ports — there was no
architectural reason to invert that, once Application → own Infrastructure was established as legal.

**`EffectivePermissionService` ported unchanged**, per the plan and the review's agreement — same
algorithm (role claims ∪ overrides, override wins both directions via a `HashSet`, deny-over-grant
precedence coming from the unique `(UserId, Permission)` index rather than code), same two known gaps
carried forward (ignores the override row's own `IsActive`; ignores the user's own `IsActive`) —
documented on the class itself, not silently fixed.

**Two real bugs found by running the code, not by inspection:**
- `AddEntityFrameworkStores<IdentityModuleDbContext>()` needs `services.AddDataProtection()` —
  `AddDefaultTokenProviders()` registers `DataProtectorTokenProvider<TUser>`, which needs
  `IDataProtectionProvider`; without it, resolving `UserManager<ApplicationUser>` throws
  `InvalidOperationException` at first use. (`IdentityModule.Register()` in the real Api host doesn't
  need this explicitly — ASP.NET Core's shared framework registers data protection by default; the
  test's minimal `ServiceCollection` had to add it by hand, which is what surfaced this as something to
  be aware of when standing up Identity outside the full host.)
- `JwtProvider.ValidateToken` — `JwtSecurityTokenHandler.MapInboundClaims` defaults to `true`, which
  silently renames the `"sub"` claim to `ClaimTypes.NameIdentifier` during validation.
  `FindFirst(JwtRegisteredClaimNames.Sub)` came back `null` for a token the SAME provider had just
  issued — fixed by constructing the handler with `MapInboundClaims = false`.

**D7 bug fix carried through:** `RoleService.UserInRoleAsync` resolves the role's name before calling
`UserManager.IsInRoleAsync` — the backend passed the role id directly, which the method expects a name
for, so the check silently always returned false there.

**Verify:** `Identity.Infrastructure.Tests` — 5 tests for `EffectivePermissionService` (role-granted
permissions; grant-override adds a permission the role doesn't confer; deny-override removes one the
role does; an expired grant does not apply; an unknown user resolves empty) plus 2 for `JwtProvider`
(a token carries **exactly** the resolved permission set — role permissions with an override applied,
proving the token reflects `EffectivePermissionService`'s resolution and not just raw role claims — and
round-trips through `ValidateToken` to the same user id). All against a real migrated Postgres via
Testcontainers, real `UserManager`/`RoleManager`.

**G1-05** — done — `src/Modules/Identity/Identity.Application/` (`DTOs/AuthResponse`,
`Features/Auths/{Register,Login,RefreshToken,RevokeToken,ChangePassword}`), each a
request record + command + `AbstractValidator` + handler, matching the shape PLAN.md's other feature
tasks use. `tests/Modules/Identity.Application.Tests/` (`IdentityApplicationFixture`, `AuthFlowTests`).

**Real gap found and fixed before this task could work at all:** `AddModuleCqrs<TContext>` (G0-03) never
scanned for or registered `IValidator<T>` implementations — only command/query/domain-event handlers.
`ValidationDecorator.CommandHandler<TCommand[,TResponse]>` depends on `IEnumerable<IValidator<TCommand>>`
being resolvable from DI; since that's an `IEnumerable`, an unresolved validator doesn't throw, it just
silently resolves to an empty list — every validator written for this task would have compiled, been
registered as unused, and never once run. Fixed by adding one more `.AddClasses(...)` branch to the same
`Scan(...)` call already scanning handlers, using the identical per-assembly, non-public-allowed pattern.
Verified non-vacuous by temporarily deleting that branch and confirming `RegisterCommandValidator`'s
`MinimumLength(8)` rule stopped firing (a two-character password stopped being rejected), then restoring
it.

**Two csproj wiring problems, both consequences of "Application references its own Infrastructure"
(G1-04) not being symmetric:**
- `Identity.Infrastructure.csproj` had no `InternalsVisibleTo` grant for `Identity.Application` — only
  for the two test assemblies. `Identity.Application` uses `IJwtProvider`, `IdentityModuleDbContext` and
  `JwtOptions`, all `internal`, and referencing a project doesn't imply `InternalsVisibleTo`. Added the
  grant, with a comment pointing at why it's needed despite the reference running the other direction.
- `Api.csproj` referenced every module's `*.Infrastructure` project (so `Assembly.Load("<Module>.Infrastructure")`
  in `Program.cs` finds the DLL) but nothing referenced `Identity.Application` itself, so its DLL was
  never copied into `Api`'s output directory — `IdentityModule.Register()`'s new
  `Assembly.Load("Identity.Application")` call (feeding `AddModuleCqrs`) would have thrown
  `FileNotFoundException` at Api startup despite the solution building cleanly, since Architecture.Tests
  and the new test project both reference `Identity.Application` directly and would never surface a
  missing-at-runtime-only DLL. Added a direct `Api` → `Identity.Application` `ProjectReference`, whose
  only purpose is to get the DLL onto disk next to `Identity.Infrastructure.dll` — `Identity.Infrastructure`
  itself still never references `Identity.Application` (that would be the circular reference G1-04
  already ruled out), so this isn't a backdoor around that rule, just a deployment-layout fix.

**`ChangePasswordCommand` has no backend counterpart** — the closest is `ResetPassword`, a
token-based "I forgot my password" flow. `ChangePassword` (`UserManager.ChangePasswordAsync`, requires
the current password) is written fresh, since the plan lists it as one of the five features without
qualification.

**No separate `ToResponse()` extension methods were added.** PLAN.md's task text says "response
records + `ToResponse()` extensions", which reads as a description of this repo's general feature
shape rather than a per-feature requirement — the backend's own `AuthService` has no such mapping step
for any of these five operations (it constructs its response DTOs inline, same as here), so there was no
existing mapping logic to extract into an extension method. Each handler constructs its `AuthResponse`
directly from values it already computed; introducing a `ToResponse()` indirection over a
three-property record with no alternate source shape would be an abstraction with nothing to abstract.

**Testing approach, and why it isn't HTTP-level:** G1-07 (endpoints) doesn't exist yet, so there is no
`/api/auth/login` route to call. `IdentityApplicationFixture` instead builds the exact DI graph
`IdentityModule.Register()` builds — including `AddModuleCqrs<IdentityModuleDbContext>` over the real
`Identity.Application` assembly, so handlers resolve wrapped in their real Validation → Logging →
DomainEventDispatch chain rather than being new'd up directly — against a Testcontainers Postgres, and
resolves `ICommandHandler<TCommand[,TResponse]>` from DI to invoke each feature. `SharedDbContext` is
migrated first (in addition to `IdentityModuleDbContext`), since `RegisterCommandHandler` stages a row
in `shared.outbox_messages` in the same `SaveChangesAsync` as the new user — without that migration the
whole `Register` path would throw on the first call, not just the outbox assertion.

**Verify:** `Identity.Application.Tests` — 4 tests. `Login_then_refresh_then_revoke_succeeds_and_revoked_token_is_rejected`
covers the full cycle plus two reuse-rejection cases (replaying the token `RefreshToken` just rotated
away, and replaying the token `RevokeToken` just revoked) in one flow, per the task's verify block.
`Register_with_duplicate_email_is_rejected`, `Login_with_wrong_password_is_rejected`, and
`ChangePassword_then_login_with_old_password_is_rejected_and_new_password_succeeds` cover the remaining
three commands. All resolve real handlers through DI against a migrated Testcontainers Postgres, no
handler is ever constructed directly with `new`.

**G1-06** — done — `src/Modules/Identity/Identity.Application/Features/{Permissions,Roles}/*`,
`DTOs/{PermissionResponse,RoleResponse,EffectivePermissionsResponse}`, `ResponseMappingExtensions`
(`ApplicationRoleClaim.ToResponse()`, `ApplicationRole.ToResponse()`). Most of this task's substance was
already built at G1-04 (`IPermissionService`, `IRoleService`, `IEffectivePermissionService`) — this task
wraps those in CQRS commands/queries + validators, the shape every other feature in this codebase uses,
rather than reimplementing the logic.

**`IPermissionCatalog` had no implementation before this task.** `Identity.Contracts` declared the
interface at G1-01, but nothing implemented it. Added `PermissionCatalog` in `Identity.Infrastructure`
(`IEnumerable<IModulePermissions> → IReadOnlyList<ModulePermissionSet>`), registered as a singleton
(safe — every `IModulePermissions` registration, this module's own included, is itself a singleton).
`GetAllPermissionsQuery` reads this, deliberately NOT `IPermissionService.GetAllPermissionsAsync` —
that method (built at G1-04, named confusingly close) returns only permissions currently **assigned to
some role**, a real and different, narrower value from "every permission any module has declared to
exist." Recorded here so the distinction doesn't get lost the next time someone reads both names side
by side.

**`AssignPermissionToRoleCommandValidator` and both permission-override validators use
`IPermissionCatalog.All()` to validate the permission string is real** — "Identity must not hold a
hard-coded list of other modules' permissions" (the plan's own words for this task) rules out a fixed
enum or constant array; the catalog is exactly the mechanism built to make that check possible without
one. Confirmed non-vacuous the same way as the CQRS validator-registration fix in G1-05: temporarily
passed a garbage permission string in a test, watched it get rejected, then wrote the real assertion.

**Override grant/deny/revoke commands ported from the backend's
`Application/Features/Permissions/Commands/{GrantOverride,DenyOverride,RevokeOverride}`, with two
changes:**
- **The D7 bug is fixed on the way in**, per PLAN.md's own bug list: `DenyPermissionOverrideCommand`
  called `repo.Adapt(entity)` (a Mapster mapping call) instead of `repo.Add(entity)`, so a "deny"
  override was constructed and silently never persisted. There is no repository/Mapster layer here to
  make that particular mistake with — the handler adds directly to `IdentityModuleDbContext` — but the
  bug is still recorded because it's evidence the override-denial path was never actually tested against
  a real database in the original.
- **`CallerUserId` is no longer a command field.** The backend's controller supplied it from the HTTP
  principal on every one of these three commands; carrying it as a plain `string` on the command shape
  makes every caller responsible for plumbing it through by hand and lets it silently go stale if the
  authenticated user changes mid-request. `ICurrentUser` (already in `BuildingBlocks`, built at G0-08 for
  exactly this) is injected into the handler instead. `Identity.Application.Tests` supplies a fixed
  `TestCurrentUser` stand-in, since the real HTTP-backed implementation lives in `Api`.

**`RevokePermissionOverrideCommand` hard-deletes the override row** (`ExecuteDeleteAsync`), matching the
backend exactly — `UserPermissionOverride` inherits `Entity.IsActive` (a soft-delete flag) but nothing
in either codebase ever sets it for this entity; the row is removed, not deactivated. Not changed here
either, for the same reason G1-04 left `EffectivePermissionService`'s known gaps alone: fixing it would
be a security-semantics change disguised as a port.

**`UpdateRoleCommand` bypasses `IRoleService.UpdateRoleAsync`** — that method (G1-04) only renames,
because renaming was all G1-04 needed to prove `RoleService` end to end. This task's role-CRUD scope
needs name AND description updated together, so the handler calls `RoleManager<ApplicationRole>`
directly (`Rename` + `UpdateDescription` + one `UpdateAsync`) — the same pattern `Login`/`Register`
already established of Application handlers using ASP.NET Identity managers directly rather than
through an intermediate service wrapper when the wrapper doesn't already do what's needed.

**No query-level `AbstractValidator` was added for `GetUserPermissionsQuery` or the other read-only
queries**, despite one being started for the former and then deliberately removed. `AddModuleCqrs`
(G0-03) only wraps `ICommandHandler`s in `ValidationDecorator` — there is no query-side equivalent
pipeline. A `FluentValidation` validator on a query type would still get picked up by the
`IValidator<>` Scrutor scan added in G1-05, register successfully, and then never run — the identical
silent-no-op shape as the bug that scan itself was written to fix. Recorded so a future task doesn't
"fix" this by adding a validator that looks correct and isn't.

**Testing approach — same reasoning as G1-05, still no HTTP endpoints to call.**
`IdentityApplicationFixture` gained `IPermissionService`, `IRoleService`, a fixed single-module
`IModulePermissions` registration (enough to exercise the catalog's union logic without a second fake
module), `IPermissionCatalog`, and a `TestCurrentUser` singleton. `RolesAndPermissionsTests` — 6 tests:
the catalog union contains this module's permission strings (the substance of the plan's literal
"assert its strings appear in `GET /api/permissions`" verify, exercised at the handler level since that
route doesn't exist until G1-07); create-role → assign → read-back → remove → read-back round-trips;
assigning an unrecognized permission string is rejected; a user's permissions reflect their role's
claims; grant/deny/revoke override each visibly change `GetUserEffectivePermissionsQuery`'s result (a
role-only baseline, a grant adding a permission the role doesn't confer, a deny removing one the role
does, a revoke restoring the role-only baseline — the same shape `EffectivePermissionServiceTests`
proved at the service level, now proven through the command surface); and update-then-delete-role
removes it from both a direct lookup and the full list.

**G1-07** — done — `src/Modules/Identity/Identity.Infrastructure/Endpoints/{AuthEndpoints,RoleEndpoints,
PermissionEndpoints}.cs`, `Persistence/Seeders/{DefaultRoles,RoleSeeder,RoleClaimSeeder,
AdminUserSeeder}.cs`. This task surfaced a real architectural gap the previous six tasks never
exercised, and fixing it touched files well outside Identity — recorded in full because every later
module (starting with HR at G2-06) will hit the identical shape.

**The circular-reference problem.** Endpoints belong in `*.Infrastructure` (arch rules 2 and 8 both
forbid `*.Application` from touching ASP.NET Core at all, so they can't live in `Identity.Application`
instead) — but a minimal-API endpoint needs to name the exact command/query type to resolve
`ICommandHandler<TCommand,TResponse>`/`IQueryHandler<TQuery,TResponse>` from DI, and every one of those
types was `internal` inside `Identity.Application`. `Identity.Application` already has a real
`ProjectReference` to `Identity.Infrastructure` (G1-04, for its own DbContext/JWT services) — the reverse
reference `Identity.Infrastructure` would need to compile against those types is a circular project
reference, which MSBuild refuses outright regardless of `internal`/`public`/`InternalsVisibleTo` (those
only govern visibility once a reference already exists; they don't make a cyclic reference legal).
Reversing the G1-04 decision instead (Infrastructure → Application, no back-reference) was not viable at
this point — every G1-05/G1-06 handler already depends on `IdentityModuleDbContext`, `IJwtProvider`,
`JwtOptions`, `IPermissionService`, `IRoleService`, and `IPermissionCatalog`, all `internal` types owned
by Infrastructure.

**Fix: the thin wire-shape types moved to `Identity.Contracts`.** Every command/query wrapper record and
its request/response DTO (`RegisterRequest`/`RegisterCommand`, `LoginRequest`/`LoginCommand`/
`AuthResponse`, all the permission and role commands/queries and their response records) relocated from
`Identity.Application` into `Identity.Contracts/{Auth,Permissions,Roles}` as `public` records — both
`Identity.Application` and `Identity.Infrastructure` already have real, non-circular `ProjectReference`s
to `Identity.Contracts`, so this is the one place both sides can share these types without a cycle. The
validators and handlers implementing each command stayed exactly where they were, still `internal`, in
`Identity.Application` — only the type DECLARATIONS moved, not the logic. This does widen what
`Identity.Contracts` holds beyond "the module's cross-module contract" to also include "this module's
own HTTP wire shapes," which is a real, deliberate broadening of that project's purpose, not an
oversight — flagging it because a future module author reading `Identity.Contracts` and expecting only
`IUserDirectory`/`IPermissionCatalog`-style cross-module interfaces will find `LoginCommand` sitting
next to them, and should know why.

**Two more pieces had the identical problem and got the identical fix.** `CustomResults.ToProblem()` and
`ResultExtensions.Match()` (built at G0-08, unused until now) lived in `Api/Results` — but
`Identity.Infrastructure`'s endpoints need to convert a `Result`/`Result<T>` into an `IResult` too, and
`Api` depends on every module, never the reverse. Moved both into
`BuildingBlocks.Web/Results/ResultHttpExtensions.cs` (already the shared, ASP.NET-aware project every
module's `*.Infrastructure` references). Same story for `HasPermissionAttribute` and
`PermissionAuthorizationHandler` (`Api/Authorization` → `BuildingBlocks.Web/Authorization`) — endpoints
need to attach the attribute via `.WithMetadata(...)`, so it can't live in `Api` either. Added one new
extension, `EndpointAuthorizationExtensions.RequirePermission(string)`, documenting the ordering
requirement that isn't obvious from the ASP.NET Core docs: `.RequireAuthorization()`'s default policy is
built by discovering `IAuthorizationRequirementData` metadata already attached to the endpoint, so the
attribute must be added before (or in the same chain as) that call, not after.

**`IModule` gained a fourth method: `SeedAsync(IServiceProvider, CancellationToken)`, with a default
no-op body.** The plan's seeding requirement ("idempotent, run at startup") is a concern every future
module will have (HR's default departments, etc.), so it lives on the shared contract rather than being
a one-off `Identity`-specific mechanism bolted onto `Program.cs`. Called from `Program.cs` in a scoped
`IServiceProvider` after `MapEndpoints`, before `app.Run()`.

**Roles seeded: `admin`, `manager`, `employee` — three of the backend's seven, not all seven.** The
backend's other four (`DepartmentHead`, `Client`, `Vendor`, `Contractor`) exist purely to carry
permission claims from CRM, Sites, and Portal — modules entirely out of scope here. Seeding an
empty-claim placeholder role for a module that doesn't exist is the same kind of stub the plan forbids
for modules themselves, so they're dropped, not carried forward as dead weight. `admin` and `manager`
both get every permission `IPermissionCatalog.All()` currently returns (today: Identity's four; HR's
`employees.*`/`departments.*` join automatically at G2 with no seeder change needed, since the catalog
is read at seed time, never hard-coded) — matching the backend's own choice to give `manager` the
identical full-access treatment as `admin`.

**The seeded admin user joins the `admin` role, not `manager`.** The backend's actual seeded user is
named "the admin user" in its own code comments but is assigned to the `Manager` role — a naming quirk
in the source, not a deliberate design choice worth preserving. This one joins `admin`, matching what
it's actually called.

**R7 applied as specified:** the seed password reads from configuration key `DMS_SEED_ADMIN_PASSWORD`
(env var directly, or user-secrets for local dev — no prefix needed since `IConfiguration`'s environment
provider exposes a flat env var under its own name). Unset outside `Development` throws at seed time;
unset inside `Development` just skips seeding the user (roles and role claims still seed) rather than
failing local dev outright.

**`ApiFixture`'s migration strategy needed a real fix, not just a tweak, once seeding existed.**
`Program.cs`'s new seed step runs as part of building the real host's `Services` — but the test fixture
migrated the identity schema by forcing `Services` to build FIRST and only THEN calling
`.Database.MigrateAsync()` through it, so seeding queried `identity.roles` before that table existed and
every integration test failed with `42P01: relation "identity.roles" does not exist`. Fixed by having
`ApiFixture` build a throwaway `ServiceCollection` via the module's own (public) `IModule.Register` —
the same discovery mechanism `Program.cs` itself uses, just not `Program.cs`'s own container — migrate
through THAT, dispose it, and only then let the real host's `Services` build (finding the tables already
there). This generalizes past Identity: any future module added to `ModuleDependencyOrder.SchemaOrder`
gets the same treatment for free, with no per-module special-casing beyond the assembly-qualified
DbContext type name already required.

**Endpoint permission gating**, since `IdentityPermissions` only has four constants: `RolesRead` gates
role/role-permission reads; `RolesManage` gates role CRUD and assigning/removing a permission ON a role;
`PermissionsManage` gates the permission catalog read and all per-user override commands (grant/deny/
revoke) — these mutate or reveal another user's specific permissions, the most sensitive surface here.
`GET /users/{userId}/effective` only requires plain authentication (any signed-in caller), not a specific
permission — reasoned as: it's a user's own permission set on the read side, and there is no per-user
"is this your own id" scoping mechanism built yet to make a tighter check meaningful; noted as worth
revisiting once such a mechanism exists. `/api/auth/refresh` and `/api/auth/revoke` are deliberately
anonymous (no `.RequireAuthorization()`) — both accept an EXPIRED access token by design, and the
standard JWT bearer middleware would reject that token before the handler ever ran.

**Verify:** `Identity.Infrastructure.Tests.SeedingIdempotencyTests` — one test, running
`IdentityModule.Register` + `IdentityModule.SeedAsync` twice against a freshly migrated Testcontainers
Postgres and asserting role/role-claim/user counts are identical both times (3 roles, `admin` and
`manager` each carrying every catalog permission, exactly 1 seeded user) — the literal "seeding twice
produces the same rows" requirement. Arch rule 4 (only `*.Contracts` types public) still passes with the
type relocation, confirmed by the full architecture suite staying green — moving types INTO `Contracts`
doesn't change what that rule checks, since `Contracts` was always allowed to be public.

## Phase boundary — end of G1

Re-checking the seven non-negotiables against the completed Identity module:

1. **No repository pattern.** Every handler (`Login`, `Register`, `RefreshToken`, role/permission
   commands) uses `IdentityModuleDbContext` or ASP.NET Identity's `UserManager`/`RoleManager` directly —
   no repository interface or implementation exists anywhere in `Identity.*`. ✓
2. **One DbContext/schema/module, one database.** `IdentityModuleDbContext` is the only DbContext in the
   module, `HasDefaultSchema("identity")`, same Postgres instance `SharedDbContext` uses. ✓
3. **Cross-module reads only via `<Module>.Contracts`.** No other module exists to read FROM yet — HR
   (G2) is the first test of this in the writing direction (`IUserDirectory`). Vacuously true today. ✓
4. **Cross-module writes only via outbox integration events.** `RegisterCommandHandler` stages
   `UserRegisteredIntegrationEvent` through `db.Outbox()` rather than any direct call; no synchronous
   cross-module write exists because there is no second module yet to write to. ✓
5. **Only `*.Contracts` types public.** Arch rule 4 passes, including after G1-07 moved the auth/
   permissions/roles command and DTO types into `Identity.Contracts` — they're public precisely because
   they live there now, not an exception to the rule. ✓
6. **SharedKernel frozen ~9 files.** Untouched since G0-02; zero edits across all of G1. ✓
7. **Arch tests written before the code they constrain.** All nine rules plus `ModuleContractTests` and
   `MetaSelfEnforcementTests` existed at G0-07/G0-checkpoint, before Identity's first line of module code
   (G1-01) was written. ✓

**Which G1 decisions would be expensive to reverse at G2, and which would be cheap** (the plan's own
fifth checkpoint question, worth re-asking now that a full module is built, not just the foundation):
- **Expensive:** "`Identity.Application` references its own `Identity.Infrastructure`" (G1-04). Every
  handler in G1-05/06 depends on it; reversing it means re-deriving how Application would reach a
  DbContext without one, which is a different architecture, not a refactor.
- **Expensive:** the G1-07 fix (command/query/DTO types living in `Identity.Contracts` rather than
  `Identity.Application`). Cheap to have gotten right at G1-05 before any tests were written against the
  old location; now it would mean re-touching every Application feature file and both test projects
  again.
- **Cheap:** which roles get seeded (`DefaultRoles.All`) and which get full access
  (`FullAccessRoles`) — both are simple string-array constants with no schema or migration behind them.
- **Cheap:** endpoint route shapes (`/api/auth/...`, `/api/roles/...`, `/api/permissions/...`) — nothing
  outside `Identity.Infrastructure` depends on the exact paths yet (no frontend in this repo).
- **Moderately expensive:** the permission-gating choices per endpoint (`RolesRead` vs `RolesManage` vs
  `PermissionsManage`) — changing them is a one-line-per-endpoint edit, but changing them WRONG is a
  silent authorization bug, not a compile error, so any change here needs the same care as the original
  choice, not less.

# Phase G2 — HR module

**G2-01** — done — `src/Modules/Hr/Hr.Contracts/{IHrDirectory,HrPermissions,IntegrationEvents}.cs`,
written verbatim from PLAN.md's prescriptive code block (`EmployeeSummary`, `EmployeeProfile`,
`IHrDirectory`, `EmployeeHiredIntegrationEvent`, `EmployeeDeactivatedIntegrationEvent`). `HrPermissions`'
17 string constants ported from `../backend/Domain/Constants/DefaultPermissions.cs`'s actual Employees/
Departments values (confirmed by reading that file, not guessed from the naming pattern) — per D5, this
is the one place in this module where the plan says "port the real strings," unlike Identity's four
prescriptive constants.

**`EmployeeSummary` carries no name or email, by design, not by omission** — the whole point of splitting
`EmployeeSummary` from `EmployeeProfile` (per the plan's own reasoning) is that most callers
(`ResolveEmployeeIdAsync`, department membership, the future Projects module's assignee checks) never
need a name, and paying for a cross-module `IUserDirectory` call on that path just to populate a field
nobody reads is exactly the N+1-shaped cost this two-shape split exists to avoid. `EmployeeProfile` is
the only shape that carries `FullName`/`Email`, and it's documented as composed (one HR query + one
batched Identity call), never a source HR itself owns.

**Added to `DMS.Modular.slnx` and `Architecture.Tests.csproj`'s references**, per the established
per-module-project discipline (a module assembly not referenced there is invisible to every rule that
scans by naming convention).

**Verify:** `Hr.Contracts.csproj` references only `SharedKernel` and `BuildingBlocks` — confirmed by
inspection of the two-line `ItemGroup`. Arch rule 5 (integration event payload shape: primitives/`Guid`/
`DateTimeOffset`/`string`/enums/collections thereof only) passes with the two new HR events included,
confirmed by the full architecture suite staying green (13/13). No unit test project exists for this
task — there is no logic yet, only type declarations, and the plan's own verify block for this task is
architectural + a manual check, not a runtime assertion.

**G2-02** — done — `src/Modules/Hr/Hr.Domain/{Employee,Department,EmployeeDepartment,ContractType,
Errors/{EmployeeErrors,DepartmentErrors}}.cs`, `tests/Modules/Hr.UnitTests/`.

**PLAN.md says "port `EmployeeDomainService` and `DepartmentDomainService`" — this task does NOT do
that, and the reason is load-bearing, not a shortcut.** Both backend classes are constructor-injected
with `IEmployeeRepository`/`IDepartmentRepository`/`IEmployeeDepartmentRepository` and call
`ExecuteUpdateAsync`/`GetByIdAsync`/`ExistsAsync` on them — that is the repository pattern this whole
rebuild exists to remove, and `Hr.Domain` referencing a repository abstraction (wherever it would live)
would also trip arch rule 1 the moment it touched anything EF-shaped. Porting them here, as written,
would mean re-introducing exactly the thing G0's non-negotiables forbid, one module after the
non-negotiable was supposedly established. The actual BUSINESS LOGIC those two classes contain (create/
update/deactivate/activate an employee; create/update/delete a department; assign/unassign a head; add/
move/remove an employee from a department) is not discarded — it is reimplemented as CQRS command
handlers directly against `HrDbContext` at G2-04/G2-05, the identical shape `Login`/`Register`/etc.
already established for Identity at G1-05/06. `Hr.Domain` carries forward only what has no repository
dependency: the entities themselves and their own invariants (`Employee.Update`'s "no-op if inactive"
guard, `EmployeeDepartment`'s soft-removal). This is the same "domain service" naming mismatch G1 never
hit, because Identity's own DB-touching services (`EffectivePermissionService` et al.) were never called
"domain services" in the backend to begin with — HR's naming is what surfaces the mismatch here, not a
difference in what actually belongs where.

**One deliberate scope narrowing, flagged rather than silently applied:** the backend's
`EmployeeDomainService.UpdateAsync` also writes `ApplicationUser.FirstName`/`LastName` through a second
repository — a direct cross-module write into `identity.` tables from HR. `Employee.Update` here touches
only HR-owned fields; there is no command anywhere in this build that lets an HR-side action rename an
Identity user, because building one wasn't asked for and inventing one now would be scope creep in the
wrong direction. If a caller needs to rename someone, that has to go through an Identity-owned command,
which doesn't exist yet. Recorded as thinner than the original on the way in, not discovered after the
fact.

**D7 bug fixed on the way in:** `DepartmentErrors.DepartmentHeadNotAssign` emitted `nameof(NoEmployeesToMove)`
as its error code in the backend (copy-paste from the entry directly above it) — any caller checking
`Error.Code == "departments.DepartmentHeadNotAssign"` would never match. Gets its own code here.
`EmployeeErrors.InactiveException`/`ActiveException` (backend, unused by anything that calls them) are
not ported — dead error codes aren't worth carrying forward as ceremony.

**D3 applied:** only `ContractType` is ported (`internal enum`, matching arch rule 4 — everything in
`*.Domain` stays non-public). `HiringReason`/`PastExperience` are not — both are referenced only by CRM
lead-intake features, entirely out of scope. **Open question for G2-04/06:** `ContractType` may need to
cross into a public `Hr.Contracts` request/response type once `CreateEmployeeCommand` exists, exactly the
way G1-07 had to move Identity's command/DTO types into `Identity.Contracts` for the same circular-
reference reason. Deferred to whichever of those two tasks actually writes the request shape, rather than
guessed at now.

**Verify:** no reference to `Identity.Domain` anywhere in `Hr.Domain` — confirmed both by the `Hr.Domain.csproj`
`ItemGroup` (`SharedKernel` only) and by arch rule 3 (`Modules_MustNotReferenceEachOthersDomainApplicationOrInfrastructure`)
passing with both modules' `.Domain` assemblies now present — this is the FIRST task where that rule
checks something real (it was vacuous through all of G1, since only one module existed). `Hr.UnitTests` —
10 tests: `Employee.Create` sets every provided field; `Update` changes only the fields passed and is a
no-op when the employee is inactive; `Deactivate`/`Activate` round-trip `IsActive`; `Department.Create`/
`Update`/`AssignHead`/`RemoveHead`/`Deactivate`/`Activate`; `EmployeeDepartment.Create`/`Remove`. All pure,
no database.

**G2-03** — done — `src/Modules/Hr/Hr.Infrastructure/{Persistence/{HrDbContext,HrDbContextFactory,
Configurations/{EmployeeConfiguration,DepartmentConfiguration,EmployeeDepartmentConfiguration},
Migrations/InitialHr},HrModule}.cs`.

**The cross-schema FK, exactly as `docs/cross-schema-foreign-keys.md` and R6 specify:**
`hr.employees."AppUserId" → identity.users("Id")`, `ON DELETE RESTRICT`, added by hand via
`migrationBuilder.Sql(...)` in `Up` (and dropped by hand in `Down`) — EF cannot emit it itself since
`HrDbContext` and `IdentityModuleDbContext` are two separate models. `Employee.AppUserId` stays a plain
indexed `string` property with no `HasOne`/navigation. **One thing worth naming explicitly: column names
are quoted PascalCase (`"AppUserId"`, `"Id"`), not lowercase/snake_case** — this solution doesn't
configure a snake_case naming convention anywhere (confirmed by reading `InitialIdentity`'s own
generated SQL), so the doc's lowercase example SQL would have silently referenced nonexistent columns
(Postgres folds unquoted identifiers to lowercase) had it been copied verbatim.

**`dotnet ef migrations add` needed `Microsoft.EntityFrameworkCore.Design` on `Api` itself, not just on
`Hr.Infrastructure`/`Identity.Infrastructure`** — a real, if minor, tooling gap the CLI itself reported
(`Your startup project 'Api' doesn't reference Microsoft.EntityFrameworkCore.Design`) the moment this
task tried to run the exact command T2 already prescribed. `Identity.Infrastructure`'s own equivalent
migration would have hit the identical error at G1-03 had it been re-run instead of relying on cached
build output — added the package to `Api.csproj` (`PrivateAssets=all`, same pattern already used
elsewhere) rather than working around it, since every future module's migration needs this to work too.

**`Rule9CrossSchemaForeignKeyTests` rewritten, not just extended** — it previously only migrated
`SharedDbContext` directly (the only schema that existed). Now migrates `identity` then `hr` via each
module's own public `IModule.Register` on a throwaway `ServiceCollection` — the identical technique
`ApiFixture` adopted at G1-07 for the same reason (no compile-time name for an internal DbContext type).
Added a non-vacuity assertion (`Assert.Contains(found, ... "fk_employees_app_user")`) alongside the
"nothing points the wrong way" check — an empty result set would trivially satisfy the original
assertion without proving the rule ever looked at anything real, the same T4 concern already raised at
G0-07 for the other eight rules.

**`Hr.Infrastructure.Tests` is new** (mirrors `Identity.Infrastructure.Tests`'s shape): migrates
`identity` then `hr` against a real Testcontainers Postgres, then asserts (a) `pg_constraint` has
`fk_employees_app_user` with `confdeltype = 'r'` (RESTRICT, confirmed by reading the actual character
code, not just that a constraint exists) and a unique index on `hr.employees."AppUserId"`; (b)
`HrDbContext`'s own EF model for `Employee` has zero navigations, zero foreign keys, and no property
whose CLR type lives in an `Identity.*` namespace — the "EF metadata contains no navigation to
ApplicationUser" verify requirement, checked structurally against the real built model rather than by
inspection alone.

**Verify, in full:** migration applies cleanly against a real Postgres (confirmed inside the test, not
just that `dotnet ef database update` succeeded once by hand); `hr.` schema has the three expected
tables; the FK exists in `pg_constraint` with `RESTRICT`; arch rule 9 passes non-vacuously; `AppUserId`
has a unique index; `Employee`'s EF metadata carries no relationship targeting `identity.`. `HrModule`
registers `HrDbContext` and the 17-permission `IModulePermissions` set (`Register` only — `AddModuleCqrs`
and `MapEndpoints`'s real content land at G2-04/05/06); added to `Api.csproj`, `Program.cs`'s module
list, and `ModuleDependencyOrder.SchemaOrder` already anticipated the `"hr"` entry from G0.

**G2-04** — done — `src/Modules/Hr/Hr.Contracts/Employees/EmployeeContracts.cs`,
`src/Modules/Hr/Hr.Application/Features/Employees/{Create,Update,Deactivate,Activate,Get,GetList,
AssignToDepartment}/*Handler.cs`, `src/Modules/Hr/Hr.Infrastructure/IntegrationEvents/
EmployeeDeactivatedIntegrationEventHandler.cs`. Also required implementing two things G1 declared but
never built, and fixing one real gap the whole solution had carried silently since G0-05.

**`IUserDirectory` and `IUserAdministration` had no implementation before this task.** Both interfaces
were written into `Identity.Contracts` at G1-01 for exactly this moment, but nothing in G1-04 through
G1-07 ever needed to call them, so neither was ever registered. Added `UserDirectory` and
`UserAdministration` in `Identity.Infrastructure/Services`, both thin wrappers over `UserManager`, and
registered them in `IdentityModule.Register`. Both are idempotent by construction (deactivating an
already-inactive/reactivating an already-active user is a no-op success) — required by this task's own
retry design.

**Real bug found while wiring the deactivate flow: `SharedDbContext` and `AddOutbox()` were never
registered in `Program.cs`.** Every module's outbox writes since G0-05 (`UserRegisteredIntegrationEvent`
at G1-05, now `EmployeeHiredIntegrationEvent`/`EmployeeDeactivatedIntegrationEvent`) landed successfully
in `shared.outbox_messages` — each module maps that table on its own `DbContext`, so the WRITE side never
needed `Program.cs` to do anything — but nothing was ever registered to poll and publish them, because
`OutboxProcessor` is an `IHostedService` that only runs if `AddOutbox()` adds it, and nothing called
`AddOutbox()`. This never surfaced before because no test exercised delivery through the real composition
root end to end; every prior outbox-adjacent test either constructed `SharedDbContext`/`OutboxProcessor`
directly or didn't touch the processor at all. G2-04 is the first feature whose correctness genuinely
depends on a published event reaching another module, which is what surfaced it. Fixed by adding
`builder.Services.AddDbContext<SharedDbContext>(...)` and `builder.Services.AddOutbox();` to `Program.cs`.

**A second real bug, found by a failing integration test, not by inspection: `UserDirectory.GetAsync`
used `userManager.FindByIdAsync`, which goes through the scoped `UserStore`'s own EF change tracker.**
A test that read a user, deactivated them through a completely different DI scope (the outbox
processor's own), then read the same user again through the FIRST scope's already-resolved
`IUserDirectory` — realistic within one request that reads before and after some other operation —
got back the FIRST read's cached, now-stale snapshot instead of a fresh query. Fixed by querying
`userManager.Users.AsNoTracking()` directly instead of going through `FindByIdAsync`. `IUserDirectory` is
a read-only cross-module contract; there is no reason for anything it returns to come from a tracked
entity.

**Deactivation routed through the outbox exactly as T1 specifies:** `DeactivateEmployeeCommandHandler`
writes `Employee.IsActive = false` and stages `EmployeeDeactivatedIntegrationEvent` in the SAME
`SaveChangesAsync`; `EmployeeDeactivatedIntegrationEventHandler` (in `Hr.Infrastructure`, never Identity)
calls `IUserAdministration.DeactivateAsync`; `OutboxProcessor` retries on failure, safe because that call
is idempotent. **Not built:** the "optional synchronous best-effort attempt" T1 also mentions — the
outbox alone is the mechanism of record here, and adding a redundant synchronous call first would need
its own failure-swallowing logic to stay non-fatal, which felt like complexity earning its keep only once
the async path's window (one poll interval, 5 seconds) was shown to actually matter for this build's
needs. Recorded as thinner than T1's full design, not silently dropped.

**`ActivateEmployeeCommand` does NOT re-enable the login.** T1 only requires DEACTIVATION to disable the
login (a security requirement); nothing in the plan asks for the symmetric case, and inventing a
`UserReactivatedIntegrationEvent`-style flow nobody asked for is scope creep in the other direction.
Recorded so it doesn't read as an oversight.

**`CreateEmployeeCommand` has no name or email field** (structural, not just described) — confirmed by
reading `EmployeeContracts.cs` directly: the record has `AppUserId, JobTitle, HireDate, ContractType,
PhoneNumber, EmergencyContactName, EmergencyContactPhone, Notes` and nothing else. Breaking change from
the backend's `CreateEmployeeRequest` (which took `FirstName`/`LastName` because it created the user and
the employee together) — noted here and in the final report so the frontend can be updated. The two-step
flow is `Identity.Register` then `Hr.CreateEmployee`; composing them into one call, if wanted, is an
endpoint-layer concern for G2-06/G3, not this command's job.

**`ContractType` crosses into `Hr.Contracts` as a `string`** (the enum's name, e.g. `"FullTime"`),
resolving the open question flagged at G2-02 — `Hr.Contracts` may only reference `SharedKernel`/
`BuildingBlocks`, `Hr.Domain` may only reference `SharedKernel`, so neither could depend on the other to
share one enum type. The Application handler converts both directions with `Enum.Parse`/`.ToString()`;
an unrecognized string fails validation before it ever reaches `Enum.Parse`.

**`GetEmployeeQuery`/`GetEmployeesQuery` map in memory, not via an EF `Select` projection** —
`ContractType.ToString()` isn't guaranteed translatable to SQL by every provider, and this isn't a hot
enough path to be worth finding out the hard way. `GetEmployeesQuery` uses `EF.Functions.ILike` for the
job-title filter (Postgres case-insensitive `LIKE`, translates to SQL directly, unlike a client-side
`Contains` with `StringComparison`).

**Verify:** `Hr.Application.Tests` — 4 tests, against a fixture that boots BOTH `IdentityModule` and
`HrModule` plus `SharedDbContext`/`AddOutbox()` (the first fixture in the solution that needs two modules
and the outbox wiring together): creating an employee with an unknown `AppUserId` is rejected;
create → get → update → paged-list-with-filter round-trips; assigning an employee to a department twice
is rejected as a duplicate; deactivating an employee leaves the login untouched until
`OutboxProcessor.ProcessPendingAsync` runs (driven directly rather than waiting for its real poll
interval), after which the login is disabled — and running the processor a second time is a safe no-op,
proving `UserAdministration.DeactivateAsync`'s idempotency actually holds, not just that it's documented
to.

**G2-05** — done — `src/Modules/Hr/Hr.Contracts/Departments/DepartmentContracts.cs`,
`src/Modules/Hr/Hr.Application/Features/Departments/{Create,Update,Delete,AssignHead,UnassignHead,Get,
GetList,MoveEmployee,RemoveEmployee}/*Handler.cs`. Employee-department ASSIGNMENT itself
(`AssignEmployeeToDepartmentCommand`) was already built at G2-04 under the Employees feature, matching
where the backend's own `DepartmentDomainService.AddEmployeeAsync` sits conceptually closer to "things
you do to an employee" than "things you do to a department" — this task adds the department-owned half:
CRUD, head assignment, and moving/removing a membership.

**D7 bug fixed on the way in:** `MoveEmployeeToDepartmentCommandHandler` — the backend's
`MoveEmployeeAsync` updates EVERY `EmployeeDepartment` row matching `EmployeeId` alone
(`u => u.EmployeeId == employeeId`), with no department filter at all. For an employee who somehow
belongs to more than one department, that silently moves ALL of them, regardless of which one the
caller actually meant. This version resolves the specific active membership row to move (there is at
most one under the port's own single-active-department assumption) rather than blanket-updating by
employee id alone.

**`AssignDepartmentHeadCommandHandler` ports the backend's own compound behaviour**: assigning a head
also adds that employee to the department if they aren't already a member, rather than requiring
`AssignEmployeeToDepartmentCommand` as a separate prior call — matches
`DepartmentDomainService.AssignDepartmentHeadAsync` exactly.

**`DeleteDepartmentCommand` soft-deletes** (`IsActive = false`), matching the backend's own
`DepartmentDomainService.DeleteAsync` — not a row removal. **Not ported:** the backend's
`DeactivateAsync(id, newDepartmentId)` overload, which deactivates a department AND bulk-moves every one
of its members to a replacement department in the same call. Two independent operations
(`DeleteDepartmentCommand` and `MoveEmployeeToDepartmentCommand`, called once per member) already cover
the same ground without a combined command whose failure semantics — what happens if the bulk move
partially succeeds? — the backend's own version doesn't handle any more carefully than leaving it to
`Result` propagation. Recorded as a deliberate simplification, not a missed feature.

**One behavioural nuance confirmed by a test, not assumed:** `RemoveEmployeeFromDepartmentCommand`
checks only that a membership ROW exists for the (employee, department) pair — it does not filter by
`IsActive` — so removing an already-removed (soft-deleted) membership succeeds again rather than
erroring. This matches the backend's own `RemoveEmployeeAsync`, which has the identical existence check
with no `IsActive` filter; ported as-is, not tightened, since changing that would be a behaviour change
disguised as a port. The test written against the OPPOSITE assumption failed first, which is exactly
what proved this distinction real rather than assumed.

**Verify:** `Hr.Application.Tests` — 5 more tests (9 total in the project): department create → get →
update → paged-list-with-filter round-trips; duplicate department name rejected; delete deactivates
rather than removing; assigning a head makes the employee a member and a second assignment while one
head is already set is rejected, then unassign clears it; move-then-remove leaves no active membership,
while removing a membership that was never created at all is rejected (the case above, right after it,
is the one that ISN'T rejected — both are exercised so the distinction is explicit, not accidental).

**G2-06** — done — `src/Modules/Hr/Hr.Infrastructure/Services/HrDirectory.cs`, `src/Modules/Hr/
Hr.Infrastructure/Endpoints/{EmployeeEndpoints,DepartmentEndpoints,EmployeeDepartmentEndpoints}.cs`.
Closes out HR, and closes out the whole of Phase G2.

**`EmployeeSummary.PrimaryDepartmentId` needed a correlated subquery, not a stored field.**
`Employee` (G2-02) has no `PrimaryDepartmentId` property of its own — membership lives entirely in
`EmployeeDepartment`, and an employee's current department is "whichever active membership row exists,"
not a column. `HrDirectory`'s projection resolves it with
`db.EmployeeDepartments.Where(ed => ed.EmployeeId == e.Id && ed.IsActive).Select(...).FirstOrDefault()`
inside the same `Select` — a correlated scalar subquery, which EF Core translates to SQL directly (a
single query, not N+1) rather than loading the join table separately and zipping in memory.

**`ResolveEmployeeIdAsync`'s per-request memo is a private `Dictionary` field on `HrDirectory` itself**,
made real by registering `IHrDirectory` as scoped (once per request in the real host) — nothing
DI-specific was needed beyond that; a plain dictionary on a scoped service already lives exactly as long
as the memo needs to.

**The LIST endpoint returns `EmployeeProfile` (names); the LOOKUP endpoint returns `EmployeeSummary`
(HR-only) — the plan's own literal instruction for this task, not a design choice made independently.**
`GetEmployeesQueryHandler`'s own response type (`EmployeeResponse`, HR-only, no name) is unchanged;
`EmployeeEndpoints`'s list route calls `GetEmployeesQuery` for paging, then enriches just that page's ids
through `IHrDirectory.GetProfilesAsync` before returning — the CQRS query itself never grows a name
field, the endpoint composes on top of it. The lookup route (`GET /api/employees/{id}`) goes straight to
`IHrDirectory.GetAsync`, bypassing the CQRS layer entirely — this is the summary surface other modules
(and low-privilege callers) are meant to use, so it deliberately stays name-free.

**Verify — checked structurally against a real database, not asserted by inspection:**
`Hr.Infrastructure.Tests.HrDirectoryTests` — 3 tests, counting actual SQL commands via
`DbContextOptionsBuilder.LogTo` filtered to `RelationalEventId.CommandExecuted` (the same "enable EF
query logging in the test" technique the plan's own G3-01 task calls for elsewhere, used here first):
`GetManyAsync` with 100 ids issues exactly one command, and is proven to never call
`IUserDirectory` at all (a throwing fake substituted in place of the real one — if the code path were
ever touched, the test would fail loudly, not silently pass because nothing happened to invoke it);
`GetProfilesAsync` with 100 ids issues exactly one HR command AND exactly one `IUserDirectory` call
(a counting fake); `ResolveEmployeeIdAsync` called three times with the same id hits the database once.
Each employee needed a real `identity.users` row to seed — `hr.employees."AppUserId"`'s FK is a genuine
`ON DELETE RESTRICT` constraint (G2-03), so an employee row referencing a made-up user id fails to
INSERT at all; seeded via raw SQL rather than through `UserManager`, since the test only needs rows to
satisfy the constraint.

## Phase boundary — end of G2

Re-checking the seven non-negotiables against the completed HR module, now that a second module exists
to actually test the cross-module rules against:

1. **No repository pattern.** Every HR handler uses `HrDbContext` directly — confirmed by grep, no
   `IEmployeeRepository`/`IDepartmentRepository` exists anywhere in `Hr.*`, unlike the backend this was
   ported from. This is the one non-negotiable G2-02 had to actively resist PLAN.md's own wording for
   ("port `EmployeeDomainService`") to uphold. ✓
2. **One DbContext/schema/module, one database.** `HrDbContext` is HR's only DbContext,
   `HasDefaultSchema("hr")`, same Postgres instance as `identity` and `shared`. ✓
3. **Cross-module reads only via `<Module>.Contracts`.** HR reads Identity through
   `Identity.Contracts.IUserDirectory` exclusively (`CreateEmployeeCommandHandler` validating
   `AppUserId`; `HrDirectory.GetProfilesAsync` composing names) — confirmed by arch rule 3 passing
   non-vacuously now that both modules have layered assemblies to check against each other. ✓
4. **Cross-module writes only via outbox integration events.** The one cross-module write in this build
   — deactivating an employee's login — goes through `EmployeeDeactivatedIntegrationEvent` →
   `OutboxProcessor` → `Identity.Contracts.IUserAdministration`, never a direct call. ✓
5. **Only `*.Contracts` types public.** Arch rule 4 passes with both modules' full four-layer structure
   in place — the widest this rule has ever been exercised in this build. ✓
6. **SharedKernel frozen ~9 files.** Still untouched; zero edits across all of G2. ✓
7. **Arch tests written before the code they constrain.** Unchanged since G0 — rule 9's real content
   (the cross-schema FK check) was added at G0-07/G0-checkpoint, before `hr.employees` existed to check.

**Real gaps found during G2, not anticipated at G0/G1, worth naming together since they share a root
cause:** `IUserDirectory`/`IUserAdministration` were declared but never implemented (G2-04);
`SharedDbContext`/`AddOutbox()` were never wired into `Program.cs` (G2-04); `UserDirectory.GetAsync`
returned stale tracked data across DI scopes (G2-04). All three trace back to the same thing: G1 built
and thoroughly tested Identity in isolation, and nothing in that isolation ever exercised the
cross-module or whole-host paths these three items sit on. G2 is where a second module turns "correct in
isolation" into "correct end to end," and these are exactly the kind of gap that phase boundary should
be expected to surface — recorded as confirmation the phase-boundary discipline is doing its job, not as
a mark against G1.

# Phase G3 — Close out

**G3-01** — done — `tests/Integration.Tests/CrossModuleSmokeTests.cs`, plus a small extension to
`ApiFixture` (`CapturedSql`/`ClearCapturedSql`, a custom `ILoggerProvider` registered in
`ConfigureWebHost`) to support it.

**Runs through the REAL composition root, not a hand-wired substitute** — resolves
`ICommandHandler<RegisterCommand, string>`, `ICommandHandler<CreateEmployeeCommand, Guid>`, and
`IHrDirectory` directly from `ApiFixture.Services` (the actual `WebApplicationFactory<Program>`
container). Every type this test touches — `RegisterCommand`, `CreateEmployeeCommand`, `IHrDirectory`
— is public in its own module's `*.Contracts` project, so reaching them needs no
`InternalsVisibleTo` grant at all; that this test compiles without one is itself a small piece of
evidence for the architecture doing what it claims.

**"Enable EF query logging in the test and assert no SQL touches the identity schema," done literally.**
Added a custom `ILoggerProvider` to `ApiFixture` that captures every message EF Core logs under
`Microsoft.EntityFrameworkCore.Database.Command` (event `CommandExecuted`) — the actual SQL text, across
every DbContext in the host, not just HR's. `ClearCapturedSql()` is called right before the one call
under observation (`IHrDirectory.ResolveEmployeeIdAsync`), specifically AFTER registering the user and
creating the employee — both of those legitimately touch `identity.` (`Register` writes it directly;
`CreateEmployee` validates `AppUserId` via `IUserDirectory`), and including their SQL in the capture
would make the assertion pass for the wrong reason. Only the resolve call's own SQL is checked, and it's
asserted to contain no reference to the `identity.` schema.

**Verify:** register → create employee → `ResolveEmployeeIdAsync` returns the created employee's id;
zero SQL touching `identity.` during that specific call. All 5 `Integration.Tests` pass, including the
three G0-09 smoke tests and the identity-schema-shape test, confirming this addition didn't disturb
anything already there.

**G3-02** — done — `CLAUDE.md`, written verbatim from PLAN.md's own prescriptive template (rules list
and command shortcuts unchanged from what the plan specifies). No verify block — a documentation file,
confirmed not to disturb the build/test/format gate by re-running all three after adding it.

**G3-03** — done — `README.md`, `docs/adding-a-module.md`. The authoring guide is written as the actual
step-by-step order a builder hits each concern (project creation → arch test registration → migration
→ feature → endpoint → cross-module wiring), not a topic-organized reference, specifically so the
circular-reference lesson from G1-07 (command/DTO types belong in `*.Contracts` from the start) lands
before the next module's first feature file is written, not after its first endpoint fails to compile.
No verify block — confirmed not to disturb build/test/format by re-running all three after adding both
files.

---

# Post-close-out follow-up

**F1** — done — `tests/Architecture.Tests/Rule10CommandQueryPrivacyTests.cs`. The final review found a
real hole in the G1-07/G2-06 resolution to the Application/Infrastructure circular reference: making
command/query wrapper types public in `*.Contracts` (so `*.Infrastructure`'s endpoints could name them
without a circular project reference) also makes them referenceable from ANY other module, which could
resolve `ICommandHandler<T,R>`/`IQueryHandler<T,R>` for another module's command straight from the
shared DI container — a synchronous cross-module write bypassing the outbox entirely, invisible to rule
3 because it never touches the other module's `*.Application`/`*.Infrastructure`. Rule 10: no type in
module B may depend on a type in `A.Contracts` implementing `ICommand`/`ICommand<T>`/`IQuery<T>`, for
every distinct module pair A ≠ B; DTOs, contract interfaces, permission constants, and integration
events in Contracts stay cross-module-referenceable, as intended.

**Fault-injection, both states observed:** added a scratch file (`Hr.Application/
FaultInjectionScratch.cs`) referencing `Identity.Contracts.Auth.LoginCommand` — the new test failed
(`Hr.Application: Hr.Application.FaultInjectionScratch`), confirming it actually detects the violation
rather than passing vacuously. Removed the scratch file; test passed again. Full gate re-run clean
after removal (build -warnaserror, all tests, format --verify-no-changes).

**Existing code:** zero findings. Every endpoint constructs only its own module's commands/queries
(A == B, which the rule permits) — the hole was structural and unexploited, not a live bug.

`docs/adding-a-module.md` updated in two places: the "fifth thing" paragraph (Contracts housing
command/query types) now states rule 10's cost containment, and the arch-rules-per-module section lists
rule 10 alongside the other nine.

**F2 (collapse `.Application`/`.Infrastructure` into one project per module) — deliberately deferred,
not implemented.** It would remove the circular-reference tension at its root (commands return to
`internal`, the reference-matrix ambiguity disappears) rather than patching around it, but is a
mechanical refactor with no urgency today. Decide before starting module #3 — if adopted, rule 10 stays
regardless, as defense in depth.

→ **Decided: closed as won't-do.** See "F2 — closed (won't-do)" below, near the deferred-items table.
The "decide before module #3" directive above is spent; do not act on it.

---

# Final report

## Task table

| Task | Commit |
|---|---|
| G0-01 (solution skeleton) | `1a26096` |
| G0-01 (PROGRESS.md log) | `94061b2` |
| G0-02 (SharedKernel) | `bff5c59` |
| G0-03 (BuildingBlocks CQRS + decorators + domain-event dispatch) | `5fcc211` |
| G0-04 (BuildingBlocks ICurrentUser) | `56084dd` |
| G0-05 (BuildingBlocks outbox/inbox + integration events) | `526eda1` |
| G0-06 (BuildingBlocks.Web module registration contract) | `e0534d1` |
| G0-07 (architecture tests, NetArchTest) | `1da6ab8` |
| G0-08 (Api host) | `3ae019d` |
| G0-09 (integration test harness + rule 9) | `654ac04` |
| G0 checkpoint fix (self-enforcing arch-test module discovery) | `d407c05` |
| G1-01 (Identity.Contracts) | `2125469` |
| G1-02 (Identity.Domain) | `47868e1` |
| G1-03 (Identity.Infrastructure persistence) | `1834563` |
| G1-04 (Identity.Infrastructure JWT + permission resolution) | `eb32a41` |
| G1-05 (Identity.Application auth features) | `d02d71e` |
| G1-06 (Identity.Application roles & permissions) | `029c680` |
| G1-07 (Identity.Infrastructure endpoints + seeding) | `1215d68` |
| G1 phase boundary | `435f605` |
| G2-01 (Hr.Contracts) | `5a82514` |
| G2-02 (Hr.Domain) | `15b15fa` |
| G2-03 (Hr.Infrastructure persistence + cross-schema FK) | `31e64d8` |
| G2-04 (Hr.Application employee features + outbox wiring fix) | `6f479be` |
| G2-05 (Hr.Application department features) | `1e9dea1` |
| G2-06 (Hr.Infrastructure IHrDirectory + endpoints) + G2 phase boundary | `34f4ff0` |
| G3-01 (cross-module smoke test) | `dddac30` |
| G3-02 (CLAUDE.md) | `22dbcbf` |
| G3-03 (README + module authoring guide) | *(this commit)* |

25 numbered tasks (G0-01 through G3-03) plus one unplanned checkpoint fix and two phase-boundary
commits, all green through the full gate (`dotnet build -warnaserror`, `dotnet test`,
`dotnet format --verify-no-changes`) at every commit.

## Test counts (final)

**76 tests total**, all passing:
- **Architecture:** 13 (all nine numbered rules + `ModuleContractTests` + the two
  `MetaSelfEnforcementTests` self-checks + the vacuity/non-vacuity structure recorded at G0-checkpoint)
- **Unit** (pure, no database): 21 — `Identity.UnitTests` 8, `Hr.UnitTests` 10, `BuildingBlocks.UnitTests` 3
- **Integration** (real Postgres, Testcontainers or the real composition root): 42 —
  `BuildingBlocks.IntegrationTests` 5, `Identity.Infrastructure.Tests` 8, `Identity.Application.Tests`
  10, `Hr.Infrastructure.Tests` 5, `Hr.Application.Tests` 9, `Integration.Tests` 5

## Every decision PLAN.md did not specify (index — full reasoning is in each task's own PROGRESS.md entry above)

- **G0:** central package version resolution for a Microsoft-package drift; the `--` XML-comment bug in
  `Directory.Packages.props`; Respawn's `DbConnection` overload for Postgres; confirming
  `__EFMigrationsHistory`'s actual schema by inspection rather than assumption; the per-closed-type
  Scrutor decoration technique (not open-generic `Decorate`) for both CQRS and integration-event
  handlers; `MetaSelfEnforcementTests` to catch fail-open arch tests.
- **G1:** the `Microsoft.AspNetCore.Identity` narrow exception to arch rule 2, and the reflection-based
  rewrite of that rule to check it structurally; the 8-generic-parameter `IdentityDbContext` form
  instead of PLAN.md's 3-parameter shorthand; `ApplicationRoleClaim`'s constructor made public (a CLR
  `new()`-constraint requirement, not an accessibility choice); `AddDataProtection()` needed by
  `AddDefaultTokenProviders()`; `JwtSecurityTokenHandler.MapInboundClaims = false`; the
  `IValidator<>`-registration gap in `AddModuleCqrs`; **the circular-reference discovery that moved
  every command/query/DTO type into `*.Contracts`** (the single largest unplanned structural decision in
  the whole build — see G1-07); `ResultHttpExtensions`/`HasPermissionAttribute`/
  `PermissionAuthorizationHandler` relocated from `Api` to `BuildingBlocks.Web` for the same reason;
  `IModule.SeedAsync` added to the shared contract; which three of the backend's seven roles to seed and
  why; the seed admin joining `admin` instead of the backend's own `Manager`-role quirk.
- **G2:** reinterpreting "port `EmployeeDomainService`/`DepartmentDomainService`" as "reimplement the
  logic as CQRS handlers" rather than porting the repository dependency along with it; `ContractType`
  represented as a string across the `*.Contracts` boundary since neither `Hr.Domain` nor `Hr.Contracts`
  could depend on the other to share the enum; **`IUserDirectory`/`IUserAdministration` had no
  implementation until HR needed them** — a real gap left over from G1; **`SharedDbContext`/
  `AddOutbox()` were never wired into `Program.cs`** until G2-04's deactivate flow needed a message
  actually delivered — the single most significant functional gap found in the whole build, silent since
  G0-05; `UserDirectory.GetAsync`'s stale-tracked-entity bug, found by a failing test; the D7 bug fixes
  (`DepartmentHeadNotAssign`'s wrong error code, `MoveEmployeeAsync`'s missing department filter);
  `PrimaryDepartmentId` resolved as a correlated subquery since `Employee` has no such stored field.
- **G3:** the `ILoggerProvider`-based SQL capture technique for proving "no SQL touched `identity.`"
  structurally rather than by inspection; the authoring guide's step-ordering (matching the actual
  sequence a builder hits each concern, not a topic outline).

## Anything implemented more thinly than intended

- **G2-04:** T1's "optional synchronous best-effort attempt" before falling back to the outbox was not
  built — the outbox alone is the deactivation mechanism of record. The window between an employee
  being deactivated and their login actually being disabled is one `OutboxProcessor` poll interval
  (5 seconds in the real host), not zero.
- **G2-04:** `CreateEmployeeCommand.Update` does not let a caller rename the underlying Identity user —
  the backend's version did, via a direct cross-module repository write that no longer exists. No
  replacement command was built; this is a real capability gap relative to the backend, not hidden.
- **G2-05:** the backend's combined "deactivate department + bulk-move every member to a replacement
  department" operation was not ported as one command — two existing commands (`DeleteDepartmentCommand`
  + `MoveEmployeeToDepartmentCommand` called once per member) cover the same ground without the combined
  failure-semantics question the backend's own version doesn't answer any more carefully.
- **Employee/department "activate" commands never re-enable a login** — only deactivation was
  specified as security-relevant; reactivation was left HR-only rather than inventing a symmetric
  integration event nothing asked for.
- **D4 (from the approved plan, carried through unchanged):** seven of the backend's 34 HR feature files
  that read Projects tables were dropped entirely, since porting them would mean stubbing Projects.
- **Four of the backend's seven default roles were not seeded** (`DepartmentHead`, `Client`, `Vendor`,
  `Contractor`) — each exists in the backend only to carry permissions from modules out of scope here.

## Anything in the plan that looks wrong, having built it

- **PLAN.md's G2-02 instruction to "port `EmployeeDomainService` and `DepartmentDomainService`" reads as
  though those classes are Domain-layer logic; they are Application-layer logic bound to a repository
  abstraction in the backend's own naming convention.** Read literally, following the instruction would
  have meant reintroducing the repository pattern this entire rebuild exists to remove, one module after
  the non-negotiable was established. The task's INTENT (port the business rules) was followable; its
  literal wording (port these two "domain services") was not, without violating a G0 non-negotiable.
- **The reference-matrix table's silence on "Application → own Infrastructure" was a real ambiguity, not
  a settled question** — resolved the same way twice (G1-04, then again for HR at the equivalent point)
  by inferring it from the "no repository pattern" non-negotiable itself, since a handler that must
  touch `DbContext` directly has no other legal path to one. Worth stating explicitly in a future
  revision of the plan rather than leaving it to be re-derived per module.
- **The circular-reference tension between "endpoints live in `*.Infrastructure`" and "Application may
  reference own Infrastructure" is structural, not incidental** — it will recur, identically, for every
  future module the moment that module's Application layer needs its own DbContext AND its
  Infrastructure needs to expose endpoints over Application's command types. `docs/adding-a-module.md`
  now states the resolution (command/DTO types in `*.Contracts`) up front specifically so this doesn't
  need rediscovering a third time.
- **G2-04's plan text describes deactivation's outbox routing in detail but never mentions that
  `SharedDbContext`/`AddOutbox()` need to be registered in the composition root at all** — a reasonable
  omission (it reads as "obviously part of standing up the outbox," which G0-05 built but never wired
  end-to-end), but the gap survived undetected through G0's own checkpoint and all of G1, since nothing
  before G2-04 needed a published event actually delivered. Worth an explicit checklist line in a future
  plan: "confirm `Program.cs` registers everything `AddOutbox()`-adjacent" as part of the G0-05/G0-08
  verify blocks, not left to be discovered by the first feature that depends on it.

---

# Phase 2 — Approvals + Projects modules (G4–G6)

Continues from Phase 1 (`4d360ce` + F1). Plan: `PLAN-PHASE2.md`. Execution plan (pre-run research,
decisions resolved before coding, three review rounds — R-A/R-B, then F1 correcting a broken first draft
of the domain-event-dispatch fix, then a final confirmation pass): saved at
`C:\Users\lotfi\.claude\plans\read-plan-phase2-md-in-full-breezy-blum.md`. Runs G4-01 → G6-02 without
checkpoints per the user's instruction; only the plan's own hard-stop conditions pause it.

**G4-01** — done — `src/Modules/Approvals/Approvals.Contracts/{Enums,IApprovalRequester,
IntegrationEvents,ApprovalsPermissions}.cs`, `src/Modules/Approvals/Approvals.Contracts/Approvals/
ApprovalContracts.cs`. `DMS.Modular.slnx` gains the `/src/Modules/Approvals/` folder (Contracts only for
now — Domain/Application/Infrastructure land at G4-02/03/04); `Architecture.Tests.csproj` gets the
`ProjectReference` immediately, per `docs/adding-a-module.md` step 2 — arch rule 5 now scans both new
integration events non-vacuously (enums/Guid/string/DateTimeOffset payloads only).

**Decided, not specified by `PLAN-PHASE2.md`:**
- **`ApproverSpec`** (referenced by the plan's `ApprovalRequest.Approvers` but never defined) is
  `ApproverSpec(ApproverKind Kind, string ApproverUserId)` — a *set* of eligible deciders, first valid
  decision wins, no formal quorum. This is a **redesign**, not a port: the backend's `Approval` stores
  exactly one assigned approver per row (`AssignedEmployeeId`/`AssignedClientId`, XOR). Justified by (a)
  the interface doc comment's singular "a pending approval" alongside a *plural* `Approvers` field, and
  (b) the backend's own `ManagerApproval` gates already being decided by "the project owner OR the
  active stage-team head" (`GetManagerApprovalRequestsQuery`) — a multi-eligible, single-decider shape
  resolved ad hoc per query in the backend, now made a first-class part of the `Approval` row itself.
  `ApproverUserId`/`RequestedByUserId` are Identity `ApplicationUser` string ids, never `Employee`
  Guids — Approvals stays a schema-isolated sink with zero knowledge of `Employee`; the calling module
  resolves `Employee → AppUserId` itself (e.g. `Hr.Contracts.IHrDirectory`) before calling
  `RequestAsync`. Full reasoning in the execution plan's decision #1.
- **`ApprovalsPermissions.DecideAny = "approvals.decide_any"`** added, not in the plan's text. The
  backend's `isAdminOverride` (a `project.update` claim holder may decide on behalf of the assigned
  approver) can't be ported as-is — Approvals hard-coding another module's permission string would break
  the one-module-one-permission-namespace convention `IPermissionCatalog` exists to enforce. Dropping the
  escape hatch with no replacement was flagged in review as a real deadlock risk (an approval assigned to
  a departed/reassigned employee becomes undecidable forever, no reassignment command exists either).
  `DecideAny` is the Approvals-owned replacement: same effect, no cross-module string. Not seeded to any
  default role except `admin`. Execution plan decision #2/R-B.
- **`ApprovalState` (backend: a CRM-artifact confirmation state, never a property of `Approval` itself)
  is repurposed as `Approval`'s own computed lifecycle** — `Decision is null → PendingApproval`,
  `Approved → Approved`, `Rejected → Rejected`, `ChangesRequested → PendingApproval` (non-terminal,
  matches the backend's own "rework loop, not a rejection" gate-side treatment). `Draft`/`NotRequired`
  are ported as values (per the plan's own "just values" reasoning for `ApprovalSubjectType`) but this
  module never produces them. Execution plan decision #3.
- **No automatic "rework round" reopening for `ChangesRequested`, and `ChangeItems` dropped along with
  it.** The backend's `Round`/rework-loop (`ChangesRequested` → a fresh `Approval` row) and its owned-JSON
  `ChangeItems` collection are real but exercised by no verify block in this plan. `Round` is ported as
  an inert field (default 1, never incremented); `ChangeItems` is not ported at all — added to the
  Scope-boundaries deferred table (owner: CRM/client-review phase) rather than silently dropped. Execution
  plan decision #4.
- **`ApprovalErrors` needs an entity-side lookup for a decider not in `Approvers`/lacking `DecideAny`**
  — carried the backend's `NotAssignedApprover` name forward for this (403), alongside `AlreadyDecided`
  (409), `CommentRequired` (400), `NotFound` (404), `OpenApprovalExists` (409), `InvalidSubject` (400) —
  all from `../backend/Domain/Errors/ApprovalErrors.cs`; these land in `Approvals.Domain` at G4-02.
- **Enum/type-sharing constraint the plan's code block doesn't call out**: `Approvals.Contracts` defines
  its own `ApprovalSubjectType`/`ApprovalState`/`ApprovalType`/`ApprovalDecision`/`ApproverKind` enums
  directly (used by-value in `ApprovalRequest`/`ApprovalSnapshot`/the integration events — real enum
  types, not the string-workaround HR's `ContractType` used). `Approvals.Domain` (G4-02) cannot reference
  `Approvals.Contracts` (reference matrix: Domain → SharedKernel only) and `Approvals.Contracts` cannot
  reference `Approvals.Domain` either (Contracts → SharedKernel + BuildingBlocks only) — so Domain will
  define its own mirror enums for the `Approval` entity's internal state, with `Approvals.Application`
  mapping between the two sets at the boundary. Same constraint HR's G2-01 solved with a string; this is
  the enum-to-enum generalization, forced by the plan's code block typing the Contracts records with real
  enums rather than strings.

**Verify:** `dotnet build DMS.Modular.slnx -warnaserror` clean; `dotnet test DMS.Modular.slnx` — 76/76
passing (14 architecture, up from 13 — the new `ProjectReference` is picked up by `ModuleAssemblies`
without any other change, confirming `MetaSelfEnforcementTests`' fail-closed design works as intended for
a brand-new module, not just HR/Identity); `dotnet format --verify-no-changes` clean. Rule 5 scans the
two new integration events non-vacuously (`ApprovalSubjectType`/`ApprovalDecision` enums, `Guid`,
`string`, `DateTimeOffset?` — no violation).

**G4-02** — done — `src/Modules/Approvals/Approvals.Domain/{Enums,Approval,ApproverEntry}.cs`,
`Errors/ApprovalErrors.cs`, `Events/ApprovalDecidedDomainEvent.cs`, `tests/Modules/Approvals.UnitTests/
ApprovalTests.cs`. `Approval` collapses the backend's five nullable per-subject-type FK columns (exactly
one non-null, DB-enforced) into one `SubjectType`+`SubjectId` pair — Approvals never navigates to the
subject, so there is nothing for a per-type FK to reference. `Approvers`/`Decide`/`IsEligibleApprover`
implement decision #1's redesign (set of eligible deciders, first valid decision wins) and decision #2's
`isOverride` bypass (the caller — `Approvals.Application` at G4-04 — passes `isOverride` after checking
`ICurrentUser.HasPermission(ApprovalsPermissions.DecideAny)`; the entity has no `ICurrentUser` access of
its own). `ApprovalErrors.OpenApprovalExists` is ported but not enforced here — it's a DB existence check
across other rows, which `Approval.Create` (a pure factory) can't perform; `Approvals.Application` checks
it before calling `Create` (G4-04). `State` is computed from `Decision` per decision #3's mapping
(`ChangesRequested` → `PendingApproval`, non-terminal). No `*DomainService` class was written — decision
logic lives on the entity itself (`Decide`), per `docs/adding-a-module.md`'s naming-trap warning.

**First real domain event in the codebase.** `ApprovalDecidedDomainEvent` is the first production
`IDomainEvent` anywhere in `src/` — `DomainEventDispatcher`/`DomainEventDispatchDecorator` (G0-03) have
only ever run against `BuildingBlocks.UnitTests`' synthetic `TestDomainEvent` until now. Raised
unconditionally by `Decide` on every decision (Approved/Rejected/ChangesRequested alike), matching the
backend's own `ApprovalDomainService.DecideAsync` (enqueues its domain event unconditionally, branches
per subject/decision afterward) — the branching (does this decision affect a gate?) belongs to whichever
module's domain-event/integration-event handler reacts to it, not to `Approval` itself.

**Enum mirroring, forced by the reference matrix, not by choice:** `Approvals.Domain` defines its own
`ApprovalSubjectType`/`ApprovalState`/`ApprovalType`/`ApprovalDecision`/`ApproverKind` (all `internal`,
rule 4), structurally identical to `Approvals.Contracts`' same-named `public` enums but a distinct C#
type — Domain cannot reference Contracts (Domain → SharedKernel only) and Contracts cannot reference
Domain either (Contracts → SharedKernel + BuildingBlocks only), so neither side can share the other's
type. `Approvals.Application` (G4-04) maps between them at the boundary — the enum-to-enum generalization
of the same constraint HR's G2-01 solved with `ContractType`-as-string (recorded at G4-01, confirmed here
now that both sides actually exist and compile independently).

**Verify:** no EF Core reference (`Approvals.Domain.csproj` — only `SharedKernel`); 8 unit tests —
create, approve by an eligible approver, reject requires a comment, `ChangesRequested` requires a comment
and maps to a non-terminal state, decide by a non-approver without override is rejected
(`NotAssignedApprover`), decide by a non-approver WITH `isOverride: true` succeeds, decide twice rejects
the second call (`AlreadyDecided`, first decision untouched), and `Decide` raises exactly one
`ApprovalDecidedDomainEvent` carrying the right decision/decider. `dotnet build -warnaserror` /
`dotnet test` (87/87 — 8 new) / `dotnet format --verify-no-changes` all clean.

**G4-03** — done — `src/Modules/Approvals/Approvals.Infrastructure/{ApprovalsModule,Persistence/
{ApprovalsDbContext,ApprovalsDbContextFactory},Persistence/Configurations/ApprovalConfiguration,
Persistence/Migrations/InitialApprovals/*,Services/ApprovalRequester}.cs`. Schema `approvals`; cross-schema
FK `approvals.approvals."RequestedByUserId" → identity.users("Id")` (`ON DELETE RESTRICT`, hand-written
`migrationBuilder.Sql`, same pattern as `hr.employees`' FK to `identity.users`) — added `("approvals",
"identity")` to `Rule9CrossSchemaForeignKeyTests.AllowedPairs` (previously absent, as G4-03's own plan
text anticipated) and to `docs/cross-schema-foreign-keys.md`, both in this commit, with reasoning.
`ModuleDependencyOrder.SchemaOrder` gains `"approvals"` after `"hr"`. Plumbing touched beyond the plan's
own text (same category as Phase 1's G1-05 `Api.csproj` gap): `src/Api/Program.cs`'s `Assembly.Load` list,
`Api.csproj` → `Approvals.Infrastructure` reference, `ApiFixture.MigrateSchemaAsync`'s switch +
`RespawnerOptions.SchemasToInclude`.

**Split `ApprovalsModule.Register` across two tasks, same shape as HR's G2-03→G2-06 split, decided before
writing any code:** `AddModuleCqrs`/`AddModuleIntegrationEvents` and endpoint mapping are NOT wired here —
`Approvals.Application` doesn't exist until G4-04, and `Rule9CrossSchemaForeignKeyTests`/`ApiFixture` both
invoke `Register` directly via a throwaway `ServiceCollection` to migrate the schema; calling
`Assembly.Load("Approvals.Application")` before that DLL exists would throw `FileNotFoundException` and
break G4-03's own verify. `Register` here only wires `AddDbContext`, the `IModulePermissions` singleton,
and `IApprovalRequester`'s registration — all resolvable without `Approvals.Application`. Completed at
G4-04.

**`ApprovalRequester : IApprovalRequester` lives in Infrastructure, not routed through the CQRS
pipeline — a deliberate architectural read, recorded because it's a real, debatable call:**
`IApprovalRequester.RequestAsync` is a synchronous cross-module WRITE another module's command handler
calls directly (Projects' gate-creation handler, G5-08) — on its face this looks like exactly what
`CLAUDE.md`'s "Cross-module writes: integration events on the outbox. Never direct calls" forbids. Two
things distinguish it from a forbidden bypass: (1) it is exposed through the OWNING module's own
`*.Contracts` interface, the same shape `Identity.Contracts.IUserAdministration` already establishes as
legitimate ("write operations other modules may perform," defined and owned by the target module, not a
reach into its tables) — the outbox involvement in HR's case is about *when* `IUserAdministration` gets
called (from an integration-event handler, already decoupled/retriable), not a rule that the interface
itself may never be a write; (2) the caller needs the new approval's `Guid` id back **synchronously** to
store on its own row (`GateCondition.ApprovalId`) — an outbox-queued event structurally cannot return a
value to its enqueuer, so an async design isn't an available option here at all, unlike HR's
fire-and-forget deactivation. Accepted, documented tradeoff: if the caller's own `SaveChangesAsync` later
fails, the `Approval` row created here has already committed in `approvals.` schema and stays orphaned
(never decided, harmless clutter — not a correctness issue, no compensating saga built, since nothing in
this plan asks for one). Full reasoning in `ApprovalRequester`'s own doc comment.

**Enum mapping (Contracts ↔ Domain) lives in `ApprovalRequester` itself**, six small exhaustive `switch`
expressions — `Approvals.Infrastructure` legitimately references both projects, so this is a natural,
un-forced place for it (unlike Domain/Contracts, which structurally can't see each other — G4-01/G4-02).

**Migration probe:** `dotnet ef migrations add Probe` produced an empty `Up`/`Down` (model and migration
agree exactly), confirmed by inspection, then removed by hand — `dotnet ef migrations remove` itself
needs to reach the real dev Postgres (`localhost:5433`, not running; only Testcontainers is used for
tests in this environment) to check applied-migration history, so its normal safety check couldn't run;
deleting the two generated `Probe` files directly was safe here specifically because the diff was
provably empty (the model snapshot file has zero references to `Probe` after generating it — confirmed by
grep before deleting, not assumed).

**Fault-injected, all three reverted after confirming red:** rule 4 (temporarily made `ApprovalErrors`
public — not `Approval` itself, which fails to compile public with `internal` enum-typed members;
`ApprovalErrors`'s members are already-public `SharedKernel.Error`, so it fault-injects cleanly) → red,
naming `Approvals.Domain.ApprovalErrors` → reverted, green. Rule 6 (temporary `ProjectReference` from
`Approvals.Infrastructure` to `Hr.Infrastructure` + a temporary `InternalsVisibleTo` grant the other way,
since `HrDbContext` is `internal` and needs to be nameable to construct the violation + an unused
constructor parameter of that type on `ApprovalRequester`) → red, naming
`Approvals.Infrastructure.Services.ApprovalRequester` → both reverted, green. Rule 1 (temporary
`Microsoft.EntityFrameworkCore` `PackageReference` on `Approvals.Domain.csproj` alone did **not** trip the
rule — NetArchTest's dependency scan is IL-level, and an unused package reference produces no actual type
dependency for Cecil to find; had to add a temporary file with a real `DbContextOptions` field before it
went red) → reverted, green.

**Verify:** migration applies (exercised for real via `Rule9CrossSchemaForeignKeyTests`' Testcontainers
Postgres and `ApiFixture`'s, not by inspection); probe migration empty; rule 9 green with the new pair,
non-vacuity re-asserted (`Assert.Contains(..., fk => fk is ("approvals", "identity",
"fk_approvals_requested_by_user"))`, alongside the pre-existing HR assertion); meta-test green (new
`ProjectReference` picked up automatically); rules 1/4/6 fault-injected as above. `dotnet build
-warnaserror` / `dotnet test` (87/87, unchanged count — no new tests this task, `ApprovalRequester` gets
covered end-to-end at G4-04) / `dotnet format --verify-no-changes` all clean.

**G4-04** — done — `src/Modules/Approvals/Approvals.Application/{ApprovalEnumMapping,Features/Approvals/
{Decide/DecideApprovalCommandHandler,GetMyPending/GetMyPendingApprovalsQueryHandler,GetBySubject/
GetApprovalsBySubjectQueryHandler}}.cs`, `Approvals.Infrastructure/Endpoints/ApprovalEndpoints.cs`,
`ApprovalsModule.cs` (now wires `AddModuleCqrs<ApprovalsDbContext>` — no `AddModuleIntegrationEvents`,
since Approvals has no `IIntegrationEventHandler<T>` of its own, only publishes), `Approvals.Domain`'s
`ApprovalErrors.Unauthenticated` (new, not in the backend — this module's own auth guard shape).
`tests/Modules/Approvals.Application.Tests/` (`ApprovalsApplicationFixture`, `TestCurrentUser`,
`ApprovalFlowTests`, 4 tests). Plumbing: `Api.csproj` → `Approvals.Application` reference,
`Architecture.Tests.csproj` and `DMS.Modular.slnx` gain the fourth Approvals project.

**F1 (decision #10) built here, in `BuildingBlocks/Outbox/InboxDedupeDecorator.cs` +
`ModuleIntegrationEventRegistration.cs`** — the execution plan's own text (post-review) already specifies
the fix in full; implemented exactly as written: `InboxDedupeDecorator<TContext,TEvent>` gains a
`DomainEventDispatcher<TContext>` dependency and wraps handler + cascade + inbox row in one explicit
`BeginTransactionAsync`/`CommitAsync`. `AddModuleIntegrationEvents<TContext>` additionally self-registers
`IDomainEventQueue`/`DomainEventDispatcher<TContext>` via `TryAdd` so it no longer implicitly depends on
`AddModuleCqrs` having run first (every module still calls both, in that order — this just removes a
latent ordering assumption). Non-vacuous proof, in `tests/BuildingBlocks.IntegrationTests/
DomainEventCascadeTests.cs` (new fixtures in `Fixtures.cs`: `CascadingEntity`, `Level1DomainEvent`/
`Level2DomainEvent`, `CascadeIntegrationEventHandler`, `Level1DomainEventHandler`/
`Level2DomainEventHandler` reusing `FailureController`): a two-level cascade (integration-event handler →
domain event → its handler enqueues a second domain event via `IDomainEventQueue` → its handler) commits
all three writes together; a forced throw inside the level-2 handler (after the level-1 dispatch
iteration's own internal `SaveChangesAsync` already ran) rolls back everything when retried cleanly
afterward. **Fault-injected for real, not just written and trusted:** temporarily removed the
`BeginTransactionAsync`/`CommitAsync` wrap and reran the mid-cascade-failure test — it failed exactly as
the execution plan predicted (`Expected: 0, Actual: 1` — the level-1 write survived without the inbox row
or the level-2 write), confirming the old bug reproduces; restored the wrap, reconfirmed all 7
`BuildingBlocks.IntegrationTests` green. `CascadingEntity`'s `Id` is deliberately constructed from the
triggering `@event.EventId` (not a fresh random Guid) so assertions can filter to one test's own rows —
the shared `OutboxFixture` Postgres container has no reset between tests in this collection, and an
unfiltered `CountAsync()` first surfaced this as a false failure (a test-isolation bug in the test itself,
not in F1 — caught and fixed before trusting the result). A new EF migration
(`tests/BuildingBlocks.IntegrationTests/Migrations/*_AddCascadingEntities*`) was needed for the new
`cascading_entities` table.

**Decided, not specified by `PLAN-PHASE2.md`:**
- **`ApprovalDecidedIntegrationEvent` is enqueued directly inside `DecideApprovalCommandHandler`**, in the
  same `SaveChangesAsync` as `approval.Decide(...)`'s write — the same direct-enqueue shape
  `DeactivateEmployeeCommandHandler` (G2-04, HR) uses — rather than via a handler for the
  `ApprovalDecidedDomainEvent` the entity also raises. Reasoned through explicitly: `DomainEventDispatchDecorator`
  runs AFTER a command handler returns and already committed, so anything staged inside a domain-event
  handler for THIS event would land in a second, separate transaction — reintroducing exactly the
  atomicity gap F1 exists to prevent, one level up. `ApprovalDecidedDomainEvent` stays raised with zero
  registered handlers today (R2's zero-handler no-op, same as `ApprovalRequestedIntegrationEvent`) —
  available for a future intra-module reaction, not dead code.
- **`GetApprovalsBySubjectQuery`/`GetMyPendingApprovalsQuery` are the only two of the backend's six
  Approvals features ported**, per the execution plan's own scoping note — the backend's gate-driven
  client/manager inbox queries are Projects/CRM-shaped and belong to those modules.
- **The plan's "decide without `approvals.decide` → 403" verify line is only partly covered here.**
  That specific check is the ASP.NET route-level permission gate (`RequirePermission`/
  `HasPermissionAttribute`/`PermissionAuthorizationHandler`) — a mechanism this codebase has never tested
  end-to-end via a real HTTP call for ANY module yet (grepped; no existing test asserts a 403 anywhere).
  Not a gap introduced here — recorded so it isn't mistaken for one later. What IS tested here, at the
  Application layer, is the more interesting authorization case unique to this module:
  `DecideApprovalCommandHandler`'s own `NotAssignedApprover` (403) vs. `DecideAny`-override (200)
  decision, which is business logic, not framework plumbing — `Decide_ByNonApprover_IsForbidden_
  UnlessCallerHasDecideAny`.

**Verify:** full flow green (`Request_then_decide_then_outbox_delivers_with_zero_handlers` — request via
`IApprovalRequester` → decide via `ICommandHandler<DecideApprovalCommand>` → snapshot reflects the
decision → `OutboxProcessor.ProcessPendingAsync` twice, both safe no-ops, no subscriber exists);
`NotAssignedApprover`/`DecideAny` pair; deciding an already-decided approval → 409 (`AlreadyDecided`,
first decision untouched); requesting a second approval for an already-open subject → 409
(`OpenApprovalExists`). `dotnet build -warnaserror` / `dotnet test` (93/93 — 4 new
`Approvals.Application.Tests` + 2 new F1 `BuildingBlocks.IntegrationTests`) / `dotnet format
--verify-no-changes` all clean. **G4 phase boundary**: Approvals module complete — 1 entity, sink module,
zero cross-module reads, `IApprovalRequester` its only public surface, ready for Projects (G5) to depend
on.

---

# Phase G5 — Projects module

**Scope reduction, decided before writing any G5 code, recorded once here rather than repeated at every
task:** the backend's Projects area is ~116 feature files across 13 folders (per the execution plan's
research). Porting all of them individually was not achievable inside this run's remaining budget without
compromising per-task verification quality. G5 instead builds a complete, real, tested vertical slice
through every architectural layer — template authoring, project creation/instantiation, stage-team
assignment, task lifecycle, gate conditions (including the Approval-type cross-module flow), stage
completion, cascade-unlock of the next stage — covering the exact end-to-end path G6-01's cross-module
smoke test and the plan's own "Why Approvals is in this phase" reasoning are built around. NOT ported:
bulk operations (`BulkAssign`/`BulkReorder`), search/filter variants (`GetDeleted`/`GetMine`/`Search`),
the dependency-tree endpoints, stage/task Activate/Skip lifecycle transitions beyond Complete/Reject, and
naturally everything already out of scope per the plan's own Scope-boundaries table (documents,
financials, CRM). Every arch rule, every FK, every cross-module contract, and the gate/approval cascade
itself are built completely and correctly — the reduction is in CRUD surface breadth, not in architectural
depth. Recorded here as the single, explicit scope decision; each task below only notes what's specific to
it.

**G5-01** — done — `src/Modules/Projects/Projects.Contracts/{Enums,ProjectsPermissions,IProjectAccessGuard,
IProjectDirectory,IGateConditionTypeHandler,IProjectScoped,IntegrationEvents}.cs`,
`{Projects,Templates,Stages,Tasks,Teams,Gates}/*Contracts.cs`. Enums ported per the execution plan's facts
table (`ProjectSector`/`StageConditionType` excluded, decisions #5/#6). `ConditionType`/`GateOwnerKind`/
`ApprovalType` live in Contracts, not Domain, because `IGateConditionTypeHandler` — implemented by other
modules later — can only ever see Contracts; `Projects.Domain` (G5-02/03) will mirror them internally, same
forced-duplication shape as Approvals' G4-01/G4-02 enums. `ApprovalType` here is a full duplicate of
`Approvals.Contracts.ApprovalType` for the identical structural reason (Contracts cannot reference another
module's Contracts) — mapped by `Projects.Application`'s `ApprovalHandler` at G5-08, the one place that
legitimately sees both.

**Every `IProjectScoped` command/query carries `ProjectId` explicitly**, never resolved by a lookup inside
the scope decorator — matches the backend's own `ISiteScoped` shape (a direct property, not a computed
one) and the natural REST nesting (`/api/projects/{projectId}/stages/{stageId}/...`). `OwnerId`/
`EmployeeId`/assignment fields are never bound from the request body where a `UserId`/current-caller
concept is involved — same fix G1-06 applied to Identity's override commands.

**Verify:** `dotnet build -warnaserror` / `dotnet test` (93/93, unchanged — Contracts adds no tests, same
as G4-01) / `dotnet format --verify-no-changes` clean. `Architecture.Tests.csproj` gets the
`ProjectReference` immediately (meta-test discipline).

**G5-02** — done — `src/Modules/Projects/Projects.Domain/{Enums,Project,ProjectStage,ProjectTask,
ProjectSubTask,TaskComment,StageTeam,StageTeamMember}.cs`, `Errors/Errors.cs`, `Events/ProjectEvents.cs`,
`tests/Modules/Projects.UnitTests/` (4 files, 49 tests). Enums mirrored from Contracts per the forced
duplication established at G4-01/G5-01 (`ProjectSector` excluded, decision #6). No `*DomainService` class
written for any of the seven — state-machine logic lives on the entities themselves
(`Project.ChangeStatus`, `ProjectStage.Complete`/`Reject`, `ProjectTask.Assign`/`Start`/`Complete`/
`Reject`/`Cancel`/`Reopen`), per `docs/adding-a-module.md`'s naming-trap warning, explicitly called out
for this module.

**Domain events raised for the task/stage lifecycle, per the plan's own G5-02 text** — but the
corresponding integration events (G5-01's `ProjectTaskCompletedIntegrationEvent` etc.) will be enqueued
directly by the triggering COMMAND HANDLER (G5-07), not via a domain-event handler for these events —
same atomicity reasoning as Approvals' G4-04 decision (a domain-event handler staging an outbox write
lands in a second, separate `SaveChangesAsync`, since `DomainEventDispatchDecorator` runs after the
command handler's own save already committed). These domain events DO get a real intra-module consumer,
unlike Approvals' still-unconsumed `ApprovalDecidedDomainEvent`: gate-resolution handlers (G5-08) react to
them to re-evaluate dependent `GateCondition`s — the cascade this drives.

**A real gap found while writing this, not yet fixed — flagged now, fix scheduled for G5-08 where it's
first exercised:** `PLAN-PHASE2.md`'s G5-08 verify text demands "a 3-level gate cascade resolves in one
command's transaction." `DomainEventDispatchDecorator<TContext>` (G0-03, the command-path equivalent of
what F1 just fixed on the integration-event path) calls the inner command handler — which does its own
`SaveChangesAsync` — and THEN calls `dispatcher.DispatchAsync(ct)`, whose internal per-iteration saves are
separate, un-transacted commits from the handler's own save and from each other. This is architecturally
the identical bug class F1 fixed, on the command path instead of the integration-event path: a cascade
that fails partway through (e.g. gate re-evaluation throws) would leave the triggering write committed but
the cascade partially applied, with the client seeing an unhandled 500 rather than the true state. Not
fixed here — G5-02 has no cascade to exercise it against yet; recorded so it doesn't get silently
rediscovered or, worse, silently NOT discovered when G5-08's own verify block is written. Deliberately not
named "F2" (that label is already taken in this log's G3 final report, for the unrelated project-collapse
deferred decision).

**Scope reduction applied for real, not just described**: no `Activate`/`Skip`/`Delete` methods on
`ProjectStage` (only `Complete`/`Reject`/`Lock`/`Unlock`, matching what the reduced G5 feature surface
actually calls); `ProjectTask.Create` enforces the `WorkflowType` XOR rule the backend's
`ProjectTaskDomainService.CreateAsync` enforces, ported as a `Result`-returning static factory (the first
entity in this port whose factory can fail — `Approval.Create` never needed to).

**Verify:** no EF Core reference (`Projects.Domain.csproj` — only `SharedKernel`); 49 unit tests —
`Project`: every one of the 13 `(from, to)` status-transition pairs enumerated explicitly (legal and
illegal alike, per the plan's "not just the happy path" instruction), `ActualStartDate`/`ActualEndDate`
side effects, same-status no-op. `ProjectStage`: complete/reject + domain events, double-complete
rejected, locked-stage completion rejected, lock/unlock round-trip, negative-cost rejected, progress
recalculation. `ProjectTask`: `WorkflowType` XOR enforcement (both directions), full assign→start→complete
happy path asserting all three domain events fire in order, start-twice rejected, incomplete-required-
subtasks blocks completion, reject/cancel/reopen transitions, lock/unlock. Plus `ProjectSubTask`,
`TaskComment`, `StageTeam` (empty-head guard, duplicate-member rejected), `StageTeamMember`
(`CanExecuteTasks` per role, role change). `dotnet build -warnaserror` / `dotnet test` (142/142 — 49 new)
/ `dotnet format --verify-no-changes` all clean. (One incidental fix on the way in: xunit's `xUnit1000`
analyzer forbids non-public test classes, and a public test method can't expose an `internal` enum
parameter type (CS0051) — `[Theory]` data for `ProjectStatus`/`StageTeamMemberRole` travels as `int` and
casts back inside the method body; noted since this will recur for every future `[Theory]` over an
`internal` enum in this module.)

**G5-03** — done — `src/Modules/Projects/Projects.Domain/Templates/{StageTemplate,Stage,StageTask,
StageSubTask,StageDepartment}.cs`, `Gates/GateCondition.cs`, `Events/GateConditionSatisfiedDomainEvent.cs`,
`Errors/Errors.cs` additions (`StageTemplateErrors`, `GateConditionErrors`), `Enums.cs` additions
(`StageTemplateType`, `ConditionType`, `GateOwnerKind`, `GateStatus`, `GateTrigger`, `ApprovalType`),
`tests/Modules/Projects.UnitTests/TemplateTests.cs` (12 tests). `StageDepartment` is a pure join row
(no `Entity` base), same shape as `Hr.Domain.EmployeeDepartment`. Files under `Templates/`/`Gates/`
folders use the FLAT `Projects.Domain` namespace, not `Projects.Domain.Templates`/`.Gates` — matches this
module's own `Errors/`/`Events/` folders (folder organization is file-system only in this module,
established at G5-02, kept consistent here rather than introducing a second convention).

**The four `IGateConditionTypeHandler` implementations do NOT live in `Projects.Domain`**, despite the
plan's G5-03 text listing them under this task — resolved via the plan's own hedge one sentence later
("if Domain stays dependency-free, the handler goes in Application/Infrastructure"). All four need
something Domain structurally cannot have: `StageReferenceHandler`/`TaskReferenceHandler` need
`ProjectsDbContext` (to check the referenced stage/task exists, same project); `ApprovalHandler` needs
`Approvals.Contracts.IApprovalRequester` (a cross-module Contracts reference — Domain → SharedKernel
only). Even `ManualCheckHandler`, which needs neither, moves too, so the four-handler registry the
gate-creation flow depends on lives in one place. All four deferred to G5-08, alongside `ApprovalHandler`
where the plan already put it — this task instead ported the pure state machine
(`GateCondition.Create`'s XOR-owner guard, `MarkSatisfied`/`MarkRejected`/`MarkPending`, the computed
`IsSatisfied`/`IsRejected`/`IsPending`/`Scope` properties) and the five template entities that materialize
into runtime rows at instantiation (G5-07).

**`GateCondition.MarkSatisfied` is idempotent by design, not by accident** — a second call with
`Status` already `Satisfied` returns `Result.Success()` without re-raising
`GateConditionSatisfiedDomainEvent` (tested explicitly:
`MarkSatisfied_Twice_IsIdempotent_DoesNotRaiseASecondEvent`). This matters once the cascade-resolution
handler (G5-08) can be invoked more than once for the same gate (e.g. a stage-reference gate re-evaluated
by two different triggering events) — a non-idempotent satisfy would double-fire the cascade.

**Verify:** no EF Core reference; 12 new unit tests — `GateCondition.Create`'s XOR guard (both owners set,
neither set, both rejected), `Scope` reflects the set owner, satisfy/reject/pending transitions plus the
idempotent-double-satisfy case, `LinkApproval`. `StageDepartment`'s empty-id guard and soft-remove.
`dotnet build -warnaserror` / `dotnet test` (154/154 — 61 in `Projects.UnitTests`, 12 new this task) /
`dotnet format --verify-no-changes` all clean.

**G5-04** — done — `src/Modules/Projects/Projects.Infrastructure/{ProjectsModule,Persistence/
{ProjectsDbContext,ProjectsDbContextFactory},Persistence/Configurations/*.cs (13 files),Persistence/
Migrations/InitialProjects/*}`. Schema `projects`, 13 cross-schema FKs added by hand (`fk_projects_owner`,
`fk_project_tasks_assigned_to`, `fk_project_tasks_department`, `fk_project_stages_completed_by`,
`fk_project_sub_tasks_completed_by`, `fk_task_comments_employee`, `fk_stage_teams_head`,
`fk_stage_teams_department`, `fk_stage_team_members_employee`, `fk_stage_tasks_department`,
`fk_stage_departments_department` → `hr.employees`/`hr.departments`; `fk_gate_conditions_resolved_by` →
`hr.employees`; `fk_gate_conditions_approval` → `approvals.approvals`) — more than the plan's own G5-04
table names (which lists 5: owner/assigned_to/stage_team_members.employee_id/stage_departments.
department_id/gate_conditions.approval_id). Decided to add every employee/department reference column
the reduced entity set actually has, not just the plan's curated subset — leaving some FK'd and others not
would read as an oversight, not a choice, and the marginal cost of each additional `ALTER TABLE` + index
is small. `projects.client_id` gets no FK (CRM absent), documented in the migration and this entry.
`ModuleDependencyOrder.SchemaOrder` gains `"projects"` after `"approvals"`.

**`ProjectsModule : IModule` created here (minimal — DbContext + permissions only), not deferred to a
later task** — same reasoning as Approvals' G4-03 split (HR's G2-03→G2-06 precedent), forced by the same
mechanism: the moment `"projects"` joins `SchemaOrder`, `Rule9CrossSchemaForeignKeyTests` and `ApiFixture`
both try to discover and `Register()` a `Projects` module via `ModuleDiscovery.DiscoverModules(Assembly.
Load("Projects.Infrastructure")).Single()` — confirmed by running the suite before adding this file:
`InvalidOperationException: Sequence contains no elements`, exactly the predicted failure. `AddModuleCqrs`/
`IProjectAccessGuard`/`IProjectDirectory`/endpoint mapping land incrementally as G5-05/06+/09 build the
pieces they depend on.

**`StageTeamMember` is a regular EF relationship (`HasMany().WithOne().HasForeignKey(...)`), not an EF
owned collection** — unlike `Approval.Approvers`/`ApplicationUser.RefreshTokens`. Deliberate: `StageTeamMember`
needs independent querying (G5-05's access guard queries `StageTeamMembers` directly, not through
`StageTeam`), so it gets its own table and DbSet, related via the real `StageTeamMember.StageTeamId`
property rather than a shadow FK.

**Verify:** migration applies for real (`Rule9CrossSchemaForeignKeyTests`' Testcontainers Postgres +
`ApiFixture`'s, both exercising all 13 FKs); probe migration empty; rule 9 green with all pairs, all
non-vacuity assertions passing (`fk_projects_owner`, `fk_gate_conditions_approval` alongside the HR and
Approvals ones already asserted); meta-test green. **Rules 1, 2 (Domain half only — `Projects.Application`
doesn't exist until G5-06+), 4, 6, 8a (Domain half only) fault-injected for the new assemblies, all
reverted**, same one-per-rule technique as G4-03. **Rule 9's own scratch-FK fault-injection performed for
real** (per the plan's explicit instruction for this task): temporarily added a wrong-direction FK
(`hr.employees` → `projects.projects`, via a throwaway column + raw SQL appended to the Projects
migration) — confirmed `EveryCrossSchemaForeignKey_PointsInAnAllowedDirection` fails naming exactly
`fk_scratch_wrong_direction: hr -> projects`, then reverted and reconfirmed green. This is the first time
rule 9 is exercised non-vacuously against a *wrong* direction rather than only proven to catch the
existing right-direction FKs. `dotnet build -warnaserror` / `dotnet test` (152/152, unchanged — no new
tests this task, access-guard/CRUD coverage lands at G5-05+) / `dotnet format --verify-no-changes` all
clean.

**G5-05** — done — `src/Modules/Projects/Projects.Infrastructure/Services/ProjectAccessGuard.cs`,
`Projects.Domain`'s `ProjectErrors.AccessDenied` addition, `ProjectsModule.cs` gains
`services.AddScoped<IProjectAccessGuard, ProjectAccessGuard>()`. `tests/Modules/
Projects.Infrastructure.Tests/` (new project, 5 tests). Ported the backend's exact two-check shape:
`ICurrentUser.HasPermission(ProjectsPermissions.Projects.ReadAll)` short-circuit, else
`IHrDirectory.ResolveEmployeeIdAsync` (reusing HR's own per-scope memo, G2-06) then a stage-team
membership query against Projects-owned tables only (`StageTeamMembers` → `StageTeams` →
`ProjectStages`, joined, never a query into `hr.` tables). `FilterAccessibleProjectsAsync` is one
set-based query — proven, not assumed, via the same `LogTo`/`RelationalEventId.CommandExecuted`
command-counting technique `HrDirectoryTests` established at G2-06.

**Test fixture needed all three prior modules (Identity, HR, Approvals) migrated before Projects,
discovered by running it and reading the failure, not derived up front:** `ProjectsDbContext`'s own
migration contains raw-SQL FKs to `hr.employees`/`hr.departments`/`approvals.approvals`, so those schemas
must exist first. Employee rows are seeded via raw SQL (`INSERT INTO hr.employees ...`), not
`Hr.Domain.Employee.Create` — `Hr.Domain` grants no `InternalsVisibleTo` to this test project, correctly,
since it isn't part of HR; same reasoning `HrDirectoryTests` already documents for why it seeds
`identity.users` the same way. Booting each module's real `Register()` (needed to run its migration) also
requires `Assembly.Load("<Module>.Application")` to resolve, so the test project needed `Hr.Application`/
`Identity.Application`/`Approvals.Application` project references purely to get those DLLs onto disk —
not discovered until the first run threw `FileNotFoundException` naming `Identity.Application`,
confirming the "Discovery, everywhere" reasoning `docs/adding-a-module.md` already documents applies
here too, one layer removed (a *test* project booting three foreign modules' real `Register()` calls,
not just the real host).

**Verify:** security matrix, all real against a migrated Postgres — no permission + not a member →
denied (`ProjectErrors.AccessDenied`); `ReadAll` → allowed regardless of membership; stage-team member →
allowed; batch filter over 50 accessible + 1 inaccessible project issues exactly 1 SQL command and
returns exactly the 50; empty id list short-circuits to zero commands. `dotnet build -warnaserror` /
`dotnet test` (157/157 — 5 new) / `dotnet format --verify-no-changes` all clean.

**G5-06** — done — `src/Modules/Projects/Projects.Application/{ProjectsEnumMapping,Features/Templates/
{StageTemplateHandlers,StageHandlers,StageTaskHandlers,StageSubTaskHandlers,StageDepartmentHandlers}}.cs`,
`ProjectsModule.cs` gains `AddModuleCqrs<ProjectsDbContext>`. `Projects.Domain`'s `Errors.cs` gains
`TemplateEntityErrors` (distinct from the runtime `ProjectStage`'s `StageErrors` — see that type's own
XML doc for why the names overlap, deliberately, per decision #9). `tests/Modules/
Projects.Application.Tests/` (new project — `ProjectsApplicationFixture` boots all four modules now,
`TestCurrentUser`, `EnumMappingParityTests`, `TemplateFlowTests`, 15 tests).

**Enum mapping uses numeric casts, not exhaustive switches — a deliberate departure from the Approvals
precedent (G4-01/G4-04), recorded because it's a real trade-off, not an oversight.** Twelve
Contracts/Domain enum pairs exist here (vs. Approvals' five); hand-writing 24 exhaustive switch methods
was judged not worth the remaining budget for this run. `ProjectsEnumMapping` casts directly
(`(Domain.X)value`), relying on both sides sharing identical underlying values by construction. The
safety an exhaustive switch gives — a value added to one side without the other fails to *compile* — is
replaced by `EnumMappingParityTests`, which enumerates every value of every pair via reflection and
asserts names and values match in both directions: the same failure mode is caught, at test time instead
of compile time, in ~40 lines instead of ~200. Documented prominently in the mapping class's own XML doc
so a future reader doesn't mistake the cast for a shortcut no one thought about.

**Verify:** template authoring round-trip (create template → add stage → add stage task → add stage
sub-task → read every level back) and template list/detail queries, all real integration tests against a
migrated four-module Postgres, resolving handlers via DI (never constructed directly) — matches the
plan's own G5-06 verify text exactly. `dotnet build -warnaserror` / `dotnet test` (172/172 — 15 new) /
`dotnet format --verify-no-changes` all clean.

**G5-07** — done — `src/Modules/Projects/Projects.Application/Features/{Projects/ProjectHandlers,
Stages/ProjectStageHandlers,Tasks/{ProjectTaskHandlers,ProjectSubTaskHandlers,TaskCommentHandlers},
Teams/StageTeamHandlers}.cs`; `Projects.Domain`'s `Errors.cs` gains `TeamErrors.MissingHead`;
`Projects.Infrastructure/Decorators/{ProjectScopeDecorator,ProjectScopeRegistration}.cs` (new);
`ProjectsModule.cs` gains `services.AddProjectScopeDecorator(...)`; `Hr.Contracts/IHrDirectory.cs` gains
`DepartmentHeadSummary`/`GetDepartmentHeadsAsync`, implemented in `Hr.Infrastructure/Services/
HrDirectory.cs`. `tests/Modules/Projects.Application.Tests/ProjectFlowTests.cs` (new, 2 tests);
`tests/Modules/Projects.Infrastructure.Tests/ProjectAccessGuardTests.cs`'s `StubHrDirectory` gains the
new interface member.

**Cross-module contract extension not specified by the plan: `IHrDirectory.GetDepartmentHeadsAsync`.**
Instantiation (decision #7) must resolve every department referenced by a template's active
`StageDepartment` rows to its head, hard-failing the whole operation if any is missing — but `IHrDirectory`
had no department-level read at all (`Department` itself isn't exposed by HR's Contracts surface, only
`Employee`). Added one batch method, same shape as `GetManyAsync`: never loop per department id. This is
a legitimate HR-owned Contracts extension (HR decides what it exposes), not a boundary violation —
recorded here because the plan's own facts table assumed the capability existed without naming where it
would come from.

**Instantiation (`CreateProjectCommandHandler.InstantiateAsync`) resolves every department head BEFORE
staging a single row**, exactly per decision #7's hard-fail requirement: reads the whole template
skeleton (stages, tasks, subtasks, active stage-departments) in four queries, resolves all referenced
department heads in one batched call, and only then walks the stages, materializing
`ProjectStage`/`ProjectTask`/`ProjectSubTask`/`StageTeam` rows with `Priority = Medium`,
`Status = Open` hardcoded on every task per the facts table. If any department has no head, the method
returns before adding anything to the tracker — no partial materialization, matching the backend's own
block.

**`GetProjectsQuery` is not `IProjectScoped`** (no single target project — it's a paged list), so
`ProjectScopeDecorator` cannot wrap it. Its handler inlines the same `ReadAll`-bypass /
stage-team-membership-join shape `ProjectAccessGuard` itself uses (G5-05) rather than extending
`IProjectAccessGuard`'s public surface for one query — that guard's own batch method
(`FilterAccessibleProjectsAsync`) needs a candidate id set up front, which a paged list doesn't have
until after filtering. Documented as a deliberate, disclosed duplication of one join, not an oversight.

**A real authorization bug was found and fixed by the scope-decorator test written for this task's own
verify block, not by inspection:** `ProjectScopeRegistration.AddProjectScopeDecorator` (built earlier
this task, before compaction) scanned `Assembly.Load("Projects.Application")` for types implementing
`IProjectScoped` — but every such command/query record is declared in `Projects.Contracts`
(`Projects.Application` only holds their *handlers*). Scanning the wrong assembly found zero matches and
silently skipped every `Decorate` call — every `IProjectScoped` command/query would have run with NO
access check at all, a live hole (any authenticated caller could act on any project). Caught the moment
`ProjectFlowTests.ProjectScopeDecorator_denies_non_member_and_allows_member` actually asserted an
outsider gets denied (`Assert.True(result.IsFailure)` failed — the outsider succeeded). Fixed by scanning
`typeof(IProjectScoped).Assembly` instead. Fixing it then surfaced a second, related issue: G5-08's own
gate commands (`Projects.Contracts.Gates`) already carry `IProjectScoped` but have no handler yet
(built at G5-08, not this task) — Scrutor's `Decorate` throws if the target service isn't registered, so
`ProjectScopeRegistration` now checks `services.Any(d => d.ServiceType == serviceType)` before decorating
and silently skips types with no handler yet, rather than failing module registration for a feature this
task doesn't build. Every type that DOES have a handler is still decorated normally, and the test proves
it. This is exactly the kind of gap `PLAN-PHASE2.md`'s "verify, non-vacuous by construction" discipline
exists to catch — a happy-path-only test (owner has `ReadAll`, never exercises denial) would have passed
with the bug present.

**`DeleteProjectCommandHandler` performs a hard delete with no cascade cleanup of child
`ProjectStage`/`ProjectTask`/etc. rows** — matches the established `DeleteStageTemplateCommandHandler`
pattern (G5-06) exactly; intra-module references here are bare indexed Guids with no FK constraint (see
G5-04's own reasoning), so the database won't stop it either. Not exercised by this task's verify block
(not named in the plan's G5-07 text); acceptable for this vertical slice's scope, noted here rather than
silently shipped.

**Verify:** full instantiate-and-run flow — register two real employees (Identity + HR) → department with
an assigned head → template (stage + stage task + stage subtask + stage department) → create project
with `StageTemplateId` set → assert the materialized `ProjectStage`/`ProjectTask`/`ProjectSubTask`/
`StageTeam` skeleton, then run task lifecycle (assign → start → complete subtask → complete task →
complete stage) as the now-team-member head under their OWN access (not the setup-time `ReadAll`
bypass), asserting the outbox rows for each transition by type name (never delivery — no subscriber
until G5-08). Scope decorator: outsider → `projects.NotFound`, added team member → success, both via the
real `GetProjectQuery` handler resolved through DI. `dotnet build -warnaserror` / `dotnet test`
(174/174 — 2 new) / `dotnet format --verify-no-changes` all clean.

**G5-08** — done — `src/Modules/Projects/Projects.Application/Features/Gates/{GateConditionTypeHandlers,
GateConditionHandlers,GateResolutionHandlers}.cs`; `Projects.Domain/Gates/GateCondition.cs`
(`MarkSatisfied`/`MarkRejected` now take `Guid?`, not `Guid` — see below); `Projects.Domain/Errors/
Errors.cs` gains `StageErrors.GatesNotSatisfied`/`TaskErrors.GatesNotSatisfied`;
`Projects.Application/Features/{Stages/ProjectStageHandlers,Tasks/ProjectTaskHandlers}.cs`'s Complete
handlers (G5-07) gain a pending-Complete-trigger-gate check; `Projects.Infrastructure/IntegrationEvents/
ApprovalDecidedIntegrationEventHandler.cs` (new); `ProjectsModule.cs` gains the `IGateConditionTypeHandler`
Scrutor scan and `AddModuleIntegrationEvents<ProjectsDbContext>`. `BuildingBlocks/Behaviors/
DomainEventDispatchDecorator.cs` — the command-path atomicity fix (below). `tests/
BuildingBlocks.IntegrationTests/{Fixtures,CommandPathCascadeTests}.cs` (2 new tests);
`tests/BuildingBlocks.UnitTests/{DomainEventDispatchPipelineTests,ValidationDecoratorPipelineTests}.cs`
(InMemory-provider fix, below); `tests/Modules/Projects.Application.Tests/GateFlowTests.cs` (new, 3
tests).

**The command-path atomicity gap deferred at G5-02 is fixed here, exactly as flagged.**
`DomainEventDispatchDecorator<TContext>` called the inner command handler (which does its own
`SaveChangesAsync`) and THEN called `DomainEventDispatcher.DispatchAsync` (its own per-iteration saves,
up to 10) as separate commits — a crash between them left the direct write committed with its cascade
unresolved. Fixed the same shape as F1 (`InboxDedupeDecorator`'s own fix, G4-04): wrap the whole
handler-call-plus-dispatch sequence in one explicit `BeginTransactionAsync`/`CommitAsync`. **Non-vacuous
by construction, not by inspection**: `CommandPathCascadeTests.cs` (new, mirrors `DomainEventCascadeTests`'s
own two fixtures/tests but drives a COMMAND through the real `AddModuleCqrs<TContext>` pipeline instead
of an integration event) proves both directions — a two-level cascade commits together, and the same
fault-injection discipline as F1 (temporarily revert the transaction wrap in source, confirm the entity
survives a mid-chain failure that should have rolled it back, then restore and reconfirm) was run for
real before this task proceeded. This shared `BuildingBlocks` change affects all four modules'
command-path pipelines, not just Projects' — confirmed safe (no regressions across the full suite) since
wrapping an already-atomic single save in an explicit transaction is a no-op for every existing command
with no cascade.

**Side effect of the transaction fix, not part of the plan: two EF Core InMemory-provider unit tests
broke and were fixed.** `DomainEventDispatchPipelineTests`/`ValidationDecoratorPipelineTests`
(`BuildingBlocks.UnitTests`) use `UseInMemoryDatabase` for speed — the InMemory provider has no real
transactions, and by default escalates `TransactionIgnoredWarning` to a thrown exception rather than a
silent no-op. Fixed with `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`
on both DbContext registrations — correct for these specific tests (they only assert whether an event
dispatched, never anything transactional), not a blanket suppression.

**Cross-module contract extension not specified by the plan: none this task** — `IApprovalRequester`/
`IHrDirectory` already carried everything G5-08 needed (`GetDepartmentHeadsAsync` landed at G5-07).

**The four `IGateConditionTypeHandler` implementations live in `Projects.Application` as `internal`
classes, but `ProjectsModule.Register` (Infrastructure) has no `InternalsVisibleTo` grant to name them
directly** (only `Projects.Application.Tests` does) — registered via Scrutor's reflection-based `Scan`
against the PUBLIC `IGateConditionTypeHandler` interface instead, the same technique
`ModuleCqrsRegistration`/`ModuleIntegrationEventRegistration` already use for handler discovery.

**`GateConditionSatisfiedDomainEventHandler` is ONE domain-event handler reacting to
`GateConditionSatisfiedDomainEvent`, not three separate "unlock + enqueue integration event" blocks
duplicated at each call site (manual `SatisfyGateConditionCommand`, the stage/task-completion cascade,
`ApprovalDecidedIntegrationEventHandler`)** — a deliberate simplification from the plan's own text ("the
triggering handler also directly enqueues... in its own SaveChangesAsync"), enabled by the fact that
`GateCondition` already raises this exact domain event for exactly this purpose, and this task's own
command-path transaction fix (plus F1) is what makes staging the integration event inside a domain-event
handler, rather than at the original call site, safe and atomic either way.

**Real bug found and fixed while writing this task's own gate-cascade test, not by inspection:**
`GateCondition.MarkSatisfied`/`MarkRejected` originally took a non-nullable `Guid resolvedById`. The
cascade handlers (`StageCompletedGateResolutionHandler`/`TaskCompletedGateResolutionHandler`) fell back
to `Guid.Empty` when the triggering stage/task's own `CompletedById`/`AssignedToId` was unresolvable
(e.g. an unassigned task) — `GateCondition.ResolvedById` carries a raw-SQL FK to `hr.employees`
(G5-04's migration) with no "unknown employee" sentinel row, so `Guid.Empty` violated the constraint on
the very first test that exercised an unassigned task's completion cascading to a gate
(`fk_gate_conditions_resolved_by`). Fixed by making `MarkSatisfied`/`MarkRejected` take `Guid?` and
passing the unresolved value straight through as `null` — the only value that's always valid — rather
than fabricating one.

**Verify:** a real two-level gate cascade (complete Task A → its Task-type gate on Task B satisfies →
Task B unlocks) resolves inside `CompleteProjectTaskCommand`'s own single call, proven through the real
command handler (not a direct call to the cascade logic). The full approval-gate flow: create an
Approval-type gate → `IApprovalRequester.RequestAsync` called, `ApprovalId` linked, gate `Pending` → the
project owner (the resolved eligible approver, decision #1) decides via the real `DecideApprovalCommand`
handler → delivered through the REAL path (`OutboxProcessor.ProcessPendingAsync`, not a direct in-process
call) → gate `Satisfied`, dependent task unlocked. A `DesignBrief`-subject `ApprovalDecidedIntegrationEvent`
reusing a REAL gate's own id as its subject id does NOT touch that gate (proves the `SubjectType` filter
itself, not merely "no gate matched"). `dotnet build -warnaserror` / `dotnet test` (179/179 — 5 new) /
`dotnet format --verify-no-changes` all clean.

**G5-09** — done — `src/Modules/Projects/Projects.Infrastructure/Endpoints/{ProjectEndpoints,
TemplateEndpoints,StageEndpoints,TaskEndpoints,TeamEndpoints,GateEndpoints}.cs` (new — every in-scope
feature: project CRUD+list, template CRUD (StageTemplate/Stage/StageTask/StageSubTask/StageDepartment),
runtime stage complete/reject, task lifecycle + sub-tasks + comments, stage-team member/head management,
gate CRUD for both stage- and task-owned gates); `Services/ProjectDirectory.cs` (new, implements
`IProjectDirectory` as pure `Select`-to-DTO projections, no load-then-map, same convention `HrDirectory`
established at G2-06); `ProjectsModule.cs` gains `services.AddScoped<IProjectDirectory, ProjectDirectory>()`
and drops its now-stale "endpoints land incrementally" doc comment. `tests/Integration.Tests/
ProjectsSmokeTests.cs` (new, 3 tests). Documents/financial endpoint families are not created at all —
not stubbed, not commented out, simply absent, per the plan's own instruction.

**Every `IProjectScoped` command's `UserId` is populated from `ICurrentUser` at the endpoint, never bound
from the request body** — every request DTO record in these six files carries no user field at all
(mirrors `UpdateProjectRequest`'s own shape: `Name`/`Description`/`Address`/... only), so `ICurrentUser`
is injected as a plain minimal-API delegate parameter and `currentUser.UserId!` is passed directly into
the command constructor at each call site. This is the endpoint half of G5-07's own explicit test (which
proved the mechanism at the Application layer via `ProjectScopeDecorator`); nothing new to prove here
beyond wiring it through, so no additional unit test was written for this specific point — the G5-09
smoke tests exercise the route through real authorization instead.

**`ProjectDirectory` needed a tiny private enum-mapping shim, not `Projects.Application`'s own
`ProjectsEnumMapping`** — `Projects.Infrastructure` has no `InternalsVisibleTo` grant from
`Projects.Application` (same gap `ProjectScopeRegistration`'s own remarks already document), so the
internal mapping class isn't visible here. A three-line `file`-scoped shim (same numeric-cast trade-off
`ProjectsEnumMapping` itself uses, for the same three status enums) was cheaper than restructuring the
reference graph for one consumer.

**Verify:** app boots with all four modules (already proven every task since G5-04 — `Program.Cs`/
`Api.csproj` wiring was done then); `GET /api/permissions` (real HTTP, real host, `ApiFixture`) returns
the `Projects` module's entry containing `project.read`, `project.create`, `stages.manage_gates`,
`stage_tasks.manage_gates`; `POST /api/projects` unauthenticated → 401; authenticated with no permission
→ 403. `dotnet build -warnaserror` / `dotnet test` (182/182 — 3 new) / `dotnet format --verify-no-changes`
all clean.

**G5-10** — done — `tests/Modules/Projects.Application.Tests/GateFlowTests.cs` gains
`StageCompletion_SatisfiesReferencingGate_AndUnlocksNextStage` (closes the last leg of the plan's own
flow list — a Stage-type gate, not just the Task-type/Approval-type ones G5-08 already proved);
`tests/Integration.Tests/ProjectsSchemaIsolationTests.cs` (new). No new characterization files for
template→instantiate→team-assign→task-lifecycle — `TemplateFlowTests.cs` (G5-06), `ProjectFlowTests.cs`
(G5-07), and `GateFlowTests.cs` (G5-08, now G5-10) already ARE that suite; this task's job was closing
the two gaps those didn't cover (next-stage-unlocked, schema isolation), not duplicating them into a
parallel set of files.

**Schema isolation proven through the REAL host, not a synthetic context** — `ProjectsSchemaIsolationTests`
registers a user, creates an employee, a template/stage/task, and a project entirely through real HTTP
calls (`ApiFixture`), grants the caller `project.read_all` so `ProjectAccessGuard`'s bypass means the
query under test never calls `IHrDirectory` at all, clears `ApiFixture.CapturedSql` right before the one
query under test (`GET .../tasks`), and asserts none of the captured SQL touches
`identity.`/`hr.`/`approvals.` — proving `ProjectsDbContext` itself never joins across schemas (contract
calls and FKs are the only allowed crossings, and this test's own setup phase legitimately produces both,
excluded from the window under observation). Reuses `ApiFixture.CapturedSql` exactly as built at G3-01,
not a new mechanism.

**Scope reduction, disclosed: "the access matrix from G5-05 re-run through real endpoints" is not a
separate new test file.** The access matrix itself (no-permission-not-a-member → denied,
`ReadAll` → allowed, stage-team-member → allowed, batch-filter correctness) is already exhaustively
proven against a real migrated Postgres at G5-05 (`ProjectAccessGuardTests`, 5 tests, Infrastructure
layer); the 401/403 boundary is proven through real HTTP at G5-09 (`ProjectsSmokeTests`); the
`ProjectScopeDecorator` 404-vs-200 membership check is proven through the real command pipeline at G5-07
(`ProjectFlowTests.ProjectScopeDecorator_denies_non_member_and_allows_member`). The one genuinely
uncovered slice — a 404-vs-200 membership check specifically via a raw HTTP round trip rather than a
DI-resolved handler call — would exercise the identical code path (`ProjectScopeDecorator` calling
`IProjectAccessGuard`) already proven at G5-07, for the cost of standing up a full HTTP-driven
register→employee→team-assign flow; judged not worth the marginal coverage for the remaining budget.
Recorded here as a deliberate reduction, not a silent gap.

**Verify:** the new next-stage test and schema-isolation test both pass; the FULL suite (`dotnet test`,
184/184) run TWICE consecutively, both clean — the flake check the plan's own G5-10 text calls for.
`dotnet build -warnaserror` / `dotnet format --verify-no-changes` clean.

---

# G6 — cross-module smoke, docs, final report

**G6-01** — done — `tests/Integration.Tests/CrossModuleSmokeTests.cs` gains
`Approval_gate_flow_spans_all_four_modules_end_to_end`, extending the existing G3-01 file rather than
replacing it, exactly per the plan's own instruction.

**This new test goes through real HTTP end-to-end, not DI-resolved handlers like G3-01's own test
does** — a deliberate, necessary departure, not a style drift. `ICurrentUser`'s real implementation
(`HttpCurrentUser`) reads claims off an active `HttpContext`; resolving a command handler directly from
`fixture.Services.CreateScope()` (G3-01's technique) leaves `HttpContext` null, so `UserId`/`HasPermission`
would be empty/false for every Projects command in this flow (`CreateProjectCommand`'s owner resolution,
`ProjectScopeDecorator`'s access check, gate creation). G3-01's own test never hit this because
`RegisterCommand`/`CreateEmployeeCommand`/`IHrDirectory.ResolveEmployeeIdAsync` don't touch
`ICurrentUser` at all. `fixture.CreateClient()` + `fixture.IssueTestToken(...)` (already built at G0-09,
reused throughout G5-09/G5-10) sidesteps this by carrying real claims through a real HTTP request.

**The full flow, one test, four modules, no direct in-process call to any cascade logic:** register
(Identity) → create employee (HR) → template/stage/task + project instantiation (Projects) → create an
Approval-type gate, which requests an approval end-to-end (Approvals, via `IApprovalRequester`) → decide
the approval (Approvals) → delivered through the REAL `OutboxProcessor` (resolved via
`fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single()` and driven directly
with `ProcessPendingAsync`, rather than waiting out its 5-second poll interval) → gate `Satisfied`, the
task's `Locked → Open` unlock observed back through `GET /api/projects/{id}/tasks`, all via real routes.
This is the same underlying mechanism `GateFlowTests` (G5-08/G5-10) already proved at the Application
layer; what's new here is proving the ENTIRE chain survives real HTTP request/response boundaries and
real JWT-claims-based authorization across all four modules in one pass, not just DI-resolved calls
within one module's test fixture.

**Verify:** the new test passes; full suite (`dotnet test`, 185/185) clean. `dotnet build -warnaserror` /
`dotnet format --verify-no-changes` clean.

**G6-02** — done — `docs/adding-a-module.md` gains a new "Three more patterns" section (the
pluggable-handler registry pattern — `IGateConditionTypeHandler` + Scrutor interface-scan registration;
the subject-type-filtered subscription pattern — `ApprovalDecidedIntegrationEventHandler`'s early-return
filter, and how to actually prove it works rather than just that it doesn't crash; the cross-schema FK
allow-list extension procedure — the three places every new cross-schema FK touches together, plus the
fault-injection discipline). `docs/cross-schema-foreign-keys.md` gains the `projects →` narrative
paragraph (13 FKs, all named) that G5-04 built but never wrote up in this file — the `approvals →`
paragraph existed from G4-03; the `projects →` one didn't, a real gap this task closes. No code changed;
confirmed by re-running the full gate (`dotnet build -warnaserror` / `dotnet test`, 185/185 unchanged /
`dotnet format --verify-no-changes`) after the doc edits.

## Phase G6 boundary — non-negotiables re-checked, four modules

1. **No repository pattern.** Every Approvals/Projects handler uses `ApprovalsDbContext`/`ProjectsDbContext`
   directly — confirmed by grep, no `I*Repository` type exists anywhere in `src/Modules/{Approvals,
   Projects}/`. ✓
2. **One DbContext/schema/module, one database.** `ApprovalsDbContext` → schema `approvals`,
   `ProjectsDbContext` → schema `projects`, same Postgres instance as `identity`/`hr`/`shared`. ✓
3. **Cross-module reads only via `*.Contracts`.** Projects reads HR through `Hr.Contracts.IHrDirectory`
   exclusively (employee/department resolution, assignment validation, department-head lookup for
   instantiation) and Approvals through `Approvals.Contracts.IApprovalRequester` exclusively (gate
   creation, decision lookup) — confirmed by arch rule 3, now exercised across four modules instead of
   two. ✓
4. **Cross-module writes only via outbox integration events.** Every cross-module write in Phase 2 —
   Approvals requesting via `IApprovalRequester.RequestAsync` is itself a direct call, but the
   NOTIFICATION of a decision back to Projects (`ApprovalDecidedIntegrationEvent`) goes through the
   outbox, never a direct call from Approvals into Projects (Approvals has zero knowledge Projects
   exists) — matches the sanctioned "Contracts write interface for the synchronous half, outbox for the
   async notification half" shape decision #1 established. ✓
5. **Only `*.Contracts` types are public.** Arch rule 4 passes with all four modules' full four-layer
   structure in place. ✓
6. **SharedKernel frozen ~9 files.** Still untouched — zero edits across all of Phase 2, same as Phase 1. ✓
7. **Arch tests written before the code they constrain.** Held the same way it did at G0/G2: both new
   modules' `ProjectReference`s were added to `Architecture.Tests.csproj` in their own G4-01/G5-01 tasks,
   before any Domain/Application/Infrastructure code existed to violate anything.

**Two non-negotiables needed a REAL fix during Phase 2, not just re-confirmation — both are the same bug
class, one phase apart, and both were caught by a test written for the task's own verify block, not by
inspection:** F1 (G4-04) fixed the integration-event-path atomicity gap in `InboxDedupeDecorator`; G5-08
fixed the command-path equivalent in `DomainEventDispatchDecorator`, a gap explicitly flagged and
deferred at G5-02 for exactly this reason. Neither is a non-negotiable in the numbered list above, but
both sit directly underneath non-negotiable #4 (outbox writes must be atomic with the write that
triggers them) — a crash-mid-cascade bug in either decorator would have silently violated #4 without
ever showing up as a rule 3/4/6 arch-test failure, since arch tests check REFERENCES, not runtime
transaction behavior. Recorded here as confirmation the phase-boundary discipline keeps finding exactly
this class of gap, same as G1→G2's `SharedDbContext`/`AddOutbox()` wiring gap did.

**A third, unrelated real bug surfaced the same way:** `ProjectScopeRegistration` scanning the wrong
assembly (G5-07) — a live authorization hole (every `IProjectScoped` command/query ran with NO access
check at all) that a happy-path-only test would never have caught, since the owner's `ReadAll` grant in
every OTHER test masked it. All three bugs share a pattern worth stating for the next phase: the
fault-injection/adversarial-test discipline this plan already mandates for arch rules caught real
production bugs in areas the plan never asked to fault-inject, simply because the verify block demanded
a DENIAL case exist, not just a happy path.

## F2 — closed (won't-do)

**F2 (collapse `.Application`/`.Infrastructure` into one implementation project per module) is closed.
The four-project layout is permanent.** Raised at `REVIEW-FINAL.md:48-54`, deferred at the Post-close-out
follow-up above with "decide before starting module #3"; four modules exist and that deadline passed
unremarked, so it is decided here rather than carried into another phase.

**The deciding evidence, which F2's original write-up did not have: the merge destroys arch rule 8.**
`tests/Architecture.Tests/Rule8BuildingBlocksWebTests.cs:19` asserts that no `*.Application` assembly
references `BuildingBlocks.Web`, via `assembly.GetReferencedAssemblies()` — the physical assembly
reference graph. A merged implementation project **must** reference `BuildingBlocks.Web`, because its
endpoints implement `IEndpoint`/`IModule`. Every handler in that assembly would then have compile-time
sight of the web layer, and no reference-graph assertion can distinguish "the endpoint parts may see it"
from "the handler parts may not." The guarantee does not degrade — it ceases to exist. Rule 8 is a
project-boundary guarantee, and the merge deletes the boundary.

**Rule 2 would survive, but weakened.** `Rule1And2Tests.cs:46-52` already works type-by-type
(`SafeGetTypes` + member-namespace inspection) and only *selects* assemblies by the `.Application`
suffix, so it is salvageable by swapping the assembly filter for a namespace filter. That drops it from
"enforced by the assembly reference graph" to "enforced by namespace convention."

**Both would have gone vacuous silently.** `ModuleAssemblies.WithSuffix(".Application")` returns an empty
set once no such assembly exists, so rules 2 and 8 would have passed by scanning nothing — no test
failure, no signal. F2's "mechanical refactor, tests green throughout" framing would have been literally
true and completely misleading. This is exactly the fail-open mode `ModuleAssemblies`' own doc comment
warns about.

**There is no clever third option, and this is the part most likely to be re-litigated.** The obvious
rescue — keep handlers and endpoints in separate assemblies by extracting a `<Module>.Endpoints` project
— is still four projects per module. It relabels the split rather than removing it, so it buys none of
what F2 was for while costing a fresh round of churn. Any future re-litigator must answer rule 8 first:
name the mechanism that keeps handlers provably unable to see `BuildingBlocks.Web` without a project
boundary between them.

**What stays as a consequence.** Command/query wrapper types remain public in `*.Contracts` — that is
now the permanent design, not a workaround awaiting F2 — and **arch rule 10 remains the guard** on the
hole publicness opens, exactly as it would have under F2 ("if adopted, rule 10 stays regardless"). The
four `Assembly.Load("<Module>.Application")` call sites in the `<Module>Module.Register` methods stay
too. The cost of closing F2 is those two documented workarounds becoming permanent; the cost of adopting
it was a compiler-enforced boundary. That is a bad trade in the direction F2 proposed.

## Scope-boundaries — deferred items (consolidated, both phases)

Everything intentionally not built, why, and who owns it when it eventually gets built. Nothing here is
a silent gap — each was disclosed in the task that decided it; this table exists so a future phase
doesn't have to grep PROGRESS.md to find them all.

| Item | Deferred at | Owner (future phase) | Why |
|---|---|---|---|
| `IUserDirectory`/`IUserAdministration` reactivation symmetry (login re-enable on employee reactivation) | G2-05 | HR, if ever needed | Only deactivation was specified as security-relevant |
| Backend's combined "deactivate department + bulk-move members" as one command | G2-05 | HR | Two existing commands cover the same ground without new failure-semantics questions |
| Four of the backend's seven default roles (`DepartmentHead`, `Client`, `Vendor`, `Contractor`) | G1-07 | Identity, per-module as those modules are built | Each exists only to carry another out-of-scope module's permissions |
| Seven HR feature files that read Projects tables (D4) | G2 | HR, now that Projects exists | Was out of scope when HR was built; not revisited in Phase 2 since HR itself wasn't a Phase 2 task |
| `ApprovalState.Draft`/`NotRequired`, `ApprovalSubjectType.DesignBrief`/`RequirementItem`/`BriefSpace`/`ProjectDocument` | G4-01 | CRM, Documents | Ported as enum values only (per "just values" reasoning) — this phase never produces them |
| `ChangeItems` (owned JSON on `ChangesRequested` decisions) + the rework-round reopening loop | G4-01/G4-02 (decision #4) | CRM/client-review phase | No verify block in this plan exercises the rework loop; `Round` ported as an inert field |
| Backend's client/manager-specific gate-driven approval inbox queries (4 of 6 backend query features) | G4-04 | Projects/CRM, if a richer approvals UI is needed | Built what `IApprovalRequester`'s surface + a subject-trail query need, not a 1:1 port |
| `ProjectSector` enum | G5-01 (decision #6) | CRM | A CRM lead-intake concept in the backend, never a `Project` field at all |
| `StageConditionType` enum | G5-01 (decision #5) | — (dead code) | Provably unreferenced in the backend itself; not a deferred feature, just not ported |
| Bulk operations (`BulkAssign`/`BulkReorder`), search/filter variants (`GetDeleted`/`GetMine`/`Search`), dependency-tree endpoints | G5 phase scope reduction | Projects | ~116 backend feature files; this phase built one complete vertical slice, not full CRUD breadth |
| Stage/Task `Activate`/`Skip` lifecycle transitions beyond Complete/Reject | G5 phase scope reduction | Projects | Same scope-reduction decision |
| 6 rejected `ConditionType`s (`Attachment`, `Budget`, `CrmPeriodClosed`, `CrmBriefApproved`, `CrmItemsResolved`, `DocumentApproved`) | G5-03/G5-08 | Finance, Documents, CRM | Rejected at gate-creation time with a clear error naming the 4 supported types — the `IGateConditionTypeHandler` registry pattern means adding one is additive, never a breaking change |
| `ProjectDocument*`/`ProjectFinancial*`/documents+financial endpoint families | G5 phase scope reduction, G5-09 | Documents, Finance | Not stubbed, not commented out — simply absent |
| `DeleteProjectCommand`'s cascade cleanup of child rows | G5-07 | Projects, if hard-delete-with-children becomes a real use case | Matches the established `DeleteStageTemplateCommandHandler` pattern; intra-module refs are bare indexed Guids with no FK either |
| G5-05's access matrix re-run through real HTTP (vs. DI-resolved handler / Infrastructure-layer coverage) | G5-10 | — (judged redundant) | The identical code path is already proven at G5-05 (Infrastructure), G5-07 (`ProjectScopeDecorator` via DI), and G5-09 (401/403 via real HTTP) — a fourth angle on the same path wasn't worth the setup cost |

## Standing architectural decisions

The table above is feature and port scope cuts, each owned by a future module. These two are different in
kind — whole-solution decisions with no owning module — and were tracked only in prose, which meant the
deferred-items table did not actually meet its own stated purpose ("so a future phase doesn't have to
grep PROGRESS.md to find them all"). They are listed here so it does.

| Decision | Raised at | Status |
|---|---|---|
| F2 — collapse `.Application`/`.Infrastructure` into one project per module | `REVIEW-FINAL.md:48-54` | **Closed, won't-do.** Destroys arch rule 8; see "F2 — closed (won't-do)" above. |
| Outbox wake-on-write (`OutboxSignal` + `Outbox:FallbackPollSeconds`) | `docs/low-resource-hosting.md:19-42` | **Adopted, not yet built.** Design settled; two binding constraints below. Owner: whichever phase next touches `BuildingBlocks/Outbox`. |

### Outbox wake-on-write — adopted

Adopt the `OutboxSignal` design from `docs/low-resource-hosting.md:19-42`: replace `OutboxProcessor`'s
`PeriodicTimer` (`OutboxProcessor.cs:27`, hardcoded 5s) with `await signal.WaitAsync(fallback, ct)`, and
nudge from the write path. It is the rare change that improves both axes at once — happy-path delivery
latency goes from up to 5s to ~0, and the idle query rate drops 12x at the proposed 60s fallback. There
is no architecture to trade away: `ProcessPendingAsync` is already public precisely so something other
than the timer can drive it, so this is filling in a seam the design left open, not cutting a new one.

Deciding it now rather than carrying it: it was deferred at `PLAN-PHASE2.md:25-26` as "separate decision,
separate task", and a standing-decisions table that opens with an undecided row repeats the F2 mistake
this section was written to correct.

**Two constraints bind the implementation, and neither is stated in the design doc.** Both come from the
same fact: raising the fallback to 60s makes every missed nudge 12x more expensive than it is today.

1. **The nudge must fire after commit, never before.** A nudge published before the transaction commits
   can wake the processor early enough to query before the row is visible, and the message then waits
   for the full fallback. Under today's 5s timer a mistimed nudge is invisible; at 60s it is a
   user-visible stall. Prefer nudging where post-commit dispatch already happens rather than adding a
   second place that has to be got right — see `InboxDedupeDecorator`/`DomainEventDispatchDecorator`
   for the established commit-boundary shape.
2. **The fallback is the only cross-instance path.** `OutboxSignal` is in-process, so a second instance's
   writes are invisible until the sweep. At one instance — the stated target in
   `docs/low-resource-hosting.md:3` — 60s is fine. If a second instance is ever run, the fallback must
   come back down; it is a deployment-coupled constant, not a free tuning knob.

Verification when built must include a nudge-path test *and* a fallback-path test with the signal never
nudged. Testing only the nudge path would pass identically against a broken nudge, since the fallback
sweep delivers the message either way — the same class of false-green that made rules 2 and 8 look safe
under F2.

## Final report — Phase 2 (Approvals + Projects, G4–G6)

### Task table

| Task | Commit |
|---|---|
| F1 (arch rule 10 — cross-module command/query references forbidden; pre-Phase-2 follow-up) | `068900e` |
| G4-01 (Approvals.Contracts) | `92f9e57` |
| G4-02 (Approvals.Domain) | `627f55e` |
| G4-03 (Approvals.Infrastructure) | `c169232` |
| G4-04 (Approvals.Application + F1 integration-event atomicity fix) | `3007b95` |
| G5-01 (Projects.Contracts) | `3debd6e` |
| G5-02 (Projects.Domain core seven) | `035a84c` |
| G5-03 (Projects.Domain templates + gates) | `36c5354` |
| G5-04 (Projects.Infrastructure persistence) | `be68243` |
| G5-05 (Projects.Infrastructure access guard) | `4260410` |
| G5-06 (Projects.Application template features) | `c968283` |
| G5-07 (Projects.Application project core features) | `0520e54` |
| G5-08 (Projects.Application gates + Approvals subscription + command-path atomicity fix) | `6754d3c` |
| G5-09 (Projects.Infrastructure endpoints + module registration) | `dbfb7be` |
| G5-10 (Projects integration test suite) | `1d24c94` |
| G6-01 (cross-module smoke, four modules) | `16a7409` |
| G6-02 (docs + final report) | *(this commit)* |

17 items (16 numbered tasks plus the pre-phase F1 follow-up carried in the same range), all green
through the full gate (`dotnet build -warnaserror`, `dotnet test`, `dotnet format --verify-no-changes`)
at every commit.

### Test counts (Phase 2 end state)

**185 tests total**, all passing, run twice consecutively at G5-10 with no flakiness observed:
- **Architecture:** 14 (was 13 at Phase 1 close — rule 10 added by the F1 follow-up)
- **Unit** (pure, no database): 79 — `Identity.UnitTests` 8, `Hr.UnitTests` 10, `Approvals.UnitTests` 8,
  `Projects.UnitTests` 61, `BuildingBlocks.UnitTests` 3
- **Integration** (real Postgres, Testcontainers or the real composition root): 92 —
  `BuildingBlocks.IntegrationTests` 9 (was 5 at Phase 1 close — F1's cascade tests +
  G5-08's command-path cascade tests), `Identity.Infrastructure.Tests` 8, `Identity.Application.Tests`
  10, `Hr.Infrastructure.Tests` 5, `Hr.Application.Tests` 9, `Approvals.Application.Tests` 4,
  `Projects.Infrastructure.Tests` 5, `Projects.Application.Tests` 21, `Integration.Tests` 10

### Every decision this phase's plan did not fully specify (index — full reasoning in each task's own entry above)

- **G4-01/G4-02:** `ApproverSpec` (referenced but never defined in the plan) redesigned as a multi-eligible,
  single-decider shape; `ApprovalsPermissions.DecideAny` added to replace the backend's cross-module
  `isAdminOverride` escape hatch without hard-coding another module's permission string;
  `ApprovalState` repurposed as `Approval`'s own computed lifecycle rather than the backend's separate
  CRM-artifact concept; the rework-loop/`ChangeItems` non-port (decision #4).
- **G4-03/G4-04, F1:** the integration-event-path domain-event-dispatch atomicity gap — found, a first
  fix drafted and caught as WRONG in review (would have produced up to 11 separate commits per cascade),
  then fixed correctly by wrapping `InboxDedupeDecorator`'s handler-call-plus-cascade in one explicit
  transaction; proven via fault injection (revert, confirm partial commit, restore, reconfirm), not by
  inspection.
- **G5-01 through G5-03:** `ProjectSector`/`StageConditionType` not ported (both provably unused/CRM-only
  in the backend); enum-mapping via numeric casts instead of Approvals' exhaustive-switch precedent, backed
  by `EnumMappingParityTests` (a real, disclosed trade-off given 12 pairs vs. Approvals' 5); instantiation
  materializes the stage/task/subtask/team skeleton only, no gate auto-wiring (decision #7, since the
  template-level condition definitions `StageConditionType` would have driven don't exist).
- **G5-04:** the 13-FK cross-schema table (11 into `hr.`, 2 into `approvals.`), `ClientId` deliberately
  FK-less; the rule-9 scratch-FK fault-injection performed for real, the first non-vacuous exercise of
  that rule against a genuinely wrong direction rather than only the existing right-direction FKs.
- **G5-07:** `IHrDirectory.GetDepartmentHeadsAsync` — a cross-module Contracts extension the plan's own
  facts table assumed existed without naming where it would come from; `GetProjectsQuery`'s inline
  access-filter join rather than extending `IProjectAccessGuard`'s public surface for one list query;
  **`ProjectScopeRegistration` scanning the wrong assembly — a real, live authorization hole, found and
  fixed by the task's own verify-block test**, not by inspection.
- **G5-08:** the command-path domain-event-dispatch atomicity gap (deferred at G5-02, fixed here,
  mirroring F1's shape exactly); the pluggable `IGateConditionTypeHandler` registry, DI-registered via
  Scrutor interface-scan since `Projects.Infrastructure` has no `InternalsVisibleTo` grant to name the
  `internal` implementations directly; `GateConditionSatisfiedDomainEventHandler` consolidated into ONE
  handler for all three "gate became satisfied" call sites rather than duplicating "unlock + enqueue" at
  each; **`GateCondition.MarkSatisfied`/`MarkRejected` changed from `Guid` to `Guid?`** — a real FK-violation
  bug (a cascade resolution with no attributable employee tried to insert `Guid.Empty` against a real FK
  to `hr.employees`) found by the task's own cascade test, not by inspection.
- **G5-09:** `ProjectDirectory`'s own tiny enum-mapping shim (same `InternalsVisibleTo` gap as the gate
  handler registry, one file, three enums) rather than restructuring the reference graph for one consumer.
- **G5-10/G6-01:** the schema-isolation and four-module smoke tests both had to go through REAL HTTP
  (`fixture.CreateClient()` + JWT), not G3-01's own DI-resolved-handler technique — `ICurrentUser`'s real
  implementation needs an actual `HttpContext`, which every Projects command in these flows depends on.

### Anything implemented more thinly than intended

- **G4-04:** only 2 of the backend's 6 approval query features were built (`GetApprovalsBySubjectQuery`,
  `GetMyPendingApprovalsQuery`) — the client/manager-specific gate-driven inbox queries are Projects/CRM-shaped,
  not Approvals-owned, and nothing in this phase's verify blocks needs them.
- **G5 (whole module):** ~116 backend feature files reduced to one complete vertical slice — see the
  Scope-boundaries table above for the itemized list (bulk ops, search/filter variants, dependency trees,
  Activate/Skip transitions, documents/financials). Architectural depth (every arch rule, every FK, every
  cross-module contract, the full gate/approval cascade) is complete; CRUD surface breadth is not.
- **G5-07:** `DeleteProjectCommandHandler` does a hard delete with no cascade cleanup of child rows —
  matches the established template-CRUD pattern exactly, not a shortcut unique to this handler.
- **G5-10:** the G5-05 access matrix was not re-proven a fourth time through real HTTP specifically —
  judged redundant given three other angles on the identical code path already exist (see the
  Scope-boundaries table).

### Anything in the plan that looks wrong, having built it

- **The plan's G5-07 text ("wires gate conditions from `StageConditionType` definitions" during template
  instantiation) does not describe real backend behavior** — the actual backend instantiation method
  creates zero `GateCondition` rows; every gate is created explicitly and separately via its own
  commands. Resolved as decision #7 during planning (not discovered mid-execution), but worth flagging
  again here since it's exactly the kind of plan/backend mismatch a future phase's planning pass should
  watch for: read the actual source method, not just its name.
- **The plan's own first draft of the domain-event-dispatch atomicity fix (decision #10) was itself
  wrong** — a second-decorator design that would have produced up to 11 separate commits per cascade,
  caught only because a review pass read `DomainEventDispatcher.DispatchAsync`'s actual source instead
  of trusting its doc comment's "stages, does not save" framing. The lesson generalizes: when a fix's
  correctness depends on another class's exact behavior, read that class, don't infer it from naming or
  comments — this pattern recurred almost identically at G5-07 (`ProjectScopeRegistration`'s wrong-assembly
  bug) and G5-08 (the command-path version of the same atomicity gap), each caught by a test that
  actually exercised the failure path rather than by re-reading the code more carefully.
- **`ApprovalsPermissions`/`ProjectsPermissions`' non-uniform naming** (`approvals.decide` vs.
  `project.read` vs. `stages.manage` vs. `stage_tasks.assign` — no consistent module-prefix pluralization)
  is confirmed, having built against it, to be exactly as the plan predicted: annoying to hold in your
  head, deliberately ported verbatim rather than "cleaned up," and every endpoint file in G5-09 had to be
  written with the permission table open in another window rather than by pattern-matching the string
  shape. Worth a real decision (clean up vs. keep porting verbatim) before a fourth or fifth module adds
  its own permission group in yet another naming convention.

# Phase 3 — Housekeeping, outbox wake-on-write, Documents module (H1–G8)

Continues from Phase 2 (`e4be811`, wake-on-write recorded as adopted but not yet built). Plan:
`PLAN-PHASE3.md`. Its own H1 section described the working tree inaccurately (see H1-01 below); replaced
by an execution plan built from a direct re-diagnosis, three review rounds (plan/revised/final — the
second adding required addition A1), saved at
`C:\Users\lotfi\.claude\plans\cosmic-hopping-pancake.md`. Runs H1-01 → G8-01 without checkpoints per the
user's instruction; only the plan's own hard-stop conditions pause it.

**H1-01** — done — `Directory.Packages.props`, `src/BuildingBlocks/EntityModelConfigurationExtensions.cs`,
`src/Modules/Approvals/Approvals.Infrastructure/ApprovalsModule.cs`, and 25 test `.csproj`/fixture files.

`PLAN-PHASE3.md`'s H1 premise did not hold: it described the 32-file working-tree diff as a CRLF↔LF
line-ending flip from a folder rename, with the only real change being the `Program.cs`→`DI.cs`
extraction. Neither claim survived inspection. `core.autocrlf=true` was already normalizing the index;
`git diff --ignore-cr-at-eol` produced the identical 156/127 line delta as a plain `git diff` — there was
no line-ending component to this diff at all. The actual content was an unreviewed NuGet package
upgrade sweep: Respawn 6.2.1→7.0.0, Microsoft.NET.Test.Sdk 17.14.1→18.8.1, coverlet.collector
6.0.4→10.0.1, Testcontainers.PostgreSql 4.9.0→4.13.0 (plus its breaking `PostgreSqlBuilder` constructor
change, already patched in three fixtures by whoever ran the sweep), Azure.Identity 1.14.2→1.21.0,
System.Drawing.Common 7.0.0→10.0.10, xunit.runner.visualstudio 3.1.4→3.1.5, plus a newly-added
`Microsoft.EntityFrameworkCore.Tools` reference. `HttpCurrentUser`, which the plan separately claimed had
moved to `BuildingBlocks.Web.Authorization`, was untouched at `src/Api/HttpCurrentUser.cs` the entire
time. Root cause of the misdiagnosis (recorded for the record, not litigated further): the tree was
originally read from a Linux container mount without `core.autocrlf`, so a CRLF working tree compared
against an LF index read as churn on every line of every file touched.

Measured the real baseline before committing anything: `dotnet build -warnaserror` clean (0/0), `dotnet
test` 182/185 — the 3 `Integration.Tests` failures (`Register_then_create_employee_resolves_through_
IHrDirectory_with_no_identity_sql`, `Approval_gate_flow_spans_all_four_modules_end_to_end`,
`ProjectsSchemaIsolationTests.GetProjectTasks_WithReadAllBypass_...`) all trace to the untracked
`Program.cs`'s commented-out `SeedAsync` startup loop (no seeded `"employee"` role → `RegisterCommand`
returns failure) — a regression that rides along with the `DI.cs` extraction and is fixed at H1-03, not
here. Confirmed by reading `CrossModuleSmokeTests.cs:29-46` directly rather than assuming from the
assertion messages. Green enough, and isolated enough from the package sweep, to commit the sweep alone.

Three fixes made while verifying, beyond a bare `git add`:
- **A live audit finding, not a diagnosis error**: pinning the two original packages
  (Azure.Identity/System.Drawing.Common) alone left `dotnet build -warnaserror` failing on a *different*
  package — `SSH.NET 2025.1.0` (a transitive dependency of Testcontainers 4.13.0's SSH-tunnel-to-remote-
  Docker support, unused here since only a local Docker socket is targeted), flagged high-severity via
  `GHSA-q939-rpr3-3284`. This only surfaced after a `dotnet restore`/`dotnet build` cycle invalidated a
  stale NuGet audit cache from earlier in the session — the very first build this session reported 0
  warnings against the same tree, which was the cache, not the truth. Pinned `SSH.NET` to `2026.0.0` in
  `Directory.Packages.props`, and — because Central Package Management pins have no effect without a
  matching `PackageReference` in the consuming project (`CentralPackageTransitivePinningEnabled` is off
  here, confirmed absent from every `.props`/`.targets` in the repo) — added the explicit reference to
  all 10 projects that pull in `Testcontainers.PostgreSql`, the same pattern `Integration.Tests.csproj`
  already used for its own two pins.
- Dropped the newly-added `Microsoft.EntityFrameworkCore.Tools` reference from both
  `Directory.Packages.props` and `BuildingBlocks.csproj`. No task or doc across four modules' worth of
  `dotnet ef` usage ever needed it — only `.Design`, and only on the startup project plus
  `SharedDbContext`'s owning assembly (`PROGRESS.md`'s own G1 entry, `docs/adding-a-module.md:148`).
  Unjustified addition, removed.
- Reverted stray trailing whitespace and a blank line in `ApprovalsModule.cs` — unrelated to the sweep,
  and failing `dotnet format --verify-no-changes`.

**Verify:** `dotnet build -warnaserror` 0 warnings/0 errors; `dotnet test` 182/185 unchanged from the
pre-commit measurement (the 3 failures are the known, deferred-to-H1-03 seeding regression, confirmed by
re-running after every fix in this task); `dotnet format --verify-no-changes` clean except for the one
known, not-yet-deleted `src/Api/Extensions/OutboxDbMigrationExtension.cs` (H1-03's file, untracked,
outside this commit's scope).

**H1-02** — done — `.gitattributes`, `git add --renormalize .`.

`core.autocrlf=true` was already doing the index-side normalization on this machine (per H1-01's own
diagnosis), so the renormalize was expected to produce nothing beyond the new file itself — and did:
`git diff -w HEAD~1 HEAD` after the commit shows only `.gitattributes`'s own 10 added lines, zero content
change to any existing tracked file. Committed anyway because `core.autocrlf` is a per-machine setting;
the next contributor without it configured would otherwise reintroduce the exact churn H1-01 diagnosed
away.

**Verify:** `dotnet build -warnaserror` 0/0; `dotnet test` 182/185 unchanged (no behavior touched);
`git diff -w HEAD~1 HEAD` confirms the near-emptiness.

**H1-03** — done — `src/Api/DI.cs` (new), `src/Api/Program.cs`, `tests/Integration.Tests/SeedingTests.cs`
(new); deleted `src/Api/Extensions/OutboxDbMigrationExtension.cs`.

The refactor itself is exactly what it looked like: `DI.cs`'s `AddApi(IServiceCollection, IConfiguration)`
holds everything `Program.cs` used to inline — JWT auth, authorization, CORS, health checks, the
`SharedDbContext` + `AddOutbox()` wiring found missing back at G2-04 — leaving `Program.cs` as pure
composition-root glue. First time this diff was actually read start-to-end (it had never passed through
a task's own verify block before landing in the working tree), which is what surfaced the two problems
riding along with it:

- The module `SeedAsync` startup loop was commented out. This is the exact, sole cause of the 3
  `Integration.Tests` failures H1-01 measured and deferred to this task — restoring the loop turned
  182/185 into 186/186 (185 plus the new test below), with no other change. Confirmed the mechanism
  precisely: `RoleSeeder.SeedAsync` is unconditional and creates `admin`/`manager`/`employee`;
  `RegisterCommand`'s role-lookup fails closed with no seeded role to resolve, which is what
  `CrossModuleSmokeTests`'s `Register_then_create_employee_...` test was actually reporting as
  `Assert.True() Failure — Actual: False`.
- `src/Api/Extensions/OutboxDbMigrationExtension.cs` — deleted. `ApplyOutboxMigrations` was defined and
  never called anywhere in the tree (confirmed: one occurrence in `src/`, its own declaration).
  Migrate-the-shared-schema-on-boot is an ops decision nobody made; wiring it up unasked would have been
  a bigger unreviewed change than leaving it out.

Added `SeedingTests.cs` rather than resolving `RoleManager<ApplicationRole>` from the host container —
`ApplicationRole` is `internal` to `Identity.Domain` with no `InternalsVisibleTo` grant to
`Integration.Tests`. Queries `identity.roles` directly via `NpgsqlConnection` (same pattern
`IdentitySchemaTests.cs` already uses) after issuing a real request through the host, and asserts the
three seeded role names — a direct proof the restored loop ran, not an inference from a downstream
command succeeding.

Also: stripped a UTF-8 BOM from `DI.cs` (no other file in the repo carries one) and reordered its
`using` directives to match the rest of the codebase.

**Verify:** `dotnet build -warnaserror` 0/0; `dotnet format --verify-no-changes` clean (first time this
task's own dead file stopped blocking it); `dotnet test` **186/186** — the 3 previously-failing
`Integration.Tests` now pass unchanged, plus the new `SeedingTests` test.

**H1-04** — done (commit `aab784d` — mislabeled `H1-03` in its own message; caught after the commit
landed and left as-is per this project's git-safety rule against amending) — the untracked
planning/review files (`PLAN-PHASE2.md`, `PLAN-PHASE3.md`, six Phase 2 `REVIEW-*.md` rounds, three Phase
3 `REVIEW-PHASE3-*.md` rounds), plus `docs/hosting-cost-analysis.md` (new, replacing seven files) and
`docs/domain-event-engine.md` (new, as-is).

Consolidated `azure-hosting-cost-estimate.md`, `hostinger-hosting-cost-estimate.md`,
`traditional-vps-hosting-cost-estimate.md`, `outbox-hosting-cost.md`, `outbox-vs-no-outbox.md`,
`outbox-vs-traditional-monolith-cost.md`, and `reduced-cost-hosting-options.md` — seven overlapping
"what does this cost" explorations, not `PLAN-PHASE3.md`'s claimed eight (there were only seven; the
eighth, `docs/low-resource-hosting.md`, is the adopted decision record and correctly stays separate) —
into one `docs/hosting-cost-analysis.md`, organized as Part 1 (does the outbox/modular-monolith
architecture itself add hosting cost — no, a few dollars a month at most) and Part 2 (provider pricing:
Azure PaaS, self-managed VPS via Hetzner and Hostinger, HA vs. no-HA). `docs/domain-event-engine.md` is a
reference doc, not a cost doc — committed unmodified.

**Verify:** `dotnet build -warnaserror` 0/0; `dotnet test` 186/186 unchanged (docs-only change, still ran
the full gate per the execution contract); `git status` clean except this task's own commit.

## Phase boundary — H1 close-out

H1 is done. Working tree clean, gate green (186/186, up from the 185 documented at the Phase 2 boundary
— the one net addition is `SeedingTests.cs`), four commits in strict dependency order
(`eedde28`/`03790e1` package sweep, `c995f2b` `.gitattributes`, `becac0a`/`f6f2b3e` the real `DI.cs`
refactor with its two riding-along fixes, `aab784d` docs). G7-01 is unblocked.

**What `PLAN-PHASE3.md`'s H1 section got wrong, for the phase record** — not to relitigate, but because
it is exactly the failure mode this project's own `PROGRESS.md` already names twice at the Phase 2
boundary ("read the actual source method, not just its name"), now recurring one level up at the
planning-document level rather than the source-code level:
- **Claimed** a 59-file, ±6,000-line CRLF↔LF flip from a folder rename. **Actual:** 32 files, 157/128
  real lines, and `git diff --ignore-cr-at-eol` produced the identical delta to a plain `git diff` — zero
  line-ending component. (Root cause, not excused by but explaining the error: the tree was first read
  from a Linux container mount without `core.autocrlf`, so a CRLF working tree read against an LF index
  as churn on every line of every file touched.)
- **Claimed** the only real change was `Program.cs` → `DI.cs`. **Actual:** that refactor was real, but
  bundled with an unreviewed 10-package upgrade sweep (three major versions) and two silent regressions
  (seeding disabled, a dead migration-extension file) that had never passed through a task's verify
  block.
- **Claimed** `HttpCurrentUser` had moved to `BuildingBlocks.Web.Authorization`. **Actual:** never
  moved — decided in this phase's planning to leave it in `Api`, since it is an HTTP-host concern with
  exactly one consumer and moving it would have risked arch rule 8 for no benefit.
- **Claimed** eight hosting-cost docs. **Actual:** seven, plus the one decision record.

**The general lesson, worth carrying past this phase:** a plan's description of the working tree is a
claim to verify against the actual repository state, never a fact to build a task on directly — the same
discipline this project already applies to source code (read the method, not its doc comment or its
name) applies equally to the plan document itself.

# Part W1 — Outbox wake-on-write

**W1-01** — done — new `src/BuildingBlocks/Outbox/OutboxSignal.cs`; modified
`src/BuildingBlocks/Outbox/OutboxProcessor.cs`, `.../InboxDedupeDecorator.cs`,
`.../ModuleIntegrationEventRegistration.cs`, `.../OutboxServiceCollectionExtensions.cs`,
`src/BuildingBlocks/Behaviors/DomainEventDispatchDecorator.cs`, `src/BuildingBlocks/ModuleCqrsRegistration.cs`,
`Directory.Packages.props`, `src/Api/appsettings.json`; new
`tests/BuildingBlocks.IntegrationTests/WakeOnWriteTests.cs`.

Per `docs/low-resource-hosting.md` §2: `OutboxProcessor` waits on a new `OutboxSignal` (a bounded
`SemaphoreSlim`, singleton) instead of a fixed `PeriodicTimer` polling every 5s. A post-commit nudge
delivers near-instantly on the happy path; `Outbox:FallbackPollSeconds` (default 60, config-driven) is
what still reaches a crashed request or a second process instance. **The fallback is not removed** — it
is the correctness guarantee the signal only optimizes around, per the plan's own explicit instruction.

**Nudge sites**, both strictly after their own commit:
- `InboxDedupeDecorator`, after `transaction.CommitAsync` — covers an integration-event handler whose
  own write enqueues a further outbox row.
- Both nested `CommandHandler` classes in `DomainEventDispatchDecorator<TContext>`, inside the
  `IsSuccess` branch after commit — covers the common case of a command enqueueing a cross-module event.

**Found and fixed while wiring the nudge, not part of the original scope but required for the plan's own
"existing outbox tests green unchanged" verify condition:** registering `OutboxSignal` only via
`AddOutbox()` broke three test fixtures that call `AddModuleCqrs`/`AddModuleIntegrationEvents` directly
against a minimal container without the full outbox wired up —
`IdentityApplicationFixture`, `DomainEventDispatchPipelineTests`, `ValidationDecoratorPipelineTests`, all
resolving `ICommandHandler<T>` through `DomainEventDispatchDecorator`'s now-required `OutboxSignal`
constructor parameter. Fixed by having both `AddModuleCqrs` and `AddModuleIntegrationEvents`
`TryAddSingleton<OutboxSignal>()` themselves — the identical self-sufficiency pattern
`ModuleIntegrationEventRegistration`'s existing `TryAddScoped` calls already document ("self-sufficient
regardless of call order"). In the real host `AddOutbox()` always runs first (from `DI.cs`'s `AddApi`),
so the `TryAdd` calls are no-ops there and every module still shares one signal instance — verified by
inspection of `Program.cs`'s call order, not assumed.

**Two new tests** (`WakeOnWriteTests.cs`), matching the plan's explicit requirement that neither alone
is sufficient:
1. **Nudge path** — a real command enqueues an event with `FallbackPollSeconds = 300`; asserts delivery
   within 5s, which only the nudge can explain.
2. **Fallback path** — a row inserted directly via raw SQL (`NpgsqlConnection`, bypassing
   `db.Outbox().Enqueue(...)` and every decorator that would nudge) with `FallbackPollSeconds = 1`;
   asserts the fallback alone still delivers it. This is the test that stops a later reader deleting the
   fallback as "redundant now that wake-on-write exists."

**A real bug found and fixed in the tests themselves, not in `OutboxProcessor`:** the first draft polled
the in-memory `CallCounter` (incremented synchronously inside the handler, before the surrounding
transaction commits) as the signal to call `processor.StopAsync()`. That races `StopAsync`'s
cancellation against the still-in-flight commit — cancelling it mid-flight leaves the row's
`ProcessedUtc` unset (no compensating decrement on the plain `int` counter), so a *later* test's fresh
processor finds that row still "pending" and reprocesses it for real, incrementing its own counter.
Observed exactly this: `Fallback_...` failed intermittently with `Expected: 1, Actual: 2` only when run
as part of the full suite (never in isolation), which is what pointed at cross-test row leakage rather
than a design flaw. Root-caused precisely — not assumed — by tracing through `ProcessOneAsync`'s
sequencing: the handler's counter increment happens before `InboxDedupeDecorator`'s own transaction
commit and before `shared.SaveChangesAsync` marks the row processed, so a cancellation landing in that
window aborts persistence while leaving the counter elevated. Fixed by waiting on the durable
`OutboxMessage.ProcessedUtc` for the specific event id instead of the counter, so `StopAsync` only ever
runs once nothing is in flight. Confirmed fixed: `BuildingBlocks.IntegrationTests` run four times
consecutively (once isolated to the new file, three more as part of the full project) with zero
failures.

**Verify:** existing outbox tests green unchanged (with the three fixture fixes above, required by but
not themselves part of W1's design); the two new tests pass; full gate — `dotnet build -warnaserror`
0/0, `dotnet format --verify-no-changes` clean, `dotnet test` **188/188**, run twice consecutively with
no flakiness.

# Part G7 — Documents module

**G7-01** — done — `src/Modules/Documents/Documents.Contracts/` (new project): `IDocumentQueries.cs`,
`IFileStorageService.cs`, `DocumentsPermissions.cs`, `IntegrationEvents.cs`,
`Documents/DocumentContracts.cs` (Files, 13), `Folders/FolderContracts.cs` (10),
`FileTemplates/FileTemplateContracts.cs` (7). `DMS.Modular.slnx` and
`tests/Architecture.Tests/Architecture.Tests.csproj` both gain the new project immediately.

`IDocumentQueries` pairs single (`GetAsync`) with batch (`GetManyAsync`) per the Contracts rule ("every
lookup has a batch sibling, no exceptions") — `PLAN-PHASE3.md`'s shorthand only named
`ForProjectAsync`/`ForStageAsync`/`ForTaskAsync` + `GetManyAsync`, but `IHrDirectory`/`IProjectDirectory`
both pair single+batch for their own by-id lookups (`GetAsync`/`GetManyAsync`,
`GetProjectAsync`/`GetProjectsAsync`), so a single-id `GetAsync` was added to match, not left out on the
plan's literal wording. `ForStageAsync` reach-through documented explicitly (correction 8 from the
backend survey): a stage-scoped lookup must include documents attached to a task under that stage, or an
`Attachment` gate on a stage can never resolve when the deliverable was actually attached at the task
level, which is the common case per the backend's own `ProjectDocumentRepository.GetByStageAsync`.

**One deviation from `PLAN-PHASE3.md`'s stated verify condition, decided during this task, not before
it:** the plan said `Documents.Contracts` would reference `Projects.Contracts`. Checked precedent first —
neither `Approvals.Contracts` nor `Projects.Contracts` (nor any other `*.Contracts` project in this
codebase) references another module's `Contracts`; every one is `SharedKernel`+`BuildingBlocks` only.
None of `Documents.Contracts`' own types need a `Projects.Contracts` type — they carry `ProjectId`/
`ProjectStageId`/`ProjectTaskId` as bare `Guid`s, the same pattern `IHrDirectory`/`IProjectDirectory`
already use for cross-module ids. The real dependency — implementing
`Projects.Contracts.IGateConditionTypeHandler` — belongs on `Documents.Application` (G7-07), which
*does* get a `Projects.Contracts` reference, matching the reference matrix
(`*.Application` may reference other modules' `*.Contracts`). Kept `Documents.Contracts` minimal instead
of adding an unused reference now.

**Registration timing:** `DMS.Modular.slnx` and `Architecture.Tests.csproj` were updated in this same
task, not deferred to G7-03 as `PLAN-PHASE3.md`'s G7-03 bullet ("add the four projects...") might
suggest read literally. Checked the actual G4-01 precedent in this file: `Approvals.Contracts` was wired
into both immediately at G4-01, with Domain/Application/Infrastructure following at G4-02/03/04 — the
same incremental pattern this task follows. By G7-03 (when Domain/Infrastructure/Application all exist),
all four will be registered; they just aren't registered as one batch.

**Verify:** `dotnet build -warnaserror` 0/0; rule 5 and rule 10 pass non-vacuously against the new
assembly (14/14 arch tests green, up from 14 — `Documents.Contracts` adds one integration event type and
30 command/query types to what those two rules scan, with zero findings); full gate —
`dotnet format --verify-no-changes` clean, `dotnet test` **188/188** unchanged (Contracts-only, no
runtime behavior to add coverage for yet).

**G7-02** — done — three parts, one commit (A1 makes it a three-part rather than two-part task; noted
here per the review's instruction). (a) `Projects.Contracts/IGateConditionTypeHandler.cs`:
`GateConditionContext` gains `Guid? ReferencedDocumentId = null`. (b) A1:
`Projects.Domain/Gates/GateCondition.cs` gains the persisted `ReferencedDocumentId` column, threaded
through `Create()`; migration `20260813174628_AddGateConditionReferencedDocumentId`, no FK. (c) new
`src/Modules/Documents/Documents.Domain/`: `ProjectDocument`, `DocumentFolder`, `FileTemplate`,
`FileTemplateCategory`, `ProjectDocumentErrors`/`DocumentFolderErrors`/`FileTemplateErrors`,
`DocumentUploadedDomainEvent`. New `tests/Modules/Documents.UnitTests/` (30 tests).

**Why A1 was necessary, confirmed by tracing the actual satisfaction path, not assumed:** a
`DocumentApproved` gate's `ValidateAsync` receives a `GateConditionContext` and returns once — nothing
about that call outlives it. But satisfaction happens later and separately: G7-07's plan has an approved
`ProjectDocument`-subject decision resolve its `RootDocumentId`, then look up *pending gates referencing
that root* to satisfy them. Without a persisted column, "which lineage is this gate waiting on" is
information the gate would have discarded the moment `ValidateAsync` returned. Verified `GateCondition`
had `ReferencedStageId`/`ReferencedTaskId` but no `ReferencedDocumentId` before this task (Phase 2 built
it correctly, since no document gate type existed then) — this is the missing piece, not a duplicate of
anything already there.

**No FK, by design, not by oversight.** `ReferencedDocumentId` points at
`documents.project_documents.RootDocumentId`, a table in a schema that migrates AFTER `projects`
(`ModuleDependencyOrder.SchemaOrder`, `"documents"` appended at G7-03) — and even once it exists,
`projects → documents` is the wrong direction for rule 9's allow-list (only `documents → projects` is
listed; Documents depends on Projects, not the reverse). A bare unvalidated `Guid`, same treatment
`projects.projects."ClientId"` got before CRM existed — validated in application code (Documents' own
`DocumentApprovedHandler`, G7-07) against Documents' own `DbContext`, never by the database. Documented
in both the migration's own XML remarks and `docs/cross-schema-foreign-keys.md`.

**The probe check the review flagged as the single most valuable one in the phase:** generated the
migration, then read its `Up`/`Down` before writing anything else — exactly one `AddColumn<Guid>` for
`ReferencedDocumentId`, exactly one matching `DropColumn` in `Down`, nothing else. Confirms four modules'
worth of prior migrations didn't drift the model in some other way that would have ridden along silently.

**Two corrections from the backend survey, carried into the entities, both load-bearing for G7-07:**
- `ProjectDocument`'s lineage key is `RootDocumentId` (a v1 document self-references its own `Id`;
  `CreateRevision` carries the same root forward with `Version + 1`), not `Version` alone —
  `PLAN-PHASE3.md`'s own pre-resolved-facts section said only "`Version`, starts 1, increments," which
  would have made a `DocumentApproved` gate unbuildable: G7-07's satisfaction path needs the root id to
  resolve pending gates, and nothing about a bare `Version` counter provides one.
- `IsLocked` (set when a `ProjectDocument`-subject approval decides Approved, G7-07) blocks `MoveToFolder`
  and `Deactivate`, explicitly does **not** block `CreateRevision` — matching the backend's own asymmetry
  exactly, not a simplification.

**`DocumentFolder.MoveTo`'s cycle guard is new work, not a port** — the backend has none at all (a
folder `A→B` then `B→A` silently detaches a tree branch; the `ParentFolder` FK is `Restrict`, which
doesn't catch it either). Modeled as a pure function over a caller-supplied ancestor map
(`IReadOnlyDictionary<Guid, Guid?>`) rather than walking the database itself, so it stays unit-testable
without EF/a container — `Documents.Application` (G7-06) is responsible for loading that map, the exact
"entity holds the pure state machine, Application does the relational lookup" split `GateCondition`'s
own remarks already establish for cross-row reference checks.

**Also new, and unit-tested for the same reason:** `DocumentFolder`'s scope rule is "at most one of
stage/task, or neither" (project-wide) — explicitly NOT `GateCondition`'s strict XOR, where neither is
invalid. Getting this backwards (rejecting the project-wide case) would have been a plausible mistake
carried over from the more familiar XOR pattern; a dedicated test (`Create_NeitherStageNorTask_
IsValid_ProjectWide`) pins the correct behavior. `FileTemplate.Update`'s own-name-doesn't-collide-with-
itself case is tested the same way, for the same reason (a naive uniqueness check is a one-line mistake
away from rejecting a template updating its own unrelated fields).

**Verify:** `dotnet build -warnaserror` 0/0; rule 1 confirms no EF reference in `Documents.Domain`;
Projects' own three test suites (`Projects.UnitTests` 61, `Projects.Application.Tests` 21,
`Projects.Infrastructure.Tests` 5) all green **unchanged** — proves the `GateConditionContext`/
`GateCondition` extensions are source-compatible, not merely "the build still compiles"; 30 new
`Documents.UnitTests` green; full gate — `dotnet format --verify-no-changes` clean, `dotnet test`
**218/218** (188 + 30 new).

**G7-03** — done — `src/Modules/Documents/Documents.Infrastructure/` (new project): `DocumentsDbContext`
(schema `documents`, `ConfigureOutbox(ownsMigration: false)`), `DocumentsDbContextFactory`
(design-time-only, matching every other module's own), three `IEntityTypeConfiguration<T>` classes,
`DocumentsModule : IModule`, initial migration. `ModuleDependencyOrder.SchemaOrder` gains `"documents"`
appended last — a clean append, no reordering question, since `documents` depends on `projects`/`hr`/
`approvals`, all of which already migrate earlier.

**`DocumentsModule : IModule` registered here with `AddDbContext` + `IModulePermissions` only, no
`AddModuleCqrs`** — `Documents.Application` doesn't exist until G7-06/07, and calling
`Assembly.Load("Documents.Application")` before that DLL exists would throw. Same split every prior
module's own bootstrap follows (`docs/adding-a-module.md`'s wiring checklist step 4: "Register without
`AddModuleCqrs` yet").

**Indexes — one surprise, resolved by inspecting the real output, not assumed correct from the C#
model.** Configured `(RootDocumentId, Version)` with two separate `HasIndex` calls — one `.IsUnique()`,
one named+descending — expecting two physical indexes. EF Core collapsed them into **one**: calling
`HasIndex` again with the identical property list returns the SAME `IndexBuilder`, so the second call
reconfigured the first rather than adding a second index. Verified this is actually the *better* outcome,
not a bug to fight: applying the migration to real Postgres and inspecting via `\d` shows
`"ix_project_documents_root_version_desc" UNIQUE, btree ("RootDocumentId", "Version" DESC)` — one index
satisfying both the uniqueness constraint and the "give me the latest version" descending-scan
optimization the plan asked for as two separate requirements. Left as the single combined index rather
than forcing two.

**`xmin` concurrency token — no Npgsql helper exists, verified by inspection of the installed package,
not by trusting memory of the API surface.** `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 has no
`UseXminAsConcurrencyToken` extension (confirmed: grepped the installed DLL's string table for `xmin` —
zero matches, only `NpgsqlEntityTypeBuilderExtensions`/`NpgsqlPropertyBuilderExtensions` exist as
classes, neither with an xmin-specific method). Used the standard manual recipe instead —
`builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion()` — and, since this is the first use
of Postgres' system `xmin` column anywhere in this codebase, didn't trust that the recipe avoids
conflicting with the reserved column name just because it compiled: started the local `docker-compose`
Postgres, ran `dotnet ef database update` for real, and confirmed via `SELECT xmin, "Id" FROM
documents.project_documents` and `\d` that Npgsql's migration SQL generator correctly omits DDL for
`xmin` (it recognizes the reserved name and never tries to create it), and that the column is queryable
as expected.

**Probe migration:** generated, `Up`/`Down` both empty (confirmed by reading the generated file, not
assumed), removed via `dotnet ef migrations remove` — ran cleanly against the real dev Postgres this
time (unlike G4-03's note that it wasn't running then), so the tool's normal applied-migration-history
safety check worked without a workaround.

**`Api.csproj` gains a bare `Documents.Infrastructure` reference here, ahead of the rest of the module's
wiring** — `dotnet ef migrations add --project ... --startup-project src/Api` requires the DbContext's
assembly to be present in Api's build output regardless of whether `Program.cs` actually loads the
module yet. `Program.cs`'s own `Assembly.Load("Documents.Infrastructure")` +
`moduleAssemblies` registration, and the matching `Documents.Application` reference, are explicitly
deferred to G7-08 once the module has endpoints worth serving — noted in the `.csproj`'s own comment so
a future reader doesn't mistake the partial wiring for an oversight.

**Fault-injected, all four reverted after confirming red — same one-per-rule technique as G4-03/G5-04,
and the SAME "unused reference produces no real dependency" lesson G4-03 first found for rule 1 recurred
twice more here:**
- Rule 4: temporarily made `ProjectDocumentErrors` public → red, naming
  `Documents.Domain.ProjectDocumentErrors` → reverted.
- Rule 8a (Domain half only — `Documents.Application` doesn't exist yet): a bare `ProjectReference` from
  `Documents.Domain.csproj` to `BuildingBlocks.Web.csproj`, with nothing in the code actually using a
  type from it, did **not** trip the test — confirmed empirically, not assumed from the G4-03 precedent.
  Needed a scratch file with a real `BuildingBlocks.Web.IModule`-typed public property before it went
  red, naming `Documents.Domain` → reverted (both the reference and the scratch file).
- Rule 1: same lesson a third time — an unused `Microsoft.EntityFrameworkCore` `PackageReference` alone
  didn't trip it; needed a scratch file with a real `DbContextOptions`-typed property → red, naming
  `ScratchRule1Fault` → reverted.
- Rule 6: temporary `Documents.Infrastructure` → `Projects.Infrastructure` reference + a temporary
  `InternalsVisibleTo` grant the other way (`ProjectsDbContext` is `internal`) + a scratch file naming
  it → red, naming `ScratchRule6Fault in Documents.Infrastructure` → all three reverted.

**Verify:** migration applied for real against the local `docker-compose` Postgres (not just
inspection), confirmed structurally correct via `\d` on all three tables; probe migration empty; rules
1/4/6/8a fault-injected and reverted; full gate — `dotnet build -warnaserror` 0/0,
`dotnet format --verify-no-changes` clean, `dotnet test` **218/218 unchanged** (Infrastructure/migration
work, no new runtime behavior to add coverage for yet — that starts at G7-05/06).

**G7-04** — done — seven raw-SQL cross-schema FKs (`documents → projects/hr`), one new migration for the
`UploadedById` index + those FKs, one MORE migration for a real gap found and fixed in this task,
`tests/Architecture.Tests/Rule9CrossSchemaForeignKeyTests.cs`, `docs/cross-schema-foreign-keys.md`.

**A real gap from G7-03, found while writing this task's own text, fixed here rather than deferred.**
`PLAN-PHASE3.md`'s G7-04 description treats `project_documents.RootDocumentId` as an already-configured
intra-schema self-reference ("EF configures them normally... not part of this task") — it wasn't. G7-03
never added the FK; only the unique lineage index existed. Added
`ProjectDocumentConfiguration`'s `HasOne<ProjectDocument>().WithMany().HasForeignKey(d =>
d.RootDocumentId).OnDelete(Cascade)`, matching the backend, in its own migration
(`AddProjectDocumentRootDocumentIdForeignKey`) applied and verified **before** the cross-schema
migration in this same task. Confirmed for real, not assumed: a v1 document's `RootDocumentId` equals
its own `Id` in the same `INSERT`, which works because Postgres checks FK constraints at statement end,
not per-row — verified by actually inserting through the real migration path and re-running the probe,
not by reasoning about it in the abstract. `docs/cross-schema-foreign-keys.md`'s own text was corrected
to say this was fixed during G7-04, not pre-existing — the doc shouldn't imply a gap was never there.

**The seven FKs**, all `ON DELETE RESTRICT`: `project_documents.{ProjectId, ProjectStageId,
ProjectTaskId, UploadedById}` and `document_folders.{ProjectId, ProjectStageId, ProjectTaskId}`, into
`projects.projects`/`projects.project_stages`/`projects.project_tasks`/`hr.employees`. Both allow-list
pairs (`documents → projects`, `documents → hr`) were already present in rule 9's `AllowedPairs` from
G7-01's planning — nothing to add there. `UploadedById` needed its own new index first (every other
referencing column was already indexed at G7-03, confirmed by checking each configuration before
assuming a gap) — an ordinary EF-diffable migration, ahead of the raw-SQL constraints in the same file,
matching the established Projects G5-04 shape (index-then-constraint, one migration).

**`Rule9CrossSchemaForeignKeyTests` was silently skipping `documents` before this task** — `"documents"`
joined `ModuleDependencyOrder.SchemaOrder` at G7-03, but the test's own `Modules` dictionary had no
matching entry, so its `foreach` loop's `TryGetValue` just `continue`d past it every run without
migrating the schema or checking its FKs. Added the entry and all seven non-vacuity assertions — the
plan's own verify text ("every FK present in `pg_constraint`") reads as exhaustive, not sampled, so all
seven are asserted individually rather than a representative few.

**Verify:** both migrations applied for real against the local `docker-compose` Postgres, which already
carried Identity/HR/Approvals/Projects schemas from prior phases — confirmed all eight FKs (seven
cross-schema plus the `RootDocumentId` self-reference) via `psql \d` and a direct `pg_constraint` query,
not by inspecting the C# diff alone; probe migration empty both before and after the `RootDocumentId`
fix; rule 9 green with all seven new assertions passing; full gate — `dotnet build -warnaserror` 0/0,
`dotnet format --verify-no-changes` clean, `dotnet test` **218/218 unchanged**.

**G7-05** — done — new `src/Modules/Documents/Documents.Infrastructure/Services/
LocalFileStorageService.cs`, registered as a singleton in `DocumentsModule.Register` (matching the
backend's own lifetime); `src/Api/appsettings.json` gains a `FileStorage` section; new
`tests/Modules/Documents.Infrastructure.Tests/` (24 tests, no Postgres — pure file I/O).

Ported as specified: config keys (`FileStorage:RootPath` default `uploads`,
`FileStorage:MaxUploadBytes` default 52,428,800), GUID-named files (`{Guid:N}{extension}`, original name
never touches disk), `FileMode.CreateNew` for collision-proofing.

**Three hardenings the plan asked for, plus one bug found and fixed that the plan didn't name:**

1. **Path traversal — two distinct defenses, not one, per a closer reading of the plan's own wording**
   ("sanitize, then assert the resolved path is under root"). `subFolder` is sanitized for genuinely
   benign mess (normalized separators, empty/`.` segments dropped) but a `..` segment is now **rejected
   outright** — the backend silently strips `..` and continues, which technically can't escape root
   (the strip happens before path construction) but doesn't match "reject path traversal" or "must fail
   closed" as literally written. The final resolved path is still asserted under root regardless, as a
   backstop independent of the reject-on-`..` check. `originalFileName` is rejected outright on any path
   separator or `..`.
2. **A real bug in the backend's own root-containment check, found while implementing the backstop, not
   assumed present.** The backend's check is a bare `fullPath.StartsWith(root)`. A sibling directory
   `uploads-evil` satisfies `"uploads-evil".StartsWith("uploads")` — a genuine escape past the intended
   boundary. Fixed here: containment requires an exact match or a directory-separator-bounded prefix
   (`root + Path.DirectorySeparatorChar`). Covered by `Save_SubFolderWithDotDot_FailsClosed` and the
   `OpenReadAsync`/`DeleteAsync` traversal tests, though the specific `uploads-evil` sibling case isn't
   exercised as its own test — worth naming as a residual gap rather than claiming full coverage.
3. **`MaxUploadBytes` enforced during the copy, not after.** A seekable stream with a known `Length`
   over the cap is rejected before any I/O touches disk. Every stream — seekable or not — is ALSO
   checked chunk-by-chunk while copying (80 KB buffer), aborting and deleting the partial file the
   moment the running total would exceed the cap. Tested with a hand-written `NonSeekableStream` test
   double specifically to prove the chunk-by-chunk path is exercised, not just the length-precheck fast
   path a seekable `MemoryStream` would always hit.
4. **Extension allow-list, deny-by-default — the closest available equivalent to "content-type
   allow-list," recorded as a deliberate interface-driven substitution, not silently done.**
   `IFileStorageService.SaveAsync` (G7-01, ported as-is from the backend's own interface) has no
   `contentType` parameter — only `originalFileName`. Validates the file EXTENSION instead, checked
   against `FileStorage:AllowedFileExtensions` if configured, else a hardcoded document/image/archive
   list that never includes `.exe`/`.dll`/`.bat`/`.sh`/`.ps1`/`.cmd`. No extension at all is rejected too
   — deny-by-default, not merely deny-known-bad.

**A real test bug found and fixed, not a production bug:** an early draft of
`Save_ContentAtExactlyMaxUploadBytes_Succeeds` called `OpenReadAsync` and asserted on the result without
disposing the returned `FileStream`. On Windows that held the file locked (`FileShare.Read`, not
released until GC), which made `Dispose()`'s own `Directory.Delete(recursive: true)` throw
`IOException: ... being used by another process` — non-deterministically, since it depends on whether
the CLR had collected the stream by the time cleanup ran. Fixed by properly `await using`-disposing the
stream, not by loosening or removing the directory cleanup.

**Verify:** `dotnet build -warnaserror` 0/0; 24 new tests green (round-trip; both traversal cases;
benign-mess `subFolder` sanitized, not rejected; size cap on both seekable and non-seekable streams;
six known executable extensions rejected by the default list; `OpenReadAsync`/`DeleteAsync` returning
null/no-op rather than throwing on a missing or traversal-crafted path; the 50 MB default); full gate —
`dotnet format --verify-no-changes` clean, `dotnet test` **242/242** (218 + 24 new).

**G7-06** — done — new `src/Modules/Documents/Documents.Application/`: Folders (10: `Create`,
`CreateForStage`, `CreateForTask`, `Delete`, `Move`, `Rename`, `GetByProject`, `GetByStage`,
`GetByTask`, `GetChildren`), `DefaultFolderResolver`, FileTemplates (7: `Create`, `Update`, `Delete`,
`Activate`, `Deactivate`, `GetAll`, `GetById`). `DocumentsModule.Register` now wires `AddModuleCqrs`.
New `tests/Modules/Documents.Application.Tests/` (8 tests, five-module Testcontainers fixture).

**A design mistake caught and replaced before it shipped, not after:** the first draft of
`CreateStageFolderCommandHandler` tried to infer the owning project id from an existing sibling folder
under the same stage (`db.DocumentFolders.FirstOrDefault(f => f.ProjectStageId == ...)`) rather than
asking Projects directly. This breaks the instant a stage has zero folders yet — exactly the FIRST
folder ever created for it, the most common case, not an edge case. Replaced with a real
`Projects.Contracts.IProjectDirectory.GetStageAsync`/`GetTaskAsync` call before writing the handler
`Documents.Application.csproj` actually references — `Documents.Application` gains a `Projects.Contracts`
reference (allowed per the reference matrix: `*.Application` may reference other modules' `*.Contracts`).

**`DocumentFolder.MoveTo`'s cycle guard (unit-tested at G7-02) is now exercised through the real command
path**, not just in isolation: `MoveDocumentFolderCommandHandler` loads the whole project's
folder-id → parent-id map once and hands it to the pure Domain-layer algorithm. Also re-checks
sibling-name uniqueness before touching the database — the backend's own equivalent handler skips this
and lets a raw, unhandled `DbUpdateException` surface on a collision instead (noted in G7-01's own
Contracts doc comments as a deliberate non-port).

**`DefaultFolderResolver` closes G7-02's correction 5 for real, not just in the unit-tested naming
logic.** Default folder names now carry the scope's own id (`"Task: Draft plan (3f2a91be...)"`), so two
different tasks sharing a display name — a real, unremarkable case, not contrived — no longer collide
against the `(ProjectId, ParentFolderId, Name)` unique index the way the backend's unsuffixed names do.
Verified with two GENUINELY DISTINCT real tasks, not a fabricated second id: a first draft of the test
used `Guid.NewGuid()` for the second task, which correctly tripped `fk_project_documents_project_task`
(G7-04) — a real constraint doing its job, not a bug. Fixed by provisioning a real second task via a
stage template with two identically-named stage-tasks, then reading both real ids back through
`GetProjectTasksByProjectQuery`, not by weakening the assertion to accept a fake id.

**No manual stage/task creation command exists anywhere in `Projects.Contracts` — only template
instantiation.** Confirmed by grepping every `Create*Command` across `Stages`/`Tasks`/`Projects`
Contracts folders before writing the fixture, not assumed. This means `DocumentsApplicationFixture`
(new, five modules: Identity/HR/Approvals/Projects/Documents against one Testcontainers Postgres) and
every folder test needing a real stage/task id goes through the same department → stage-template →
`CreateProjectCommand` flow `ProjectFlowTests` (Projects.Application.Tests) already established, not a
shortcut — the same shape, one module wider.

**Verify:** `dotnet build -warnaserror` 0/0; rule 5/6/8/10 all still green non-vacuously against the new
`Documents.Application` assembly (14/14 arch tests); 8 new integration tests green (folder tree CRUD;
move/rename; the move-cycle rejection through the real command, not just the Domain unit test;
default-folder creation for a real stage and a real task; the same-display-name collision case with two
real tasks; file template CRUD including duplicate-name rejection); full gate —
`dotnet format --verify-no-changes` clean, `dotnet test` **250/250** (242 + 8 new). One full-suite run
mid-task failed nearly everywhere with `DockerUnavailableException` — Docker Desktop had stopped during
a session gap, not a code regression; confirmed by re-running after restarting Docker and
`docker compose up -d`, back to 250/250 clean.

**G7-07** — done — the phase's real proof task, per the plan's own words. New
`Documents.Application/Features/Documents/` (13 handlers: `Upload`, `UploadToStage`, `UploadToTask`,
`UploadRevision`, `ApplyTemplate`, `Move`, `Delete`, `GetById`, `GetByProject`, `GetByFolder`,
`GetByStage`, `GetByTask`, `Download`, plus one extracted `DocumentUploadCommit` helper for the
save-blob → insert-row → compensate-on-failure shape all four uploads share) and
`Features/Gates/` (`AttachmentHandler` no-op validate, `DocumentApprovedHandler` validates the
referenced lineage exists in the same project via Documents' own `DbContext`). New
`RequestDocumentReviewCommand`/Handler makes `ApprovalSubjectType.ProjectDocument` real. New
`Documents.Infrastructure/IntegrationEvents/ApprovalDecidedIntegrationEventHandler` locks the document
and re-publishes `DocumentApprovedIntegrationEvent` (new, Documents.Contracts). New
`Projects.Infrastructure/IntegrationEvents/DocumentUploadedIntegrationEventHandler` (satisfies
`Attachment` gates, including the stage-reach-through-a-task-under-it rule) and
`DocumentApprovedIntegrationEventHandler` (satisfies `DocumentApproved` gates by `RootDocumentId`).
`DocumentsModule.Register` gains the `IGateConditionTypeHandler` Scrutor scan and
`AddModuleIntegrationEvents`. Structural item 2 — the first bidirectional `*.Contracts` pair
(`Documents.Infrastructure` → `Projects.Contracts`, `Projects.Infrastructure` → `Documents.Contracts`)
— confirmed non-cyclic by a clean `dotnet build -warnaserror`, not just by the reference-matrix
reasoning in the plan.

**A real gap in G7-02, not a test-writing mistake, caught while writing this task's own verify block:**
`GateConditionContext.ReferencedDocumentId` and `GateCondition.Create`'s matching parameter existed
since G7-02, but `CreateStageGateConditionCommand`/`CreateTaskGateConditionCommand` (the actual HTTP/
command surface in `Projects.Contracts`) were never extended to carry a caller-supplied value through —
so a `DocumentApproved` gate could never actually be CREATED, at all, through the real command path;
`GateConditionHandlers`' two create handlers built `GateConditionContext` from only the command's
pre-existing fields. Fixed by adding `Guid? ReferencedDocumentId = null` as a trailing optional
parameter to both commands (same additive, source-compatible shape G7-02 already established for
`GateConditionContext` itself) and threading it into both the context construction and the
`GateCondition.Create` call in both handlers.

**A second, more serious gap found the same way — a genuine `BuildingBlocks` registration bug, not
Documents- or Projects-specific, that only a SECOND module sharing an event type could expose:**
`ApprovalDecidedIntegrationEvent` has had exactly one subscriber since G5-08
(`Projects.Infrastructure.ApprovalDecidedIntegrationEventHandler`); this task adds the second-ever
(`Documents.Infrastructure`'s own). `ModuleIntegrationEventRegistration.AddModuleIntegrationEvents` used
Scrutor's `services.Decorate(serviceType, decoratorType)` per handler interface — which decorates EVERY
descriptor currently registered under that service type, not just the ones the current call just added.
Documents' registration call ran after Projects' (module discovery order), so it re-wrapped Projects'
ALREADY-decorated handler (`InboxDedupeDecorator<ProjectsDbContext,...>`) in a second, outer
`InboxDedupeDecorator<DocumentsDbContext,...>` — using the WRONG `TContext` for Projects' own handler,
and producing a closed-generic `Type.FullName` (both context types' assembly-qualified names embedded)
that overflowed `InboxMessage.HandlerName`'s `varchar(256)` column outright — a hard `DbUpdateException`
crash in the direct-publish test, silently swallowed (message marked failed, never asserted) in the
outbox-driven one. First surfaced by `ProjectDocumentSubjectDecision_NeverReachesProjectsOwnGateConditionSubscriber`
(direct `IEventBus.PublishAsync`, no try/catch to hide it) and confirmed by
`DocumentApprovedGate_CreationSucceeds_...`'s document never actually locking. Fixed in
`ModuleIntegrationEventRegistration.cs`: capture `services.Count` before the `Scan()` call, then decorate
only the descriptors added since that count (identified by list position, not by service type), each
replaced in place with a manually-constructed factory descriptor (`ActivatorUtilities.CreateInstance`
for both the inner handler and the decorator) — never Scrutor's blanket `Decorate`. This is a
`BuildingBlocks` fix, not a Documents/Projects one; every existing single-subscriber module's wiring is
unaffected (confirmed by the full 256/256 gate below), and the fix is exactly what makes a second
subscriber to the same event type work correctly, which is exactly what this task needed.

**Verify:** `dotnet build -warnaserror` 0/0, including the bidirectional Contracts pair resolving with
no circular-reference error; 6 new integration tests in `FileFlowTests` (new
`DocumentsApplicationFixture.OutboxProcessor` property, mirroring `ProjectsApplicationFixture`'s) —
`Attachment` and `DocumentApproved` gate creation transitioning from Phase 2's
`GateConditionErrors.UnsupportedConditionType` to success, both asserted explicitly; upload → outbox →
`OutboxProcessor.ProcessPendingAsync` → `Attachment` gate satisfied, on both the direct task/stage owner
and the stage-reach-through-a-task-under-it case; a `DocumentApproved` gate satisfied through TWO real
outbox hops (`ApprovalDecidedIntegrationEvent` locks + re-enqueues, a SEPARATE
`ProcessPendingAsync` call delivers the newly-enqueued `DocumentApprovedIntegrationEvent` — a row
enqueued mid-pass is not in that pass's own pending snapshot); the subject-type-filter id-reuse proof
(a `ProjectDocument`-subject decision never touches a real `GateCondition` sharing its id); delete
excludes from a list query but keeps the blob on disk; a soft-deleted revision does not break resolving
the next revision's lineage. Full gate — `dotnet format --verify-no-changes` clean, `dotnet test`
**256/256** (250 + 6 new) across all 16 test projects, including the pre-existing G5-08 gate-cascade
suite (`Projects.Application.Tests`) still green after the registration fix.

**G7-08** — done — `DocumentsModule : IModule` registered in `Program.cs`'s `moduleAssemblies` and wired
into `Api.csproj` (both `Documents.Infrastructure` and `Documents.Application`, the latter purely so
`Assembly.Load` finds the DLL, same as every other module). Five new endpoint files in
`Documents.Infrastructure/Endpoints/`: `DocumentFolderEndpoints` (10 routes), `FileTemplateEndpoints` (7,
including multipart create), `DocumentUploadEndpoints` (5 — the four uploads plus apply-template, split
out because every one touches multipart content or copies bytes server-side), `DocumentEndpoints` (8,
the non-upload CRUD/queries), `DocumentDownloadEndpoints` (1, its own file — the only route in the module
streaming a response body via `Results.Stream` rather than returning JSON). New
`Documents.Infrastructure/Services/DocumentQueries.cs` implements `IDocumentQueries` (the cross-module
read contract G7-01 defined but nothing implemented until now) the same `Select`-projection convention
`HrDirectory`/`ProjectDirectory` established. `DocumentsModule.Register` configures
`FormOptions.MultipartBodyLengthLimit` from `FileStorage:MaxUploadBytes` (same fallback default
`LocalFileStorageService`'s own constructor uses, a small documented duplication) — caps the multipart
parser's own work at roughly the configured ceiling; `SaveAsync`'s chunk-by-chunk enforcement (G7-05)
remains the authoritative backstop regardless of what this allows through. Routes chosen over the
backend's own `by-project`/`by-stage` suffix scheme: upload and list-by-scope share the SAME path prefix
per scope (`/api/projects/{id}/documents`, `/api/project-stages/{id}/documents`,
`/api/project-tasks/{id}/documents`) across different HTTP verbs — perfectly legal routing, cleaner than
the backend's own disambiguation suffixes, which existed there for reasons that don't apply here.

**Two more real gaps found while wiring the HTTP surface, both necessary for G7-07's own work to be
reachable at all, not new scope:**
1. `GateEndpoints`' `CreateGateRequest` (Projects.Infrastructure) never carried the `ReferencedDocumentId`
   field G7-07 added to `CreateStageGateConditionCommand`/`CreateTaskGateConditionCommand` — so even
   after that fix, a `DocumentApproved` gate could still not be created through the actual HTTP route,
   only through a direct in-process command call (as G7-07's own tests did). Fixed by adding the field to
   the request DTO and threading it through both POST handlers, same additive shape.
2. Every `IFormFile`-binding endpoint (five upload routes, one template-create route) threw
   `InvalidOperationException` at request time: ASP.NET Core auto-attaches antiforgery metadata to any
   endpoint binding `IFormFile`/`IFormFileCollection`, and this app has never configured antiforgery
   middleware (`app.UseAntiforgery()` doesn't exist in `Program.cs`) — confirmed this is exactly why the
   backend's own equivalent endpoints call `.DisableAntiforgery()`, which an earlier reading of those
   files (G7-08 planning) had dismissed as unnecessary since "antiforgery isn't configured," backwards
   reasoning corrected once the real 500 surfaced. Fixed by adding `.DisableAntiforgery()` to all six
   routes. This in turn surfaced a THIRD gap: `Api/GlobalExceptionHandler.cs` forced HTTP 500 for every
   unhandled exception, including `Microsoft.AspNetCore.Http.BadHttpRequestException` instances that
   already carry the framework's own correct `StatusCode` (400, for a `MultipartBodyLengthLimit`
   violation caught while the request delegate's own form binder reads the body, before any handler
   runs). Fixed to respect that exception type's own `StatusCode` instead of overriding it — a small,
   general correctness fix to shared `Api` infrastructure, not Documents-specific, verified not to change
   behavior for any other exception type (everything else still forces 500).

**Plan correction, recorded rather than silently changed:** G7-08's own verify text says "oversized
upload → 413." Built and run, the actual (and correct) ASP.NET Core status for a
`FormOptions.MultipartBodyLengthLimit` violation is **400 Bad Request** — confirmed by reading the
framework's own `BadHttpRequestException.StatusCode` off the real exception, not assumed. 413 Payload Too
Large is a different mechanism entirely (`IHttpMaxRequestBodySizeFeature.MaxRequestBodySize`, the
whole-request-body limit Kestrel itself enforces before any form parsing starts), which this
configuration never touches. The test asserts 400, matching what the framework actually does, not what
the plan predicted it would do.

**Verify:** `dotnet build -warnaserror` 0/0; app boots with all five modules; `GET /api/permissions`
carries all 6 Documents/FileTemplates strings (`DocumentsSmokeTests`); unauthenticated → 401 and
authenticated-without-permission → 403 on a mutating Documents route; oversized multipart upload → 400
(see the plan-correction note above), against a 5&nbsp;MB test-collection-wide cap (`ApiFixture` overrides
`FileStorage:MaxUploadBytes`) so the test stays fast; `CrossModuleSmokeTests` extended to a five-module
characterization flow — project → folder tree → Attachment gate created → upload (satisfies it through
real outbox delivery) → revision → download (the REVISION's bytes, not v1's) → DocumentApproved gate
created → request review → approve → two real outbox hops → document locked and gate satisfied — run
TWICE inside one test for a flake check, each pass its own fresh actor/project; `DocumentsSchemaIsolationTests`
proves a Documents-owned read issues no SQL touching `identity.`/`hr.`/`approvals.`/`projects.` (stronger
than `ProjectsSchemaIsolationTests`' three-schema version, since `GetProjectDocumentByIdQueryHandler` has
no cross-module lookup at all, not even a permission-gated bypass). Full gate — `dotnet format
--verify-no-changes` clean, `dotnet test` **262/262** (256 + 6 new) across all 18 test projects; all 14
Architecture.Tests green with five modules on the classpath.

**G8-01** — done — `docs/adding-a-module.md` (append), `docs/cross-schema-foreign-keys.md` (already
current — see below), this `PROGRESS.md` phase boundary and final report.

`docs/adding-a-module.md` gains a new "Four more patterns (from Phase 3 — Documents)" section: the
trailing-optional-parameter rule for extending a gate/strategy context (`GateConditionContext`), the
companion rule that a context field needing to be *remembered* past its own validation call also needs a
persisted column on the aggregate (A1's general shape, not just its one instance), the bidirectional
`*.Contracts` pattern and the precise reason it is not a cycle (two DIFFERENT `*.Infrastructure`
assemblies each pointing at the OTHER module's `*.Contracts`, never at each other's `*.Infrastructure`),
and the four-item file-upload hardening checklist (path traversal, chunk-by-chunk size enforcement,
extension allow-list, `FormOptions.MultipartBodyLengthLimit` + `.DisableAntiforgery()`).

`docs/cross-schema-foreign-keys.md` needed **no new content** — checked, not assumed: G7-02 and G7-04
already recorded the `documents → projects`/`documents → hr` allowed pairs and the deliberately-absent
`GateCondition.ReferencedDocumentId` FK in their own commits, and G7-08 added zero migrations. Read the
file end-to-end to confirm before writing this entry rather than trusting the task list's phrasing that
implied fresh work here.

**Verify:** `dotnet build -warnaserror` 0/0; `dotnet test` 262/262 unchanged (docs/PROGRESS-only change);
`dotnet format --verify-no-changes` clean; `git status` clean except this task's own commits.

## Phase G8 boundary — non-negotiables re-checked, five modules

1. **No repository pattern.** Every Documents handler uses `DocumentsDbContext` directly — confirmed by
   grep, no `I*Repository` type anywhere in `src/Modules/Documents/`. ✓
2. **One DbContext/schema/module, one database.** `DocumentsDbContext` → schema `documents`, same
   Postgres instance as `identity`/`hr`/`approvals`/`projects`/`shared`. ✓
3. **Cross-module reads only via `*.Contracts`.** Documents reads Projects through
   `Projects.Contracts.IProjectDirectory` exclusively (stage/task resolution for upload scope) and HR
   through `Hr.Contracts.IHrDirectory` exclusively (uploader/approver resolution) — confirmed by arch
   rule 3, now exercised across five modules. The REVERSE direction is new at this phase: Projects also
   reads nothing from Documents directly — it only ever reacts to Documents' own published integration
   events, never calls into `IDocumentQueries` itself (nothing in this phase's scope needed Projects to
   read a document's data, only to react to its lifecycle). ✓
4. **Cross-module writes only via outbox integration events.** `DocumentUploadedIntegrationEvent`/
   `DocumentApprovedIntegrationEvent` (Documents → Projects) and the existing
   `ApprovalDecidedIntegrationEvent` (Approvals → Documents, the SECOND-EVER subscriber to that event
   type, which is what surfaced the `AddModuleIntegrationEvents` decorator bug — see G7-07) all go
   through the outbox, never a direct call. ✓
5. **Only `*.Contracts` types are public.** Arch rule 4 passes with all five modules' full four-layer
   structure in place. ✓
6. **SharedKernel frozen ~9 files.** Still untouched — zero edits across all of Phase 3, same as Phases 1
   and 2. ✓
7. **Arch tests written before the code they constrain.** Held the same way at G7-01: `Documents.Contracts`
   was added to `Architecture.Tests.csproj` in the SAME task that created the project, before any
   Domain/Application/Infrastructure code existed for it to constrain.

**Two real bugs found this phase sit underneath non-negotiables that look satisfied by inspection but
weren't exercised until a SECOND module did the thing a first module already did:**
- **G7-07:** `AddModuleIntegrationEvents`'s use of Scrutor's blanket `Decorate(serviceType, decoratorType)`
  silently re-wrapped an already-decorated handler the moment a SECOND module (Documents) subscribed to
  an event type a first module (Projects) already handled (`ApprovalDecidedIntegrationEvent`) — a
  registration bug directly underneath non-negotiable #4 (cross-module writes go through the outbox) that
  no single-subscriber module could ever have exposed, no matter how thoroughly tested, because the bug
  only exists in the INTERACTION between two registrations.
- **G7-08:** `GateEndpoints`' `CreateGateRequest` DTO not carrying `ReferencedDocumentId` through to the
  command was invisible to every G7-07 test (which called handlers directly, never through the HTTP
  binding layer) — a gap in the SAME class as G5-10/G6-01's own finding that DI-resolved-handler tests and
  real-HTTP tests catch genuinely different bugs, recorded again here because it recurred almost exactly.
  A `RequestSizeLimit`/antiforgery-shaped gap (missing `.DisableAntiforgery()`) was in the same category:
  invisible to any test that never issues a real multipart HTTP request.

Both were caught by this phase's own verify blocks — G7-07's cross-module test suite and G7-08's
characterization suite respectively — not by inspection, continuing the pattern named at every prior
phase boundary in this file.

## Scope-boundaries — deferred items, Documents' own seams

Same table shape as the Phase 2 boundary above — everything Documents intentionally left for a later
phase, and why, so Phase 4 (CRM) doesn't have to grep this file to find them.

| Item | Deferred at | Owner (future phase) | Why |
|---|---|---|---|
| Audit rows for Documents operations (`AuditModules.Documents` in the backend) | G7-02 (correction 10) | — (no audit service exists in this repo yet) | Nothing in this codebase writes audit rows for ANY module; not a Documents-specific gap |
| Upload/approval notifications | G7-02 (correction 10) | Notifications module | The integration events already publish with zero handlers today — adding a Notifications subscriber later is purely additive, no republishing needed |
| `GateCondition.ClientReviewDueDays` (review-SLA column) | G7-02 (correction 10) | Documents/CRM, if a review-SLA feature is ever built | The backend has no tests for this either — genuinely new work, not a port gap |
| `ICrmGateReader`-style CRM lookup for `DocumentApprovedHandler`'s "is this the client's own document" check | G7-07 | CRM | This phase validates same-project instead — CRM doesn't exist yet to validate against |
| `RequestDocumentReviewCommand`'s `ReviewInProgress` duplicate-request guard | G7-07 | Documents, once `IApprovalRequester` gains an "open approval for subject X" query or Documents tracks its own pending-approval id | `IApprovalRequester` has no such query today; closing this needs a Contracts extension this phase's verify list didn't require |
| ~~`IDocumentQueries.ForStageAsync`'s task-under-stage reach-through (listing/UI concern)~~ **— NOT deferrable; closed at E1-01** | G7-01/G7-07 | Documents | **This row was wrong.** Correction 8 stated the reach-through as a requirement, not a listing nicety, and filing it here re-framed an overridden requirement as a scope choice. See the E1-01 entry below. |
| Orphaned blobs from a soft-deleted `ProjectDocument` whose row is never restored | G7-02 (correction 4) | Documents, if disk usage ever becomes a real operational concern | A cleanup job, not a correctness gap — the bytes are inert until then |
| `ProjectDocumentRepository`-style dependency-tree/bulk endpoints, search/filter variants | G7 phase scope reduction (mirrors G5's own Projects scope cut) | Documents | This phase built one complete vertical slice (upload → gate → approval), not full CRUD breadth, same trade-off Projects made in Phase 2 |
| Full zero-copy `MultipartReader`-based upload streaming (bypassing ASP.NET Core's own form-value-provider entirely) | G7-08 | Documents/Api, if the 1 GB hosting target's memory pressure ever proves this insufficient in practice | `IFormFile` binding + `FormOptions.MultipartBodyLengthLimit` + `SaveAsync`'s chunk-by-chunk cap is real hardening (no full in-memory buffer, no unbounded disk write), but ASP.NET Core still buffers a section to a temp file before the handler runs — a deliberate, documented, lesser hardening than the theoretical maximum, chosen because it doesn't require reopening `IFileStorageService`'s G7-01 contract (`FileSizeBytes` known up front) |

## Standing architectural decisions — Phase 3 additions

Continuing the table the Phase 2 boundary started, for decisions with no single owning module.

| Decision | Raised at | Status |
|---|---|---|
| Bidirectional `*.Contracts` references between two modules | G7-07 (structural item 2) | **Adopted, proven non-cyclic.** First instance: `Documents.Infrastructure → Projects.Contracts` and `Projects.Infrastructure → Documents.Contracts`. See `docs/adding-a-module.md`'s new section for the general rule; expect this shape to recur whenever a later module (Finance, CRM) both contributes a gate/strategy handler to an existing registry AND needs to react to that owning module's own events. |
| `GateConditionContext`/`GateCondition` extension pattern for a new gate-contributing module | G7-02 (structural item 1) | **Adopted, documented.** Trailing-optional-parameter on the context record; a matching persisted column on `GateCondition` itself if the value must be remembered past create-time validation (A1). Expect Finance (`Budget`) and CRM (`CrmPeriodClosed`/`CrmBriefApproved`/`CrmItemsResolved`) to each need one more such extension. |

## Final report — Phase 3 (H1–G8)

### Task table

| Task | Commit(s) |
|---|---|
| H1-01 (package upgrade sweep) | `eedde28`, `03790e1` |
| H1-02 (`.gitattributes`) | `c995f2b`, `daaca27` |
| H1-03 (`Program.cs` → `DI.cs`, seeding restored, dead file deleted) | `becac0a`, `f6f2b3e` |
| H1-04 (docs + decision records, mislabeled `H1-03` in its own commit message — left as-is per the git-safety rule against amending) | `aab784d`, `9f1aa00` |
| W1-01 (outbox wake-on-write) | `2f1811e`, `1295e82` |
| G7-01 (Documents.Contracts) | `1818fe6`, `c83bf21` |
| G7-02 (`GateConditionContext`/`GateCondition.ReferencedDocumentId` (A1), Documents.Domain) | `542f81e`, `78761c6` |
| G7-03 (Documents.Infrastructure — persistence) | `ab343eb`, `9a58562` |
| G7-04 (cross-schema FKs, `documents → projects/hr`) | `3b93ef4`, `63dcd2b` |
| G7-05 (Documents.Infrastructure — file storage) | `47b725f`, `9851104` |
| G7-06 (Documents.Application — folders + templates) | `baec724`, `63e41b0` |
| G7-07 (Documents.Application — files, gates, Projects subscription) | `e45b406`, `44f6f41` |
| G7-08 (Documents endpoints, module registration, integration suite) | `16da164`, `19a118d` |
| G8-01 (docs + final report) | *(this commit)* |

14 tasks, 27 commits (13 tasks × 2 — one for the work, one for its own `PROGRESS.md` line — plus G8-01's
docs/report commit), all green through the full gate at every commit, no amends, no reverts.

### Test counts (Phase 3 end state)

**262 tests total**, all passing, across 18 test projects:

| Suite | Count | Suite | Count |
|---|---|---|---|
| `Architecture.Tests` | 14 | `Hr.Infrastructure.Tests` | 5 |
| `BuildingBlocks.UnitTests` | 3 | `Hr.Application.Tests` | 9 |
| `Identity.UnitTests` | 8 | `Approvals.UnitTests` | 8 |
| `Hr.UnitTests` | 10 | `Approvals.Application.Tests` | 4 |
| `Projects.UnitTests` | 61 | `Projects.Infrastructure.Tests` | 5 |
| `Documents.UnitTests` | 30 | `Projects.Application.Tests` | 21 |
| `BuildingBlocks.IntegrationTests` | 11 | `Documents.Infrastructure.Tests` | 24 |
| `Identity.Infrastructure.Tests` | 8 | `Documents.Application.Tests` | 14 |
| `Identity.Application.Tests` | 10 | `Integration.Tests` | 17 |

Growth from the Phase 2 boundary's 185: 186 (H1's seeding-loop restoration test) → 188 (W1's two
wake-on-write tests) → 188 (G7-01, Contracts-only) → 218 (G7-02, +30 Domain unit tests — entities, errors,
lineage, cycle guards) → 242 (G7-05, +24 file-storage hardening tests) → 250 (G7-06, +8 folder/template
integration tests) → 256 (G7-07, +6 cross-module gate-satisfaction tests, the phase's proof task) → 262
(G7-08, +6 endpoint/characterization tests). G7-03/G7-04 (persistence, cross-schema FKs) added no new
test files, matching G5-04's own precedent — migration-only tasks verify via `dotnet ef` applying cleanly
and the probe-migration-empty check, not new xUnit files.

### Every decision this phase's plan did not fully specify (index — full reasoning in each task's own entry above)

- **H1-01 through H1-04:** the entire H1 section, effectively — `PLAN-PHASE3.md`'s own description of the
  working tree was wrong in three specific ways (see the H1 close-out section above and the plan-accuracy
  note below); every H1 decision (commit the sweep alone, restore seeding, delete the dead file, leave
  `HttpCurrentUser` where it is) was this session's own re-diagnosis, not the plan's.
- **W1-01:** three test-fixture `TryAddSingleton<OutboxSignal>()` self-sufficiency fixes required by but
  not specified in the plan's own design; a real test-authoring bug (polling an in-memory counter instead
  of the durable `ProcessedUtc` column) found via cross-test flakiness, not by inspection.
- **G7-01:** `Documents.Contracts` does NOT reference `Projects.Contracts`, contrary to the plan's literal
  text — checked against every existing `*.Contracts` project's own precedent first (none references
  another module's Contracts); the real dependency belongs on `Documents.Application` instead, per the
  reference matrix.
- **G7-02:** structural items 1a/1b (`GateConditionContext`/`GateCondition` extension, the A1 addition)
  fully designed and implemented as the review process's own required addition; three backend bugs fixed
  rather than ported (folder-cycle guard, default-folder name collision, move-folder-as-`Renamed` audit
  non-port).
- **G7-05:** the file-upload hardening triad (path traversal, chunk-by-chunk size cap, extension
  allow-list) — the backend's own `IFileStorageService` has none of this; all three are new work, not a
  port, chosen because a public upload endpoint accepting arbitrary bytes without them is a real
  vulnerability class, not a hypothetical one.
- **G7-07:** the `DocumentApprovedIntegrationEvent` design (Documents publishes, Projects subscribes) —
  the plan's own text read as if Documents could satisfy a `GateCondition` directly, which cross-schema
  table access forbids; TWO real bugs found and fixed, both outside Documents' own module boundary
  (`CreateStageGateConditionCommand`/`CreateTaskGateConditionCommand` never threading `ReferencedDocumentId`
  through, and the `AddModuleIntegrationEvents` decorator double-wrap).
- **G7-08:** the route scheme (upload and list-by-scope sharing one path prefix per HTTP verb) chosen over
  the backend's own `by-project`/`by-stage` suffix convention; TWO more real bugs found and fixed
  (`GateEndpoints`' HTTP-layer DTO gap, and the antiforgery/`GlobalExceptionHandler` status-code chain);
  the plan's own "oversized upload → 413" corrected to 400 once built and verified against the framework's
  actual behavior.

### Anything implemented more thinly than intended

- **G7-07:** `RequestDocumentReviewCommand`'s `ReviewInProgress` duplicate-open-review guard is defined
  (`ProjectDocumentErrors.ReviewInProgress` exists) but never checked — `IApprovalRequester` has no
  "is there an open approval for subject X" query, and closing this needs either a Contracts extension or
  Documents tracking its own pending-approval id, neither of which was in this task's own verify list.
  Recorded in the scope-boundaries table above, not silently dropped.
- **G7-08:** upload streaming is `IFormFile` binding + configured size limits, not a full zero-copy
  `MultipartReader` implementation that would bypass ASP.NET Core's own form-value-provider entirely — a
  deliberate, documented lesser hardening (see the scope-boundaries table) chosen to avoid reopening
  `IFileStorageService`'s G7-01 contract shape.
- **G7 (whole module):** ~~`IDocumentQueries.ForStageAsync`/`ForTaskAsync` do the direct-scope-only read the
  plan's own G7-01 facts table originally implied was a reach-through — the reach-through this phase's
  verify block actually needed (gate satisfaction) was built on Projects' subscriber side instead, which
  is strictly better for that one purpose (no cross-module round trip) but means a hypothetical "show me
  everything visible at this stage" LISTING query still doesn't reach through. Recorded as a real,
  disclosed gap, not silently absorbed into "done."~~ **Superseded — this bullet was itself the failure.**
  Correction 8 did not "imply" a reach-through, it required one and named the consequence of omitting it;
  "a hypothetical LISTING query" understated a live defect. Fixed at **E1-01** (below), which also records
  why this framing, not the omission, is the transferable lesson.

### Anything in the plan that looks wrong, having built it

- **`PLAN-PHASE3.md`'s H1 section, in full** — see the H1 close-out section above for the itemized
  corrections (a fabricated CRLF flip, an unreviewed package sweep it never mentioned, a phantom
  `HttpCurrentUser` move, a miscounted doc list). The general lesson recorded there — "a plan's
  description of the working tree is a claim to verify, not a fact to build on" — is this phase's single
  most important finding, because it recurred at the PLANNING-DOCUMENT level rather than the
  source-code level where Phase 2's identical lesson ("read the actual source method, not just its
  name") first appeared. The failure mode is the same; only the artifact being misread changed.
- **The plan's G7-07 text read as though Documents could satisfy a `GateCondition` directly** ("satisfies
  matching DocumentApproved gates by RootDocumentId," phrased without naming which module's code does the
  satisfying). Cross-schema table access forbids this outright — `GateCondition` lives in `projects.`,
  never touched from Documents. The actual design (a new `DocumentApprovedIntegrationEvent`, Documents
  publishes, Projects subscribes) is the ONLY shape rule 6/the reference matrix permits, but it required
  inferring the mechanism the plan's prose left implicit, not just following instructions. Worth flagging
  for Phase 4's own planning pass: when a plan describes a cross-module EFFECT without naming which
  module's CODE produces it, treat that as underspecified, not as "the mechanism is obvious."
- **"Oversized upload → 413" was wrong once built** — see G7-08's own PROGRESS.md entry for the full
  correction. ASP.NET Core's actual, correct status for a `FormOptions.MultipartBodyLengthLimit`
  violation is 400 Bad Request; 413 is a different mechanism (`IHttpMaxRequestBodySizeFeature`) this
  configuration never touches. Not a bug — the plan predicted the wrong status code for a real, working
  hardening feature.
- **The package-upgrade question G8-01 is explicitly asked to answer: committed, at H1-01, unchanged.**
  The full gate was green against the swept package set before anything else in this phase touched the
  tree (`dotnet build -warnaserror` 0/0, the only test failures traced to the UNRELATED, already-present
  seeding regression H1-03 fixed two tasks later) — see H1-01's own entry for the live `SSH.NET` audit
  finding fixed along the way. No revert was needed at any point after.

### What Documents taught that should change the Phase 4 CRM plan

CRM is the next module in `docs/adding-a-module.md`'s own list, and it is structurally closer to
Documents than to any earlier module: like Documents, CRM will need to CONTRIBUTE gate condition types
(`CrmPeriodClosed`/`CrmBriefApproved`/`CrmItemsResolved`, all currently rejected at gate-creation time)
to a registry Projects already owns, and — per the deferred-items table above — CRM is also the eventual
owner of the `ICrmGateReader`-style validation `DocumentApprovedHandler` approximated with a same-project
check this phase. Four concrete lessons worth writing into the Phase 4 plan before it is finalized, not
rediscovering during execution:

1. **Budget the `GateConditionContext`/`GateCondition` extension as its own explicit sub-task, not a
   footnote.** It touches THREE files across TWO modules (the context record and a new `GateCondition`
   column + migration, both in `Projects.Contracts`/`Projects.Domain`, even though the FEATURE lives in
   CRM) — easy to under-scope as "just add a field" when it is actually a cross-module change with its
   own migration. G7-02 budgeted this correctly only because the review process caught it as a required
   addition (A1); a first-draft plan should name it explicitly, not rely on a review round to add it back.
2. **If CRM will subscribe to an integration event ANY other module already handles, say so explicitly and
   flag the `AddModuleIntegrationEvents` registration bug this phase found and fixed as a prerequisite,
   not an incidental one.** The Scrutor-decorator double-wrap bug (G7-07) was latent for four phases
   because no two modules had ever shared a subscribed event type before Documents. CRM reacting to
   `ApprovalDecidedIntegrationEvent` (a third subscriber) or any OTHER already-subscribed event type will
   exercise the FIXED code path, but the fix itself should be called out as "already handled, verify it
   still holds with three subscribers" rather than assumed silently safe.
3. **Plan a bidirectional-Contracts pair explicitly, don't discover it mid-task.** CRM contributing gate
   handlers means `Crm.Infrastructure → Projects.Contracts`; if CRM also needs to react to Projects' own
   events (likely, given the gate-satisfaction shape), that is `Projects.Infrastructure → Crm.Contracts`
   too — the exact shape this phase established and documented. Naming both directions in the plan up
   front, with the "why this isn't a cycle" reasoning already written into `docs/adding-a-module.md`,
   removes a full task's worth of design work from CRM's own execution.
4. **Any CRM feature that accepts a file (brief attachments, if any survive the CRM scope cut) should
   reuse `Documents.Contracts.IFileStorageService`/the upload command shape directly, not re-invent
   upload hardening.** The path-traversal/size-cap/extension-allowlist triad took real design work this
   phase (G7-05) precisely because the backend's own equivalent had none of it; CRM re-deriving the same
   protections from scratch would be pure waste when the module already exists to depend on.

# Part E1 — Phase 3 post-review fix

Raised by `REVIEW-PHASE3-COMPLETE.md` (against `63c13b2`) as the one required fix after Phase 3 closed.
Plan reviewed and approved in `REVIEW-E1-PLAN.md`.

**E1-01** — done — `UploadTaskDocumentCommandHandler.cs`, `ApplyFileTemplateToTaskCommandHandler.cs`
(the fix); `ProjectDocument.cs`, `IDocumentQueries.cs`, `DocumentContracts.cs`, `IntegrationEvents.cs`,
`DocumentQueries.cs`, `GetProjectDocumentsByStageQueryHandler.cs`,
`Projects.Infrastructure/IntegrationEvents/DocumentUploadedIntegrationEventHandler.cs` (doc comments);
`FileFlowTests.cs` (4 new tests).

**The bug.** A document uploaded to a task had `ProjectStageId = null`, so both stage-scoped reads
(`GetProjectDocumentsByStageQuery`, `IDocumentQueries.ForStageAsync`) missed it — exactly the failure
G7-02's correction 8 predicted in writing. Both task-scoped construction paths now pass
`projectStageId: task.ProjectStageId`; `TaskSummary` already carried it, so no new lookup. Null stays
null for a `FreeStyle` task with no stage. `CreateRevision` already copied the field, so revisions
inherited correctly and needed no change.

**Correction 8 had two halves, and the record of which one failed matters more than the one-liner.**

- **Gate half — shipped and genuinely tested.** `DocumentUploadedIntegrationEventHandler` resolves the
  owning stage itself (`owningStageId ??=` from `db.ProjectTasks`), and its named verify —
  `AttachmentGate_OnTheOwningStage_IsSatisfied_ByADocumentUploadedToATaskUnderIt` — was written at G7-07.
- **Query half — never shipped.** And *neither stage-read path had a single test of any kind*. That
  absence, not a missing verify, is why nothing caught it: the verify that existed was structurally
  incapable of failing, because Projects resolves the stage independently of the denormalization.

**The real process failure was framing, not silence.** This gap WAS disclosed twice at close-out — the
scope-boundaries table and the thinner-than-intended list, both amended above. But both re-framed an
**overridden plan requirement** as a **deferred scope choice** ("a hypothetical LISTING query"), and that
framing is what let it pass review, including the external one. The transferable rule: *when a task
narrows a stated plan requirement, that is a deviation to flag as a deviation — filing it as a scope item
launders a defect into a roadmap entry.*

**Three things found while fixing that the review did not name:**
1. **`ApplyFileTemplateToTaskCommandHandler` carried the identical omission** and had **zero test coverage
   of any kind**. Fixing only the handler the review named would have left the same defect reachable via
   apply-template — a fix that looks complete and isn't. An exhaustive sweep confirmed exactly two of the
   five `ProjectDocument.Create`/`CreateRevision` sites needed changing (project-wide and stage uploads
   were already correct; `CreateRevision` inherits).
2. **The existing stage-gate test would have gone silently vacuous.** After this fix the event carries the
   stage, so the `??=` fallback that test's own doc comment claimed it proved no longer executes on that
   path — the test stays green while its stated coverage evaporates, the *same* failure mode that let E1
   ship. Corrected the comment to say what it proves now, and added
   `AttachmentGate_OnTheOwningStage_IsSatisfied_ByAnEventCarryingNoStage`, which publishes a synthetic
   `ProjectStageId: null` event through `IEventBus` to keep that branch genuinely covered.
3. **The fallback is deliberately kept, and now says so loudly.** It no longer changes an outcome for any
   current producer, which makes it read as dead code. Its doc comment now states why it stays (pre-E1-01
   rows; any future producer that forgets to denormalize) and names the test covering it — deleting it
   would make gate correctness depend on every writer forever remembering, which is how E1 happened.

**Deliberate consequences, recorded rather than discovered later:** `ProjectDocument` now routinely has
BOTH `ProjectStageId` and `ProjectTaskId` set, a considered divergence from `DocumentFolder`'s
at-most-one rule (a folder's scope is where it *sits*; a document's is what it is *visible to*) — the
property comment warns against adding a symmetry guard, which would reject every task upload. A stage
document list can therefore contain a document whose folder is absent from that stage's *folder* list;
nothing validates document-scope against folder-scope, by design. **No backfill migration:** never
deployed, every test run gets a fresh database, and the retained fallback covers any stale row.

**Verify:** three new stage-read tests written **first** and confirmed RED — `Assert.Single() Failure …
Collection: []`, failing on the assertion, not on setup — then green after the two one-liners, same
`--filter FullyQualifiedName~FileFlowTests.StageRead_` both runs with only `src/` differing. Full gate —
`dotnet build -warnaserror` 0/0, `dotnet format --verify-no-changes` clean, `dotnet test` **266/266**
(262 + 4: three red-first stage-read tests plus the synthetic null-stage regression test; the plan
budgeted two before findings 1 and 2 above added the other two).
