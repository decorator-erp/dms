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

## Five more patterns (from Phase 4 — CRM)

CRM is the first module whose own schema had to be INSERTED into `SchemaOrder`, not appended, the
first to add a third contributor to an existing pluggable-handler registry, the first to introduce a
client-facing (not just employee-facing) actor, and the first anonymous, rate-limited endpoint anywhere
in the repo. It also produced the phase's clearest process lesson — worth reading even if none of its
structural patterns apply to the module you're adding.

### Inserting a schema into `SchemaOrder`, not appending

Every module through Phase 3 was appended to the end of `ModuleDependencyOrder.SchemaOrder` — the new
module only ever depended on modules already there. CRM is the first case where the new module's own
downstream dependents (`projects → crm`, closing the `Project.ClientId` deferral) already existed. The
rule that makes an insertion safe is purely about migration order, not about the allow-list:

> **A schema may be inserted anywhere in `SchemaOrder` as long as nothing already migrated points
> OUTWARD from it.** `crm` sits between `approvals` and `projects` because `projects → crm` needs `crm`'s
> tables to exist first, and nothing in `identity`/`hr`/`approvals` (everything before it) ever needed to
> reference `crm`. Rule 9's `AllowedPairs` allow-list is a SEPARATE, already-satisfied concern here — both
> `crm → identity`/`crm → hr` and `projects → crm` were already present in that list before this phase
> touched it (only a `Modules` dictionary entry for the count-assert test needed adding, at G9-08).

The corollary this phase's own structural item 1 ran into: an insertion can create an UNSATISFIABLE
three-way cycle if the new module's own outward references would need a schema that comes AFTER it.
`crm.crm_artifact_documents.DocumentId → documents.project_documents.Id` is exactly this —
`crm < projects < documents < crm` has no valid order. When this happens, the FK genuinely cannot exist;
document the absence and the ordering-cycle reason together in `docs/cross-schema-foreign-keys.md`,
validate the reference in application code instead, and pick which one relationship is core enough to
keep as a real FK (`crm.artifacts.manage`'s own document link is a join-table detail, not the phase's
core relationship — `projects → crm` was).

### A third contributor to an existing pluggable-handler registry

Documents (Phase 3) was the second contributor to `IGateConditionTypeHandler`; CRM is the third
(`CrmBriefApprovedHandler`/`CrmItemsResolvedHandler`/`CrmPeriodClosedHandler`). The pattern from the
"Four more patterns" section above holds unchanged — Scrutor's scan-by-interface registration, the
context-extension rule, the FK-or-application-validation choice — but the THIRD contributor is where you
actually confirm the pattern generalizes rather than accidentally being sized for exactly two. It did:
no `GateConditionContext` change was needed at all here (Lesson 1 below), which is itself informative —
not every new contributor needs a context extension, and assuming one always will is the wrong default
to carry forward.

**Lesson 1, restated precisely because it was worth checking, not assuming:** before writing any CRM
gate handler, the plan verified against the actual backend source that all three CRM condition types'
`ValidateAsync` implementations read only `input.OwnerKind` — no CRM-specific field. This CORRECTED an
earlier standing expectation in this repo's own history (`PROGRESS.md`'s G7 entries expected "one more
such extension" for the next contributor). It turned out not to be needed. The lesson generalizes past
CRM: **check what the new contributor's validation logic actually needs before assuming a context
extension is required** — the check is cheap (read the source), the alternative (extending a shared
context speculatively) is not.

### Client-facing (not employee-facing) actors: a second row-level access guard shape

Every module through Phase 3 resolved its acting principal as "an employee, or nothing" via
`Hr.Contracts.IHrDirectory`. CRM is the first with a caller who is legitimately NOT an employee — a
portal client. The shape that generalizes:

1. **A guard that resolves EITHER kind, not two separate guards.** `ICrmAccessGuard.ResolveActorAsync`
   returns one `CrmActor` with a `Kind` discriminator (`Employee` | `Client`), trying the employee branch
   first (via the existing `IHrDirectory`) and falling back to the module's own local table for the
   client branch. A caller who is neither is a 403 ("no standing"), not a 404 or an authentication
   failure — they successfully authenticated, they just have no identity in this module's terms.
2. **A scope-marker interface + decorator for the messages that need it**
   (`ICrmScoped`/`CrmScopeDecorator`, mirroring `Projects.Contracts.IProjectScoped`/
   `ProjectScopeDecorator`) — registered once, tolerant of zero consumers existing yet
   (`AddCrmScopeDecorator` skips any `ICrmScoped` type with no handler registered, rather than failing
   module startup). This is why it's safe to wire the decorator infrastructure BEFORE the first
   `ICrmScoped` command exists (CRM did this at G9-09, five tasks before its first real consumer at
   G9-14) — build the plumbing once, let real consumers arrive incrementally.
3. **Contracts predating the guard is a real, expected sequencing gap, not a design flaw.** CRM's own
   `Crm.Contracts` was written at G9-01, before `ICrmAccessGuard` existed (G9-08) — so several commands
   (`CreateCrmClientNoteCommand.WrittenById`, `CreateCrmDesignBriefCommand.CreatedById`, …) take a
   pre-resolved actor id rather than an `AppUserId` the handler resolves itself. Retrofitting every
   already-committed handler once the guard exists is unnecessary churn; resolving the actor at the
   ENDPOINT layer instead (`CrmEndpointActors.ResolveEmployeeIdAsync`/`ResolveContactIdAsync`, built at
   G9-14) keeps the fix at the boundary where "who is calling" questions already live, without touching
   handler code that was already correct for the shape it was given.

### Anonymous, rate-limited endpoints — the first in this repo

No rate limiter existed anywhere before CRM's public lead-intake endpoint needed one. The shape:
`AddRateLimiter` with a named, IP-keyed fixed-window policy lives in `Api/DI.cs` (a shared-host concern,
not a module-local one); the POLICY NAME constant lives in `BuildingBlocks.Web` (not `Api`), because the
module's own `*.Infrastructure` endpoint needs to reference it via `.RequireRateLimiting(name)` and
cannot reference `Api` (the composition root depends on modules, never the reverse — the same reasoning
`CorsPolicies` staying `Api`-only doesn't apply here, since CORS is never referenced per-endpoint).
An anonymous route is simply an `IEndpoint` group that never calls `.RequirePermission(...)`/
`.RequireAuthorization()` — no special marker needed, ASP.NET Core's own default. **A command whose
Domain aggregate has no "unassigned" state (CRM's own `OwnerId`-required `CrmLead.Create`) cannot be
satisfied by an anonymous caller without EITHER widening that Domain invariant OR falling back to
configuration** (a single configured intake-owner id, failing closed with 503 if unset/unparseable,
never a fabricated id) — widening the Domain invariant is the wrong default; it exists for the office
workflow's own reasons and an anonymous edge case is not sufficient reason to loosen it.

### The lesson worth carrying into the Sites plan: Contracts can promise more than Application delivers, silently

CRM's `Crm.Contracts` declared a full Meetings CRUD surface (12 commands/queries) and a 3-operation
Portal at G9-01 — contracts-first, matching this repo's own convention. **Across the four tasks that
built `Crm.Application` (G9-09→G9-12), the Meetings half was never implemented, and this was never
recorded as a deliberate scope cut in any of those four tasks' own `PROGRESS.md` entries.** It surfaced
only at G9-14, while wiring the Portal's `GetMyPortalMeetingsQuery` and discovering no handler existed
to call. The Portal half of this same gap WAS caught in time — G9-09's own entry explicitly flagged it
forward ("the Portal slice at G9-14 is the first") — proving the failure mode is avoidable, just not
automatic.

**The concrete practice this argues for on the Sites plan:** when a phase writes `*.Contracts` for a
full feature area up front (this repo's own stated convention, and the right one — see this doc's own
"fifth thing" reasoning), each SUBSEQUENT task that builds only part of that Contracts surface should
explicitly name, in its own `PROGRESS.md` entry, every Contracts type it did NOT build a handler for and
why — not just what it built. A plan sentence like "the complete spine" naming a feature (CRM's plan
named "meeting" explicitly) is not, on its own, a substitute for that per-task accounting; a sentence
written once at the start of a phase is easy to satisfy partially and never revisit. The check costs one
line per deferred type per task. Its absence cost a multi-task investigation at G9-14 to even discover
what was actually missing.

## Five more patterns (from Phase 5 — Sites)

Sites is the seventh module and the first to *complete* a pair the earlier phases only half-showed: CRM
inserted a schema into `SchemaOrder`, Sites appended to it, so the general rule can finally be stated
rather than illustrated once. It is also the module that found the largest number of source defects
(nineteen), which says more about reading every handler before porting it than about the source.

### Append vs insert: the rule, now that both cases exist

> **A schema may be APPENDED whenever nothing already migrated points outward at it, and INSERTED
> anywhere that no already-migrated schema points outward from it.** Both reduce to one question — at the
> moment this schema's migration runs, does every table it references already exist? — and the answer is
> decided by the FK graph, not by the module's importance or its build order.

CRM had to be inserted because `projects → crm` already existed. Sites appends because **nothing points
into sites**: its eight cross-schema references all point outward (projects, hr, crm, documents), and the
one thing that appeared to point in — `CrmDesignBrief.SiteId` — turned out to be a fabricated column that
nothing wrote, removed at S1-00 before the real link landed. Check for inbound references before assuming
either shape; a grep for your own module's name across other modules' entities is the whole check.

### Raw-SQL migration objects need their own assertion — the probe cannot see them

Every module so far has verified its schema with an **empty probe migration**: `dotnet ef migrations add
Probe` produces nothing, therefore the model and the database agree. That check is blind, by
construction, to anything created with `migrationBuilder.Sql(...)`.

> The probe diffs the **EF model** against the snapshot. A raw SQL statement is not in the model, so
> deleting a sequence, a check constraint, or a functional index still yields an empty probe **and a
> green build**.

Sites created thirteen such objects (a sequence, five functional/partial unique indexes, six check
constraints, a composite unique key and the composite FK targeting it), so it added
`SitesSchemaObjectTests`: migrate a fresh Testcontainers database and assert each object against
`pg_class`/`pg_constraint`/`pg_indexes`. Three properties make it worth copying rather than approximating:

1. **Fail-closed.** The expected constraint set is a named list, and a second test asserts the database
   contains *nothing outside it* — so adding a constraint without listing it fails, the same shape as
   `MetaSelfEnforcementTests`.
2. **Behaviour, not just existence, for the ones that can be silently wrong.** A plain `("Code")` index
   and a `lower("Code")` index have the same name and both "exist"; only one prevents `SITE-1` and
   `site-1` coexisting. Assert the definition contains `lower`, and let one live INSERT prove a check
   constraint actually rejects.
3. **Sequences especially.** The application reads `site_code_seq` by name via `nextval`. Nothing else in
   the system notices its absence until a create fails in production.

Rule 9 already covers cross-schema FKs this way; this is the same technique applied to the rest.

### Two structurally identical links, one constrained and one not — and why that is not an inconsistency

`crm.crm_artifact_documents.DocumentId` and `sites.site_documents.DocumentId` are the same relationship:
a module-owned row pointing at a `documents.project_documents` id, storing no file. CRM's carries **no
foreign key** and Sites' carries a real one. The entire difference is migration order — `crm` precedes
`documents`, so CRM's migration runs before the table it would reference exists, while `sites` is last.

Record this wherever both are visible (`docs/cross-schema-foreign-keys.md` has them as adjacent
paragraphs). Read apart they look like someone forgot; read together they are a consequence of
`SchemaOrder`. If a future refactor moves `sites` earlier, that FK has to go the same way CRM's did.

### Wiring a scope decorator proves nothing — scanning zero types succeeds silently

`ProjectScopeRegistration` (G5-07) was once pointed at the wrong assembly. It found zero
`IProjectScoped` types, decorated nothing, threw nothing, and every project-scoped command ran with **no
row-level access control at all**. The registration looked correct in review.

> A scope decorator's verify must be **behavioural**: a caller who is not entitled to the parent resource
> gets the module's not-found error from a query whose handler, called directly, would have returned the
> row. Confirm it FAILS with the registration removed. Nothing else distinguishes "wired" from "wired and
> actually applied."

Two corollaries Sites hit directly. The registration helper deliberately **skips types with no handler
registered** (so the decorator can be wired before its first consumer exists) — which also means
registering it with zero consumers is indistinguishable from success, so the module needs at least one
real scoped handler before the assertion means anything. And the scan must target the assembly where the
marker interface's implementors actually live: for every module so far that is `*.Contracts`, because
that is where command and query records live, **not** `*.Application`.

### Global master data is a deliberate scope exemption, and the exemption should be machine-checked

Sites' definition layer — modules, items, options, links — has no project and no site, so none of its 20
messages implements `ISiteScoped` while all 18 instance messages do. That is correct and it is also
exactly what a forgotten marker looks like.

The backend guards it at startup by reflecting over every command and query and throwing if one carries a
`Guid SiteId` **without** the marker; its own comment records that the per-handler version of the check
had been written once out of twenty-one times, and that adding the guard immediately caught a real miss.
Ported here as a test (`SiteScopeCoverageTests`), with the converse asserted too — every marked message
really has the id — so the check cannot be satisfied by marking everything. Express the exemption as
"has no id to scope by", never as an allow-list: an allow-list can be appended to.

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


## Positive controls

**The standing rule: an assertion that something never happens proves nothing until the instrument has
been shown to detect it happening.**

A negative assertion over a set that machinery gathered — a logger provider, an EF interceptor, a
reflection scan, a `pg_*` catalog query, a DI resolution — passes identically whether the system is
clean or the machinery is dead. Both produce an empty set. Four instances of this shape have been
found in this codebase so far (E1-01, P5F-01, D1, N1-08), and the sweep that established this rule
found the fifth in the one place designed to prevent it: `MetaSelfEnforcementTests`, the test whose
whole job is stopping other tests from passing vacuously, had no check that its own `.slnx` parse
returned anything.

### Which assertions need one

Apply the criterion literally:

> An assertion is at risk **iff the thing asserted about is gathered by machinery**, rather than being
> a direct return value of the call under test.

`Assert.False(result.IsSuccess)` is not at risk — the object exists and the property is read off it.
`Assert.Empty(fixture.CapturedSql.Where(...))` is, and so is `Assert.Empty(violatingTypes)` in an arch
rule. Most negative assertions in this repo are the first kind and must be left alone; adding controls
to all of them would be noise.

### Shape 1 — liveness. Mandatory, one line.

Assert the gathered set is non-empty **in the same test**, before asserting what it does not contain:

```csharp
// Prove the collector collected...
Assert.Contains(fixture.CapturedSql, sql => sql.Contains("crm.", StringComparison.OrdinalIgnoreCase));
// ...then that it saw no violation.
Assert.DoesNotContain(fixture.CapturedSql, sql => sql.Contains("identity.", StringComparison.OrdinalIgnoreCase));
```

Name the thing that **must** be there rather than asserting bare non-emptiness where you can. Same cost,
and it additionally proves the measured window covers the operation under test rather than some
unrelated statement.

Two details that decide whether the liveness line is worth anything:

- **Clear before the action, never after.** `ClearCapturedSql()` after the operation empties the very
  evidence you are about to assert on.
- **Watch the direction of `Except`.** `Expected.Except(actual)` fails loudly when the query returns
  nothing — the whole expected list shows up as missing. `actual.Except(Expected)` passes green on an
  empty result. Only the second needs a liveness line, and it needs one badly: a typo'd schema name in
  a `pg_constraint` query is invisible without it.

### Shape 2 — discrimination. Where a violation can be injected in a few lines.

Inject a real violation, confirm the failure names it, revert. **The injection must match the mechanism
the rule uses** (P5F-01's own finding) or you have tested something other than the rule.

**Liveness on a set does not cover a filter, and this is not hypothetical.** Where an assertion
resolves a collection and then narrows it, a dead *filter* is a second failure mode that non-emptiness
cannot see. `NotificationEventConsumerTests` asserted that no handler whose class name contains
`"Notification"` is registered for four deliberately-silent events. It could never have failed:
`AddModuleIntegrationEvents` wraps every handler in `InboxDedupeDecorator<TContext, TEvent>`, so
`GetServices` returns the decorator and `GetType().Name` is always ``InboxDedupeDecorator`2`` — the
filter matched nothing for any event, ever. Its sibling test resolved handlers *without* the name
filter and stayed green throughout, which is exactly why one control over half a query proves nothing
about the other half.

The fix was to the instrument, not the assertion: the claim is structural — *"no notification handler
class exists for this event"* — so it now reflects over the `*.Infrastructure` assemblies, which no
container wrapping can disguise. **If a filter is part of your assertion, exercise the filter against a
case you know it should match.**

### A gathered set that is legitimately empty needs a pair, not a `NotEmpty`

The at-risk criterion is necessary but not sufficient. Some assertions run against a state where the
gathered set is *correctly* empty — a pivot after its only column is detached, a guard given an empty
input list. Asserting non-emptiness there is simply wrong, and turns a correct test red.

Use a before/after pair instead: prove the query returns the row **before** the operation, so its
absence afterwards is attributable to the operation rather than to a query that returns nothing.

```csharp
var before = await ListAsync(projectId);
Assert.Contains(before, d => d.Id == documentId);   // the query can see it...

await DeleteAsync(documentId);

var after = await ListAsync(projectId);
Assert.DoesNotContain(after, d => d.Id == documentId);   // ...so this absence is the delete's doing
```

Where even that is impossible — an empty input can only produce an empty output — record why, and lean
on whatever sibling assertion carries the real claim (typically "no query was issued at all").

### What this means for a new module

A new module's schema-isolation and schema-object tests are exactly the shape that was dead for three
phases. **Liveness belongs in the verify clause of the task that writes them, not in a follow-up.** A
schema-object test whose catalog query names the wrong schema, and a schema-isolation test whose
collector is unplugged, are both green on the day they ship.

### An injection that fails to go red is a finding about the test, not a pass for the code

The Shape 2 loop has an outcome nobody plans for: you inject the violation and the test **stays
green**. The reflex is to assume the injection was wrong. Check that, then stop assuming it — the more
common cause is that the assertion never reached the clause it claims to pin, and you have just been
handed the only evidence that will ever say so.

Both of F1-08's injections did this, and neither was a near miss:

- The **"stage is not funded"** test asserted a failure that a *different, earlier* condition was
  already producing. The funding clause it named was never evaluated. Injecting a fault into that
  clause changed nothing, because nothing was reading it.
- The **"the consumer writes nothing"** test passed because there was nothing to write to. The absence
  it asserted was structural, not behavioural — it would have held with the consumer deleted.

Both were green. Both had been green since the day they were written. Both looked exactly like
coverage, and the *only* thing in the entire process that distinguished them from coverage was an
injection that refused to fail.

**So when an injection passes, the finding is about the test.** Fix the test, then re-inject and
confirm it now reddens — the fix is not done until the injection fails. And do not let a passing
injection stand as evidence about the *code*: it says nothing about the code either way, because the
instrument that would have reported on the code has just been shown not to work.

The same applies to any instrument you are reading a result off, not only to injected faults. X-01
measured line endings with `grep -c $'\r'`, whose `\r` the shell stripped; it returned the line count
for every file and reported "CRLF everywhere" for LF files and CRLF files alike. Two conclusions were
drawn from it before it was checked against a known-LF and a known-CRLF file, where a working
instrument gives 2 and 0 and that one gave 2 and 2. **A measurement that cannot distinguish the two
cases is not evidence about either**, and a dead instrument reads exactly like a real finding.

### Cite assertions, not line numbers

A plan or a comment that points at a test by line number breaks the moment any task edits that file,
and it breaks *silently*: the cited line still exists and still contains plausible code, so following
the reference lands you somewhere real and wrong.

Phase 7 is the worked example. PC-03 added liveness across eight `Architecture.Tests` files, shifting
every line number the Phase 7 plan cited into them. The plan named **three** discovery floors to raise
for the ninth module. There were **seven**, across six files, and only one of them counted modules —
`MetaSelfEnforcementTests` asserts `moduleProjectNames.Count >= 32`, a *project* count (8 modules × 4
projects), which Finance raises to 36 and not to 9. Navigating by the cited line would have written the
module number into the project-count assertion and left four floors untouched.

**Cite the assertion text and grep for it**, and cite the part that will not move. `layered.Count >=`
finds `Rule3ModuleIsolationTests`' floor in one command and keeps finding it after the number changes —
which it does every time a module lands. Writing `layered.Count >= 27` into a plan is the same mistake
one level down: correct on the day it is written, silently wrong the day after the next module ships,
and a reader who trusts it never re-greps.

## Six more patterns (from Phase 6 — Notifications & Audit)

### 1. `IPeriodicJob` — the third define-in-one-place / implement-in-many registry

`IGateConditionTypeHandler` (Projects defines, three modules implement) established the shape;
`IPeriodicJob` is the third instance, after that and the CQRS/integration-event handler scans.

The seam lives in **BuildingBlocks**, not in `Notifications.Contracts`, and the reason is the whole
design. The backend runs nine `IPeriodicJob`s inside its notification module, each reading another
area's tables. Here that is impossible — Notifications cannot read `projects.project_tasks` — and the
alternative (a read contract per job, Notifications depending on four modules) inverts the dependency.
**So the jobs moved to the data**, and the scheduling seam moved to the one assembly everybody already
depends on. One `PeriodicJobWorker : BackgroundService` resolves every registration and runs them
sequentially on one timer, interval `Notifications:SweepMinutes`, default 15.

Registration is the same Scrutor scan as the gate handlers, in each module's own `Register`:

```csharp
services.Scan(scan => scan.FromAssemblies(applicationAssembly)
    .AddClasses(classes => classes.AssignableTo<IPeriodicJob>(), publicOnly: false)
        .AsImplementedInterfaces()
        .WithScopedLifetime());
```

Two things a new job must not get wrong. **Be idempotent** — the worker gives no at-most-once
guarantee and deliberately does not try to; idempotence comes from the notification dedupe key, which
is scoped per recipient. And **keep the scan window** — bounding an overdue scan to N days costs
nothing in correctness (the dedupe key already stops repeats) and is the difference between reading a
bounded slice and a growing table every tick.

### 2. `ConfigureAudit(ownsMigration:)` — the outbox's `ExcludeFromMigrations` shape, reused

`AuditLog` lives in BuildingBlocks next to `OutboxMessage`, in the `shared` schema, and every module's
`DbContext` maps it with `ConfigureAudit(ownsMigration: false)` while `SharedDbContext` owns the
migration. Identical in shape to `ConfigureOutbox`, and for the same reason: every module writes it,
none owns it.

**Why `shared` and not the new module's own schema.** Two reasons, and the second is the decisive one:

1. `shared` is FIRST in `SchemaOrder`, so the table exists before any module migrates or seeds. A
   module-owned audit table migrating last makes correctness depend on the unstated invariant "nobody
   writes an audited entity between the first module migration and the last."
2. `tests/BuildingBlocks.IntegrationTests` references only BuildingBlocks and SharedKernel. Had the
   table been module-owned, the interceptor — a BuildingBlocks type — could not have been
   integration-tested in the project that owns it without that project depending on a module.

**Generalize it:** a table that every module writes and no module owns belongs in `shared`, mapped
everywhere and migrated once.

### 3. The audit opt-in is a marker interface, never a list

`AuditSaveChangesInterceptor` audits every `IAuditable` entity in the change tracker. There is **no**
allow-list and **no** entity-to-module dictionary. Entity name comes from `entry.Metadata.ClrType.Name`
and module from `entry.Metadata.GetSchema()` — both derived, never passed.

That is not style. The backend gates on a hand-maintained `HashSet<string>` beside a separate module
`Dictionary`, and its own source carries a red comment recording the cost: the CRM catalog was in one
and absent from the other, so every catalog write logged nothing at all, silently. Implementing
`IAuditable` is compiler-checked, lives in the entity's own file, and cannot be spelled wrong.

Consequences a new module inherits automatically: mark an entity `IAuditable` and it is audited; every
context that has auditable entities must call `ConfigureAudit(false)` or the interceptor throws (an
architecture test asserts this at build time); and **any fixture that writes auditable entities must
migrate `shared` first**.

### 4. Where a notification handler lives

> A notification handler lives in `Notifications.Infrastructure` **iff** every recipient it needs is
> derivable from the event payload plus the *existing* published Contracts surfaces. Otherwise it lives
> in the **publishing module's** `.Infrastructure`, reading its own tables and calling
> `INotificationService`.

The alternative — growing a read contract so a handler in Notifications can fetch the recipient — puts
another module's internals on a public surface for one caller. `SiteApproved` is the clearest case: the
payload carries the approver, and the person who wants to know is the submitter
(`sites.sites.CreatedById`), so the handler is in Sites.

The same rule placed the periodic jobs. It is why Phase 6 extended **no** module's Contracts.

**And the failure mode to watch:** a handler in a module that never calls `AddModuleIntegrationEvents`
is registered nowhere, and its event is marked processed with zero handlers — legitimately, per R2 — so
nothing fails, nothing logs, and the notification simply never arrives. Three modules made that call
for the first time in Phase 6.

### 5. The format gate is apply-then-diff, not `--verify-no-changes`

```bash
dotnet format DMS.Modular.slnx && git diff --exit-code
```

P5F recorded `--verify-no-changes` reporting a violation that apply mode then declined to fix, in a file
it agreed was correct. Proving cleanliness by the tool's own transformation sidesteps the disagreement.

**Operationally:** it only means "format changed nothing" when the tree is otherwise clean, so run it as
`git add -A` → `dotnet format` → `git diff --exit-code`. Staged work is the baseline; anything the diff
shows afterwards is the formatter's own edit. Run before staging, it just re-prints your own changes and
always exits 1.

### 6. `CapturedSql` measures the process, not the request

`ApiFixture.CapturedSql` records every EF "Executed DbCommand" in the host. Two things about it are
easy to get wrong, and Phase 6 got caught by both.

**It needs `UseSerilog(..., writeToProviders: true)`.** Without that flag Serilog replaces the whole
`ILoggerProvider` set and the capturing provider receives nothing. It was missing from `Program.cs` for
three phases, during which four schema-isolation assertions passed vacuously — *"no captured SQL touches
schema X"* is trivially true when nothing is captured.

**So pair every negative assertion with a positive one.** A negative-only assertion cannot distinguish a
clean run from a broken capture. Assert that the SQL you *expect* was issued, in the same test.

**And quiesce before measuring.** The capture is process-global and cannot attribute a statement to the
request that caused it, so a background `OutboxProcessor` delivery landing inside your window reads
exactly like a leak. Drain the outbox (`ProcessPendingAsync`) before `ClearCapturedSql()`. Do not widen
the schema filter instead — that hides real leaks in the same schema.

## Four more patterns (from Phase 7 — Finance)

Finance is the ninth and last business module. It is the first that had to serve routes under another
module's URL space, the first whose data drove another module's state machine, and the one that
completed the gate-type registry — so its four lessons are about **boundaries under pressure**, not
about layout.

### 1. Route shape and module ownership are independent. Let them differ.

Sixteen of Finance's 54 routes are mounted under `/api/projects`, `/api/project-stages` and
`/api/project-tasks`. They are served entirely by Finance, because Finance owns every table they read.
Projects contributes the id in the URL and nothing else.

Nothing in the module system objects: `MapModuleEndpoints` scans the module's own assembly, and two
modules mapping groups on one prefix is ordinary ASP.NET routing — the paths are distinct, so there is
nothing to resolve. `FinanceSmokeTests` asserts all sixteen **and** that Projects' own `/api/projects`
still resolves, which is what proves the absence of a collision rather than assuming it.

**The failure mode this exists to prevent is the tidy-up.** Moving those handlers into Projects so the
folder matches the URL would make Projects read `finance.*` tables and break rule 3. The URL is a
client-facing information-architecture decision; the module boundary is a data-ownership one.

**Permissions follow the data, not the URL.** Every one of those sixteen requires `finance.read`. A
caller holding `projects.read` and no finance permission gets a 403, which is correct.

### 2. When another module must react to your data, publish a VERDICT — not the inputs

`ConditionType.Budget` was the last unhandled gate type. The backend resolves it by writing
`gate_conditions`, `project_tasks` and `project_stages` from inside its own ledger and variation
services — all cross-module writes, all forbidden here.

The obvious alternative is a read contract: `IFinanceReader`, letting Projects ask whether a task is
covered. **That was considered and rejected**, because every input to the rule is a Finance column, so
Finance can decide it alone. It publishes `TaskBudgetCoverageChanged(taskId, isCovered)` and Projects
acts. Nobody reads across the boundary in either direction.

Three things that turned out to matter, each of which cost a bug or a vacuous test to learn:

- **Publish on every relevant event, not only on a change.** Publishing transitions requires remembering
  the previous verdict, and that memory is a second source of truth that drifts from the gates it
  describes. Make the consumer idempotent instead — satisfying an already-satisfied gate is a no-op.
- **The false branch is half the contract.** A consumer handling only `isCovered: true` leaves a gate
  satisfied on a task that has gone over budget. The de-satisfy path is not an edge case.
- **Do not re-query state you have just mutated but not saved.** The consumer runs inside
  `InboxDedupeDecorator`, which stages writes and saves once at the end. A query issued after
  `MarkPending()` still sees `Satisfied` in the database, so a "is anything still blocking?" check reads
  the old world and silently skips its own follow-up action. Read the in-memory list you just changed.

### 3. A contract with no consumer is not shipped early — it is not shipped

F1-01 shipped two records (`ProjectTaskFinancialBlock`, `ProjectStageFinancialBlock`) because the
backend had them. Porting the queries showed their only consumers there were **another module's** detail
endpoints, which this port does not extend. They were removed at F1-07b.

Keeping them would have been keeping half of a decision already made — they are `IFinanceReader`'s
payload without its interface. The rule, which G9-14 first recorded and Finance re-learned: **name every
Contracts type you ship without a handler, and which task builds it.** If the answer is "none", delete
the type. Two public records nothing produces is a surface someone will eventually implement against.

### 4. Money: precision belongs in the schema guard, not only in the model

All 22 decimal columns are `numeric(19,4)`, and `InvoiceLine.Create` rounds to 4 places *because* the
column does — widening one without the other silently reintroduces the truncation the rounding exists to
make explicit.

**The empty-probe verify does not cover this the way it covers a missing column.** `HasColumnType` is in
the EF model, so an unmigrated change does produce a non-empty probe — but a *coordinated* edit
(configuration changed, migration regenerated) leaves model and snapshot agreeing and the probe empty,
while every deployed column changes scale. So assert the precision against `information_schema` as an
exact named set, under the same R3 fail-closed guard as raw-SQL objects.

Two more money rules worth stating once:

- **Enums are `int` here, even where the source stores strings.** A check constraint comparing string
  literals against an int column never matches, never fires, and passes every existence test. If you
  rewrite such a constraint, the enum-parity test is what stops a renumbering unarming it later.
- **Over-budget is `Actual > EffectiveCap`, never `Available < 0`.** `ApplyActualCost` moves Committed
  and Actual in opposite directions by the same delta, so Available is *invariant* under an actual cost.
  The second form looks equivalent and reports nothing.

### One testing note, for whoever writes module ten

`BackgroundLoops:Enabled=false` (default on) stands down `OutboxProcessor`'s and `PeriodicJobWorker`'s
timer loops. `ApiFixture` sets it. Both workers stay registered and resolvable, and every test that
needs them drives them directly — which is what you want anyway, since a test that waits on a timer is
slow and still races. This is the fix for the quiesce-before-measuring problem the Positive controls
section above describes: it removes the interleave rather than draining ahead of it.
