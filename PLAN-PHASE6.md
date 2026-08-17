# Phase 6 — Notifications & Audit (N1-01 → N2-01)

**This document supersedes the earlier Phase 6 brief entirely.** That brief was wrong on four
verified points (§1) and mis-sequenced `NotificationsModule` (§2). Both are corrected here. There is
one Phase 6 plan; do not consult the previous version.

Continues Phases 1–5 + E1/E2/H2/M1/P5F. Head at planning: `8c457a4`. Seven modules, 756 tests across
24 suites, 14 architecture rules, clean tree, on `master`.

**Scale:** 3 entities, 4 feature files, 3 endpoint files, 4 permission strings, 7 in-scope background
jobs, SignalR hub, audit interceptor. **9 tasks.** Small in entity count, **largest cross-module
footprint since CRM** — the interceptor touches all eight `DbContext`s and the job inversion touches
Projects, CRM and Approvals.

---

# 1. Four corrections to the earlier brief — all verified

| Brief claimed | Verified reality | Consequence |
|---|---|---|
| `IAuditable` is unused since G0-02 | **56 implementors** — Crm 25, Projects 13, Sites 7, Identity 4, Hr 3, Documents 3, Approvals 1. Only `AuditIgnoreAttribute` is unused. | An allow-list-free interceptor audits 56 types on day one. **Redaction is mandatory, not optional** — `ApplicationUser.PasswordHash`/`SecurityStamp`/`ConcurrencyStamp` are inherited from `IdentityUser<string>` and cannot carry an attribute. |
| `NotificationsDbContext` owns the audit migration | `tests/BuildingBlocks.IntegrationTests` references only BuildingBlocks + SharedKernel — it cannot reach a `notify`-owned table. `shared` is first in `SchemaOrder`. | **`shared` schema; `SharedDbContext` owns the migration.** |
| Port `AuditAction`/`AuditModules`/`AuditEntityNames` | Those hand-maintained string lists **are** B18 (`backend/…/AuditMetadata.cs:69-77` documents the failure in a red comment). | **3-member enum, no allow-list, no name dictionaries.** |
| `RecipientResolver` goes through the three directories | `StageOwnerAsync`/`ProjectOwnerAsync` cannot — `StageSummary` and `ProjectSummary` expose neither `StageTeam.HeadId` nor `Project.OwnerId`. | **Handlers move to the publishing module. `IRecipientResolver` is not ported.** |

Also verified and folded in:

- `("notify","identity")` is **already** in `Rule9CrossSchemaForeignKeyTests.AllowedPairs:43`;
  `docs/cross-schema-foreign-keys.md:77,271` already list `notify`. FK direction and `SchemaOrder`
  append are pre-blessed.
- **Identity, Approvals and Sites do not call `AddModuleIntegrationEvents`** — `ApprovalsModule.cs:15`
  is a doc comment saying so explicitly. A handler placed in any of them registers nowhere and its
  event is marked processed with zero handlers, silently.
- `tests/Integration.Tests` references only `Api.csproj`, so wiring the module in needs only
  `src/Api/Api.csproj`.
- `ApiFixture.cs:84` Respawn `SchemasToInclude` must gain `"notify"` or notification rows leak between
  tests.
- **7 in-scope jobs, not 8** — `InvoiceDueJob` reads `Invoices` and defers to Finance.
- The earlier brief's expected-event list was wrong (omitted `UserDeactivated` and `DocumentApproved`;
  listed `ApprovalDecided` as zero-handler when Projects, Documents and Crm all handle it).
  **Enumerate from code at N1-06.**

## The rule conflict — record it, don't bury it

`CLAUDE.md` says *"Cross-module writes: integration events on the outbox. Never direct calls."* A
synchronous `INotificationService.NotifyAsync` from a Projects job **is** a cross-module write by
direct call. It is the right design, but it is a **deliberate exception to a stated rule** and must be
written into `CLAUDE.md` and `docs/adding-a-module.md` at N2-01.

## Notifications is a hub, not a sink

The earlier brief claimed the sink property survives, then had seven modules reference
`Notifications.Contracts`. The invariant that actually prevents a build cycle is:

> **`Notifications.Contracts` references SharedKernel and BuildingBlocks and nothing else, ever.**

That gets a test (N1-01), not a sentence.

---

# 2. `NotificationsModule` is created minimal at N1-03 (R1)

The earlier draft listed `NotificationsModule.Register` under N1-04's *Modified* and
`NotificationsModule : IModule` under N1-07's *New* — modifying what a later task creates. It compounds
at N1-06: handlers in `Notifications.Infrastructure` need `AddModuleIntegrationEvents` in a `Register`
that would not exist, so N1-06's verify could not pass.

Precedent — `PROGRESS.md:1739`: *"`ProjectsModule : IModule` created here (minimal — DbContext +
permissions only), not deferred to a later task."*

| Task | `NotificationsModule.Register` gains |
|---|---|
| **N1-03** | **Created minimal** — `AddModuleDbContext<NotificationsDbContext>`, `IModulePermissions`. Nothing else. |
| **N1-04** | `AddSignalR()`, `AddScoped<INotificationService, NotificationService>()` |
| **N1-06** | `AddModuleIntegrationEvents<NotificationsDbContext>()` |
| **N1-07** | `AddModuleCqrs<NotificationsDbContext>()` + endpoints |

**State the registration order explicitly at N1-07.** This project has lost three separate bugs to
registration mistakes: G0-03 (Scrutor open-generic re-wrapping), G7-07 (double-wrap on a shared event
type), G5-07 (wrong scanned assembly, silent zero-decoration).

---

# 3. Tasks

Execution contract: restate → implement → task verify → full gate → ≤3 attempts → one commit → one
`PROGRESS.md` line → next task immediately.

### N1-01 — Four projects, `Notifications.Contracts`, the `IPeriodicJob` seam

Create all four projects now though three are near-empty: `CLAUDE.md`'s four-project rule is absolute
(F2, closed), and `MetaSelfEnforcementTests` XML-parses `DMS.Modular.slnx` and goes red the moment
projects appear without matching `Architecture.Tests` references.

**New** — the four `.csproj` (copy Sites' shape; Contracts references **SharedKernel + BuildingBlocks
only**); `INotificationService` (port both signatures verbatim — the per-recipient
`"{dedupeKey}:{userId}"` scoping is load-bearing for the whole sweep design);
`NotificationsPermissions` (4 strings + `All`, the `SitesPermissions` shape);
`Enums.cs` (`NotificationType` keeping its documented numeric bands and gaps, minus the deferred
Finance 150–179 members; `NotificationEntityType`; `AuditAction { Create = 1, Update, Delete }`);
`DeadlineTiers` (**in Contracts, not BuildingBlocks** — `Phrase()` returns user-facing notification
text, and every consumer already references Contracts for `INotificationService`).

`src/BuildingBlocks/Jobs/`: `IPeriodicJob` (`Name` + `RunAsync`, idempotency-via-dedupe-key doc
comment); `PeriodicJobWorker : BackgroundService` (interval from `Notifications:SweepMinutes` default
15, the `OutboxProcessor.cs:40-41` `int.TryParse` shape, per-job scope, per-job try/catch that logs and
continues — **do not** copy the backend's resolve-by-index-across-scopes trick, which assumes
`GetServices` returns a stable order across independent scopes; enumerate names once, resolve by name);
`AddPeriodicJobs()` called from `DI.cs` beside `AddOutbox()`.

**BuildingBlocks, not Contracts**, for the job seam — it is scheduling infrastructure, and it keeps
BuildingBlocks free of any Notifications dependency.

**Modified** — `DMS.Modular.slnx` (+4), `Architecture.Tests.csproj` (+4 — omit these and every rule
scanning Notifications is vacuous), `Api.csproj`, `DI.cs`.

**Verify** — `MetaSelfEnforcementTests` green. A new XML-parse test (the `Rule7SharedKernelTests`
mechanism) asserting `Notifications.Contracts.csproj` has **exactly two** `ProjectReference` elements —
the only thing standing between N1-05 and a build cycle. `PeriodicJobWorker` with zero jobs completes a
tick cleanly (drive the sweep method directly). **Rules 5/10 are vacuous here** — no integration events,
no commands yet. Say so in `PROGRESS.md` rather than claiming they prove something.

### N1-02 — Audit, end to end, in BuildingBlocks

Audit lands **before** Notifications exists: it depends on nothing Notifications provides, touches all
seven existing modules, and doing it here avoids building the entity in `Notifications.Domain` then
moving it a task later with a migration to regenerate.

**New, under `src/BuildingBlocks/Auditing/`** — `AuditLog` (`public sealed`, private ctor, a single
**`internal static Record(...)`** factory: `internal` is the mechanical half of "B18 closed by
mechanism"; columns `UserId?`, `UserEmail?`, `IpAddress?`, `UserAgent?`, `Action`, `EntityName`,
`EntityId?`, `OldValues?`, `NewValues?`, `ChangedColumns?`, `Module?`, `Timestamp`, `RequestPath?`,
`RequestMethod?`); `ConfigureAudit(this ModelBuilder, bool ownsMigration)` mirroring
`OutboxModelConfiguration.cs:19`, `ToTable("audit_logs", "shared", …)` with `ExcludeFromMigrations()`
when false, `jsonb` value columns, indexes on `(entity_name, entity_id)`, `(user_id, timestamp desc)`,
`timestamp desc`; `IAuditContextAccessor`/`AuditContext`/`NullAuditContextAccessor` (the `ICurrentUser`
precedent — rule 8b is a raw text scan of `BuildingBlocks.csproj` for `AspNetCore`, so this split is the
only shape that compiles); `AuditRedaction` (name set + fragments + `AuditIgnoreAttribute`);
`AuditSaveChangesInterceptor` (`internal sealed`, overrides `SavingChanges` **and**
`SavingChangesAsync`, guards on `eventData.Context is not null` — **not** a concrete type, it must fire
on all eight — `entityName = entry.Metadata.ClrType.Name`, `module = entry.Metadata.GetSchema() ??
ctx.Model.GetDefaultSchema()`, stages via `ctx.Set<AuditLog>().AddRange(...)` and **never** saves; **no
allow-list, no `ModuleByEntity` dictionary** — implementing `IAuditable` is the opt-in, compiler-checked,
in the entity's own file); `AddModuleDbContext<TContext>(cfg)`; `src/Api/HttpAuditContextAccessor`
(`internal sealed`, returns `AuditContext.None` when `HttpContext` is null); one migration creating
`shared.audit_logs`.

**The interceptor must be a singleton.** With `AddDbContext((sp, options) => …)` options are built per
resolution; a different interceptor instance each time changes the `CoreOptionsExtension`
service-provider hash and makes EF rebuild its internal provider per scope — a real leak, not just a
warning. Its only dependencies are `IAuditContextAccessor` (over the singleton `IHttpContextAccessor`
/`AsyncLocal`) and `ILogger<T>`. Make `HttpAuditContextAccessor` a singleton too.

**Modified** — `SharedDbContext` (`DbSet<AuditLog>` + `ConfigureAudit(true)`; update its *"owns the
shared schema… and nothing else"* comment); all 7 modules' `OnModelCreating` (+`ConfigureAudit(false)`)
and `Register` (`AddDbContext<X>` → `AddModuleDbContext<X>`; Sites' `AddMemoryCache()`/
`TryAddSingleton(TimeProvider.System)` untouched); `DI.cs`; `TestModuleDbContext`
(+`ConfigureAudit(false)`). **`SharedDbContext` gets no interceptor** — no `IAuditable` entities, and it
would only add an options-hash variant.

**Verify**
- **B18 by mechanism, three assertions:** (i) rename an `IAuditable` test entity's CLR type and the
  audit row's `EntityName` follows with no other edit — **fault-inject by hard-coding a literal and
  confirming red**; (ii) map a test entity to a non-default schema and `Module` follows; (iii)
  grep-assert `AuditLog.Record` has exactly one call site and `AuditAction` exactly three members.
- Same-transaction atomicity: an ambient rollback discards the audited row and its audit row together.
- `ApplicationUser` create/update yields `NewValues` containing `"PasswordHash":"***"`,
  `"SecurityStamp":"***"`, `"ConcurrencyStamp":"***"`. **This is the test that catches the real security
  bug.**
- A write through `OutboxProcessor`'s background scope produces a row with `Module` populated and
  `UserId` **null**. Non-HTTP writes must still be audited — refusing the row would put the hole exactly
  where the trail matters most, and this test stops a future reader "fixing" the null by suppressing it.
- Boot `ApiFixture` and **record the `shared.audit_logs` row count** from Identity seeding (~114
  `ApplicationRoleClaim` rows cold). The number goes in `PROGRESS.md` — it makes deferred retention a
  real item rather than a theoretical one.
- Rules 1/4/6/8a **fault-injected, each injection matching the rule's own mechanism** (8a needs a real
  member access — e.g. `ModuleDependencyOrder.SchemaOrder.Count` — not just a `ProjectReference`, per
  P5F's finding). Rule 8b re-confirmed after the `BuildingBlocks.csproj` edit.
- All 756 existing tests green — specifically re-run `HrDirectoryTests` (exact SQL-command-count
  assertions) and the three `*SchemaIsolationTests`; their `CapturedSql` filters exclude `shared.`,
  which is why they hold. Note that so nobody later "tightens" the filter and breaks them.

**Risks** — interceptor lifetime (above); 56 types start writing; `AddDbContext((sp, o) => …)` makes
`DbContextOptions<T>` scoped, so grep for anything resolving it directly first; design-time factories
build options by hand and get no interceptor (harmless — migrations write no entities — but record it).

### N1-03 — `Notifications.Domain` + `.Infrastructure` + the `notify` schema + **the minimal module**

**New** — `Notification`, `Activity` (`internal sealed : Entity`; deliberately **not** `IAuditable` —
auditing the audit-adjacent tables is noise), `NotificationErrors`; `NotificationsDbContext`
(`internal sealed`, `HasDefaultSchema("notify")`, `ApplyConfigurationsFromAssembly`,
`ConfigureOutbox(false)`, `ConfigureAudit(false)`); `NotificationConfiguration` (filtered unique index
`ux_notifications_dedupe_key WHERE dedupe_key IS NOT NULL`, plus `(recipient_user_id, read_at_utc)`);
`ActivityConfiguration`; `NotificationsDbContextFactory`; the initial migration including two raw-SQL
cross-schema FKs → `identity.users`, `ON DELETE RESTRICT`; **`NotificationsModule : IModule` — minimal,
per §2.**

**Modified** — `SchemaOrder` append `"notify"` (grep first to confirm nothing points *into* it);
`ApiFixture` `case "notify"` **and** Respawn `SchemasToInclude:84`;
`Rule9CrossSchemaForeignKeyTests` `Modules` dictionary entry (line 46 — without it the schema never
migrates and the new assertions fail), two named `Assert.Contains`, and
`Assert.DoesNotContain(found, fk => fk.From == "shared")` proving `audit_logs` carries no FK.

**Verify** — eight schemas apply clean from empty; probe empty. **R3 named-list guard:** a
`NotificationsSchemaObjectTests` in the `SitesSchemaObjectTests` shape naming the filtered unique index
and both FKs, written **fail-closed** ("exactly these", not "these exist"). DbSet-count-vs-configuration
assertion. Dedupe-key uniqueness as a **domain** unit test, independent of the index.

### N1-04 — `NotificationService` + SignalR

**New** — `NotificationService` (port dedupe-on-write, per-recipient key scoping, the Postgres `23505`
catch + `Detach`, and the best-effort push that never throws — the row is the source of truth);
`NotificationsHub` (`internal sealed`, `[Authorize]`, `/hubs/notifications`, `user:{userId}` groups from
`Context.UserIdentifier`). No new PackageReference — `BuildingBlocks.Web` carries
`<FrameworkReference Microsoft.AspNetCore.App />`.

**Modified** — `NotificationsModule.Register` (+`AddSignalR()`, +`AddScoped<INotificationService,
NotificationService>()`); **`DI.cs`** — a `JwtBearerEvents.OnMessageReceived` reading `?access_token=`,
**scoped to `/hubs` paths only** (a blanket query-string token reader puts tokens in access logs and
browser history on every route). Host-level, belongs in `Api`: the module cannot reach the JwtBearer
options and should not.

**Verify** — one notification fanned to three recipients produces three rows; re-run produces **zero
new** rows (assert the *delta*). SignalR without a raw WebSocket: (1)
`GET /hubs/notifications/negotiate?…&access_token=<jwt>` with no `Authorization` header returns 200, and
the same request without the token 401 — the narrowest proof `OnMessageReceived` fires; (2) two
long-polling `HubConnection`s over `fixture.Server.CreateHandler()`, user A receives, user B does not.

**Risk** — `NotificationService` calls `SaveChangesAsync` **per recipient**. Inside an integration-event
handler wrapped by `InboxDedupeDecorator`'s explicit transaction that is N commits within one
transaction scope. **Verify that interaction before assuming the port is safe** — this is the one place
the backend's shape may not survive the move.

### N1-05 — The job inversion

**New**, all `internal sealed : IPeriodicJob` under `<M>.Application/Features/Jobs/` — Application,
because `<M>.Application.csproj` already references its own `.Infrastructure`, and jobs are
DbContext-using command-shaped logic. Rules 1/2/4/6 pass by construction.

Projects: `ProjectDeadline`, `StageDeadline`, `TaskDeadline`. Crm: `CrmMeetingReminder`,
`CrmPeriodClose`. Approvals: `ApprovalOverdueEscalation`, `ApprovalResponseDue`. **Seven, not eight** —
`InvoiceDueJob` ships with Finance.

**Modified** — three `.Application.csproj` (+`Notifications.Contracts`; Projects may also need
`Hr.Contracts` for `employeeId → appUserId` — check first); three `Register` methods gain the Scrutor
`IPeriodicJob` scan copied verbatim from `ProjectsModule.cs:44-47`'s `IGateConditionTypeHandler` block.

**Verify** — each job run twice yields one notification per recipient, asserted on the
`(dedupeKey, recipient)` pair rather than a bare count; `PeriodicJobWorker` executes all **7** in one
tick; `CapturedSql` schema isolation per job with `ClearCapturedSql()` before the tick.

**Risk** — port `TaskDeadlineJob`'s `[now-30d, now+3d]` window and active-status filter as-is. They are
load-bearing for cost; "simplifying" to a full scan is a silent regression.

### N1-06 — Integration event consumers

**Enumerate from code first and record the enumeration** — ~25 distinct event types, 7 with a consumer
today. The earlier brief's list was wrong; do not use it.

For each event: a handler, or a **recorded decision** that it is deliberately silent. Silence by
decision is legitimate; silence by omission is not.

**Placement rule — the phase's most consequential design decision, and it goes in
`docs/adding-a-module.md`:**

> A notification handler lives in `Notifications.Infrastructure` **iff** every recipient it needs is
> derivable from the event payload plus the *existing* published Contracts surfaces. Otherwise it lives
> in the **publishing module's** `.Infrastructure`, reading its own tables and calling
> `INotificationService`. **No module's Contracts is extended in Phase 6.**

This turns *"we need `StageOwnerAsync`"* into *"the `ProjectStageCompleted` handler lives in
`Projects.Infrastructure`, where `StageTeam` is a local table"* — strictly better than exporting
stage-team membership on a public contract nothing else wants, and the same inversion N1-05 mandates for
jobs. Consequence: **`IRecipientResolver` is not ported at all.** Its two surviving resolutions are
one-line calls to `IHrDirectory` and `IClientDirectory`.

**Must not be missed** — `IdentityModule`, `SitesModule` and **`NotificationsModule` itself** need
`AddModuleIntegrationEvents<TContext>`; Approvals too if a handler lands there. None have it today. A
handler placed without it registers nowhere and the event is marked processed with zero handlers,
silently.

**Verify** — the enumeration is in `PROGRESS.md`; **every** handler asserted through
`OutboxProcessor.ProcessPendingAsync`, never a direct call; at least one previously-silent event
(`EmployeeHired`) produces a notification end-to-end; the two-`ProjectReference` test still green after
six modules reference `Notifications.Contracts`.

**Risk** — this pattern has a module consuming its own event through the outbox. Legal, but unprecedented
here; confirm `EventBus` does not filter self-published events before committing to it.

### N1-07 — Endpoints + eight-module registration

**New** — `NotificationEndpoints`/`ActivityEndpoints`/`AuditEndpoints`; the CQRS handlers in
`Notifications.Application`.

**Modified** — `NotificationsModule.Register` (+`AddModuleCqrs<NotificationsDbContext>()`, **state the
registration order explicitly**); `Program.cs:19-28` (+`Assembly.Load("Notifications.Infrastructure")`);
`CrmSmokeTests.cs:65` and `SitesSmokeTests.cs:30` — **in this same commit**, so the tree is never red
between commits. **Rename both** per the standing rule: `Boot_RegistersExactlySevenModules` →
`…EightModules`, and the permission-total test to its new count. **Recount** by asserting
`NotificationsPermissions.All.Count` additively rather than trusting arithmetic (per D2).

**Verify** — eight modules asserted by name; permission total recounted; `/openapi/v1.json` lists the
routes; `audit.view_own` **cannot reach** `/api/audit` or `/api/audit/user/{userId}` at all (403), and
`/api/audit/my-activity` filters on `ICurrentUser.UserId` server-side, never from a query parameter.
This is the phase's only real authorization surface.

### N1-08 — Spine + eight-module smoke

An action in each module produces its audit row and its notification, delivered through the outbox. Run
twice. Extend `CrossModuleSmokeTests` to eight modules. `NotificationsSchemaIsolationTests` via
`CapturedSql`.

**State up front what it will and will not prove.** It proves the interceptor fires on all eight
contexts through the real host, and that notifications survive an outbox round-trip. It does **not**
prove same-transaction atomicity (N1-02), SignalR delivery (N1-04), or job idempotency under concurrency
(single-process only).

---

# N2-01 — Close-out

- `docs/adding-a-module.md`: `IPeriodicJob` as the third define-in-one/implement-in-many instance;
  `ConfigureAudit`'s reuse of the outbox `ExcludeFromMigrations` shape; the format-gate change; **the
  N1-06 placement rule**; the recorded exception to `CLAUDE.md`'s "never direct calls".
- `CLAUDE.md`: that same exception, stated where the rule is stated.
- `docs/low-resource-hosting.md`: §3 nine loops → one sweep, **now implemented**; the SignalR
  multi-instance backplane note beside the outbox one (rows are still written, so a refresh recovers —
  the degradation is real-time delivery, not data).
- `docs/cross-schema-foreign-keys.md`: the two `notify → identity` FKs, and why `shared.audit_logs`
  carries none (an audit row must outlive its subject; a `RESTRICT` FK would make user deletion fail
  *because* the user did something auditable).
- `PROGRESS.md`: eight-module phase boundary; the audit-rows-per-cold-boot number; deferred table;
  **B18 confirmed closed by mechanism** — three-member enum, `internal` factory with exactly one call
  site, entity name *and* module both from EF metadata, no allow-list to fall out of.
- **Required deliverables:** sub-area → task reconciliation; the verify-clause diff; what Notifications
  teaches the Finance plan (Finance owns `InvoiceDueJob` and is the last module before Reporting).

---

# Deferred

| Item | Owner |
|---|---|
| `InvoiceDueJob` | Finance — its `IPeriodicJob` lands with that module |
| Email/SMS channels | product decision; in-app + SignalR only |
| Audit retention/archival | ops; `shared.outbox_messages` retention (Phase 3) is the precedent, now with a measured row count to size it |
| **Failed-login / security-event auditing** | a failed login mutates nothing, so the interceptor emits no row. Needs a different mechanism (endpoint filter, or an Identity-owned event) |
| Notification read/dismiss UX beyond `view_own` | frontend-driven |

# Hard stops

A verify failing 3 attempts; any fix needing an architecture rule relaxed, a test deleted, an internal
type made public, or a forbidden reference; ambiguity that changes an outcome; any write under
`..\backend`; any module or stub beyond Notifications; building `InvoiceDueJob`; reopening F2 or any
closed decision. **Added:** extending any module's `Contracts` — the N1-06 placement rule exists so this
is never needed.

# Gate

```bash
DOCKER_HOST=tcp://127.0.0.1:2375 dotnet build DMS.Modular.slnx -warnaserror
DOCKER_HOST=tcp://127.0.0.1:2375 dotnet test DMS.Modular.slnx
DOCKER_HOST=tcp://127.0.0.1:2375 dotnet format DMS.Modular.slnx && git diff --exit-code
```

The format gate changed at P5F: `--verify-no-changes` reported a violation that apply mode then declined
to fix, in a file it agreed was correct. Proving cleanliness by the tool's own transformation sidesteps
the disagreement.

**Standing rules:** grep before you claim, and check the pattern against a known-positive case first;
when a change alters what an existing test proves, rename it and cover the branch it stopped exercising;
a fault injection must match the mechanism the rule uses, or it produces a false negative.
