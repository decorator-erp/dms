# HR & Identity remediation v3 — notes

This file stands in for `../docs/backend/dms-modular/`, which the cloud clone doesn't have. It keeps one section per phase, appended across phases. The owner moves it to `docs/backend/dms-modular/` after merging, as was done for `CRM-BRIEF-FIX-NOTES.md`.

---

## Phase 0 — Foundations

### Setup

**Branch: `claude/awesome-einstein-mw5xmb`, not `fix/hr-identity-phase-0`.**
- The cloud session can only push to its designated branch, and the owner chose it when asked.
- It was cut from `master` at `1d6aefb`.

**Environment**
- .NET SDK 10.0.400 is at `~/.dotnet`.
- `dockerd` was started inside the container.
- Docker Hub answered `429 Too Many Requests` for `postgres:17`, so the image was pulled from `mirror.gcr.io/library/postgres:17` and re-tagged `postgres:17`. Testcontainers' default pull policy (missing) then uses the local tag.
- **The Testcontainers suites ran.**

**Baseline.** Every one of the 29 test suites was green on `1d6aefb` before any change.

### 1. Item table

| Id | Status | Commit | Proving test |
|---|---|---|---|
| 0.1 Run 0 inventory | done | (this notes commit) | — (read-only; §2 below) |
| 0.2 Query validation (Aug 0.1) | fixed | `e4de0d7` | `ValidationDecoratorPipelineTests.FailingQueryValidator_ShortCircuits_InnerHandlerNeverInvoked`, `PassingQueryValidator_ReachesTheHandler`, `Validators_ReceiveTheCallersCancellationToken`, `Validators_RunOneAtATime`; `HrSmokeTests.GetDepartments_ExplicitlyInvalidPaging_IsRejected_ByTheQueryValidator` (×3) |
| 0.3 Rule 11 (Aug 0.2) | fixed | `2184c7a` | `Rule11ValidatorReachabilityTests` (2 facts) |
| 0.4a Permission-set test double | fixed | `b718595` | — (no Identity code calls `HasPermission` yet, so no behaviour changes; Phase 1A's tests are the first consumers) |
| 0.4b Real-token HTTP harness | fixed | `b718595` | `HttpHarnessTests` (4 facts) |

**Red-before-green proofs**, each run locally by reverting the fix and then restoring it:
- **Removing the `ValidationDecorator.QueryHandler<,>` registration** turns three tests red: the query short-circuit test, the token test and the one-at-a-time test (the last two run through a query).
- **Restoring `Task.WhenAll`** turns `Validators_RunOneAtATime` red (two validators in flight at once).
- **Passing `CancellationToken.None` instead of `ct`** turns `Validators_ReceiveTheCallersCancellationToken` red.
  - Simply dropping the argument is a build error, because `IDE0060` reports the parameter as unused. That is itself a guard.
- **Rule 11, with the child-validator entry replaced by a bogus name**, reports two violations:
  - `CrmBriefHeadDataValidator validates Crm.Contracts.Briefs.CrmBriefHeadData, which is neither a command nor a query`;
  - `ChildValidators lists …Gone, which no longer exists`.

**What changed**
- **`ValidationDecorator`** gains `QueryHandler<TQuery, TResponse>`.
  - `ValidateAsync` takes the caller's `CancellationToken` and runs validators one at a time.
  - The comment gives the reason: an async rule usually queries the scoped DbContext, which is not thread-safe, and running them one at a time also makes the failure order stable.
- **`ModuleCqrsRegistration.DecorateQueryHandler`** now registers Validation, then Logging, the same order as commands. The class summary says so.
- **Module scope decorators** (Projects, CRM, Sites) are still registered after `AddModuleCqrs`, so they wrap outermost. A scoped query still answers 404 before 400, which keeps CLAUDE.md's authorization order.
- **`HrSmokeTests`.** `GetDepartments_ExplicitlyInvalidPageSize_IsNotRejected_BecauseQueryValidatorsNeverRun` was written to go red at this fix. It is now `GetDepartments_ExplicitlyInvalidPaging_IsRejected_ByTheQueryValidator`, a theory covering `pageSize=0`, `pageSize=100000` and `pageNumber=0`, all → 400. Its comment keeps the history and says the gap is closed.
- **Comments.** The comment in `GetUserPermissionsQuery.cs` said query validators "silently never run". It is rewritten to say they now run, and why that query still has none.
- **No other test sent out-of-range query inputs**, so no test needed fixing.
- **`Rule11ValidatorReachabilityTests`:**
  - Asserts that `ValidationDecorator` has a nested decorator for every handler shape `AddModuleCqrs` decorates.
  - Asserts that every `AbstractValidator<T>` in `*.Application` targets a command or query, and that the same assembly contains that message's handler (`AddModuleCqrs` registers handlers and validators from the one assembly it is given).
  - One exemption, `CrmBriefHeadDataValidator`, is a child validator run through `SetValidator`. The rule checks that it still exists and that another validator still depends on it.
  - Floor: 180 command/query validators (counted), plus the 1 child.
- **Identity's `TestCurrentUser`** now takes a permission set: `TestCurrentUser(string userId, params string[] permissions)`. The fixture's shared caller holds nothing.
- **`tests/Integration.Tests/TestUsers.cs`** (new):
  - **Methods:**
    - `CreateUserWithPermissionsAsync(fixture, params permissions)` gives the user exactly those permissions. It removes the default `employee` role and creates a fresh role holding just those permissions.
    - `CreateUserInRoleAsync(fixture, roleName)`.
    - `LoginAsync(...)`.
    - `AdminClientAsync(fixture)`.
  - **How it works:**
    - Setup runs over HTTP as the seeded admin. It logs in for real, with the password from `DMS_SEED_ADMIN_PASSWORD`. A delegation guard (Phase 1A) can't break setup, because the admin is full-access and holds everything.
    - The test user then logs in through the real `POST /api/auth/login`. Its token therefore carries what `EffectivePermissionService` resolves, plus any claim later phases add (the `sstamp`).

### 2. Run 0 inventory

Every claim below was read from the code at `1d6aefb`. The key ones were re-checked by hand (grep) before being written down. "Open" means the defect is present.

#### 2.1 Phase 0 questions

- **Is `ValidationDecorator` applied to queries?** **No, before this phase.**
  - It declared only `CommandHandler<>` and `CommandHandler<,>`.
  - `DecorateQueryHandler` applied logging only.
  - 33 query validators were registered and never invoked: Finance 27, HR 2 (`GetDepartments`, `GetEmployees`), Approvals 2, CRM 2.
  - Commands validated in parallel (`Task.WhenAll`) without the `CancellationToken`.
  - Fixed in 0.2.
- **What do the test doubles return from `HasPermission`?**
  - Identity's `TestCurrentUser` returned **`true`** for everything. It was registered once as a singleton (`"test-caller-id"`), and nothing depended on the `true`, because no Identity source calls `HasPermission`.
  - Approvals, CRM, Documents, Finance, Projects and Sites already use a scoped, mutable double: `UserId { get; set; }`, plus `Grant(p)`, plus `HasPermission => _permissions.Contains(p)`.
  - **HR has no `TestCurrentUser` and registers no `ICurrentUser`.** No HR handler takes one today. The first HR handler that does will fail the HR fixture with a DI error; Phase 2 must add one.
- **HTTP-level tests, and how do they mint tokens?**
  - `tests/Integration.Tests` boots a `WebApplicationFactory<Program>` over a Testcontainers Postgres (`ApiFixture`).
  - Tokens come from `fixture.IssueTestToken(userId, perms…)`, which calls `TestJwt.Issue`: HS256, claims `sub`, `NameIdentifier`, `jti`, and one `permissions` claim per permission. The user id need not exist.
  - Before this phase, no integration test called `/api/auth/login`.
  - 403 over HTTP was already tested in `IdentityRoleAssignmentTests`, `ProjectsSmokeTests`, `SitesSmokeTests`, `FinanceSmokeTests`, `DocumentsSmokeTests`, `NotificationsEndpointTests` and `CrmBriefRouteTests`.
  - No HTTP test covers a scope-decorator 404 for a non-member; that is tested only at application level.
  - The new `TestUsers` covers the real-token path.
- **How does `GlobalExceptionHandler` map `DbUpdateException` and unique/PK violations?** **It doesn't.**
  - The only branch is `exception is BadHttpRequestException ? its status : 500`.
  - So `DbUpdateException`, `DbUpdateConcurrencyException` and Postgres `23505` all become 500.
  - CRM (`BriefWrites.SaveAsync`), Sites (`SiteWrites.SaveAsync`) and Notifications (`NotificationService`) translate these locally, with `catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation, ConstraintName: … })`.
  - No HR or Identity handler catches anything. Aug 3.5 (Phase 3) should copy the `BriefWrites` pattern.
- **Does CRM's client activate path have the HR-1 pattern?** **Not that shape. It is a different gap, recorded only.**
  - `SetClientActiveCommandHandler` (`PUT /api/clients/{id}/active`) only flips `client.Activate()` / `Deactivate()` and saves.
  - It never calls `IUserAdministration`, either synchronously or through an event, so the client's portal login is never enabled or disabled with the client.
  - It doesn't check current state.
  - `DeleteClientCommandHandler` has the same two gaps.
  - The only CRM call into `IUserAdministration` is `ClientCreatedIntegrationEventHandler` (`ReactivateAsync`, through the outbox).
- **Is end-of-line handling consistent?** **No.**
  - `git ls-files --eol src` shows 747 files `w/lf` and 40 `w/crlf`. All 40 CRLF files are `*.csproj`, which matches `.gitattributes` (`*.csproj text eol=crlf`).
  - `.editorconfig` sets `end_of_line = crlf` for `[*.cs]`, which contradicts `.gitattributes` `*.cs text eol=lf`. Every `.cs` file is LF in both the index and the working tree.
  - The format gate passes, but the two files disagree, and this is visible: the gate's `dotnet format DMS.Modular.slnx` rewrites **every** `.cs` working copy to CRLF, per `.editorconfig`. `git diff --exit-code` still passes, because `.gitattributes` normalises them back to LF, but it prints one "CRLF will be replaced by LF" warning per file (~750 lines) and leaves a CRLF working tree behind.
  - Not changed (out of scope). The owner should pick one; most likely `end_of_line = lf` in `.editorconfig`.

#### 2.2 Status of every Phase 1A–5 item

**Phase 1A — Authorization**

| Item | Status | Evidence |
|---|---|---|
| N1 delegation guard | open | `AssignRoleToUserCommand` → `RoleService.AssignRoleToUserAsync` → `userManager.AddToRoleAsync` with no caller check. The same holds for `AssignPermissionToRole` → `PermissionService`, and for the grant-override handler (it checks only `currentUser.UserId is null`). `FullAccessRoles` is checked only in `UserProvisioning` (`RoleNotAssignableAtProvisioning`), so a `roles.manage` holder can assign `admin` to themselves. `RoleService.UserIsAdminAsync` exists and has no callers. |
| K6 self-target / last holder / shared body | open | `GrantPermissionOverrideCommand.cs` and `DenyPermissionOverrideCommand.cs` are identical apart from `UserPermissionOverride.Grant`/`.Deny`. Neither compares `TargetUserId` with the caller. There is no last-holder check, and no target-exists check, so the FK violation becomes a 500. |
| K8 read side | open | `GET /api/permissions/me/effective` doesn't exist. `GET /api/permissions/users/{userId}/effective` is `.RequireAuthorization()` only: any authenticated user can read anyone's permissions and override rows. |
| N3 full-access lock | open | `PermissionService.Assign/RemovePermissionFromRoleAsync` ignore `FullAccessRoles`. Separately, `RoleClaimSeeder` re-adds every missing permission to admin/manager on every boot, so today a removal is silently undone at the next restart. |
| N4 system roles / in-use / audited delete | open | `RoleService.DeleteRoleAsync` calls `roleManager.DeleteAsync` with no name check. `UpdateRoleCommandHandler` renames any role, `admin` included. There is no member check, and the `user_roles` and `role_claims` FKs are `Cascade`, so deleting a role silently strips it from every user. The delete goes through the change tracker (`RoleManager`). |
| Role validators (Aug 3.6) | partial | They exist: Create has `Name NotEmpty().MaximumLength(256)`; Update has RoleId/Name `NotEmpty`, Name ≤256, Description ≤500; Delete has RoleId `NotEmpty`. They have no reserved-name rule. |
| K7 Rule 12 + FallbackPolicy | open | No architecture test reads endpoint metadata. `Api/DI.cs` has a bare `services.AddAuthorization();`. 321 `Map*` endpoints, plus `/health` and the hub. No group-level authorization; `AllowAnonymous` is never used. The routes without `RequirePermission` are: `/api/auth/{login,refresh,revoke}` and `/api/crm/public/leads` (anonymous); `/api/auth/change-password`, `/api/permissions/users/{userId}/effective`, `/api/me` (Program.cs) and `GET /api/crm/briefs/{briefId}/rounds/{round}/diff` (handler-side `HasPermission`) — authenticated only; `/hubs/notifications` (`[Authorize]`) and `/health` (anonymous). **Phase 1A must decide `/api/me` and the brief-diff route for Rule 12 tier 2, which the prompt's list doesn't name.** |

**Phase 1B — Authentication & sessions**

| Item | Status | Evidence |
|---|---|---|
| K4 lockout / uniform responses / dummy hash | open | `LoginCommand.cs` uses `userManager.CheckPasswordAsync`. There is no `SignInManager`, no `AccessFailedAsync`, no lockout. `AddIdentityCore<ApplicationUser>()` has no options lambda, so the stock policy applies: length 6, plus digit/lower/upper/non-alphanumeric. An unknown user returns `InvalidCredentials` without hashing (a timing oracle). An inactive user with the right password returns the distinct `AuthErrors.UserNotActive`. |
| Rate limit on auth | open | The only policy is `RateLimitPolicies.CrmPublicLeads` (fixed window, 5 per minute, IP-keyed, `DI.cs`). In `Program.cs`, `UseRateLimiter()` comes **after** `UseAuthentication()` / `UseAuthorization()`. |
| K5 refresh tokens | open | `ApplicationUser.RotateRefreshToken` rejects an inactive token without revoking the token family. `RevokeAllRefreshTokens` has no production caller. `ChangePasswordCommandHandler` doesn't revoke. There is no `/logout`. `RefreshToken.Token` is plaintext (max 256, unique index). |
| K8 write side | open | `ChangePasswordRequest(string UserId, …)`. The handler calls `userManager.FindByIdAsync(request.UserId)`, and the validator requires `UserId`. |
| N2 security stamp | open | `JwtProvider` claims are sub, email, jti, name, roles and permission; there is no `sstamp`. The JwtBearer events have only `OnMessageReceived`. No `IMemoryCache` in Identity or Api. `UserAdministration.DeactivateAsync` only calls `user.Deactivate()` and `UpdateAsync`, so a live access token keeps working for up to 60 minutes. |
| `ValidAlgorithms` pinned | open | Set neither in `JwtProvider.ValidateToken` nor in the `DI.cs` `TokenValidationParameters`. |
| `ValidateToken` catches `ArgumentException` | open | It catches only `SecurityTokenException`. |

**Phase 2 — HR data integrity**

| Item | Status | Evidence |
|---|---|---|
| K3 query filters / `includeInactive` / re-hire | open | There is no `HasQueryFilter` in HR. The department list hard-codes `Where(d => d.IsActive)`. The employee list returns everyone. There is no re-hire path: `CreateEmployeeCommandHandler`'s `AnyAsync(e => e.AppUserId == appUserId)` runs after a brand-new user is provisioned, so it is dead code. Re-using an email fails in `UserProvisioning` with `DuplicatedEmail`. |
| K1 one active department | open | `EmployeeDepartment` has `IsActive`, `CreatedAtUtc` and `Remove()`, but no `StartedAtUtc` and no `Restore()`. The PK is `(EmployeeId, DepartmentId)`, with no partial unique index. Assign checks only "same department, active", so an employee can be active in several departments, and re-assigning after a removal collides with the PK and returns 500. Move's existence check has no `IsActive` filter, so moving back fails with `AlreadyInDepartment`. Move's comment mentions a source department id the command doesn't have. |
| K2 membership service | open | There is no `DepartmentMembershipService`; each handler writes `HrDbContext` itself. Delete-department and deactivate-employee leave memberships and headships alone. AssignHead's `isMember` check ignores `IsActive`. |
| Expired overrides (Aug 2.4) | open | The resolver filters on `ExpiresAt`. The grant/deny existence check and the `/effective` projection don't, so an expired row blocks a new grant, and `/effective` lists expired overrides. |
| Update `ContractType` | partial | Create is fixed (`Enum.TryParse && Enum.IsDefined`). Update is open (`Enum.TryParse` only, so `"99"` is stored). `Employee.Create`/`Update` have no guard. |
| HR-1 (Aug 2.6) | open | `ActivateEmployeeCommandHandler` uses `ExecuteUpdateAsync(SetProperty(IsActive, true))`, which never reaches Identity (the login stays disabled) and bypasses the audit interceptor. `EmployeeActivatedIntegrationEvent` doesn't exist. `FailedToActivate` is unreachable (`affectedRows < 0`). `EmployeeDeactivatedIntegrationEventHandler` doesn't check current state. |
| Aug 2.7 FKs | open | The join-table FKs are `DeleteBehavior.Cascade`. `departments.DepartmentHeadId` is a plain indexed column with no FK. The hand-written FK pattern to follow is `fk_employees_app_user` in `InitialHr` (raw `ALTER TABLE … ON DELETE RESTRICT`, `Down` drops it). `Rule9` sees only cross-schema FKs; `HrSchemaTests` asserts the first `hr.employees` FK. |
| Aug 2.8 `[AuditIgnore]` | open | The attribute exists and `AuditRedaction` honours it, but no property uses it. `PhoneNumber`, `EmergencyContactPhone` and `Notes` are audited in clear. |
| N7 full-replacement `PUT` | open | Partial patch (`if (x is not null) X = x`). `Employee.Update` returns silently when the employee is inactive, so the caller gets 204 and nothing changes. `EmployeeDetailResponse` has no `LoginActive`. |
| N8 department rules | open | `HasIndex(Name).IsUnique()` is not partial. Create normalizes the name with current-culture `ToTitleCase` ("IT" becomes "It") and checks it against inactive rows too. Update compares the raw name and doesn't exclude itself, so a casing-only rename reports a duplicate of itself. Update skips an empty description and writes via `ExecuteUpdateAsync` (no audit, and it doesn't reject a deleted department). There is no `Description` length validator. The list has no `HeadName`/`MemberCount`. AssignHead accepts inactive employees. |
| One HR migration with K9 guards | open | — |

**Phase 3 — Correctness & API gaps**

| Item | Status | Evidence |
|---|---|---|
| Aug 3.2 `RemovePermissionFromRole` | open | `ExecuteDeleteAsync` with no role check and the row count ignored, so it always answers 204 and is unaudited. |
| Aug 3.2 `RevokeOverride` | partial | It checks the affected count (`rowsDeleted == 0 ? PermissionOverrideNotFound`), which is the fixed part. It still uses `ExecuteDeleteAsync`, so it is unaudited. |
| Aug 3.3 dedup | partial | `EffectivePermissionService` projects `ClaimValue`, then `Distinct()` (fixed). `PermissionService.GetUserPermissionsAsync` and `GetAllPermissionsAsync` call `Distinct()` on whole `ApplicationRoleClaim` rows (open). |
| Aug 3.4 paging | open | HR computes `Skip((PageNumber - 1) * PageSize)` in `int`, orders with no `ThenBy(Id)`, and passes `ILike($"%{filter}%")` unescaped. Identity has no paging at all. |
| Aug 3.5 unique → 409 | open | Grant/deny, employee create and department create/rename all check-then-insert. A race produces a 500 (see §2.1). |
| Aug 3.6 remaining validators | open | Update employee: `EmployeeId NotEmpty`, `JobTitle ≤200`, `ContractType` parse — nothing else. |
| Aug 3.7 silent successes | open | Seen so far: `Employee.Update` on an inactive employee (204), Remove-member twice (success, pinned on purpose by `DepartmentFlowTests`), `RemovePermissionFromRole` on nothing (204). Phase 3 sweeps for the rest. |
| Aug 3.9 `_resolveCache` / missing user | open | `HrDirectory._resolveCache[appUserId] = employeeId` caches `null`. `GetProfilesAsync` falls back to an empty name with no log, and `HrDirectory` has no logger. |
| Aug 3.10 | open | Delete department requires `departments.delete` (Phase 3 to confirm against the soft-delete semantics). `departments.set_active` and `employees.delete` are registered but gate no route (record, don't remove). `/members` and the list/detail/me enrichment call `IHrDirectory` straight from the endpoint. An unknown department's members answer `200 []`. |
| N5 baseline claims | open | Only `FullAccessRoles` are seeded. **No marker or seed-history pattern exists anywhere in `src`**: idempotency is always "does it exist?" (`RoleExistsAsync`, `Except`, fixed admin id). Phase 3 must introduce the marker. |
| N6 read endpoints | open | There is no `GET /api/users` (`users.read` gates nothing) and no `GET /api/roles/{roleId}/users`. `GET /api/roles/users/{userId}` requires `roles.read` with no self path. The catalog `GET /api/permissions` requires `permissions.manage` only. |
| N7 `UpdateNameAsync` / `LoginActive` | partial | There is no `IUserAdministration.UpdateNameAsync`. `UserSummary(Id, Email, DisplayName, IsActive)` already exists. |
| N9 create password policy | open | `RuleFor(x => x.Password).NotEmpty().MinimumLength(8)`. An Identity rejection collapses into a generic `UserErrors.CreateFailed`. CRM's `CreateClientCommandHandler` has the same rule. |

**Phase 4 — Performance**

| Item | Status | Evidence |
|---|---|---|
| Login loads every token | open | `OwnsMany(u => u.RefreshTokens)` always loads, so `FindByEmailAsync` pulls the never-pruned history. |
| `EffectivePermissionService` query count | open | Four queries (`FindByIdAsync` tracked, `GetRolesAsync`, the claims join, overrides). At login, `GetRolesAsync` runs twice. `/effective` re-queries the overrides. |
| Employee list reads the table twice | open | COUNT plus the page, then `GetProfilesAsync` re-reads `hr.employees`. **`GetProfilesAsync` has no `OrderBy`, so the JobTitle page order is lost on the way out (a correctness bug, not only performance).** |
| `AsNoTracking` on reads | already-fixed (HR) | GetEmployee, GetEmployees, GetDepartment and GetDepartments all use it; `HrDirectory` projects. Identity's resolver user load is still tracked (see above). |

**Phase 5 — Hygiene**

| Item | Status | Evidence |
|---|---|---|
| Outbox dead-letter | open | `OutboxMessage` has `Attempts`/`Error` only, and `ProcessPendingAsync` retries every unprocessed row forever. **Also:** in `ProcessOneAsync`, an unknown event type throws outside the try/catch and aborts the rest of that sweep. |
| `UserPermissionOverride.IsActive` | open | Inherited from `Entity` and never read. The resolver's own doc says so. |
| Aug 5.5 rules | open | — except **Identity never referencing `Hr.Contracts`**, which is **already enforced** by `IdentityContractsIsolationTests` (`IdentityProject_ReferencesNoOtherModule`, `NoIdentityProject_ReferencesAnotherModulesContracts`). `UserDeactivatedIntegrationEvent` **doesn't exist** (only a comment in `IntegrationEventConsumerTests` mentions it), so the "delete or publish" item is moot. |
| Aug 5.6 docs/tooling | open | Notes only, at Phase 5. |

#### 2.3 Extra findings (recorded, not fixed)

- **Committed secrets in `src/Api/appsettings.json`:**
  - `JwtSettings:Key` is a real-looking signing key. The `DI.cs` comment says a committed signing key must never exist.
  - `DMS_SEED_ADMIN_PASSWORD` holds a dev password. The `AdminUserSeeder` comment calls a committed seed password "a credential in the repository".
  - Both are Development conveniences, but they contradict their own comments. The owner should move them to user-secrets and rotate them anywhere they were used. (Values not copied here.)
- **`HttpCurrentUser` reads `ClaimTypes.NameIdentifier`.** It works only because JwtBearer's default inbound claim mapping turns `sub` into it; `JwtProvider.ValidateToken` turns that mapping off for its own use. Phase 1B touches the same options and must keep the mapping, or switch to `sub`.
- **`ModuleAssemblies`' summary still says "Empty today"**, which is stale. Not tidied (rule 4).

### 3. Tests written but not run

None. Docker came up, and every Testcontainers suite ran.

### 4. Migration guards added

None. Phase 0 has no migration.

### 5. Frontend contract changes

- **Query routes now enforce their validators.** An out-of-range paging or filter input now returns **400 `Validation.General`**, with per-field `errors`, where before it returned 200. Affected routes:
  - **HR:**
    - `GET /api/departments` and `GET /api/employees`: `pageNumber ≥ 1`, `pageSize` 1..200.
    - Before this, `pageSize=0` returned 200 with an empty page, and `pageSize=100000` was unbounded.
  - **Finance:** the 27 query routes behind the validators listed in §2.1, e.g. `take`/`skip` and date-window ranges.
  - **Approvals:** `GetApprovalsBySubject` and `GetMyPendingApprovals`.
  - **CRM:** `GetCrmMeetingsByProject` and `GetFreeMeetingSlots`.

  No existing test sent an out-of-range value, so the defaults the frontend relies on are unaffected.
- **Scoped queries are unchanged:** Projects, CRM and Sites still answer 404 before 400.

### 6. Blockers, questions and contradictions with the prompt

1. **Branch name.** The work is on `claude/awesome-einstein-mw5xmb`, not `fix/hr-identity-phase-0`, as explained under Setup.
2. **Rule 11 needs one exemption the prompt doesn't mention.** `CrmBriefHeadDataValidator` validates a DTO (`CrmBriefHeadData`), not a command or query; two command validators run it through `SetValidator`. It is exempted as a child validator, and the rule checks the exemption still holds.
3. **Rule 11's floor is 180, not "about 182".** 181 `AbstractValidator<T>` classes exist: 180 for commands/queries plus 1 child. The 182nd `AbstractValidator<…>` text match is the generic `ValidationExtensions` helper, not a validator.
4. **Rule 12 (Phase 1A) has two routes to classify.** `GET /api/me` (`Program.cs`) and `GET /api/crm/briefs/{briefId}/rounds/{round}/diff` are authenticated-only today. The prompt's tier-2 exemption list doesn't name them. Phase 1A must either add a `RequirePermission` or justify them in the list.
5. **The harness and the Phase 1B rate limit.** `TestUsers` logs in through `/api/auth/login` on every call: once as the admin, once as the user. Once 1B puts `RateLimitPolicies.AuthLogin` on login, the integration suite (one host, one IP) will hit 429. Phase 1B must make the limit configurable, raise it in `ApiFixture`, and keep a dedicated test at the production limit.
6. **The harness and the Phase 1A guards.** Setup runs as the seeded full-access admin, so N1, N3 and N4 don't affect it. If Phase 1A also stops a full-access caller from creating custom roles or assigning claims, `TestUsers` must be revisited.
7. **The prompt's gate uses `dotnet format` apply-then-diff**, as `gate.yml` does. `--verify-no-changes` was not used.

### 7. PR body

(Opened as a draft PR; see the PR. Body kept here only if creation fails.)

---

## Phase 1A — Authorization

### Setup

**Branch.** Phase 1A also lives on `claude/awesome-einstein-mw5xmb`, which is the only branch this session can push to.
- It builds on Phase 0, which is not merged yet (PR #3).
- The commits therefore join PR #3 rather than starting a new `fix/hr-identity-phase-1A` PR.

**Environment and baseline.** Same environment as Phase 0. Phase 0's head was green both locally and in CI (`gate` ×2).

### 1. Item table

| Id | Status | Commit | Proving test |
|---|---|---|---|
| N1 delegation guard | fixed | `02a401b` | `IdentityAuthorizationTests`: `RolesManageOnly_CannotAssignTheAdminRole`, `RolesManageOnly_CannotAddAPermissionTheyLack_EvenToTheirOwnRole`, `RolesManageOnly_CanAssignARoleWhoseClaimsTheyHold`, `PermissionsManage_CannotGrantAnOverrideTheyDoNotHold`, `EditingTheManagerRole_ByANonFullAccessCaller_Is403_BeforeTheLock` |
| K6 self-modification, last holder, shared body | fixed | `02a401b` | `GrantingAnOverrideToYourself_Is403` (grant and revoke); `LastHolderTests`; `LastHolderInactiveTests` |
| K8 read side | fixed | `02a401b` | `PlainEmployee_CannotReadAnotherUsersEffectivePermissions_ButCanReadTheirOwn` |
| N3 full-access lock | fixed | `02a401b` | `EditingTheManagerRolesClaims_Is409_EvenForAnAdmin` (add and remove) |
| N4 system role / in use / audited delete | fixed | `02a401b` | `DeletingTheEmployeeRole_Is409`, `RenamingTheClientRole_Is409_ButItsDescriptionIsEditable`, `DeletingARoleWithAMember_Is409`, `DeletingAnUnusedRole_WritesAnAuditRowPerClaim` |
| Role validators (Aug 3.6) | already-fixed | — | `CreateRoleCommandValidator`: `Name NotEmpty().MaximumLength(256)`. `UpdateRoleCommandValidator`: the same, plus `RoleId NotEmpty` and `Description ≤ 500`. `DeleteRoleCommandValidator`: `RoleId NotEmpty`. FluentValidation's `NotEmpty` also rejects whitespace, so names are 1..256 already. |
| K7 Rule 12 + FallbackPolicy | fixed | `23c6bfe` | `Rule12EndpointAuthorizationTests` |

**Red-before-green proofs**, each run locally by reverting the fix and then restoring it:
- **Reverting all 1A guards at once:**
  - **11 of the 12 `IdentityAuthorizationTests` went red,** along with both last-holder tests.
  - **`RolesManageOnly_CanAssignARoleWhoseClaimsTheyHold` is the positive control.** It went red in that combined run too; re-run on its own with only the guard reverted, it passes, which is what a positive control should do.
  - **The audit test survived the first, partial revert.**
    - That revert skipped only `RemoveRange`. The claims were still loaded, so EF tracked them and cascaded the delete itself, which still writes audit rows.
    - With the load removed as well (the original code), the test goes red: `Assert.Equal() Failure` on the row count.
- **Rule 12:**
  - With `.RequirePermission` replaced by `.RequireAuthorization()` on `GET /api/roles`, tier 2 reports `GET /api/roles requires a signed-in caller but no permission`.
  - With `.AllowAnonymous()` removed from login, the rule reports `POST /api/auth/login is on the anonymous list but does not call .AllowAnonymous()`.
  - With `FallbackPolicy = null`, the runtime assertion goes red.

### What changed

- **N1, the delegation guard.** `IDelegationGuard`/`DelegationGuard` live in `Identity.Infrastructure/Services`.
  - **How it checks:**
    - "Holds" means **effectively** holds: resolved from the database through `IEffectivePermissionService`, not read from the token.
    - "Full-access" means **membership** of `DefaultRoles.FullAccessRoles` (an active caller), not holding today's catalog through a custom role.
  - **Where it runs:**
    - **Assign role:** the caller must hold every claim of the role, and a full-access role requires a full-access caller.
    - **Remove role from user, and remove permission from role:** a full-access role requires a full-access caller.
    - **Add permission to role:** the full-access rule, plus the caller must hold the permission.
    - **Grant override:** the caller must hold the permission.
  - **Failures** are 403 `permissions.CannotDelegate`, naming the missing permissions or the role.
  - **Order:** role not found → 404 first (there's nothing to evaluate), then the guard (403), then the state checks (409).
- **K6, overrides.**
  - `PermissionOverrideWrites.WriteAsync` is the single body behind grant and deny. The two handlers keep their own validators and doc comments.
  - **Checks, in order:**
    1. no caller → 401;
    2. target is the caller → 403 `permissions.SelfModification`;
    3. grant → the delegation guard (403);
    4. **target user doesn't exist → 404 `users.NotFound`** (new: this used to be an FK violation and a 500);
    5. an override already exists → 409 (unchanged);
    6. deny of `permissions.manage` with no other **active** effective holder → 409 `permissions.LastHolder`.
  - The holder count is `IEffectivePermissionService.HasOtherActiveHolderAsync`. The candidates are members of roles carrying the permission plus users with a grant for it; each is resolved with `ResolveAsync` and must be active.
- **K8, the read side.**
  - `GET /api/permissions/users/{userId}/effective` now requires `permissions.manage`. It used to be authenticated-only.
  - New `GET /api/permissions/me/effective`, authenticated-only, takes the user id from the token.
- **N3.** `PermissionService` refuses both adding and removing a claim on a full-access role, with 409 `roles.FullAccessLocked`.
  - `RemovePermissionFromRoleAsync` now loads the role for this check, so a missing role answers 404 instead of 204. That is part of Aug 3.2, done early because the check needs the role; the affected-count and audited delete stay in Phase 3.
  - `RoleClaimSeeder` stays additive, and its comment now says why.
- **N4.**
  - **Delete, or rename (`RoleService` and `UpdateRoleCommandHandler`), of a role in `DefaultRoles.All`** → 409 `roles.SystemRole`.
    - The name comparison is ordinal, so a casing change counts as a rename.
    - Editing the description is still allowed.
  - **Delete of a role with members** → 409 `roles.RoleInUse` (with role id and count).
  - **Delete of an unused custom role:** its `role_claims` are removed through the change tracker first, in the command's transaction, so one audit row is written per claim. Then `RoleManager.DeleteAsync` runs.
- **K7.**
  - **`Api/DI.cs`** sets `FallbackPolicy = RequireAuthenticatedUser`.
  - **Explicit `.AllowAnonymous()`** on login, refresh, revoke, the CRM public lead form, `/health`, and the Development-only OpenAPI/Scalar docs.
  - **`tests/Architecture.Tests/Rule12EndpointAuthorizationTests.cs`:**
    - Boots the real host (`WebApplicationFactory<Program>`) against a Testcontainers database. It is migrated from Rule 9's module map, which is now `internal`, as is Rule 9's migrate helper, which takes the connection string; Rule 9's own behaviour is unchanged.
    - Reads `EndpointDataSource`.
    - Checks the two tiers, checks the lists in reverse (every entry must name a mapped route), and checks that the fallback policy denies anonymous users.
    - Floor: 330 route endpoints counted, so ≥ 320. The count is in the message.
- **Test infrastructure.**
  - `IdentityApplicationFixture` now seeds a real active `test-caller-id` in an `admin` role holding the catalog. `ICurrentUser` is scoped through a settable slot (`CreateScope(caller)`).
  - The four existing role/permission tests that went red under N1 stay unchanged; only their caller became real.
  - `TestUsers` exposes `RoleId`, `CreateRoleAsync` and `RoleIdAsync`.

### 2. Run 0 for this phase

There is no separate Run 0 for 1A in the prompt. Phase 0's inventory (§2.2) was re-checked against the code before each fix, and every 1A item was still open as recorded there, except the role validators, which are already-fixed (above).

### 3. Tests written but not run

None.

### 4. Migration guards added

None. Phase 1A has no migration.

### 5. Frontend contract changes

| Route / behaviour | Before | After |
|---|---|---|
| `GET /api/permissions/users/{userId}/effective` | any authenticated user | **403** without `permissions.manage` |
| `GET /api/permissions/me/effective` | — | **new**, authenticated; returns `EffectivePermissionsResponse` for the caller |
| `POST /api/roles/{roleId}/users/{userId}` | `roles.manage` was enough | **403 `permissions.CannotDelegate`** if the caller doesn't hold every claim of the role, or the role is admin/manager and the caller isn't in one |
| `DELETE /api/roles/{roleId}/users/{userId}` | — | 403 `permissions.CannotDelegate` for admin/manager unless the caller is full-access |
| `PUT /api/permissions/roles/{roleId}/{permission}` | — | 403 `permissions.CannotDelegate` if the caller lacks the permission; **409 `roles.FullAccessLocked`** on admin/manager |
| `DELETE /api/permissions/roles/{roleId}/{permission}` | always 204 | 404 `roles.NotFound` for an unknown role; 403 (non-full-access caller) or 409 `roles.FullAccessLocked` on admin/manager |
| `POST …/overrides/grant` | — | 403 `permissions.SelfModification` on yourself; 403 `permissions.CannotDelegate` if you lack it; **404 `users.NotFound`** for an unknown target (was 500) |
| `POST …/overrides/deny` | — | 403 `permissions.SelfModification`; 404 `users.NotFound`; **409 `permissions.LastHolder`** for `permissions.manage` |
| `DELETE …/overrides/{permission}` | — | 403 `permissions.SelfModification` on yourself |
| `PUT /api/roles/{roleId}` | renamed anything | **409 `roles.SystemRole`** renaming admin/manager/employee/client; description edits still 204 |
| `DELETE /api/roles/{roleId}` | deleted anything, stripping members | **409 `roles.SystemRole`** for system roles; **409 `roles.RoleInUse`** with members |
| Any route with no authorization declared | anonymous | requires authentication (`FallbackPolicy`); no such route exists today, so no visible change |

### 6. Blockers, questions and contradictions with the prompt

1. **Rule 12's lists are longer than the prompt's.** Running it against the real host found routes the prompt's lists don't name. Each is listed in the test with its reason:
   - **Tier 1 (anonymous):**
     - `/health`, probed without credentials;
     - the Development-only `/openapi/…` and `/scalar…` docs, as prefixes.
   - **Tier 2 (authenticated-only):**
     - `/api/me`;
     - `/hubs/notifications` and `/hubs/notifications/negotiate`;
     - `GET /api/crm/briefs/{briefId}/rounds/{round}/diff`: either `crm.briefs.view_any` or the brief's own client may read it, so the handler decides; changing that is CRM work, out of scope.
2. **`/api/auth/logout`** is in tier 2 as the prompt asks, but the route arrives in Phase 1B. It sits in `NotYetMapped`, so the "listed route still exists" check skips it. **Phase 1B must remove it from `NotYetMapped`.**
3. **Self-modification also blocks revoke** (`DELETE …/overrides/{permission}` on yourself). The prompt names grant and deny only, but revoking a deny is how someone would lift one placed on themselves. Same error code.
4. **The last-holder rule is only reachable with a stale caller.** The route requires the caller to hold `permissions.manage`, and self-deny is refused, so a caller who still holds it is themselves the other holder. The rule therefore catches the case where the caller's token outlived their permission. Phase 1B's security stamp narrows that window; the tests reach the rule with a token-only caller in an isolated database.
5. **N1 "full-access caller" means role membership,** not "holds every permission today". The prompt says "in a full-access role"; this records why that is the right reading: a custom role that holds today's catalog misses the permissions modules add later.
6. **Not changed, recorded only:** the existing `IRoleService.UserIsAdminAsync` hard-codes `"admin"`/`"manager"` and has no callers. The guard uses `DefaultRoles.FullAccessRoles` instead. Dead-code removal is Aug 5.7.

---

## Phase 1B — Authentication & sessions

### Setup

- **Branch:** `claude/awesome-einstein-mw5xmb`, which builds on 1A. The commits go to the same PR #3.
- **Baseline:** 1A's head was green, both locally and in CI.
- **Tooling:** `dotnet-ef` 10.0.13 was installed as a global tool to scaffold the migration.

### 1. Item table

| Id | Status | Commit | Proving test |
|---|---|---|---|
| K4 lockout, uniform login, dummy hash | fixed | `335890e` | `AuthSessionTests`: `UnknownEmail_WrongPassword_AndInactiveAccount_AnswerIdentically`, `AfterFiveFailures_TheRightPasswordIsRefused_WithLockedOut`, `ASuccessfulLogin_ResetsTheFailureCount` (positive control); `AuthFlowTests.Provisioned_user_cannot_log_in_until_activated` (now expects `InvalidCredentials`) |
| Auth rate limit | fixed | `335890e` | `BeyondTheWindowsLimit_AuthRoutesAnswer429` |
| K5 family revoke on reuse | fixed | `335890e` | `PresentingARotatedRefreshToken_RevokesTheFamily`; `AuthFlowTests.Reusing_a_rotated_refresh_token_revokes_the_whole_family`; unit `RotateRefreshToken_WithAlreadyRotatedToken_RevokesTheWholeFamily` |
| K5 revoke on password change | fixed | `335890e` | `ARefreshTokenFromBeforeAPasswordChange_IsRejected` |
| K5 `/logout` | fixed | `335890e` | `Logout_RevokesEveryRefreshToken_AndEndsTheLiveAccessToken` |
| K5 hashed storage + migration | fixed | `335890e` | `NoRefreshTokenValueIsStored_OnlyItsSha256`; unit `IssueRefreshToken_StoresOnlyTheHash`; `RefreshTokenHashMigrationTests.ExistingTokens_AreHashedInPlace_AndExpired` |
| K8 change-password from `ICurrentUser` | fixed | `335890e` | `ChangePassword_IgnoresAnyUserIdInTheBody` |
| N2 security stamp | fixed | `335890e` | `DeactivatingAUser_EndsTheirLiveToken_AndTheirRefresh`, `AssigningARole_MakesTheOldTokenStale_AndRefreshCarriesTheNewPermissions`, `EveryAuthenticatedRequest_ChecksTheSession` |
| Pinned algorithms (Aug 5.1) | fixed | `335890e` | — (configuration; `HmacSha256` tokens keep validating across the whole suite) |
| `ValidateToken` catches `ArgumentException` (Aug 3.10) | fixed | `335890e` | `AMalformedOrOversizedAccessToken_OnRefresh_Is401_Not500` (×3) |

**Red-before-green proofs**, each run locally by reverting the fix and then restoring it:
- **One combined revert:**
  - **What was reverted:**
    - `lockoutOnFailure: false`, and an inactive account answering differently;
    - the auth routes' rate limiting;
    - the family revocation, both in the domain and in the handler;
    - revoke-on-password-change;
    - logout's revocations;
    - `OnTokenValidated`'s `Fail`;
    - the `ArgumentException` catch.
  - **Result:** 11 of the 15 `AuthSessionTests` went red, each on its own assertion.
  - **The four that stayed green, and why:**
    - the reset-counter test is a positive control;
    - hashing was not part of this revert (it has its own, below);
    - the change-password `UserId` field cannot be reverted without restoring the old contract;
    - the session-check test asserts only that the query runs.
- **Hashing:** with `RefreshToken.Hash` made the identity function, `IssueRefreshToken_StoresOnlyTheHash` goes red. This was proved at unit level, because the 64-character column rejects a plaintext token outright.

### What changed

- **K4, login.**
  - **`IdentityPolicy.Configure`** is applied by `IdentityModule` and by both Identity test fixtures:
    - lockout: 5 attempts, 15 minutes, `AllowedForNewUsers`;
    - password: length 8 with a digit, lower, upper and non-alphanumeric.
  - **`.AddSignInManager()`** is added, plus `AddAuthenticationCore()`, which supplies the scheme provider in hosts that don't call `AddAuthentication`. All registrations are TryAdd.
  - **`IPasswordSignIn`/`PasswordSignIn` run in a scope of their own.**
    - Every command runs in a transaction that `DomainEventDispatchDecorator` **rolls back on a failed result**, and a failed login is a failed result.
    - Written inside the command, the failed-attempt counter would never persist, and lockout would never trigger.
    - This is the same reasoning as the existing `INotificationService` exception.
  - **The unknown-email path** verifies a dummy hash made by the real hasher, so it costs the same time.
  - **Answers:**
    - unknown email, wrong password and inactive account → `InvalidCredentials`;
    - locked → `authentications.LockedOut`. The lockout is checked before the password, so the answer reveals nothing about it.
- **Rate limit.**
  - `RateLimitPolicies.AuthLogin` is a fixed window keyed by IP, registered like `CrmPublicLeads`.
  - Limits: 10 requests per 60 s, configurable through `RateLimiting:AuthLogin:PermitLimit` and `RateLimiting:AuthLogin:WindowSeconds`.
  - It covers `/login`, `/refresh`, `/revoke`, `/change-password` and `/logout`, all sharing one window per IP.
  - `UseRateLimiter()` now runs **before** `UseAuthentication()`.
- **K5, refresh tokens.**
  - **Hashed storage:**
    - `RefreshToken.TokenHash` holds the lower-case hex SHA-256 of the token; the token value itself is never stored.
    - The handlers return the plaintext they generated (`RefreshTokenValues.New()`).
  - **Reuse detection:**
    - Presenting an already-**revoked** token makes `ApplicationUser.RotateRefreshToken` return `authentications.RefreshTokenReused` (401).
    - The handler then calls `ISessionRevocation.RevokeAllInOwnTransactionAsync`, which revokes every refresh token and bumps the stamp in its own scope. It has to: this command fails and its transaction rolls back, and the revocation must survive.
    - An unknown or expired token is still just `InvalidRefreshToken`.
  - **Change-password** revokes every refresh token. `ChangePasswordAsync` bumps the stamp itself; the cache is evicted.
  - **`POST /api/auth/logout`** is new: authenticated, rate-limited. It revokes every refresh token and bumps the stamp, so live access tokens die at once.
  - **Migration `20261019205422_HashRefreshTokens`**, hand-written:
    - renames the column, hashes the existing values in place (so the unique index holds and no plaintext survives), sets every `ExpiresOn` to the migration time, narrows the column to 64 characters and recreates the index;
    - also sets `LockoutEnabled = true` on every user;
    - **expires all existing refresh tokens, so every user signs in again after deploy**, as its comment says;
    - `Down` restores the shape; the tokens cannot come back.
    - The regenerated snapshot also picked up `shared.audit_logs` as `ExcludeFromMigrations`. That is the tool catching up with the model; it generates no DDL.
- **K8, the write side.** `ChangePasswordRequest(CurrentPassword, NewPassword)` has no `UserId`. The handler uses `ICurrentUser.UserId`; null → 401.
- **N2, the security stamp.**
  - **The `sstamp` claim:** `JwtProvider` adds it (`JwtProvider.SecurityStampClaim`).
  - **`IUserSessionState`** is new in `Identity.Contracts`, with `GetAsync` and `GetManyAsync` per the one-and-many rule, returning `UserSessionSnapshot(UserId, SecurityStamp, IsActive)`.
    - It is backed by `IMemoryCache`, with TTL `Identity:SessionStateCacheSeconds` (default 30; 0 in the test hosts).
    - Only hits are cached.
    - Its query is tagged `identity:session-state`.
  - **The host's JwtBearer `OnTokenValidated`** (Api `DI.cs`) refuses a token when the user is missing, inactive, has no stamp, or has a different stamp.
    - `OnChallenge` then writes a 401 problem with `authentications.StaleToken`, in the same shape as every other error.
    - Refresh does **not** check the stamp: picking up a changed stamp is what refresh is for.
  - **Stamp bump plus eviction** goes through `ISecurityStamps`, whose `BumpRoleHoldersAsync` is the one "all holders of a role" helper. Where it happens:
    - deactivation (`UserAdministration.DeactivateAsync`, which also revokes every refresh token);
    - role assign and remove;
    - any role-claim change, for every holder;
    - override grant, deny and revoke, for the target;
    - password change, logout, and refresh-token reuse.
  - **Eviction timing.** It happens inside the command, before commit. A request racing the commit could re-cache the old stamp, so in production the window is at most one TTL (30 s).
- **Pinned algorithms.** `ValidAlgorithms = [HmacSha256]` in both `JwtProvider.ValidateToken` and the JwtBearer options.
- **`ValidateToken`** also catches `ArgumentException` (an oversized or malformed token), giving `InvalidToken` (401) instead of 500.
- **Rule 12.** `/api/auth/logout` is mapped and in tier 2. `NotYetMapped` is now empty, and the count is 331.

### Test infrastructure changes

These were required by N2 and K4–K5.
- **`ApiFixture.IssueTestToken`** now carries the user's current `sstamp`.
  - An id nobody provisioned first gets a bare, active login row, inserted with raw SQL as `SeedPublicLeadIntakeOwnerAsync` already does.
  - Without this, every one of the ~50 arbitrary-claim test tokens would be `StaleToken`.
  - A token issued before a change to its user is stale afterwards, as in production.
- **`ApiFixture`** sets the auth rate limit to 100000 (one IP, hundreds of logins) and the session cache TTL to 0.
- **`ApiFixture.CapturedSql` excludes exactly the tagged session-check statement.**
  - Why: since N2, every authenticated request reads `identity.users` during authentication, which is host work, before any module's handler. Without the exclusion, eight schema-isolation tests reported every module as touching `identity`.
  - The match is on the query tag, not on the query's shape. Any other `identity.` statement still counts.
  - `CapturedSessionChecks` keeps the excluded statements visible, and `EveryAuthenticatedRequest_ChecksTheSession` proves the check runs.
- **`CrmBriefRouteTests` and `CrossModuleSmokeTests`** now process the outbox before using the new client's token.
  - The portal login is activated by `ClientCreatedIntegrationEvent`, and a token for an inactive account is now refused.
  - In production such an account could not have signed in at all.
- **Identity fixtures:**
  - **`IdentityApplicationFixture`:** new registrations, `IdentityPolicy`, `AddSignInManager`, and TTL 0.
  - **`AuthFlowTests`:** these now run one scope per call, as HTTP requests do. The sign-in bookkeeping and the family revocation commit from scopes of their own, and a shared scope's tracked user would go stale.
    - `Login_then_refresh_then_revoke…` no longer replays the first token before revoking the second: that replay now (correctly) revokes the family. The replay has its own test.
  - **`ApplicationUserRefreshTokenTests`** compare through `Matches(...)`, because only hashes are stored.

### 2. Run 0 for this phase

There is no separate Run 0 for 1B in the prompt. Every 1B item was re-checked against the code before its fix; all were open as recorded in Phase 0 §2.2.

### 3. Tests written but not run

None.

### 4. Migration guards added

None. This migration adds no constraint that existing data could violate: the hashed values stay unique because the plaintext values were unique, and the column narrows to the hash's exact 64 characters. Before deploying, run this against a production snapshot:

```sql
-- Rows this migration expires (every user with one must sign in again):
SELECT count(*) AS live_refresh_tokens FROM identity.refresh_tokens WHERE "RevokedOn" IS NULL AND "ExpiresOn" > now();
-- Users this migration subjects to lockout for the first time (expected 0):
SELECT count(*) FROM identity.users WHERE NOT "LockoutEnabled";
```

### 5. Frontend contract changes

| Route / behaviour | Before | After |
|---|---|---|
| `POST /api/auth/login`, inactive account | 401 `authentications.UserNotActive` | **401 `authentications.InvalidCredentials`**, identical to an unknown email or a wrong password |
| `POST /api/auth/login`, 5th failure in a row and for 15 min after | — | **401 `authentications.LockedOut`**, even with the right password |
| `/login`, `/refresh`, `/revoke`, `/change-password`, `/logout` | unlimited | **429** past 10 requests/min per IP, shared across all five |
| `POST /api/auth/refresh` with a rotated-away token | 401 `InvalidRefreshToken` | **401 `authentications.RefreshTokenReused`**, and **every** refresh token of the account is revoked; sign in again |
| `POST /api/auth/refresh` with a malformed or oversized access token | 500 | 401 `authentications.InvalidToken` |
| `POST /api/auth/change-password` | body `{ userId, currentPassword, newPassword }`, any user | body **`{ currentPassword, newPassword }`**, always the caller; a `userId` is ignored. All refresh tokens revoked, and live access tokens become `StaleToken`: sign in again with the new password |
| `POST /api/auth/logout` | — | **new**, authenticated, 204; ends every session of the caller |
| Any authenticated request after deactivation, role assign/remove, a claim change on one of your roles, an override on you, a password change or logout | worked until the token expired | **401 `authentications.StaleToken`**. Call `/refresh`; after deactivation, password change or logout the refresh fails too and the user must sign in |
| Access token claims | — | new `sstamp` claim (opaque) |
| Existing refresh tokens at deploy | — | **all expired; every user signs in again once** |

### 6. Blockers, questions and contradictions with the prompt

1. **The command transaction forces two "own scope" writes.** The prompt's K4 and K5 designs assume a failed login and a reused refresh token can persist state. `DomainEventDispatchDecorator` rolls back every failed command, so the lockout bookkeeping (`PasswordSignIn`) and the family revocation (`SessionRevocation`) each run in a scope of their own. They are documented in their interfaces, in the spirit of the `INotificationService` exception.
2. **Beyond the prompt:**
   - **Logout and refresh-token reuse also bump the security stamp**, so live access tokens die at once. The prompt's N2 list doesn't name either. The prompt says logout "revokes all of the caller's tokens", and reuse means theft; without a bump, the thief's access token would live out its hour.
3. **Refresh deliberately ignores the stamp.** Otherwise "assign a role → old token 401 → after refresh, the new token has the role's permissions" could not work.
4. **Integration tests changed to match N2** (§Test infrastructure). The schema-isolation exclusion is the one judgement call. It excludes only the tagged host query, it is proved to run, and the tests still catch any module SQL touching `identity.`.
5. **Rate-limit numbers** (10 per 60 s) are my choice; the prompt gives none. Both are configurable.
6. **Dead code, recorded for Aug 5.7, not removed:** `AuthErrors.UserNotActive` (unused since K4).
7. **Eviction timing:** a stamp change is evicted before its transaction commits, so a racing request can re-cache the old stamp for at most one TTL (30 s by default). Evicting after commit would need a post-commit hook the pipeline doesn't have.
