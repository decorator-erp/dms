# dms-modular — Greenfield Build Plan

Target architecture from `../backend/docs/backend/architecture-anatomy.md`, built clean instead of
restructured in place.

> **How to use this file.** Each task is one PR. Give Claude Code the task by ID:
> `Execute task G1-03 from PLAN.md`. Do not batch tasks. Do not skip the *Verify* block.
> If a *Verify* block fails, stop and report — never loosen the check to make it pass.

---

## Scope decision: Identity + HR only

You asked for Identity, HR and Projects **unless Projects has dependencies**. It does — four of them:

| Projects needs | From | Contract |
|---|---|---|
| Employee names, assignee resolution | **HR** | `IHrDirectory` ✓ (in scope) |
| `Project.Client` → client name/summary | **CRM** | `IClientDirectory` ✗ |
| Gate handlers for attachments / document approval | **Documents** | `IGateConditionTypeHandler` ✗ |
| `ApprovalHandler` → request + read approval state | **Approvals** | `IApprovalRequester` ✗ |

Building Projects now means stubbing three modules that don't exist. Stubs get shipped, then get
forgotten, then get depended on. **Scope is Identity + HR.**

Projects is unblocked after CRM, Documents and Approvals exist. Suggested next order:
`Approvals → Documents → CRM → Projects`.

---

## Decisions locked for this solution

| Decision | Choice | Why |
|---|---|---|
| Repository pattern | **None** | EF Core's `DbSet` + `SaveChanges` already *is* a repository + unit of work. The old solution has 37 repository classes wrapping it. Handlers use the module's `DbContext` directly. |
| Persistence | Fresh migrations, schema per module | `identity.`, `hr.` — no baggage from the 139 existing migrations |
| Database | One PostgreSQL DB, one connection string | It is a modular monolith, not microservices |
| Mediator | None — Scrutor-registered handler interfaces + decorators | Same shape as the existing `ICommandHandler`/`IQueryHandler`; no MediatR dependency |
| Mapping | Manual `ToResponse()` extension methods | Mapster/AutoMapper hide contract drift the compiler could catch |
| Errors | `Result` / `Error` from SharedKernel | Ported as-is; it works |
| Validation | FluentValidation via a decorator | Same as existing |
| Cross-module calls | `*.Contracts` interfaces only | |
| Cross-module events | Outbox from day one | Retrofitting an outbox later is the expensive path |

### On dropping the repository pattern

The tradeoff, stated plainly: you lose the ability to swap the persistence technology without touching
handlers. That swap has never happened in this codebase and almost certainly never will.

What you gain: no `IGenericRepository<T>` leaking `IQueryable` (which defeats the abstraction anyway),
no repository-per-entity ceremony, and queries that can be written as projections instead of
load-then-map.

Handlers depend on the concrete `HrDbContext`. Testing is Testcontainers, not mocks — which is where
the real coverage should be regardless.

---

## Target layout

```
dms-modular/
├── DMS.Modular.slnx
├── Directory.Build.props
├── Directory.Packages.props          ← central package versions
├── .gitignore
├── PLAN.md
├── CLAUDE.md
├── docker-compose.yml                ← postgres for local dev
├── src/
│   ├── SharedKernel/
│   ├── BuildingBlocks/          ← zero ASP.NET. CQRS, decorators, ICurrentUser, outbox
│   ├── BuildingBlocks.Web/      ← IModule, IEndpoint. The ONLY shared project with ASP.NET
│   ├── Api/
│   └── Modules/
│       ├── Identity/
│       │   ├── Identity.Contracts/
│       │   ├── Identity.Domain/
│       │   ├── Identity.Application/
│       │   └── Identity.Infrastructure/
│       └── Hr/
│           ├── Hr.Contracts/
│           ├── Hr.Domain/
│           ├── Hr.Application/
│           └── Hr.Infrastructure/
└── tests/
    ├── Architecture.Tests/
    ├── Integration.Tests/
    └── Modules/
        ├── Identity.UnitTests/
        └── Hr.UnitTests/
```

**Reference matrix, enforced by arch tests.** Nothing else crosses. Only `*.Contracts` types are
`public`; everything else is `internal`.

| Project | May reference |
|---|---|
| `*.Domain` | `SharedKernel` |
| `*.Contracts` | `SharedKernel`, `BuildingBlocks` |
| `*.Application` | `SharedKernel`, `BuildingBlocks`, own `.Domain`, other modules' `.Contracts` |
| `*.Infrastructure` | all of the above **+ `BuildingBlocks.Web`** |
| `Api` | everything |

`BuildingBlocks.Web` is the only shared project carrying `Microsoft.AspNetCore.App`. A module's
`Application` and `Domain` projects must not reference it — that is what keeps ASP.NET out of them at
**compile time**, not just at test time.

---

## Standing rules

```
Build:  dotnet build DMS.Modular.slnx -warnaserror
Test:   dotnet test DMS.Modular.slnx
Format: dotnet format DMS.Modular.slnx --verify-no-changes
```

1. One task = one PR.
2. **Arch tests are written before the code they constrain**, not after.
3. No `IApplicationDbContext`. No shared context. Ever.
4. Every module-internal type is `internal`. If something must be public, ask whether it belongs in Contracts.
5. Port logic from `../backend` where it is sound — do not reinvent JWT issuance or permission
   resolution. Read the original first.
6. Do not add a package without adding it to `Directory.Packages.props`.

---

# Phase G0 — Foundation

### G0-01 — Solution skeleton

Create `dms-modular/` with `.gitignore`, `Directory.Build.props`
(`net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`),
`Directory.Packages.props` with central versions matching the existing repo
(Npgsql 10.0.3, FluentValidation 12.1.1, Scrutor 7.0.0, xunit 2.9.3, NSubstitute 5.3.0),
`DMS.Modular.slnx`, `docker-compose.yml` with Postgres 17.

`git init`. No projects yet.

**Verify:** `dotnet build DMS.Modular.slnx` succeeds on an empty solution; `docker compose up -d` gives a
reachable Postgres.

### G0-02 — SharedKernel

Port from `../backend/SharedKernel/` unchanged: `Entity`, `Result`, `Error`, `IDomainEvent`,
`IDomainEventHandler`, `IDomainEventDispatcher`, `IDomainEventQueue`, `IAuditable`, `AuditIgnoreAttribute`.

No dependencies. **This project is frozen at ~9 files** — additions here are coupling that arch tests
cannot catch.

**Known gap to record, not to fix:** `IAuditable` and `AuditIgnoreAttribute` come across, but the EF
audit interceptor that acts on them lives in the Notifications & Audit module, which is out of scope.
`Employee` implements `IAuditable` in the old solution and will implement it here, and **no audit rows
will be written**. That is correct for this scope and wrong to discover by surprise in six months — put
it in `PROGRESS.md` and the final report.

**Verify:** builds standalone with zero package references.

### G0-03 — BuildingBlocks: CQRS + decorators

`ICommand`, `ICommand<T>`, `IQuery<T>`, `ICommandHandler<>`, `ICommandHandler<,>`, `IQueryHandler<,>` —
port from `../backend/Application/Abstractions/Messaging/`.

`ValidationDecorator` and `LoggingDecorator` — port from `../backend/Application/Abstractions/Behaviors/`.

`PagedResult<T>`, `ValidationExtensions`.

Add `AddModuleCqrs<TContext>(this IServiceCollection, Assembly) where TContext : DbContext` —
Scrutor-scans **one** assembly and applies the decorator chain. Each module calls it with its own
assembly and its own context, so handler registration never spans modules.

**Domain-event dispatch belongs here, generic over the context.** The backend's `DomainEventDispatcher`
takes `ApplicationDbContext` concretely; port it as `DomainEventDispatcher<TContext>` and wire
`DomainEventDispatchDecorator` in `AddModuleCqrs<TContext>`. Building it inside the first module instead
means the second module either duplicates it or references the first — and the second module is HR,
which would be a boundary violation on the way in.

Keep the drain-both-sources behaviour (EF `ChangeTracker` **and** `IDomainEventQueue`) and the
re-loop-on-new-events logic. The 10-iteration cap can stay for now; it exists because gate cascades in
the old solution could chain, and neither module in scope has cascades.

**Verify:** unit test — a command with a failing validator returns `Result.Failure` and the inner
handler is never invoked. Second test — a command raising a domain event dispatches it after success and
not after failure.

### G0-04 — BuildingBlocks: `ICurrentUser`

```csharp
public interface ICurrentUser
{
    string? UserId { get; }
    bool IsAuthenticated { get; }
    bool HasPermission(string permission);
}
```

Interface only — the HTTP implementation lands in `Api` (G0-08). Modules never see `HttpContext`.

**Verify:** `BuildingBlocks.csproj` has no `Microsoft.AspNetCore.*` package or `FrameworkReference`. This
is a **permanent** rule, not a snapshot — the ASP.NET-facing pieces live in `BuildingBlocks.Web` (G0-06).

### G0-05 — BuildingBlocks: integration events + outbox

```csharp
public interface IIntegrationEvent { Guid EventId { get; } DateTimeOffset OccurredAtUtc { get; } }
public interface IIntegrationEventHandler<in T> where T : IIntegrationEvent
{
    Task HandleAsync(T @event, CancellationToken ct);
}
public interface IEventBus { Task PublishAsync(IIntegrationEvent @event, CancellationToken ct); }
```

`OutboxMessage` + `InboxMessage` entities, an `IOutboxWriter` that any module's `DbContext` can persist
through, and `OutboxProcessor : BackgroundService`.

Schema `shared`:

```sql
shared.outbox_messages(id uuid PK, type text, content jsonb, occurred_utc timestamptz,
                       processed_utc timestamptz NULL, attempts int DEFAULT 0, error text NULL)
shared.inbox_messages(event_id uuid, handler_name text, processed_utc timestamptz,
                      PRIMARY KEY (event_id, handler_name))
```

Partial index on `outbox_messages(occurred_utc) WHERE processed_utc IS NULL`.

Processor: poll → resolve handlers → **each in its own scope and transaction** → skip if the
`(event_id, handler_name)` pair is already in the inbox → record `attempts`/`error` on failure.

**The inbox marker must commit in the handler's own transaction.** If the handler writes through
`HrDbContext` and the inbox row is written through `SharedDbContext`, they are two transactions: a crash
between them re-runs the handler and the side effect happens twice, which is the exact thing the inbox
exists to prevent. So every module `DbContext` maps **both** `OutboxMessage` and `InboxMessage` with
`ExcludeFromMigrations()`, and the dedupe insert goes through the same context the handler is using.
`SharedDbContext` owns the migration for both tables and nothing else.

**Zero-handler events count as processed.** `UserRegisteredIntegrationEvent` has no subscriber in this
scope. If `processed_utc` is only set when a handler ran, those rows retry forever and `attempts`
climbs without bound. Resolving zero handlers is success.

**Verify:** integration test — publish an event, handler runs once. Publish the same event twice,
handler still runs once. Handler throws → `attempts=1`, error recorded, retry succeeds. Handler commits
its own write but the process is killed before returning → on retry the side effect is **not** repeated.
An event with no registered handler is marked processed on the first pass.

### G0-06 — `BuildingBlocks.Web`: module registration contract

**Separate project.** `IModule.MapEndpoints` takes `IEndpointRouteBuilder`, which is
`Microsoft.AspNetCore.Routing`. Putting it in `BuildingBlocks` would give every module's `Application`
and `Domain` project a transitive line of sight to ASP.NET — which is exactly how
`IHttpContextAccessor` ended up inside `ProjectAccessService` in the old solution
(`Infrastructure.csproj` carries `FrameworkReference Microsoft.AspNetCore.App` for SignalR). Arch rules
would still catch actual *usage*, but compile-time prevention beats test-time detection.

`src/BuildingBlocks.Web/` — references `BuildingBlocks`, adds
`<FrameworkReference Include="Microsoft.AspNetCore.App" />`:

```csharp
public interface IModule
{
    string Name { get; }
    IServiceCollection Register(IServiceCollection services, IConfiguration cfg);
    IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app);
}
public interface IEndpoint { void MapEndpoint(IEndpointRouteBuilder app); }
```

`IModulePermissions` stays in **`BuildingBlocks`** — it is a list of strings, no ASP.NET involved, and
`Identity.Contracts` consumes it.

`IEndpoint` — port the pattern from `../backend/API/Extensions/EndpointExtensions.cs`, but scanning
**per module assembly**, not the whole app.

**Verify:** arch test — every `IModule` implementation lives in a `*.Infrastructure` assembly. No
`*.Application` or `*.Domain` project references `BuildingBlocks.Web`.

### G0-07 — Architecture tests (before any module exists)

`tests/Architecture.Tests` with NetArchTest. Rules, written now so they constrain everything after:

1. `*.Domain` may not reference `Microsoft.EntityFrameworkCore`
2. `*.Application` and `*.Domain` may not reference `Microsoft.AspNetCore.*`
3. No module may reference another module's `.Domain`, `.Application`, or `.Infrastructure`
4. Only `*.Contracts` may contain `public` types — scoped to `*.Domain`, `*.Application`,
   `*.Infrastructure`, and **excluding types deriving from `Migration` or `ModelSnapshot`**.
   `dotnet ef migrations add` emits those as `public` on every run and regenerates them on every model
   change; hand-editing each one to `internal` fights the tool forever and breaks the moment someone
   forgets. Generated code was never in this rule's scope — the rule exists to stop *hand-written* types
   leaking out of a module. Scoping it correctly is not relaxing it.
5. `IIntegrationEvent` implementations expose only primitives, `Guid`, `DateTimeOffset`, `string`, enums, or collections thereof
6. No type may reference a `DbContext` outside its own module
7. `SharedKernel` has zero project references

8. No `*.Application` or `*.Domain` project references `BuildingBlocks.Web`, and `BuildingBlocks` itself
   references no `Microsoft.AspNetCore.*`
9. **Every cross-schema foreign key points in an allowed module dependency direction** — queries
   `pg_constraint` against a migrated database and checks each `(from_schema → to_schema)` pair against
   an allow-list derived from the module graph. See `docs/cross-schema-foreign-keys.md`.

**Only rule 7 is live today.** Rules 1–6 and 8 have no module projects to constrain yet, and rule 9 has
no cross-schema FK to check until G2-03. That is the point of writing them first — but say so honestly
in `PROGRESS.md` rather than reporting "nine green" as if it meant something at G0-07. Each rule becomes
non-vacuous at a specific later task; record which.

Rule 9 needs the Testcontainers fixture, so implement it as part of G0-09 and reference it here.

**Verify:** all nine pass, and the vacuity table is recorded.

### G0-08 — Api host

`src/Api`: `Program.cs`, JWT bearer auth, `HttpCurrentUser : ICurrentUser`, permission authorization
handler + `HasPermissionAttribute` (port from
`../backend/Infrastructure/Services/Authentication/Filters/`), `GlobalExceptionHandler`,
`CustomResults`/`ResultExtensions` (port from `../backend/API/Infrastructure` and `API/Extensions`),
Serilog, Scalar/OpenAPI, CORS.

Program.cs discovers `IModule` implementations and calls `Register` / `MapEndpoints`. **The composition
root is the only place that sees every module.**

**Verify:** app boots with zero modules registered; `/health` returns 200; `/scalar` renders.

### G0-09 — Integration test harness

`tests/Integration.Tests` — `Testcontainers.PostgreSql` + `WebApplicationFactory`, a fixture that
applies every module's migrations, and a helper to issue a test JWT with a given permission set.

Migrations must run in **module dependency order**, not registration order — `hr`'s FK to
`identity.users` cannot be created before that table exists.

**Define the order in exactly one place.** The `Api` host and the G0-09 integration fixture both need
it; two hand-maintained lists will drift, and the failure mode is a migration error that only shows up
in one of them. Put it in `BuildingBlocks.Web` — either a static ordered list with a comment tying it to
the module graph, or an `Order`/`DependsOn` member on `IModule` that the host topologically sorts.
Whichever you pick, the test fixture consumes the same source.

**Respawn:** configure `SchemasToInclude = ["identity", "hr", "shared"]` and make sure
`__EFMigrationsHistory` is **not** reset — otherwise the fixture re-runs migrations between tests and
the suite slows to a crawl or fails outright.

**Verify:** smoke test — app boots against a container, migrations apply, a protected route returns 401
unauthenticated. Arch **rule 9** (cross-schema FK direction) is implemented here — it needs a migrated
database. It passes vacuously until G2-03 creates the first cross-schema FK.

---

# Phase G1 — Identity module

Source to port from: `../backend/Application/Features/{Auths,Permissions,Roles}`,
`../backend/Infrastructure/Services/{AuthService,PermissionService,EffectivePermissionService,RoleService,Authentication/*}`,
`../backend/Infrastructure/Persistence/Seeders/*`, `../backend/Domain/Constants/{DefaultPermissions,RolePermissions,Roles,DefaultUsers}.cs`.

### G1-01 — `Identity.Contracts`

```csharp
public interface IUserDirectory
{
    Task<UserSummary?> GetAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct);
}
public sealed record UserSummary(string Id, string Email, string? DisplayName, bool IsActive);

public interface IPermissionCatalog          // collects IModulePermissions from every module
{
    IReadOnlyList<ModulePermissionSet> All();
}
public sealed record ModulePermissionSet(string Module, IReadOnlyList<string> Permissions);

/// <summary>
/// Write operations other modules may perform on a user account. Deliberately narrow.
/// HR calls this when an employee is deactivated — HR → Identity is an allowed direction, and it is
/// the only way to keep "employee left" and "login disabled" in step now that Identity is a sink and
/// cannot subscribe to Hr.Contracts events.
/// </summary>
public interface IUserAdministration
{
    Task<Result> DeactivateAsync(string userId, CancellationToken ct);
    Task<Result> ReactivateAsync(string userId, CancellationToken ct);
}

public static class IdentityPermissions
{
    public const string UsersRead = "users.read";
    public const string RolesRead = "roles.read";
    public const string RolesManage = "roles.manage";
    public const string PermissionsManage = "permissions.manage";
}

// integration events
public sealed record UserRegisteredIntegrationEvent(Guid EventId, string UserId, string Email, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
public sealed record UserDeactivatedIntegrationEvent(Guid EventId, string UserId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
```

**Batch method is mandatory.** Without `GetManyAsync`, every list screen becomes N+1 across a boundary.

**Verify:** Contracts references only SharedKernel + BuildingBlocks.

### G1-02 — `Identity.Domain`

`ApplicationUser` (extends `IdentityUser<string>`), `ApplicationRole`, `ApplicationRoleClaim`,
`RefreshToken`, `UserPermissionOverride`. Errors: `AuthErrors`, `UserErrors`, `RoleErrors`,
`PermissionErrors`. Enum: `UserType`.

Port the entities but **review each one** — this is the chance to fix anything that was awkward. Note
anything you change in the PR description.

**Verify:** no EF Core reference; unit tests for refresh-token expiry and rotation.

### G1-03 — `Identity.Infrastructure`: persistence

`IdentityDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>` with
`HasDefaultSchema("identity")`. EF configurations. Initial migration.

`IdentityModule : IModule` registering the context, ASP.NET Identity, and
`IModulePermissions` for the `IdentityPermissions` constants.

**No repositories.** Handlers take the context directly.

**Naming:** call it `IdentityModuleDbContext`. `IdentityDbContext` collides with the ASP.NET base type
`Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext<,,>` — it compiles with
qualification at the base-type position but costs every future reader a double-take.

**Verify:** migration applies to a clean container; `identity.` schema contains exactly the expected
tables; a probe migration yields an empty diff. **`--context` and the project flags are required** once
more than one `DbContext` exists, or EF fails with *"More than one DbContext was found"*:

```bash
dotnet ef migrations add Probe --context IdentityModuleDbContext \
  --project src/Modules/Identity/Identity.Infrastructure --startup-project src/Api
dotnet ef migrations remove --context IdentityModuleDbContext \
  --project src/Modules/Identity/Identity.Infrastructure --startup-project src/Api
```

### G1-04 — `Identity.Infrastructure`: JWT + permission resolution

Port `JwtProvider`, `JwtOptions`, `EffectivePermissionService` (role claims ∪ user overrides),
`PermissionService`, `RoleService`.

Keep the effective-permission logic identical — it is the security core. Read the original carefully
before rewriting anything.

**Verify:** unit tests for permission resolution including the override path (grant and revoke).
Integration test: a token issued for a user carries exactly the resolved permission claims.

### G1-05 — `Identity.Application`: auth features

`Register`, `Login`, `RefreshToken`, `RevokeToken`, `ChangePassword`. Commands + handlers +
validators + response records + `ToResponse()` extensions.

`Login` publishes nothing. `Register` publishes `UserRegisteredIntegrationEvent`.

**Verify:** integration tests for the full login → refresh → revoke cycle, plus reuse of a revoked
refresh token being rejected.

### G1-06 — `Identity.Application`: roles & permissions features

`GetAllPermissions`, `GetUserPermissions`, `GetRolePermissions`, `AssignPermissionToRole`,
`RemovePermissionFromRole`, role CRUD.

`GetAllPermissions` reads from `IPermissionCatalog` — the union of every module's registered strings.
**Identity must not hold a hard-coded list of other modules' permissions.**

**Verify:** integration test — register a fake `IModulePermissions` in the test host and assert its
strings appear in `GET /api/permissions`.

### G1-07 — `Identity.Infrastructure`: endpoints + seeding

`AuthEndpoints`, `RoleEndpoints`, `PermissionEndpoints` implementing `IEndpoint`.
Seeders for default roles, role claims, and the default admin user — idempotent, run at startup.

**Verify:** seeding twice produces the same rows. Arch rule 4 passes (only Contracts is public).

---

# Phase G2 — HR module

Source: `../backend/Application/Features/{Employees,Departments,EmployeeDepartments}`,
`../backend/Domain/Entities/{Employee,Department,EmployeeDepartment}.cs`,
`../backend/Domain/Services/{EmployeeDomainService,DepartmentDomainService}.cs`.

### G2-01 — `Hr.Contracts`

**Where the name and email come from.** `Employee` in the old solution has `JobTitle`, `HireDate`,
`ContractType`, phone and emergency contacts — **no name, no email**. Those live on `ApplicationUser`
(`FirstName`, `LastName`, `Email`), and `V_EmployeeProfile` (a SQL view in the `dms` schema) joins the
two. That view cannot survive the split.

The fix is not to pick a source for `EmployeeSummary.FullName` — it is to notice that **most callers do
not want a name at all** and should not pay for one. Split the two shapes:

```csharp
// HR-owned data only. One query. No cross-module call. This is the hot path.
public sealed record EmployeeSummary(
    Guid Id, string AppUserId, string JobTitle, Guid? PrimaryDepartmentId, bool IsActive);

// Composed: HR + Identity. Two queries. Display paths only.
public sealed record EmployeeProfile(
    Guid Id, string AppUserId, string FullName, string? Email,
    string JobTitle, Guid? PrimaryDepartmentId, bool IsActive);

public interface IHrDirectory
{
    // --- HR-only. No Identity involvement. ---
    Task<Guid?> ResolveEmployeeIdAsync(string appUserId, CancellationToken ct);
    Task<EmployeeSummary?> GetAsync(Guid employeeId, CancellationToken ct);
    Task<IReadOnlyList<EmployeeSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<Guid>> DepartmentMembersAsync(Guid departmentId, CancellationToken ct);

    // --- Composed with IUserDirectory.GetManyAsync. Costs one extra batched call. ---
    Task<IReadOnlyList<EmployeeProfile>> GetProfilesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

public static class HrPermissions { /* employees.*, departments.* */ }

public sealed record EmployeeHiredIntegrationEvent(Guid EventId, Guid EmployeeId, string AppUserId, Guid? DepartmentId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
public sealed record EmployeeDeactivatedIntegrationEvent(Guid EventId, Guid EmployeeId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
```

**Compose, do not denormalize.** Copying `full_name`/`email` into `hr.employees` needs a `UserUpdated`
event the plan does not define, and buys eventual consistency on a field users read constantly. A stale
name is a bug people report and you cannot reproduce. Names change rarely — the cost of getting them
right is one batched lookup on display paths only.

**What this buys:** `ResolveEmployeeIdAsync` — called by every authorization check in the future Projects
module — touches only `hr.employees`, indexed on `app_user_id`. Zero cross-module cost on the hottest
path in the system. Cache it per request (scoped memo) and say so in the XML doc.

`V_EmployeeProfile`'s remaining fields (role name, last login, department name, created dates) belong to
the **Reporting** module when it exists. Do not recreate the view in HR.

**Verify:** Contracts references only SharedKernel + BuildingBlocks. Arch rule 5 passes.
`EmployeeSummary` contains no field sourced from Identity.

### G2-02 — `Hr.Domain`

`Employee`, `Department`, `EmployeeDepartment`. `Employee.AppUserId` is a **`string` id**, not a
navigation to `ApplicationUser` — that is the module boundary, and it is the single most important line
in this module.

Port `EmployeeDomainService` and `DepartmentDomainService`. Enums `HiringReason`, `PastExperience`.
Errors `EmployeeErrors`, `DepartmentErrors`.

**Verify:** no reference to `Identity.Domain`. Unit tests for the domain services.

### G2-03 — `Hr.Infrastructure`: persistence

`HrDbContext` with `HasDefaultSchema("hr")`. Configurations, initial migration, `HrModule : IModule`.

`Employee.AppUserId` gets an index — `ResolveEmployeeIdAsync` filters on it constantly.

**The DB-level FK stays even though the EF navigation goes.** These are two different things: dropping
`Employee.AppUser` is the module boundary; dropping the constraint would just remove referential
integrity inside a single database and buy nothing. Do not conflate them.

**Cross-schema FK:** add `hr.employees.app_user_id → identity.users(id) ON DELETE RESTRICT` via
`migrationBuilder.Sql(...)`. EF cannot emit it (the two entities are in different models), so write it by
hand and reverse it in `Down`. The EF configuration keeps `AppUserId` as a plain indexed property — no
`HasOne`. Full rationale and the ordering/indexing gotchas: `docs/cross-schema-foreign-keys.md`.

**Verify:** migration applies; `hr.` schema correct; the FK exists in `pg_constraint`; arch **rule 9**
passes; `AppUserId` has a unique index; EF metadata contains **no navigation** to `ApplicationUser` and
no relationship targeting an `identity.` table.

### G2-04 — `Hr.Application`: employee features

CRUD, list with paging/filter, activate/deactivate, assign to department.
`Create` publishes `EmployeeHiredIntegrationEvent`.

**Deactivation must still disable the login.** Today `EmployeeDomainService.DeactivateAsync` writes
`ApplicationUser.IsActive = false` directly. After the split HR owns `Employee.IsActive` and nothing
else — so unless this is handled, **deactivating an employee no longer prevents them signing in.** That
is a security regression, not a behaviour note.

Fix: HR → Identity is an allowed direction. Do **not** invert it — Identity is a sink and must never
reference `Hr.Contracts`.

**Route it through the outbox, not a bare synchronous call.** A direct
`IUserAdministration.DeactivateAsync` after the employee write is two transactions with no retry: if the
second fails, the employee is inactive, the login still works, and nothing will ever try again. For a
security-relevant action that is the wrong failure mode.

Instead:

1. `DeactivateEmployeeCommandHandler` writes `Employee.IsActive = false` **and** the
   `EmployeeDeactivatedIntegrationEvent` outbox row in one transaction.
2. An `IIntegrationEventHandler<EmployeeDeactivatedIntegrationEvent>` **living in `Hr.Infrastructure`**
   calls `IUserAdministration.DeactivateAsync(appUserId)`. The handler is in HR, so Identity still
   references nothing.
3. `OutboxProcessor` retries until it succeeds. `DeactivateAsync` is idempotent, so retries are safe.

Optionally attempt the call synchronously as well, for immediacy, and treat its failure as non-fatal —
the outbox is the mechanism of record either way. Do not let a failed Identity call fail the command
after the employee write has committed.

The window between the two is one poll interval. That is strictly better than a permanent silent gap.

Validate `AppUserId` via `IUserDirectory.GetAsync` — a contract call, never a join into `identity.`.

**`CreateEmployee` takes an existing `AppUserId`, not a name.** The old `CreateEmployeeRequest` takes
`FirstName`/`LastName` because it creates the user and the employee in one call across what are now two
modules. Keep the two commands separate — `Identity.Register` then `Hr.CreateEmployee` — and let the API
endpoint orchestrate if the UI wants one call. A single command spanning two modules' write models is
the thing this architecture exists to prevent.

Note in the PR description that the endpoint contract changed, so the frontend can be updated.

**Verify:** integration tests. Creating an employee with an unknown `AppUserId` fails validation.
`CreateEmployeeCommand` has no name or email field.

### G2-05 — `Hr.Application`: department features

Department CRUD, employee-department assignment, department tree/list queries.

**Verify:** integration tests.

### G2-06 — `Hr.Infrastructure`: `IHrDirectory` + endpoints

Implement `IHrDirectory` over `HrDbContext` with projections (`Select` to `EmployeeSummary`, never
load-then-map). Add the per-request cache for `ResolveEmployeeIdAsync`.

`GetProfilesAsync` = one `HrDbContext` projection + one `IUserDirectory.GetManyAsync`, zipped in memory.
Two calls total regardless of how many ids are passed. Never loop.

`EmployeeEndpoints`, `DepartmentEndpoints`, `EmployeeDepartmentEndpoints`. The employee **list** endpoint
returns profiles (names are what the screen shows); the employee **lookup** endpoints used by other
modules return summaries.

**Verify:** integration tests —

- `GetManyAsync` with 100 ids issues **one** query and **zero** calls to Identity
- `GetProfilesAsync` with 100 ids issues **one** HR query and **one** `IUserDirectory` call
- `ResolveEmployeeIdAsync` called 3 times in a request hits the database once

These are the tests that stop the N+1 that would otherwise appear the moment Projects is built.

---

# Phase G3 — Close out

### G3-01 — Cross-module smoke test

Register a user (Identity) → create an employee referencing that user (HR) → assert
`IHrDirectory.ResolveEmployeeIdAsync` returns the employee id, and that HR issued **no** query against
`identity.`.

**Verify:** enable EF query logging in the test and assert no SQL touches the `identity` schema.

### G3-02 — `CLAUDE.md`

```markdown
# dms-modular

Modular monolith. Anatomy: ../backend/docs/backend/architecture-anatomy.md · Plan: PLAN.md

## Rules
- No repository pattern. Handlers use their module's DbContext directly.
- One DbContext per module, one schema per module, one database.
- Cross-module reads: <Module>.Contracts interfaces. Never another module's tables.
- Cross-module writes: integration events on the outbox. Never direct calls.
- Domain events = in-transaction, intra-module. Integration events = post-commit, cross-module.
- Integration event payloads: IDs and primitives only.
- Sync cross-module calls are depth-1. A→B→C means the design is wrong.
- Every contract that returns one thing also returns many. No exceptions.
- Only *.Contracts types are public.
- SharedKernel is frozen at ~9 files.
- Authorization order: route claims (403) → module scope decorator (404) → aggregate state (409).

## Commands
- Build: dotnet build DMS.Modular.slnx -warnaserror
- Test:  dotnet test DMS.Modular.slnx
- Arch:  dotnet test tests/Architecture.Tests
- DB:    docker compose up -d
```

### G3-03 — README + module authoring guide

A short `docs/adding-a-module.md`: the 4-project layout, the `IModule` contract, what goes in Contracts
vs. Domain, and the arch rules a new module must satisfy. Write it while the memory of building Identity
and HR is fresh — it is the checklist for the next seven modules.

---

## Task summary

| Phase | Tasks | Delivers |
|---|---|---|
| G0 | 01–09 | Solution, SharedKernel, BuildingBlocks, outbox, arch tests, API host, test harness |
| G1 | 01–07 | Identity: auth, JWT, roles, permissions, seeding |
| G2 | 01–06 | HR: employees, departments, `IHrDirectory` |
| G3 | 01–03 | Cross-module verification, CLAUDE.md, authoring guide |

**25 tasks.** G0 is the majority of the thinking; G1 and G2 are mostly disciplined porting.

## Explicitly not in scope

Projects, CRM, Sites, Finance, Documents, Approvals, Notifications, Reporting. Do not stub them, do not
add placeholder projects, do not add `TODO` references to them. The next module starts when this plan
is finished and green.
