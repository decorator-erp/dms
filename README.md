# dms-modular

A .NET 10 modular monolith: one Postgres database, one schema and one `DbContext` per module,
cross-module reads only through `<Module>.Contracts` interfaces, cross-module writes only through
integration events on a shared outbox.

Built by porting logic out of `../backend` (a single-`DbContext` Clean Architecture solution) rather than
restructuring it in place. `../backend` is read-only reference material — nothing in this repository
modifies it.

**In scope:** Identity and HR. **Explicitly not in scope:** Projects, CRM, Sites, Finance, Documents,
Approvals, Notifications, Reporting — no placeholder projects or stub references exist for any of them.
See `docs/adding-a-module.md` when one of them is built.

## Architecture

- `src/SharedKernel/` — `Entity`, `Result`/`Error`, domain event abstractions. Frozen at ~9 files; no
  module-specific concept belongs here.
- `src/BuildingBlocks/` — CQRS (`ICommand`/`IQuery`/handlers), the Validation → Logging →
  DomainEventDispatch decorator chain, the outbox/inbox pattern, pagination. No ASP.NET Core reference.
- `src/BuildingBlocks.Web/` — `IModule`/`IEndpoint`, module discovery, permission-based authorization,
  `Result` → `IResult` HTTP mapping. The only shared project referencing `Microsoft.AspNetCore.App`.
- `src/Api/` — the composition root. Discovers modules by assembly name (never by referencing an
  internal type), wires JWT auth, migrations ordering, and the outbox processor.
- `src/Modules/<Module>/` — `<Module>.Contracts` (public), `.Domain`, `.Application`, `.Infrastructure`
  (all internal except Contracts). See `docs/adding-a-module.md` for the full shape and rationale.
- `tests/Architecture.Tests/` — NetArchTest-backed rules enforcing the module boundaries above; a new
  module's assembly must be added here before its layers mean anything (see `ModuleAssemblies.cs`).

Non-negotiables, checked by the architecture tests and re-verified at every phase boundary in
`PROGRESS.md`:
- No repository pattern — handlers use their module's `DbContext` directly.
- One `DbContext`, one schema, one module.
- Cross-module reads: `<Module>.Contracts` interfaces only, never another module's tables.
- Cross-module writes: outbox integration events only, never a direct call.
- Only `*.Contracts` types are public.

## Local development

### Prerequisites

- .NET 10 SDK
- Docker (for Postgres — `docker compose up -d`)

### Database

```bash
docker compose up -d
```

Starts Postgres 17 on host port 5433 (`Database=dms_modular;Username=postgres;Password=postgres`).
Testcontainers-based tests spin up their own disposable Postgres and don't touch this one.

Apply each module's migrations (schema order matters — see
`src/BuildingBlocks.Web/ModuleDependencyOrder.cs`):

```bash
dotnet ef database update --context IdentityModuleDbContext --project src/Modules/Identity/Identity.Infrastructure --startup-project src/Api
dotnet ef database update --context HrDbContext --project src/Modules/Hr/Hr.Infrastructure --startup-project src/Api
```

(`shared.outbox_messages`/`inbox_messages` are created by `SharedDbContext`'s own migration, applied
automatically by every module context that maps those tables — no separate command needed.)

### Secrets

Two values have no default and must be set via user-secrets (or environment variables) before the API
will start correctly — both are deliberately absent from `appsettings.Development.json`, since a
committed signing key or seed password is a credential in the repository:

```bash
dotnet user-secrets set "JwtSettings:Key" "<a long random value>" --project src/Api
dotnet user-secrets set "DMS_SEED_ADMIN_PASSWORD" "<a strong password>" --project src/Api
```

`JwtSettings:Key` is required outright — the API throws on startup without it. `DMS_SEED_ADMIN_PASSWORD`
is only required outside the `Development` environment; in `Development` its absence just skips seeding
the default admin user (roles and role claims still seed).

### Run

```bash
dotnet run --project src/Api
```

`/health` returns 200; `/scalar` renders the OpenAPI UI in `Development`. The default admin user (if
seeded) is `admin@system.dms` in the `admin` role, which carries every permission every registered
module has declared.

## Testing

```bash
dotnet build DMS.Modular.slnx -warnaserror
dotnet test DMS.Modular.slnx
dotnet format DMS.Modular.slnx --verify-no-changes
```

Integration and Infrastructure-level tests use Testcontainers — Docker must be running. Test layout per
module: `<Module>.UnitTests` (pure domain, no database), `<Module>.Infrastructure.Tests` (real Postgres,
Infrastructure-level services), `<Module>.Application.Tests` (the module's real DI graph, resolving
handlers rather than constructing them directly). `tests/Architecture.Tests` enforces the module
boundaries; `tests/Integration.Tests` boots the real Api host end to end.

## Adding a module

See `docs/adding-a-module.md` — the four-project layout, the `IModule` contract, what belongs in
`Contracts` vs `Domain`, a wiring checklist in the order you'll actually hit each step, and what each
architecture rule means for a new module.
