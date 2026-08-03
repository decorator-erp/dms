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
that; decide this before writing the first feature, not after the first endpoint.

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
