# Adding a module

A checklist and reference for the next module (Projects, CRM, Sites, Finance, Documents, Approvals,
Notifications, Reporting), written immediately after building Identity and HR — while the mistakes made
building them are still fresh, not reconstructed from memory later.

## The four-project layout

Every module is exactly four projects, one folder: `src/Modules/<Module>/`.

```
<Module>.Contracts/       Public. The only public surface. Referenced by other modules.
<Module>.Domain/          Internal. Entities, value objects, domain errors. No EF Core, no ASP.NET.
<Module>.Application/     Internal. CQRS command/query handlers + validators. Uses own Infrastructure.
<Module>.Infrastructure/  Internal. DbContext, EF configurations, migrations, IModule, endpoints, seeders.
```

Reference matrix (a summary of the CROSS-MODULE isolation rules the arch tests enforce — not an
exhaustive intra-module graph; a project may always reference its own module's other layers where the
non-negotiables require it, e.g. Application → own Infrastructure, below):

| Project | May reference |
|---|---|
| `*.Contracts` | `SharedKernel`, `BuildingBlocks` |
| `*.Domain` | `SharedKernel` only |
| `*.Application` | `SharedKernel`, `BuildingBlocks`, own `Domain`, own `Infrastructure`, other modules' `*.Contracts` |
| `*.Infrastructure` | All of the above + `BuildingBlocks.Web` |
| `Api` | Everything |

**Why four and not three.** Collapsing `*.Application` into `*.Infrastructure` was proposed (F2) and
**rejected** — the layout is settled, don't re-open it per module. Arch rule 8 asserts that no
`*.Application` assembly references `BuildingBlocks.Web`, checked against the physical assembly
reference graph; a merged project must reference it for its endpoints, so handlers would gain
compile-time sight of the web layer and the rule could no longer be stated at all. Extracting a
`<Module>.Endpoints` project instead is still four projects. The trade-off, including what the merge
would have bought, is recorded under "F2 — closed (won't-do)" in `PROGRESS.md`.

## What goes where

**`*.Contracts`** — three things, and nothing else:
1. Cross-module read interfaces (`I<Module>Directory`) and their DTOs. Every method that returns one
   thing also has a sibling that returns many (`GetAsync`/`GetManyAsync`) — no exceptions to this.
2. Cross-module write interfaces, if this module needs another module to act on its behalf
   (`IUserAdministration`-style). Narrow, imperative, named for the action.
3. Integration event records (`IIntegrationEvent`) this module publishes. Payloads are IDs and
   primitives only — never an entity, never a reference type from `*.Domain`.
4. Permission string constants (`<Module>Permissions`).

**A fifth thing ends up here too, and it's worth planning for up front, not discovering at the same
point Identity and HR both did:** the command/query wrapper records and their request/response DTOs
for THIS module's own features. `*.Infrastructure`'s endpoints need to name these exact types to bind
an HTTP request and to resolve `ICommandHandler<TCommand,TResponse>`/`IQueryHandler<TQuery,TResponse>`
from DI — and `*.Application` already has a real `ProjectReference` to its own `*.Infrastructure` (next
section), so the reverse reference `*.Infrastructure` would need is a circular project reference MSBuild
refuses outright. `*.Contracts` is the one project both sides already reference without a cycle. Put
these types there from the start (see `Identity.Contracts.Auth`/`.Permissions`/`.Roles` and
`Hr.Contracts.Employees`/`.Departments` for the shape) — Identity's own history is the counter-example:
its command types started in `Identity.Application` and had to be relocated at G1-07, once endpoints
were built and the cycle became a compile error, touching every feature file a second time. Don't repeat
that; decide this before writing the first feature, not after the first endpoint. This placement is
permanent, not provisional: F2 — the project collapse that would have let these types go back to
`internal` — is closed (see the layout section above), so treat Contracts as their proper home rather
than somewhere they are parked.

Making these types public in `*.Contracts` has a cost this pattern doesn't remove — it only contains
it: nothing about "public" stops another module from referencing your `*.Contracts`, naming your command
type, and resolving `ICommandHandler<TCommand,TResponse>` straight from the shared DI container, a
synchronous cross-module write that never touches your `*.Application`/`*.Infrastructure` and so isn't
caught by rule 3's cross-module-reference check. **Arch rule 10 closes this**: no other module may
depend on a type in your `*.Contracts` that implements `ICommand`/`ICommand<T>`/`IQuery<T>`. Everything
else in Contracts (DTOs, contract interfaces, permission constants, integration events) stays fair game
across modules — only the command/query types themselves are module-private, enforced the same way
publicness is required in the first place.

**`*.Domain`** — entities (deriving from `SharedKernel.Entity` unless they're a pure join/association
row with no identity of their own — see `Hr.Domain.EmployeeDepartment`), enums actually referenced by
those entities (don't port enums nothing in scope uses), and `<Module>Errors` static classes. No
repository interfaces. No EF attributes or fluent configuration — that's Infrastructure's job.
`AppUserId`-style cross-module references are bare primitive columns (`string`, `Guid`), never a
navigation property to another module's entity type.

**`*.Application`** — one folder per feature (`Features/<Noun>/<Verb>/`), each a validator + handler
pair. The command/query record itself and its DTOs live in `*.Contracts` (above). Handlers use the
module's own DbContext directly — no repository pattern, anywhere, ever, even when a task's own wording
describes porting a "domain service" that turns out to be repository-shaped in the source (see G2-02's
PROGRESS.md entry: reimplement the LOGIC as a handler, don't port the repository dependency along with
it). A handler MAY call another module's `*.Contracts` interface directly for a cross-module read or
write; it may NOT reference another module's `Domain`/`Application`/`Infrastructure`.

**`*.Infrastructure`** — `<Module>DbContext : DbContext` (never named `<Module>ModuleDbContext` unless
the module has its own ASP.NET Identity-style base-type collision to avoid — Identity does, HR doesn't,
and the asymmetry is deliberate, not an oversight), one `IEntityTypeConfiguration<T>` per entity, initial
migration, `<Module>Module : IModule`, endpoint classes (`Features/*Endpoints.cs`), seeders if the module
has default data, and the implementation(s) of this module's own `*.Contracts` interfaces (e.g.
`HrDirectory : IHrDirectory`).

## The `IModule` contract

```csharp
public interface IModule
{
    string Name { get; }
    IServiceCollection Register(IServiceCollection services, IConfiguration cfg);
    IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app);
    Task SeedAsync(IServiceProvider services, CancellationToken ct = default) => Task.CompletedTask;
}
```

`Register` — in order: `AddDbContext<TContext>`, any module-specific service registrations (JWT,
directory implementations, etc.), the module's `IModulePermissions` singleton (built from a plain
string array — never hard-code another module's permissions), `AddModuleCqrs<TContext>(Assembly.Load
("<Module>.Application"))`, and `AddModuleIntegrationEvents<TContext>(typeof(<Module>Module).Assembly)`
if this module handles any integration events.

`MapEndpoints` is always just `app.MapModuleEndpoints(typeof(<Module>Module).Assembly);` — the one-liner
never changes; endpoint discovery is by convention (`IEndpoint` implementations found by reflection),
not by hand-listing routes here.

`SeedAsync` — only override if the module has default data. Must be idempotent: called on every
startup, not just the first (`Program.cs` runs it unconditionally after `MapEndpoints`, before
`app.Run()`). Idempotency test pattern: call `Register` + `SeedAsync` twice against the same database in
a test, assert row counts match both times (see `Identity.Infrastructure.Tests.SeedingIdempotencyTests`).

**Discovery, everywhere, is by assembly name, never by referencing a concrete type:**
`Assembly.Load("<Module>.Infrastructure")` in `Program.cs`'s module list (and `ApiFixture`'s, and
`Rule9CrossSchemaForeignKeyTests`'s, if the new module has a cross-schema FK); add a `ProjectReference`
to `Api.csproj` for both `<Module>.Infrastructure` AND `<Module>.Application` (the latter purely so
`Assembly.Load("<Module>.Application")` inside `<Module>Module.Register` finds the DLL — never to name a
type from it). Any test project that boots the module the same way needs the identical
`<Module>.Application` reference for the identical reason.

## Wiring checklist (in the order you'll actually hit each step)

1. Create the four projects. `*.Contracts` and `*.Domain` first — they have no forward dependencies.
2. Add all four to `DMS.Modular.slnx` and to `tests/Architecture.Tests/Architecture.Tests.csproj`'s
   `ProjectReference`s. **Do this before writing any code that should be constrained by the arch
   tests** — a module's assembly not referenced there is invisible to every rule that scans by naming
   convention, and the rule constraining it stays silently vacuous (see `ModuleAssemblies.cs`'s own
   comment, and `MetaSelfEnforcementTests`, which exists specifically to catch this).
3. Write the entities and errors in `*.Domain`. No EF Core reference — arch rule 1 checks this.
4. Write `*.Infrastructure`'s `DbContext` (`HasDefaultSchema("<schema>")`,
   `ApplyConfigurationsFromAssembly`, `ConfigureOutbox(ownsMigration: false)`), configurations, and
   `<Module>Module : IModule` (`Register` without `AddModuleCqrs` yet — there's no Application assembly
   to scan until step 6).
5. Generate the initial migration: `dotnet ef migrations add Initial<Module> --context <Module>DbContext
   --project src/Modules/<Module>/<Module>.Infrastructure --startup-project src/Api`. If this is the
   first migration you've generated in a while, confirm `Api.csproj` still references
   `Microsoft.EntityFrameworkCore.Design` — the EF CLI requires it on the STARTUP project, not just the
   migrations project, and its absence produces a build-looking error that has nothing to do with your
   code. If the module has a cross-schema FK, add it by hand via `migrationBuilder.Sql(...)` in `Up`
   (and the matching `DROP CONSTRAINT` in `Down`) — EF cannot emit it, the two entities are in different
   models. **Column names in the raw SQL must be quoted PascalCase** (`"AppUserId"`, not `app_user_id`)
   unless a snake_case naming convention gets configured solution-wide later — this solution has none
   today, confirmed by reading the generated migrations, and an unquoted lowercase reference silently
   folds to a nonexistent column in Postgres. Add the module's schema to
   `ModuleDependencyOrder.SchemaOrder` in dependency order (after whatever it has a cross-schema FK to).
5b. Add the module to `ApiFixture.MigrateSchemaAsync`'s `switch` (one `case`, calling the existing
   `MigrateModuleSchemaAsync` helper with the assembly name and assembly-qualified context type name) —
   and to `Rule9CrossSchemaForeignKeyTests`'s migration list if it has a cross-schema FK to check.
6. Write `*.Contracts`' command/query/DTO types for the first feature (see the fifth bullet under "What
   goes where" above), then the validator + handler in `*.Application`. Add the `Hr.Application`-style
   `ProjectReference`s: own `Contracts`, own `Domain`, own `Infrastructure`, and any other module's
   `Contracts` this feature reads/writes through.
7. Back in `<Module>Module.Register`, add `AddModuleCqrs<TContext>(Assembly.Load("<Module>.Application"))`
   now that the assembly exists. Add the `Api.csproj` → `<Module>.Application` reference (step above,
   "Discovery, everywhere").
8. Write the endpoint classes in `*.Infrastructure/Endpoints/`, gated with
   `.RequirePermission(<Module>Permissions.X)` (note: `.WithMetadata(...)` must be applied as part of, or
   before, the parameterless `.RequireAuthorization()` call it wraps — the extension method handles the
   ordering, but if you ever write this by hand instead, the order matters and silently doing it
   backwards produces an endpoint that never enforces the permission at all).
9. If this module needs to read another module's data: implement its own `I<Module>Directory` (project
   every list/lookup straight to the response DTO via a `Select` — never load-then-map) and call the
   OTHER module's directory interface for anything cross-module, batched (`GetManyAsync`), never in a
   loop. Memoize repeat per-request lookups (`ResolveXIdAsync`-shaped calls) with a private dictionary
   field on the scoped service instance — nothing DI-specific needed, a scoped service's field already
   lives exactly as long as one request.
10. If this module needs to write to another module (a state change that module owns): define the
   integration event in ITS `*.Contracts`, publish it via `db.Outbox().Enqueue(...)` in the SAME
   `SaveChangesAsync` as the triggering write, and put the
   `IIntegrationEventHandler<TEvent>` in the CONSUMING module's `*.Infrastructure` (never the producing
   module — the producer shouldn't need to know who reacts). Register it via
   `AddModuleIntegrationEvents<TContext>(typeof(<ConsumingModule>Module).Assembly)` in the consuming
   module's own `Register`.

## Arch rules a new module must satisfy

All ten rules apply automatically once the module's assemblies are referenced in
`Architecture.Tests.csproj` (step 2 above) — nothing else to configure. What each one means for you:

1. **`*.Domain` ⇏ EF Core.** No `Microsoft.EntityFrameworkCore` reference in the Domain csproj.
2. **`*.Application`/`*.Domain` ⇏ `Microsoft.AspNetCore.*`.** The one standing exception is
   `Microsoft.AspNetCore.Identity` for a module whose Domain models an `IdentityUser`-shaped concept —
   almost certainly only Identity itself.
3. **No cross-module references, no cross-module `InternalsVisibleTo`.** Your `*.Domain`/
   `*.Application`/`*.Infrastructure` never reference another module's same three layers, and your
   `InternalsVisibleTo` grants only ever name your OWN module's sibling/test projects.
4. **Only `*.Contracts` types are public.** Generated migrations are exempt automatically.
5. **Integration event payloads: primitives/`Guid`/`DateTimeOffset`/`string`/enums/collections thereof
   only.** No entity, no value object with behavior.
6. **No `*.Application`/`*.Domain` reference to a DbContext outside its own module.**
7. **`SharedKernel` stays frozen.** You should never need to touch it; if a new module seems to need a
   change there, that's a stop-and-ask moment, not a quick edit.
8. **No `*.Application`/`*.Domain` reference to `BuildingBlocks.Web`; `BuildingBlocks` itself stays
   ASP.NET-free.** Endpoints, and anything using `Microsoft.AspNetCore.*` beyond rule 2's exception,
   belong in `*.Infrastructure`.
9. **Cross-schema foreign keys only point in an allowed module-dependency direction** (see
   `docs/cross-schema-foreign-keys.md` for the full allow-list). `ON DELETE RESTRICT`, never `CASCADE`,
   across the boundary.
10. **Commands and queries are module-private.** No other module's `*.Domain`/`*.Application`/
    `*.Infrastructure`/`*.Contracts` may depend on a type in your `*.Contracts` that implements
    `ICommand`/`ICommand<T>`/`IQuery<T>` — see the "fifth thing" note above for why this exists
    alongside rule 4 rather than instead of it.

## Three more patterns (from Phase 2 — Approvals + Projects)

Phase 1 (Identity, HR) never needed these; Projects did, building on top of two prior modules instead of
zero. Worth knowing before the third module that needs them re-derives them from scratch.

### The pluggable-handler registry pattern

When a module's own domain has an open-ended "kind" that other modules — including ones that don't exist
yet — will want to add cases to, don't model it as a switch statement. Ship a strategy interface in
`*.Contracts` and let each kind register its own implementation via DI.

```csharp
// Projects.Contracts — the interface, not the implementations
public interface IGateConditionTypeHandler
{
    ConditionType Type { get; }
    Task<Result> ValidateAsync(GateConditionContext context, CancellationToken ct = default);
}
```

Implementations live wherever they need to live — `ManualCheckHandler`/`StageReferenceHandler`/
`TaskReferenceHandler` are pure and could sit in `Projects.Domain`, but `ApprovalHandler` needs
`Approvals.Contracts.IApprovalRequester` injected, which `*.Domain` can never reference — so all four
live in `Projects.Application` as `internal` classes instead of forcing an artificial split. The owning
module's `*.Infrastructure.Register` can't name `internal` implementation types across the
`*.Application`/`*.Infrastructure` boundary (no `InternalsVisibleTo` grant exists for that direction —
only test projects get one), so register them by scanning for the PUBLIC interface instead of listing
concrete types:

```csharp
services.Scan(scan => scan.FromAssemblies(applicationAssembly)
    .AddClasses(classes => classes.AssignableTo<IGateConditionTypeHandler>(), publicOnly: false)
        .AsImplementedInterfaces()
        .WithScopedLifetime());
```

The consuming handler resolves the whole set and picks by key at runtime:

```csharp
var typeHandler = typeHandlers.SingleOrDefault(h => h.Type == command.ConditionType);
if (typeHandler is null)
    return Result.Failure<Guid>(GateConditionErrors.UnsupportedConditionType);
```

A condition type this phase doesn't support yet (`Budget`, `Attachment`, `DocumentApproved`, …) fails
this lookup and returns a clear "not supported yet, supported types: X, Y, Z" error at creation time —
never a stub, never a silent no-op. Adding a new kind later is "write one handler + let DI find it,"
never a change to the dispatching code itself.

### The subject-type-filtered subscription pattern

When a sink module (Approvals, Notifications — anything that serves many other modules through one
generic subject/event shape) publishes one integration event that many different owning modules could
plausibly care about, each subscriber filters on the subject-type discriminator FIRST, before doing
anything else, and returns silently for every type it doesn't own:

```csharp
public async Task HandleAsync(ApprovalDecidedIntegrationEvent @event, CancellationToken ct)
{
    if (@event.SubjectType != ApprovalSubjectType.GateCondition)
        return;
    // ... only GateCondition-subject work below this line
}
```

This is NOT the same thing as R2's "zero handlers registered for an event type" no-op — here a handler
IS registered and DOES run for every decision of every subject type, and deliberately does nothing for
the ones it doesn't own. Two consequences worth being deliberate about: (1) the filter must be the very
first thing the handler does, before any query — cheap to check, and a query issued before the filter
would be wasted work on every subject type this module doesn't care about; (2) prove the filter itself
works, not just its absence of a crash — the way to do that is to reuse a REAL row's own id as the
subject id of a fake event with the WRONG subject type, and assert that real row is untouched (see
`Projects.Application.Tests.GateFlowTests.ApprovalDecidedEvent_ForADesignBriefSubject_DoesNotTouchAnyGate`)
— a test that only checks "no gate matching a random guid changed" would pass even if the filter were
deleted entirely, since no gate would match by id either way.

### Extending the cross-schema FK allow-list

Every new module that adds a cross-schema FK touches the same three places, together, in the same
commit as the migration that needs them:

1. **The migration itself** — raw `migrationBuilder.Sql(...)` in `Up` (and the matching `DROP CONSTRAINT`
   in `Down`), quoted PascalCase columns, `ON DELETE RESTRICT`. See the wiring checklist above, step 5.
2. **`tests/Architecture.Tests/Rule9CrossSchemaForeignKeyTests.cs`** — add the new module to the
   `Modules` dictionary (assembly name + assembly-qualified `DbContext` type name) so its migration gets
   scanned, and add every new `(fromSchema, toSchema)` pair this module introduces to `AllowedPairs`.
   Both are required — a module present in `Modules` but missing a pair in `AllowedPairs` fails the
   build the moment its own migration runs (the rule catches you writing the code, not reading about it
   after); a pair added to `AllowedPairs` without the module in `Modules` is a lie the test can't check
   (nothing scans that module's migration, so the constraint could point anywhere and the test would
   stay green).
3. **`docs/cross-schema-foreign-keys.md`** — one paragraph per module under "Allowed pairs for the
   current design," naming every constraint by its actual `fk_*` name and what it points at (see the
   `approvals →` and `projects →` paragraphs there for the shape). Not optional busywork: the next
   builder reading this doc to understand the FK graph should never need to go read migrations to find
   out what actually exists versus what the aspirational table at the top of the file merely allows.

**Prove the check catches a violation for real, once per module, not just by inspection**: temporarily
add a wrong-direction scratch FK (a throwaway column + raw SQL appended to the new migration, pointing
the wrong way — e.g. from a module you depend ON, back to you), confirm
`EveryCrossSchemaForeignKey_PointsInAnAllowedDirection` fails naming exactly that constraint, then revert
and reconfirm green. `Rule9CrossSchemaForeignKeyTests`' own test suite stays vacuous — always green,
never actually exercised against a bad direction — until some module does this once; G5-04 was the first
real exercise of it in this codebase (a scratch `hr.employees → projects.projects` FK), and the fault
injection is cheap enough that skipping it just means trusting the rule instead of proving it.

## Four more patterns (from Phase 3 — Documents)

Documents is the first module whose own domain (file attachments, document approval) needed to plug
INTO another module's already-shipped extension point (Projects' gate system) rather than merely
consuming a directory/integration-event contract. Two structural firsts and a hardening checklist worth
keeping close at hand.

### Extending a gate/strategy context (`GateConditionContext`-style)

When a NEW module contributes a new case to an existing pluggable-handler registry (the pattern two
sections up), its `ValidateAsync(TContext, ct)` call needs a piece of data no earlier case ever needed —
Documents' `DocumentApprovedHandler` needs to know WHICH document lineage a gate references, and neither
`ManualCheckHandler` nor `ApprovalHandler` (Projects' own Phase 2 cases) ever needed a document id at
all. The context record is not module-private — it lives in the OWNING module's `*.Contracts`
(`Projects.Contracts.GateConditionContext`), so extending it is a decision the owning module makes on the
new contributor's behalf, guided by one rule:

> **A trailing optional parameter is the only shape allowed.** `Guid? ReferencedDocumentId = null` added
> after every existing parameter — never inserted in the middle, never required, never removed or
> repurposed. Every existing caller (positional or named args) keeps compiling unchanged; every existing
> `IGateConditionTypeHandler` implementation that never reads the new field is unaffected.

This is additive-only by construction, not by convention someone has to remember — a breaking change
here would mean editing the middle of the parameter list or making the new field required, both of which
are easy to *notice* in review even without a rule written down, but worth stating so the next module
doesn't have to rediscover why "just add a parameter" is safe here and isn't everywhere.

### The companion rule: a context field that must be *remembered* also needs a persisted column

Extending the CONTEXT record is necessary but not sufficient. `GateConditionContext` only lives for the
duration of one `ValidateAsync` call — if the gate type's validation needs to check something again
LATER (Documents' `DocumentApprovedHandler` doesn't re-validate at satisfaction time, but the
SATISFACTION path still needs to know which document lineage this specific gate is waiting on, long
after creation returned), the value must ALSO land on the aggregate itself, not just pass through:

1. The property on the entity (`GateCondition.ReferencedDocumentId`), threaded through its `Create`
   factory alongside the fields already there.
2. The EF configuration entry for the new column.
3. A migration for the owning module's own schema (this is a `projects` migration, not a `documents`
   one — the column lives on `GateCondition`, which Documents never touches directly).
4. **No FK if the reference points at a module the owner doesn't depend on** — see
   `docs/cross-schema-foreign-keys.md`'s `GateCondition.ReferencedDocumentId` paragraph for the full
   reasoning (`projects → documents` would invert the module graph; Documents depends on Projects, not
   the reverse). A bare unvalidated `Guid`, validated in the CONTRIBUTING module's own application code
   against its own `DbContext`, is correct here, not a gap to close later.

Skipping this and only extending the context record produces a handler that validates correctly at
CREATE time and then has nothing to check against at SATISFY time — a bug that unit tests of
`ValidateAsync` alone will never catch, since satisfaction is a separate code path entirely.

### The bidirectional Contracts pattern — two modules, each depending on the other's `*.Contracts`

Documents implements `Projects.Contracts.IGateConditionTypeHandler` (needs `Projects.Contracts`) AND
Projects subscribes to `Documents.Contracts.DocumentUploadedIntegrationEvent`/
`DocumentApprovedIntegrationEvent` (needs `Documents.Contracts`) — the first time in this codebase two
modules reference each other's `*.Contracts` at all, in BOTH directions. This looks like it should be a
cycle. It is not, and the reason generalizes to any future pair that ends up here:

> **`*.Contracts` may only reference `SharedKernel`/`BuildingBlocks`** (the reference matrix at the top of
> this doc) — never its own module's `Domain`/`Application`/`Infrastructure`, and never another module's
> `Contracts` either. Two `*.Contracts` assemblies referencing EACH OTHER would violate this on both
> sides and never compile; what actually happens is `Documents.Infrastructure → Projects.Contracts` and
> `Projects.Infrastructure → Documents.Contracts` — two DIFFERENT layers (`*.Infrastructure`, which the
> reference matrix already allows to reach any `*.Contracts`), each depending on the OTHER module's
> `Contracts`, never on each other's `Infrastructure`. The dependency graph among `*.Contracts` assemblies
> themselves stays a flat, cycle-free layer underneath everything; `*.Infrastructure` assemblies are free
> to point at each other's `Contracts` in any combination without that ever becoming circular, because
> `Infrastructure` sits strictly above `Contracts` in every module, symmetrically.

Confirm this by building, not by reasoning about it in the abstract — `dotnet build -warnaserror`
resolving with no circular-reference error IS the proof; MSBuild would refuse to build a genuine cycle
outright, so a green build is not weak evidence here, it is the whole proof. Record which two `*.Contracts`
each new `*.Infrastructure` reference is added for, in both csproj files' own comments, so the next reader
doesn't have to reconstruct which direction is which from the diff alone.

### File-upload hardening checklist

Any module accepting user-supplied file content (`IFormFile` or a raw request body) needs all four of
these — Documents' `LocalFileStorageService` (G7-05) and its upload endpoints (G7-08) are the reference
implementation:

1. **Path traversal.** Sanitize `subFolder`/`originalFileName` inputs, reject `..` outright, and after
   resolving the final absolute path, assert it is still under the configured root
   (`resolvedPath.StartsWith(root + Path.DirectorySeparatorChar)`, not a bare `StartsWith(root)` — the
   separator-bounded check is what stops a sibling directory whose name happens to start with the same
   prefix, e.g. `uploads` vs `uploads-evil`, from passing a naive check).
2. **Size cap enforced during the copy, not only from a pre-known `Length`.** A seekable stream over the
   cap is rejected before any I/O touches disk as a fast path, but every stream — seekable or not — is
   ALSO checked chunk-by-chunk while copying, aborting and deleting the partial file the instant the
   running total would exceed the cap. A non-seekable source (a raw multipart section stream, unlike a
   buffered `IFormFile`) has no `Length` to pre-check at all, so the chunk-by-chunk path is not optional
   hardening, it is the ONLY enforcement for that case.
3. **Extension allow-list, deny-by-default** — check the actual file extension against a configured
   allow-list (or a conservative hardcoded default that never includes `.exe`/`.dll`/`.bat`/`.sh`/
   `.ps1`/`.cmd`), never a deny-list of known-bad extensions. No extension at all is rejected too.
4. **`FormOptions.MultipartBodyLengthLimit`, configured from the SAME source-of-truth config key the
   storage service's own byte-cap reads** (`FileStorage:MaxUploadBytes` for Documents) — this rejects a
   wildly oversized request before the multipart form parser spends any time on it, but is defense in
   depth, not a substitute for point 2: the storage service's own chunk-by-chunk enforcement is what
   actually protects the disk regardless of what the HTTP layer lets through. **Every endpoint binding
   `IFormFile`/`IFormFileCollection` needs `.DisableAntiforgery()`** — ASP.NET Core auto-attaches
   antiforgery metadata to such endpoints as a default, and this codebase has no antiforgery middleware
   configured at all, so without this call every such endpoint throws `InvalidOperationException` on the
   FIRST real request, not at startup — easy to miss until an actual multipart POST is exercised, which a
   unit test resolving handlers directly (bypassing HTTP entirely) never does. A genuine end-to-end HTTP
   test (`WebApplicationFactory`, real multipart body) is the only thing that catches this.

## Testing shape (mirror what Identity/HR already have)

- `<Module>.UnitTests` — pure `*.Domain` entity behavior. No database.
- `<Module>.Infrastructure.Tests` — real Testcontainers Postgres, migrates real migrations, tests
  Infrastructure-level services (`I<Module>Directory` implementations, seeders) directly. If the module
  has a cross-schema FK, migrate the module(s) it depends on first, in dependency order, via each
  module's own public `IModule.Register` on a throwaway `ServiceCollection` (never through the real
  host's `Services` before the tables exist — `Program.cs` seeds as part of building that container
  since G1-07, and seeding queries tables a fresh database doesn't have yet).
- `<Module>.Application.Tests` — the module's own real DI graph (`AddModuleCqrs`, validators, decorator
  chain) against Testcontainers Postgres, resolving `ICommandHandler<T[,R]>`/`IQueryHandler<T,R>` from
  DI rather than constructing handlers directly — proves the wiring, not just the handler logic. If a
  feature crosses into another module (an outbox-routed write, a directory read), boot BOTH modules'
  `Register` in one fixture (see `Hr.Application.Tests.HrApplicationFixture`) plus
  `AddDbContext<SharedDbContext>()` + `AddOutbox()` if anything gets published — and drive
  `OutboxProcessor.ProcessPendingAsync(ct)` directly in the test rather than waiting for its real
  5-second poll interval.
