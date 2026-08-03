# Review of the autonomous build plan

**Verdict: approved to run, after the seven corrections in §2.** `PLAN.md` has been updated for all of
them. Re-read `PLAN.md` before starting — it has changed since the plan was written.

---

## 1. Findings confirmed — no action needed

These were checked against `..\backend` source and are correct:

- **C1** (BuildingBlocks contradiction) — real, and the two-project resolution is right. It is also the
  mistake the old solution made: `Infrastructure.csproj` carries `FrameworkReference
  Microsoft.AspNetCore.App` for SignalR, which is how `IHttpContextAccessor` reached
  `ProjectAccessService`. Compile-time prevention over test-time detection.
- **C2** (`InternalsVisibleTo`) — correct and necessary; `internal` everywhere would not compile without it.
- **C6** — verified. `HiringReason` is `[Flags]` "why the client hired a designer" and `PastExperience`
  is "how a previous designer engagement went"; both are referenced only from
  `Clients`/`CrmBriefs`/`CrmLeads`/`CrmDesignProfile`. `ContractType` is what `Employee` uses. **This is
  a plan error, correctly caught.**
- **C9** — verified: exactly 7 files, the 7 named. They are Projects read-models living in HR. Dropping
  them is right.
- **C11** — verified: mutable setters, no `PageSize` property, `Default => default!` returns null.
- **C12** — verified `DenyPermissionOverrideCommand:31` calls `repo.Adapt(entity)` where
  `GrantPermissionOverrideCommand:37` calls `repo.Add(entity)`. Denies never persist.
- **G1-05** — verified: only `SendResetPasswordCommand` and `ResetPasswordCommand` exist.
- **Porting `EffectivePermissionService` unchanged, including its two latent gaps** — right call.
  Changing security semantics mid-port is how subtle authorization bugs get introduced.

---

## 2. Required corrections

### R1 — The inbox is not atomic *(correctness bug, highest priority)*

C3 says *"`InboxMessage` is touched only by `OutboxProcessor` via `SharedDbContext`."*

The handler writes through its module's `DbContext` (transaction A). The inbox marker writes through
`SharedDbContext` (transaction B). A crash between A and B re-runs the handler on retry and the side
effect happens twice — which is precisely what the inbox exists to prevent. At-least-once delivery is
fine; at-least-once *effect* is not.

**Fix:** map **both** `OutboxMessage` and `InboxMessage` in every module `DbContext` with
`ExcludeFromMigrations()` — the same technique C3 already applies to the outbox — and write the dedupe
row through the context the handler is using, so it commits atomically with the handler's own work.
`SharedDbContext` keeps ownership of the migration for both tables and nothing else.

Add to G0-05's verify: *handler commits its write, process is killed before returning; on retry the side
effect is not repeated.*

### R2 — Zero-handler events must be marked processed

`UserRegisteredIntegrationEvent` is published at G1-05 and nothing in scope subscribes to it. If
`processed_utc` is only set when a handler ran, those rows retry forever and `attempts` climbs without
bound. Resolving zero handlers is success. Added to G0-05.

### R3 — C4 (internal migrations) is the wrong trade

`dotnet ef migrations add` emits `public partial class` on every run, regenerates on every model change,
and also emits a `ModelSnapshot` that is public and is *not* a `Migration` subclass. Hand-editing each
one fights the tool permanently and breaks the first time someone forgets — and C4 itself concedes it may
be a hard stop.

**Fix:** scope arch rule 4 to exclude types deriving from `Migration` and `ModelSnapshot`. Generated code
was never in that rule's scope; the rule exists to stop *hand-written* types leaking out of a module.
Scoping a rule correctly is not relaxing it. Updated in G0-07.

### R4 — Deactivating an employee must still disable the login *(security)*

C8 correctly identifies that `EmployeeDomainService.DeactivateAsync` currently writes
`ApplicationUser.IsActive`, and correctly refuses to let Identity subscribe to `Hr.Contracts`. But it
stops there, and the consequence is understated: **after the split, deactivating an employee no longer
prevents them signing in.** That is a regression with a security consequence, not a behaviour note.

The reasoning was one step short. Identity must not reference HR — but **HR → Identity is an allowed
direction**. So Identity exposes the write:

```csharp
// Identity.Contracts
public interface IUserAdministration
{
    Task<Result> DeactivateAsync(string userId, CancellationToken ct);
    Task<Result> ReactivateAsync(string userId, CancellationToken ct);
}
```

`DeactivateEmployeeCommandHandler` calls it after its own write. The two writes are not atomic; order
them employee-then-user so a failure leaves an active login on an inactive employee rather than locking
out an active one. Added to `Identity.Contracts` (G1-01) and G2-04.

### R5 — Arch rules are 9, not 7

The plan was written against an earlier `PLAN.md`. Two rules were added since:

- **Rule 8** — no `*.Application` or `*.Domain` references `BuildingBlocks.Web`, and `BuildingBlocks`
  itself references no `Microsoft.AspNetCore.*`. This is C1's conclusion turned into a standing rule
  rather than a one-time decision.
- **Rule 9** — every cross-schema foreign key points in an allowed module dependency direction, checked
  by querying `pg_constraint` against a migrated database. Needs the Testcontainers fixture, so it is
  implemented at G0-09 and referenced from G0-07.

The plan's addition to rule 3 (no cross-module `InternalsVisibleTo`) is good — keep it.

Update every "seven rules" / "all seven architecture rules" reference to nine, including the end-state
checklist.

### R6 — The cross-schema FK stays

G2-02 says *"no navigation, no FK to `identity.`"*. Half right. Dropping `Employee.AppUser` is the module
boundary and is correct. Dropping the **database constraint** is a separate decision, and the answer is
no: inside one database it removes referential integrity and buys nothing.

`hr.employees.app_user_id → identity.users(id) ON DELETE RESTRICT`, written via `migrationBuilder.Sql`
because EF cannot emit it across two models. Rationale and the ordering/indexing traps:
`docs/cross-schema-foreign-keys.md`. Also requires migrations to run in **module dependency order** —
`hr` cannot create that constraint before `identity.users` exists.

The unique index on `app_user_id` is correct and should stay.

### R7 — The seed password fix is half a fix

C12 replaces the hard-coded hash with a value from `appsettings.Development.json`. That file is
committed, so it is still a credential in the repository — a smaller one, not a removed one.

Read the seed password from user-secrets or an environment variable. **Fail fast at startup if it is
unset outside Development**, rather than falling back to a default. A silent fallback is how a known
password reaches production.

---

## 3. Minor

- `Directory.Packages.props` needs `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`
  in a `PropertyGroup` — without it the file is inert.
- G0-01: `dotnet test` on a solution with no test projects may exit non-zero rather than warn. If it
  does, that is a tooling artifact, not a failure — note it in `PROGRESS.md` and continue.
- The end-state checklist should add: the `hr → identity` FK exists in `pg_constraint`, and arch rules
  8 and 9 are green.

---

## 4. Not changing

- **C5** (rule 4 scoped to module assemblies; `IModule` implementations internal, discovered via
  `Activator.CreateInstance(t, nonPublic: true)`) — correct.
- **C7** — matches the updated `PLAN.md`.
- **C10** — porting the backend's actual permission strings is right.
- **C13** — port 5433 and a dev-only JWT key are fine. Testcontainers picks random ports, so no conflict.
- **The `dotnet format` before `--verify-no-changes` ordering** — pragmatic, and generated migrations are
  the reason. Keep it.

---

## 5. One process note

Running all 25 tasks unattended means the foundation and both modules arrive as one diff. G0 is where
the architecture is decided — R1 and R3 are both G0 issues, and both would have propagated through
everything after them.

The per-task commits make that recoverable. If you want one checkpoint without giving up autonomy,
**stop after G0-09 and report before starting G1**: foundation complete, no module code written, and the
two highest-consequence decisions (outbox/inbox semantics, arch rule scoping) reviewable while they are
still cheap to change.
