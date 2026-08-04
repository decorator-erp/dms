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
