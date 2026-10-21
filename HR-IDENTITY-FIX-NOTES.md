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

## Phase 2 — HR data integrity

### Setup

- **Branch:** `claude/awesome-einstein-mw5xmb`. Per A12, it restarted from `master` after PR #3 merged (`3ea23a8`), so only merged history sits under it. Phase 2 is a **new** PR.
- **Baseline:** `master` at `3ea23a8`, green.
- **Plan:** the approved Phase 2 plan, with review amendments A1–A12 applied before coding.

### 1. Item table

| Id | Status | Commit | Proving test |
|---|---|---|---|
| K1 restore a former membership | fixed | `3e9e4ed` | `HrDataIntegrityTests.AssignMoveAndMoveBack_RestoresTheFormerMembership_WithANewStart` |
| K1/K2 one active department per employee | fixed | `3e9e4ed` | `AssigningSomeoneAlreadyInAnotherDepartment_Is409_Not500`; A6 `TwoConcurrentAssignments_OneWins_TheOtherIs409` |
| K2 remove: inactive row → 400, head → 409 | fixed | `3e9e4ed` | `RemovingTwice_Is400_AndRemovingTheHead_Is409`; `DepartmentFlowTests` (inverted, §6) |
| K2 deactivating a head clears the headship | fixed | `3e9e4ed` | `DeactivatingAHead_ClearsTheHeadship` |
| K2 deleting a department ends memberships, clears the head | fixed | `3e9e4ed` | `DeletingADepartment_EndsItsMemberships_AndClearsItsHead` |
| A3 inactive employee → 409 `employees.Inactive` (assign, move, head) | fixed | `3e9e4ed` | `AnInactiveEmployee_CannotBeAssigned_Moved_OrMadeHead` |
| K3 query filters + `includeInactive` | fixed | `3e9e4ed` | `Lists_HideInactiveByDefault_AndShowThemWithIncludeInactive`; `HrDirectoryTests.A_deactivated_employee_is_invisible_to_the_directory_but_still_linked` |
| A1 reaper keeps a deactivated employee's login | fixed | `3e9e4ed` | `UnlinkedUserReaperTests.A_deactivated_employees_login_is_never_reaped` |
| HR-1 activation re-enables the login | fixed | `3e9e4ed` | `DeactivateProcessActivateProcess_LoginWorks`, `DeactivateActivateThenProcessOnce_LoginWorks`, `ALateDeactivationEvent_AfterActivation_LeavesTheLoginWorking`; A9 `Reactivation_IsTheRehirePath` |
| ContractType `IsDefined` (update validator + domain) | fixed | `3e9e4ed` | `UpdatingWithAnUndefinedContractType_Is400` (×3); unit `An_undefined_contract_type_is_refused_by_the_domain` (×3) |
| N7 full-replacement PUT; inactive → 409 | fixed | `3e9e4ed` | `UpdatingAnInactiveEmployee_Is409`, `APutWithANullPhone_ClearsIt`; unit `Update_ReplacesEveryEditableField_AndANullClears`, `Update_OnInactiveEmployee_IsRefusedWithInactive` |
| N8 names unique among active only; casing rename; description cap and clear | fixed | `3e9e4ed` | `ACasingOnlyRename_IsNotADuplicateOfItself`, `ADeletedDepartmentsName_CanBeReused_ButTwoActiveCannotShareOne`, `ADescriptionOver2000Characters_Is400`, `ThePutReplacesTheDescription_AndEmptyClearsIt` |
| N8 `HeadName` / `MemberCount` | fixed | `3e9e4ed` | `TheDepartmentList_CarriesTheHeadsName_AndTheMemberCount` |
| `ExecuteUpdateAsync` bypassing audit (Activate, UpdateDepartment) | fixed | `3e9e4ed` | covered by the Activate/Update tests above (tracked entities now) |
| Aug 2.7 Restrict FKs, head FK, partial indexes, CHECK | fixed | `3e9e4ed` | `HrSchemaTests.Phase2_constraints_are_restrict_foreign_keys_partial_unique_indexes_and_a_check`; A4 probe migration empty |
| Aug 2.8 `[AuditIgnore]` phone, emergency phone, notes | fixed | `3e9e4ed` | `ThePhoneChangesAuditRow_DoesNotContainThePhone` |
| K9 migration guards | added | `3e9e4ed` | `HrDataIntegrityMigrationGuardTests` (4 refusals + clean-migrate control) |
| Aug 2.4 expired overrides | fixed | `fe9dd6f` | `ExpiredOverrideTests` (both renew directions with stamp bump, live-409 control, `/effective`) |
| A11 EF query-filter warning | configured | `3e9e4ed` | — (build is `-warnaserror`; the warning is ignored on `HrDbContext` only, with a comment) |

**Red-before-green proofs.** Each fix was reverted on its own, its test run, and the file restored from a `cp` backup (never `tar`):

| Reverted | Went red |
|---|---|
| `PermissionOverrideWrites`: any existing row → 409 | both `ExpiredOverrideTests` renew tests |
| `LiveAt` filter on `/effective` | `Effective_permissions_leave_an_expired_override_out` |
| reaper back on `ResolveEmployeeIdAsync` | `A_deactivated_employees_login_is_never_reaped` (and the sweep then fails for every candidate after it) |
| `Employee` query filter | `Lists_HideInactiveByDefault_AndShowThemWithIncludeInactive` |
| restore of a former membership | `AssignMoveAndMoveBack_…` |
| remove-head check | `RemovingTwice_Is400_AndRemovingTheHead_Is409` |
| deactivated handler's state check | `ALateDeactivationEvent_AfterActivation_LeavesTheLoginWorking`. `DeactivateActivateThenProcessOnce` stayed green: delivered in order, the activation re-enables what the deactivation disabled. So the late-redelivery test was added. |
| enqueue of `EmployeeActivatedIntegrationEvent` | `DeactivateProcessActivateProcess_LoginWorks` |
| `IsDefined` in the update validator | `UpdatingWithAnUndefinedContractType_Is400` (×3) |
| `[AuditIgnore]` on `PhoneNumber` | `ThePhoneChangesAuditRow_DoesNotContainThePhone` |
| `d.Id != id` in the rename duplicate check | `ACasingOnlyRename_IsNotADuplicateOfItself` |
| the membership guard's `RAISE` | `Refuses_an_employee_with_two_active_memberships` |
| A6 23505 mapping (constraint name changed) | `TwoConcurrentAssignments_OneWins_TheOtherIs409`, **5 of 5 runs**: `[204, 500]` |

### What changed

- **`DepartmentMembershipService`** (Hr.Infrastructure, internal). Every membership and head write goes through it: assign, move, remove, assign head, unassign head, deactivate employee, delete department. The handlers delegate to it. This is the one service between HR handlers and `HrDbContext`; `Hr.Application.csproj`'s comment records the exception.
  - Rejoining restores the pair's existing row (`EmployeeDepartment.Restore`, new `StartedAtUtc`); the composite key allows only one row per pair.
  - Move saves after leaving, so the one-active-membership index never sees two active rows.
  - The index's 23505 is mapped, by constraint name, to 409 `AlreadyInAnotherDepartment` (A6).
- **Query filters** (`IsActive`) on `Employee` and `Department`. `IgnoreQueryFilters` is used, each with a one-line why, at:
  - the re-hire check;
  - both lifecycle event handlers;
  - Activate and Update employee;
  - the employee and department detail reads;
  - `includeInactive=true`;
  - `HrDirectory.GetProfilesAsync`, `IsLinkedAsync` and `GetLinkedAsync`.
- **`IHrDirectory`** gains `IsLinkedAsync` and `GetLinkedAsync` (A1, with the many-form CLAUDE.md requires). `ResolveEmployeeIdAsync` stays active-only.
- **HR-1:** a new `EmployeeActivatedIntegrationEvent`, and `EmployeeActivatedIntegrationEventHandler` (Hr.Infrastructure → `IUserAdministration.ReactivateAsync`). Both lifecycle handlers return early when the employee's current state disagrees with the event. `EmployeeErrors.FailedToActivate` was deleted; it was only used by the `ExecuteUpdateAsync` path this replaces.
- **Migration `20261020045733_HrDataIntegrity`:**
  - four guards, then the DDL;
  - `StartedAtUtc` backfilled from `CreatedAtUtc`;
  - everything is in the EF model (A4); `dotnet ef migrations add Probe` came out empty and was deleted.
- **Identity:** `UserPermissionOverride.LiveAt(now)` is the one live predicate, and `Renew(...)` reuses an expired row in place. Renewal goes through the existing post-save stamp bump (A7), which also evicts the session cache.

### 2. Run 0 for this phase

All Phase 2 items were open as recorded in the plan's context. The A2 audit of every external `IHrDirectory` read, done **before** `GetAsync`/`GetManyAsync`/`DepartmentMembersAsync` were filtered, found:

| Call site | Category | Effect of the filter |
|---|---|---|
| Projects `StageTeamHandlers` (two sites), `ProjectTaskHandlers` | (a) assignment | already treated an inactive employee like a missing one; now it is one |
| Projects `GateConditionHandlers` (approvers) | (a) authorization | an inactive approver is skipped |
| Projects `ProjectDeadlineJob`, `StageDeadlineJob`, `TaskDeadlineJob`; `ProjectStageNotificationHandlers` (two sites) | (a) notification recipient | inactive people are no longer notified |
| Projects `ProjectHandlers` → `GetDepartmentHeadsAsync` | (a) assignment | deleted departments are omitted, and a head cleared by deactivation is gone, see §6.4 |
| Documents `RequestDocumentReviewCommandHandler` | (a) authorization | an inactive approver answers `UploaderNotFound` |
| CRM `CrmMeetingReminderJob`, `MeetingScheduledNotificationHandler` | (a) recipient | as above |
| Finance `InvoiceDueJob` | (a) recipient | as above |
| Notifications `ProjectTaskNotificationHandlers` (two sites) | (a) recipient | as above |
| Sites `SiteApprovedNotificationHandler` | (a) recipient | as above |
| Api `UnlinkedUserReaperJob` | **linkage**, neither (a) nor (b) | moved to `IsLinkedAsync` (A1) |

No call site is (b) display or history, so no `includeInactive` overload was added to the contract. HR's own display reads use `GetProfilesAsync`, which already ignores the filter. **No test in another module had to be inverted**: the full suite passed after the filter landed, before any test was touched.

### 3. Tests written but not run

None.

### 4. Migration guards added

`HrDataIntegrity` starts with four `DO $$ … RAISE EXCEPTION … $$` blocks, before any DDL. A refusal changes nothing and the migration is not recorded (`AssertRefusedAsync` checks both). Run these against a production snapshot **before** deploying; each must return 0 rows:

```sql
-- G1: employees with more than one active membership (the new partial unique index)
SELECT "EmployeeId", count(*) FROM hr.employee_departments WHERE "IsActive" GROUP BY "EmployeeId" HAVING count(*) > 1;
-- G2: ContractType outside the enum (the new CHECK)
SELECT "Id", "ContractType" FROM hr.employees WHERE "ContractType" NOT BETWEEN 1 AND 4;
-- G3: a head who is not an employee (the new head FK)
SELECT d."Id", d."DepartmentHeadId" FROM hr.departments d
WHERE d."DepartmentHeadId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM hr.employees e WHERE e."Id" = d."DepartmentHeadId");
-- G4: two active departments sharing a name (the new partial unique index)
SELECT "Name", count(*) FROM hr.departments WHERE "IsActive" GROUP BY "Name" HAVING count(*) > 1;
```

**A5: G1 will probably fire in production.** Until now, assign never checked other departments. The listing:

```sql
SELECT ed."EmployeeId", ed."DepartmentId", d."Name", ed."CreatedAtUtc"
FROM hr.employee_departments ed JOIN hr.departments d ON d."Id" = ed."DepartmentId"
WHERE ed."IsActive" AND ed."EmployeeId" IN (
  SELECT "EmployeeId" FROM hr.employee_departments WHERE "IsActive" GROUP BY "EmployeeId" HAVING count(*) > 1)
ORDER BY ed."EmployeeId", ed."CreatedAtUtc" DESC;
```

The proposed cleanup **keeps each employee's latest membership** (by `CreatedAtUtc`) and ends the others. It is to be reviewed against the listing and run **before** the migration, by hand, never inside it. A head whose department loses them this way shows up in the second report below.

```sql
BEGIN;
UPDATE hr.employee_departments ed SET "IsActive" = false
FROM (
  SELECT "EmployeeId", "DepartmentId",
         row_number() OVER (PARTITION BY "EmployeeId" ORDER BY "CreatedAtUtc" DESC, "DepartmentId") AS rn
  FROM hr.employee_departments WHERE "IsActive") ranked
WHERE ed."EmployeeId" = ranked."EmployeeId" AND ed."DepartmentId" = ranked."DepartmentId" AND ranked.rn > 1;
-- check the row count against the listing, then COMMIT (or ROLLBACK)
```

**The deploy is blocked until someone with HR authority decides which department each listed employee keeps.** "Latest wins" is a proposal, not a rule.

Report-only queries (no guard, no automatic fix; the new code refuses to create either state, but existing rows stay until someone acts):

```sql
-- departments headed by an inactive employee
SELECT d."Id", d."Name", d."DepartmentHeadId" FROM hr.departments d
JOIN hr.employees e ON e."Id" = d."DepartmentHeadId" WHERE d."IsActive" AND NOT e."IsActive";
-- heads who are not active members of the department they head
SELECT d."Id", d."Name", d."DepartmentHeadId" FROM hr.departments d
WHERE d."IsActive" AND d."DepartmentHeadId" IS NOT NULL AND NOT EXISTS (
  SELECT 1 FROM hr.employee_departments ed
  WHERE ed."EmployeeId" = d."DepartmentHeadId" AND ed."DepartmentId" = d."Id" AND ed."IsActive");
```

**A10: HR-1 stragglers.** These are employees who were re-activated before this fix, so they are active in HR with a login still disabled. Count them with this query. It is the one cross-schema query in these notes: an ops query run by hand, not application code.

```sql
SELECT count(*) FROM hr.employees e JOIN identity.users u ON u."Id" = e."AppUserId"
WHERE e."IsActive" AND NOT u."IsActive";
```

Fix each one through the API, not SQL: deactivate → let the outbox process → activate → let it process. The activation event then re-enables the login.

### 5. Frontend contract changes

| Route / behaviour | Before | After |
|---|---|---|
| `PUT /api/employees/{id}` | partial patch; omitted fields kept | **full replacement**: `jobTitle` and `contractType` required (400 otherwise); omitted or null `phoneNumber`, `emergencyContactPhone`, `notes`, `endDate` are **cleared** |
| `PUT /api/employees/{id}`, inactive employee | 204, nothing changed | **409 `employees.Inactive`** |
| `PUT /api/employees/{id}`, `contractType` "99"/"0"/"-1" | accepted | **400** |
| `GET /api/employees`, `GET /api/departments` | inactive rows included | inactive **hidden**; `?includeInactive=true` shows them |
| `GET /api/employees/{id}`, `GET /api/departments/{id}` | — | still return inactive records, so admins can open and reactivate them |
| Assign, while active in another department | 200/204, two departments | **409 `employees.AlreadyInAnotherDepartment`**; use move |
| Assign/move back to a former department | 500 / `AlreadyInDepartment` | 204, membership restored with a new start date |
| Remove, not an active member | 204 | **400 `departments.UserNotInDepartment`** |
| Remove or move the department head | 204, head kept | **409 `departments.RemoveHeadFirst`** |
| Assign/move/make head an inactive employee | accepted | **409 `employees.Inactive`** |
| Deactivate an employee who heads a department | head kept | headship **cleared** |
| Delete a department | memberships and head kept | memberships ended, head cleared; **its name can be reused** |
| Rename to a casing variant of itself | 400 duplicate | 204 |
| Department description | uncapped; empty kept | max 2000 (400); empty or null **clears** it |
| `DepartmentResponse` | — | new **`headName`** and **`memberCount`** |
| Activate an employee | login stayed disabled | login **re-enabled** (after the outbox runs) |
| Grant/deny over an expired override | 409 | 204, renewed in place |
| `GET /api/permissions/users/{id}/effective` | listed expired overrides | expired overrides omitted |
| Projects template instantiation | — | hard-fails for a department whose head was cleared by deactivation (§6.4) |

**A8 deploy check:** because the PUT is now a full replacement, a client that sends only changed fields will **silently clear** phone, emergency phone, notes and end date. Before deploying, confirm that both the old app's and DMS's employee edit dialogs send all six fields. If either doesn't, ship that frontend first.

### 6. Blockers, questions and contradictions with the prompt

1. **The deploy is blocked on the G1 data decision** (§4, A5).
2. **The detail reads ignore the filter.** The prompt says to filter everywhere. An admin who can't open a deactivated employee can't reactivate them, so `GET /{id}` keeps showing inactive records. The lists are filtered.
3. **Status codes differ from the prompt's test list:**
   - a casing-only rename answers **204**, not 200, because every HR PUT is 204;
   - re-creating a deleted department's name answers **200**, not 201, because department create has always been 200.

   The tests assert the real codes.
4. **Projects template instantiation hard-fails** for a department whose head was cleared by deactivating them, because `GetDepartmentHeadsAsync` now returns no head for it. That is correct (nobody can head it), but ops must assign a new head before instantiating. Recorded for the frontend and ops.
5. **`DepartmentErrors.DepartmentAlreadyDeleted` is unreachable.** A deleted department is filtered and answers 404 (A3). Left for Phase 5's dead-code pass.
6. **G4 (duplicate active names) can't fire on a database that has the old plain unique index.** It guards a drifted database only. The test simulates the drift by dropping the index.
7. **Inverted tests**, each by name, with the reason:
   - **`EmployeeTests.Update_ChangesOnlyProvidedFields`** → `Update_ReplacesEveryEditableField_AndANullClears` (N7, full replacement).
   - **`EmployeeTests.Update_OnInactiveEmployee_IsNoOp`** → `Update_OnInactiveEmployee_IsRefusedWithInactive` (N7, 409).
   - **`DepartmentFlowTests`, the remove test.** Its comment pinned "removing an already-inactive row is a no-op success" as intended behaviour. The comment is rewritten, and an assertion added that it is now `UserNotInDepartment` (K1).
   - **`EmployeeFlowTests`, `HrSmokeTests.UpdateEmployee_WithEmergencyContactNameInTheBody_…`:** these now send the required `contractType`. Their intent is unchanged.
   - **Access-guard test stubs** in CRM and Projects implement the two new `IHrDirectory` members by throwing; they are never called.
8. **The full PUT does not include the employee's name or `LoginActive`.** Both are Phase 3 (N7's second half), as the plan states.

## Phase 3 — Correctness & API gaps

### Setup

- **Branch:** `claude/awesome-einstein-mw5xmb`, restarted from `master` `b464459` after PR #4 merged. Phase 3 goes in a **new** PR.
- **Plan:** the approved plan "Phase 3: Correctness & API gaps, final", with every review item merged in (B1–B12, C1–C7, D1–D2).
- **Decision taken before coding (user):** a race on a unique index returns **the same error the check already returns**. There is no frontend status change:
  - an override grant or deny answers 409;
  - a duplicate department name and a duplicate employee email stay 400.

### 1. Item table

| Id | Status | Commit | Proving test |
|---|---|---|---|
| Aug 3.2 remove role permission | fixed | `0758c36` | `Phase3IdentityTests`: `Removing_a_held_permission_removes_it_and_bumps_the_holders`, `Removing_a_held_stale_code_removes_it`, `Removing_a_known_permission_the_role_does_not_hold_is_a_no_op_without_a_bump`, `Removing_an_unknown_code_the_role_does_not_hold_is_400`, `Removing_from_an_unknown_role_is_404`; `Phase3HttpTests.A_held_stale_code_is_removable_over_HTTP` |
| Aug 3.2 revoke override (audited) | fixed | `0758c36` | `Revoking_an_override_removes_the_row_and_bumps_the_target`, `Revoking_a_missing_override_is_404_and_for_a_missing_user_too`; `A_grant_a_revoke_and_a_role_claim_removal_each_leave_an_audit_row` |
| Aug 3.3 dedup | fixed | `0758c36` | `A_permission_held_through_two_roles_is_listed_once` |
| Aug 3.4 paging + literal ILIKE | fixed | `78e5b25` | `Employee_pages_are_stable_on_ties_and_a_huge_page_is_empty_not_a_500`, `Job_title_and_department_filters_match_wildcards_literally`, `Users_list_pages_searches_literally_and_needs_users_read`; unit `LikeAndPageWindowTests` |
| Aug 3.5 override race | fixed | `0758c36` | `A_grant_that_loses_the_race_to_the_unique_index_is_409` (deterministic); `Two_concurrent_grants_give_one_success_and_one_409_never_a_500` |
| Aug 3.5 employee-create race | fixed | `0758c36` | `Provisioning_that_loses_the_race_to_UserNameIndex_is_DuplicatedEmail` (deterministic); `Two_concurrent_hires_with_one_email_give_201_and_DuplicatedEmail` |
| Aug 3.5 department create/rename race | fixed | `78e5b25` | `DepartmentNameRaceTests.A_create_and_a_rename_that_lose_to_the_unique_index_are_DuplicatedName` (deterministic); `Two_concurrent_department_creates_with_one_name_give_200_and_400` |
| Aug 3.6 update validators | fixed | `78e5b25` | `Update_refuses_a_long_phone_long_notes_and_an_end_date_before_the_hire_date` |
| Aug 3.7 silent successes | fixed / recorded | `0758c36` | `Assigning_a_held_permission_succeeds_without_a_bump`; the remove no-op above; §6.6 |
| Aug 3.7 unknown code on assign | already-fixed | — | `Assigning_an_unknown_code_is_400` (the validator already checks the catalog) |
| Aug 3.9 resolve cache, missing-user log | fixed | `78e5b25` | `ResolveEmployeeIdAsync_does_not_remember_a_miss`, `GetProfilesAsync_warns_about_an_employee_with_no_login` |
| Aug 3.10 department delete permission | checked, unchanged | — | §6.8 |
| Aug 3.10 unused permissions | recorded | — | §6.9 |
| Aug 3.10 CQRS bypasses + members 404 | fixed | `78e5b25` | `Members_of_an_unknown_department_and_me_for_a_non_employee_are_ProblemDetails_404s` |
| N5 baseline claims behind a marker | fixed | `0758c36` | `BaselineClaimSeedingTests` (fresh, stays-removed, concurrent); `IdentitySchemaTests` (+`seed_markers`) |
| N6 `GET /api/users` | fixed | `0758c36` | `Users_list_pages_searches_literally_and_needs_users_read`, `Users_list_query_count_does_not_grow_with_the_page` |
| N6 `GET /api/roles/{id}/users` | fixed | `0758c36` | `Role_members_are_paged_and_an_unknown_role_is_404` |
| N6 self roles read | fixed | `0758c36` | `A_user_reads_their_own_roles_but_not_someone_elses_unless_they_hold_roles_read` |
| N6 any-of catalog | fixed | `0758c36` | `The_catalog_is_open_to_roles_read_or_permissions_manage`; Rule 12 |
| N7 name update + `LoginActive` | fixed | `0758c36`, `78e5b25` | `A_PUT_with_a_name_renames_the_login_and_LoginActive_follows_deactivation`, `A_PUT_with_only_one_name_part_is_400`; `UpdateNameAsync_trims_is_idempotent_and_refuses_bad_names`; unit `ApplicationUserNameTests` |
| N9 password policy | fixed | `0758c36`, `78e5b25` | `A_weak_password_is_WeakPassword_not_a_vague_provisioning_failure`; `IdentityPasswordPolicy_agrees_with_the_configured_IdentityOptions` (×9) |

**Red-before-green proofs.** Each fix was reverted on its own, its test run, then the file restored from a `cp` backup: see §3a.

**Commits:** `0758c36` (Identity) and `78e5b25` (HR). The new BuildingBlocks helpers (`Like`, `PageWindow`) landed with HR, so the Identity commit alone does not build; the pair does.

### What changed

**Identity**
- **`PermissionService.RemovePermissionFromRoleAsync`** follows the plan's step table:
  1. unknown role → 404;
  2. Phase 1A's full-access lock, unchanged;
  3. if the code is held, remove it through the change tracker (audited) and bump the holders, **even when the catalog no longer knows the code**;
  4. if the code isn't held and the catalog doesn't know it → 400 `InvalidPermission`;
  5. if the code isn't held and is known → 204 no-op, with no bump and no audit row.

  The remove validator never had a catalog rule, so a stale code reaches step 3 (C2).
- **Revoke** loads and removes the override through the change tracker. A missing target user → `users.NotFound`; a missing row stays 404.
- **Dedup:** the user and all-permissions lists return one row per code (`OnePerCode`).
- **Race mapping:**
  - `PermissionOverrideWrites` maps 23505 on `UserPermissionOverrideConfiguration.UniqueUserPermissionIndex`;
  - `UserProvisioning` maps 23505 on `UserNameIndex`, **and** ASP.NET Identity's own `DuplicateUserName` result to `users.DuplicatedEmail`. The concurrent test found this second window: when the winner has already committed, Identity's `UserValidator` refuses before any insert, and that used to become the vague `users.CreateFailed`.
- **N5:**
  - new `SeedMarker` entity and table `identity.seed_markers` (migration `SeedMarkers`);
  - new `BaselineClaimSeeder`, run after `RoleSeeder` and `RoleClaimSeeder`;
  - the `DefaultRoles` comment, which said `client` gets no automatic claims, is corrected.
- **N6:**
  - `HasAnyPermissionAttribute`, `RequireAnyPermission` and `AnyPermissionAuthorizationHandler` (registered in `Api/DI.cs`); `HasPermissionAttribute` is untouched;
  - new `UserEndpoints` (`GET /api/users`), `GET /api/roles/{roleId}/users`, and the shared `UserListing`;
  - `GetUserRolesQueryHandler` allows self or `roles.read`;
  - the catalog route takes any-of.
- **N7:**
  - `IUserAdministration.UpdateNameAsync`, through `ApplicationUser.UpdateName`, which trims and validates 1..100;
  - idempotent;
  - no stamp bump.
- **N9:**
  - `Identity.Contracts/IdentityPasswordPolicy` holds the rules;
  - `IdentityPolicy.Configure` reads it;
  - the `IdentityPolicy` comment, which claimed HR's and CRM's validators already shared the rule, is corrected.

**HR**
- **`BuildingBlocks/Persistence/Like`** (`Contains`, `EscapeCharacter`) and **`BuildingBlocks/Pagination/PageWindow`** (long skip) are used by both HR lists and the user list. The lists gain `ThenBy(Id)` and a count/page skew comment.
- **The employees list** puts profiles back in page order. `GetProfilesAsync` answers unordered, so the sort was lost after the query.
- **`DepartmentSave`** maps 23505 on `DepartmentConfiguration.UniqueActiveNameIndex` for create and rename.
- **Update employee:**
  - phone ≤ 50, emergency phone ≤ 50, notes ≤ 2000;
  - name both-or-neither, ≤ 100;
  - `Employee.Update` refuses `EndDate < HireDate`;
  - the name is applied through `UpdateNameAsync` after HR's save.
- **New queries:** `GetEmployeeDetailQuery`, `GetMyEmployeeDetailQuery` and `GetDepartmentMembersQuery`, which replace the endpoint compositions. `EmployeeDetailResponse` gains `LoginActive`.
- **`HrDirectory`:** a miss is no longer memoized, and a missing login is logged as a warning (ids only).
- **Create employee:** checks `IdentityPasswordPolicy` before provisioning → `employees.WeakPassword` (§6.2).

### 2. Run 0 for this phase

All items were open as recorded in Phase 0 §2.2. Two had moved since:
- the role lookup in remove-permission had already been added in 1A;
- assign already validated against the catalog.

Exploration on 2026-10-20 re-confirmed the rest by content.

### 3. Tests written but not run

None.

### 3a. Red-before-green proofs

Each row is one fix, reverted alone, with the file restored from a `cp` backup afterwards.

| Reverted | Went red |
|---|---|
| unknown code not held → plain success | `Removing_an_unknown_code_the_role_does_not_hold_is_400` |
| bump on a not-held removal | `Removing_a_known_permission_the_role_does_not_hold_is_a_no_op_without_a_bump` |
| catalog check **before** the held check (C2's ordering) | `A_held_stale_code_is_removable_over_HTTP` |
| role-claim removal back to `ExecuteDeleteAsync` | `A_grant_a_revoke_and_a_role_claim_removal_each_leave_an_audit_row` |
| revoke back to `ExecuteDeleteAsync` | the same audit test |
| `OnePerCode` removed | `A_permission_held_through_two_roles_is_listed_once` |
| override 23505 constraint name changed | `A_grant_that_loses_the_race_to_the_unique_index_is_409` (a `DbUpdateException`, i.e. the old 500) |
| `UserNameIndex` constraint name changed | `Provisioning_that_loses_the_race_to_UserNameIndex_is_DuplicatedEmail` |
| `DuplicateUserName` result mapping removed | `Two_concurrent_hires_with_one_email_give_201_and_DuplicatedEmail` |
| department 23505 constraint name changed | `DepartmentNameRaceTests` |
| the marker mechanism removed (no marker written) | `A_baseline_claim_an_admin_removed_stays_removed_on_the_next_start`. Removing only the marker **check** stayed green: the marker's primary key then refuses the second save, and the catch discards it, so the claim still isn't re-added. The property holds by two mechanisms; the proof removes both. |
| marker 23505 catch's key name changed | `Two_concurrent_seeds_apply_the_baseline_once` |
| catalog route back to `permissions.manage` only | `The_catalog_is_open_to_roles_read_or_permissions_manage` |
| self/`roles.read` check removed | `A_user_reads_their_own_roles_but_not_someone_elses_unless_they_hold_roles_read` |
| `Like.Contains` without escaping | `Job_title_and_department_filters_match_wildcards_literally`, `Users_list_pages_searches_literally_and_needs_users_read` |
| employees list back to `int` skip | `Employee_pages_are_stable_on_ties_and_a_huge_page_is_empty_not_a_500` |
| update phone rule removed | `Update_refuses_a_long_phone_long_notes_and_an_end_date_before_the_hire_date` |
| `EndDate < HireDate` check removed | the same test |
| `UpdateNameAsync` call removed | `A_PUT_with_a_name_renames_the_login_and_LoginActive_follows_deactivation` |
| `LoginActive` hard-coded true | the same test |
| handler password check removed | `A_weak_password_is_WeakPassword_not_a_vague_provisioning_failure` |
| members existence check removed | `Members_of_an_unknown_department_and_me_for_a_non_employee_are_ProblemDetails_404s` |
| a miss cached again | `ResolveEmployeeIdAsync_does_not_remember_a_miss` |
| missing-login warning removed | `GetProfilesAsync_warns_about_an_employee_with_no_login` |
| the symbol rule dropped from `IdentityPasswordPolicy` | `IdentityPasswordPolicy_agrees_with_the_configured_IdentityOptions("Aa11aaaa")` |
| `UpdateName` without trimming | `ApplicationUserNameTests` (×2) |

Not proved by revert: the users list's fixed query count (there is no older code to revert to; the test pins the shape), and the new Rule 12 catalog check (an arch rule, green on today's routes).

### 4. Migration guards added

None. `SeedMarkers` creates a new, empty table, so there is no existing data to violate it.

**Deploy effect (N5):** on the first start after this deploy there is no marker, so `employee` and `client` gain the baseline once. To see beforehand what will be added, run against a production snapshot:

```sql
SELECT r."Name" AS role, b.code
FROM (VALUES ('employee','employees.view_own'), ('employee','notifications.view_own'),
             ('client','crm.portal'), ('client','crm.briefs.view_own'), ('client','crm.meetings.view_own'),
             ('client','crm.meetings.request_own'), ('client','crm.meetings.respond_own')) AS b(role, code)
JOIN identity.roles r ON r."Name" = b.role
WHERE NOT EXISTS (SELECT 1 FROM identity.role_claims rc
                  WHERE rc."RoleId" = r."Id" AND rc."ClaimType" = 'permissions' AND rc."ClaimValue" = b.code);
```

The new claims reach each user's access token **at their next refresh** (C7). The role claims change in the database, but this seed bumps no security stamps, so nobody is signed out.

### 5. Frontend contract changes

| Route / behaviour | Before | After |
|---|---|---|
| `GET /api/users?page&pageSize&search` (`users.read`) | — | **new**: `PagedResult<UserListItem(id, email, firstName, lastName, userType, isActive, lastLoginAt, roles[])>`. Includes clients and the seeded admin; `pageSize` ≤ 200 (400 otherwise); search is literal over email and names |
| `GET /api/roles/{roleId}/users` (`roles.read`) | — | **new**, paged; an unknown role → 404 |
| `GET /api/roles/users/{userId}` | `roles.read` only | **self, or `roles.read`**; anyone else → 404 |
| `GET /api/permissions` (catalog) | `permissions.manage` | **`roles.read` or `permissions.manage`** |
| `DELETE /api/permissions/roles/{roleId}/{permission}` | 204 always, unaudited, bumped every holder | held → 204, audited, holders bumped; **not held → 204 no-op** (C1); unknown code not held → 400 `permissions.InvalidPermission`; unknown role → 404; a held code the catalog no longer knows is still removable |
| `DELETE /api/permissions/users/{id}/overrides/{permission}` | unaudited | audited; a missing user → 404 `users.NotFound` (was `PermissionOverrideNotFound`) |
| `GET /api/permissions/users/{id}` | a code held through two roles listed twice | once |
| `PUT /api/employees/{id}` | — | optional `firstName` + `lastName` (both or neither, 400 otherwise) rename the login; phone/emergency phone > 50 or notes > 2000 → 400; `endDate` before `hireDate` → 400 `employees.EndDateBeforeHireDate` |
| `GET /api/employees/{id}`, `/me` | — | new `loginActive` |
| `GET /api/employees/me`, no employee record | bare 404 | ProblemDetails 404 `employees.NotFound` |
| `GET /api/departments/{id}/members`, unknown or deleted department | `200 []` | 404 `departments.NotFound` |
| `POST /api/employees`, weak password | 400 `users.CreateFailed` (vague), or a field error for < 8 characters | 400 **`employees.WeakPassword`** for every policy failure |
| `POST /api/employees`, concurrent duplicate email | 500 or `users.CreateFailed` | 400 `users.DuplicatedEmail` |
| Override grant/deny, department create/rename, concurrent duplicates | 500 | the check's own error (409 / 400) |
| `GET /api/employees`, `/api/departments` filters | `%` and `_` were wildcards | literal |
| `GET /api/employees` | the page came back unordered | ordered by job title, then id |
| Any list, a huge `pageNumber` | 500 | 200, empty page with the real total |
| `employee` / `client` roles | no claims | baseline added once at the first start after deploy (§4) |

### 6. Blockers, questions and contradictions with the prompt

1. **No blockers.**
2. **N9 is checked in the handler, not the validator.** The pipeline's validation errors use the property name as their code (`ValidationDecorator.CreateValidationError`), so a validator cannot answer `employees.WeakPassword`. The create validator keeps `NotEmpty`; its `MinimumLength(8)` moved into the policy check, so every policy failure gets the one code.
3. **The users list issues a fixed number of statements per page.** The roles come from a subquery inside the page's projection, not from one query per user, so the count does not grow with the page size; `Users_list_query_count_does_not_grow_with_the_page` asserts that.
4. **B6:** a concurrent seed's 23505 on `seed_markers` is caught, followed by `ChangeTracker.Clear()`. Chosen over `pg_advisory_xact_lock`, because the marker row already is the lock. A module not registered at the first seed contributes nothing and is never re-tried: the marker is global.
5. **B1:** `HasAnyPermissionAttribute` is new, and `HasPermissionAttribute` is unchanged.
   - Rule 12 tier 2 now accepts either attribute.
   - Rule 12 also gained a check that **every permission a route names is in the catalog**. No such rule existed. It passed on all routes when added.
   - `/api/roles/users/{userId}` joins `AuthenticatedOnlyRoutes`, with its reason inline.
6. **Silent successes kept on purpose:**
   - assign of a held permission (idempotent PUT; it no longer bumps);
   - `UserAdministration` deactivate/reactivate no-ops (outbox retries);
   - `UserLifecycleMaintenance` returning success for a missing user (the reaper's retry).
7. **B3:** `EmailIndex` is **not unique** (model snapshot: `HasIndex("NormalizedEmail")` without `IsUnique`). Only `UserNameIndex` is mapped. HR's `IX_employees_AppUserId` can't collide, because the id was just provisioned, so it is not mapped.
8. **Aug 3.10, delete permission:** `DELETE /api/departments/{id}` keeps `departments.delete`. It is a soft delete, but its name becomes reusable and no route restores it, so "delete" is what it does.
9. **Unused permissions, for the owner:** `departments.set_active` and `employees.delete` gate no route. They stay in the catalog.
10. **Out of scope, recorded:**
    - other modules still compute `(page - 1) * size` in `int`: Projects `ProjectHandlers`, CRM `GetCrmMeetingsByProjectQueryHandler`, Approvals `GetMyPendingApprovalsQueryHandler`, Notifications `NotificationHandlers` and `AuditAndActivityHandlers`, Sites `GetSitesQueryHandler`;
    - CRM's `CreateClientCommandHandler` has the same weak-password gap N9 closed for HR;
    - the employees list's enrichment stays in the endpoint (only the order fix).
11. **N7 window:** the name write is synchronous and cross-module, as N7 decided. If Identity refuses, the command's transaction rolls HR back. If Identity commits and HR's commit then fails, the name is ahead of HR; the PUT is idempotent, so a retry repairs it.
12. **B7:** the baseline seed inverts **no** existing test. The full suite passed with it before any test was touched; only `IdentitySchemaTests`' table list changed (+`seed_markers`).
13. **Tests, a scope per command:** a save that fails on a unique index leaves its entity tracked by that scope's context. In the host, a request is one scope and one command. The department race test therefore uses a fresh scope per command, and the notes record it so nobody "fixes" it by reusing a scope.
14. **`/me` with no user id** answers 401 `employees.Unauthenticated`. It is unreachable behind the route's permission and is kept only so the handler is total.

## Phase 4 — Performance

### Setup

- **Branch:** `claude/awesome-einstein-mw5xmb`. PR #5 (Phase 3) was still open when coding started, so the `Phase 4:` commits are **stacked on it** and join #5. Its body lists the range (the A12 fallback).
- **Plan:** the approved "Phase 4, final", with review items P1–P9 and the "Also" line merged in.
- **Decision (user):** keep the refresh token as an owned collection and bound it with a cap and a prune job, rather than making the token a standalone entity.

### 1. Item table

| Id | Status | Commit | Proving test |
|---|---|---|---|
| 4.1 cap on active refresh tokens | fixed | `fde9714` | unit `ApplicationUserRefreshTokenCapTests` (15 logins → 10 active; rotation keeps the count; a cap-revoked token is reuse; no cap → nothing revoked) |
| 4.1 prune job (P1, P4) | fixed | `fde9714` | `Phase4HttpTests.Prune_deletes_old_expired_and_old_revoked_rows_and_keeps_everything_still_usable`, `Prune_works_in_batches_and_stops_at_the_per_sweep_cap`, `Fifty_rotations_then_a_prune_leave_the_account_within_the_cap` |
| 4.1 invariant (P8) | fixed | `fde9714` | `A_user_loaded_before_a_prune_still_rotates_and_saves_after_it` |
| 4.1 indexes (P5) | fixed | `fde9714` | probe migration empty (§2) |
| 4.1 login projections | fixed | `fde9714` | `Regression_guard_login_statement_count_does_not_depend_on_the_token_count` (labelled guard, P3) |
| P2 session check is a projection | already-fixed (verified) | — | `The_session_check_is_one_statement_and_never_reads_refresh_tokens` |
| 4.2 `ResolveAsync` one statement | fixed | `fde9714` | `Phase4PermissionQueryTests.ResolveAsync_is_one_statement`; semantics `ResolveAsync_semantics_role_minus_live_deny_plus_grant_and_expired_overrides_ignored`, `ResolveAsync_for_an_unknown_user_is_empty` |
| 4.2 `HasOtherActiveHolderAsync` one statement | fixed | `fde9714` | `HasOtherActiveHolderAsync_is_one_statement_however_many_candidates`, `A_holder_only_through_an_expired_grant_is_not_a_holder`, `A_role_holder_with_a_live_deny_is_not_a_holder`, `A_live_grant_without_a_role_is_a_holder_and_an_inactive_one_is_not`, `The_excluded_user_does_not_count_for_themselves` |
| 4.3 employee list reads the table once | fixed | `fde9714` | `The_employee_list_is_three_statements_two_on_hr_employees_whatever_the_page_size` |
| 4.4 `GetUserRolesAsync` one statement | fixed | `fde9714` | `A_users_roles_are_one_statement` |
| 4.4 `AsNoTracking` on role, role-permission and role-detail reads | fixed | `fde9714` | — (no behavioural change; the full suite is unchanged) |

**Statement counts, before → after:**

| Path | Before | After |
|---|---|---|
| `ResolveAsync` | 4 (user + its tokens, role names, role claims, overrides) | **1** (`UNION ALL`) |
| `HasOtherActiveHolderAsync` | 2 + about 5 per candidate | **1** (`EXISTS`) |
| `GET /api/employees` | 4 (count, full rows, re-read for profiles, Identity) | **3** (count, projected page, Identity) |
| `GetUserRolesAsync` | 3 | **1** |
| Login, in the command's own scope | the user loaded, then loaded again inside `ResolveAsync` | the user loaded once; permissions are one statement |

### What changed

- **`ApplicationUser.IssueRefreshToken(…, maxActive)`** revokes the oldest active tokens beyond the cap. Login passes `Identity:RefreshTokens:MaxActivePerUser` (default 10).
  - Rotation is not given the cap: it revokes one token for every one it issues, so it can never cross it. This is a small deviation from the plan's wording ("passes the cap too") with the same effect.
- **`RefreshTokenOptions`** (`Identity:RefreshTokens`) and **`RefreshTokenPruneJob`** are registered in `IdentityModule`.
  - The job reads its settings per run (`IOptionsSnapshot`; it is scoped), like the reaper.
  - **P4:** the token is an owned type, and an owned type can't be the root of `ExecuteDeleteAsync`. So each batch is one plain-SQL `DELETE FROM identity.refresh_tokens WHERE id IN (SELECT id … LIMIT n)` on Identity's own table. Its shape is checked in captured SQL.
- **Indexes (P5):** on `ExpiresOn` and `RevokedOn`, in the owned-type mapping. Migration `RefreshTokenPruneIndexes`.
- **The invariant (P8)** is commented on `IssueRefreshToken` and on the job.
- **`IPeriodicJob`'s doc** said a job with any side effect beyond notifying didn't belong there; it now names the two housekeeping jobs.
- **`EffectivePermissionService`:**
  - one `UNION ALL` for resolution; one `EXISTS` for the last-holder check;
  - a new `ResolveAsync(ApplicationUser)` overload, used by `JwtProvider`;
  - no `UserManager` dependency any more;
  - its doc keeps the ported-unchanged text and the two known gaps (Phase 5), and adds the Phase 4 paragraph.
- **`GetEmployeeProfilesQuery`** serves `GET /api/employees`.
  - **P6:** `EmployeeProfiles.ToSummary(db)`, the single HR projection, and `EmployeeProfiles.Zip`, the join with Identity's names plus Phase 3's missing-login warning, live in `Hr.Infrastructure/Services/EmployeeProfiles.cs`.
  - Both `HrDirectory` and the Hr.Application handler use it. `Hr.Infrastructure.csproj` already grants `InternalsVisibleTo` to `Hr.Application` (verified, line 26), so no reference was added.
- **Role reads:**
  - `RoleService.GetUserRolesAsync` is one untracked query;
  - `GetAllRolesAsync`, `GetRolePermissionsAsync` and `GetRoleQuery`'s read are `AsNoTracking`;
  - `RoleService.GetByIdAsync` stays tracked, because commands use it.
- **`PeriodicJobTests`:** its exact job list gains `RefreshTokenPruneJob` (an addition), and its comment's count is corrected.
- **The `EmployeeDetailResponse` doc,** which still said "composed at the endpoint" (stale since Phase 3), is corrected.

### 2. Run 0 for this phase

The exploration on 2026-10-20 confirmed every item as described in the plan's context. **P2 was already true:** `UserSessionState` projects `(Id, SecurityStamp, IsActive)` with `AsNoTracking` and a query tag, and never calls `FindByIdAsync`. The new test pins it.

The probe migration after `RefreshTokenPruneIndexes` came out empty. The snapshot's only change is the two indexes.

### 3. Tests written but not run

None.

### 3a. Red-before-green proofs

| Reverted | Went red |
|---|---|
| the cap never applied | `Fifteen_logins_leave_ten_active_and_revoke_the_five_oldest` |
| the `RevokedOn` condition removed from the prune | `Prune_deletes_old_…`, `Fifty_rotations_then_a_prune_…` |
| the `ExpiresOn` condition removed from the prune | `Prune_deletes_old_…` |
| the batch loop stops after one batch | `Prune_works_in_batches_and_stops_at_the_per_sweep_cap` |
| the per-sweep cap removed | the same test |
| an extra statement added to `ResolveAsync` | `ResolveAsync_is_one_statement` (the pre-Phase-4 body can't be swapped back in, because the old constructor took `UserManager`) |
| the live-deny clause disabled in `HasOtherActiveHolderAsync` | `A_role_holder_with_a_live_deny_is_not_a_holder` |
| `LiveAt` dropped from the holder check | `A_holder_only_through_an_expired_grant_is_not_a_holder` |
| `GetUserRolesAsync`'s three-statement body restored | `A_users_roles_are_one_statement` |
| `EmployeeEndpoints.cs` restored to its pre-Phase-4 version | `The_employee_list_is_three_statements_two_on_hr_employees_whatever_the_page_size` |

**Not proved by revert:**
- the session check: P2 was already true and there is nothing to revert;
- the P8 invariant test: a guard on a property of the design, not of one line;
- the login statement check: a labelled regression guard (P3);
- the `AsNoTracking` reads: no observable behaviour.

### 4. Migration guards added

None. `RefreshTokenPruneIndexes` only adds indexes.

**Ops, before deploying (P4).** Size the backlog the first sweeps will work off:

```sql
SELECT count(*) FILTER (WHERE "ExpiresOn" < now() - interval '1 day') AS expired,
       count(*) FILTER (WHERE "RevokedOn" < now() - interval '24 hours') AS revoked_past_window
FROM identity.refresh_tokens;
```

The worker sweeps every `Notifications:SweepMinutes` (default 15). Each sweep deletes at most `PruneBatchSize × PruneMaxBatchesPerSweep` = 1000 × 10 = **10,000 rows**, so about **40,000 rows an hour**. A backlog of N rows clears in about N / 40,000 hours, with each statement bounded at 1000 rows.

**Per-user row bound (P1).**
- At most **10 active** rows.
- Plus the rows revoked within the last **24 h**: one per rotation, plus any the cap or a logout revoked.
- With 60-minute access tokens, a client refreshing continuously rotates about 24 times a day per session. That is about **10 + 24 × sessions** rows per user: about 34 for one busy session, about 250 for ten.
- Before this phase the count was unbounded, because nothing was ever deleted.

**The P1 trade-off.** A rotated-away token presented more than 24 h after its rotation is still **rejected** (`authentications.InvalidRefreshToken`). It no longer revokes the whole family, because the row that proved it was rotated has been pruned. Inside the window, reuse detection is exactly as before.

### 5. Frontend contract changes

| Behaviour | Before | After |
|---|---|---|
| An 11th concurrent session for one account | allowed; unlimited sessions | **the oldest session's refresh ends**: its next `POST /api/auth/refresh` answers 401 `authentications.InvalidRefreshToken`, and that device must sign in again |
| Replaying a rotated refresh token more than 24 h later | 401 `RefreshTokenReused`, and the family revoked | 401 `InvalidRefreshToken`; the family is not revoked |
| `GET /api/employees` | — | same response shape and order; one fewer statement |

**P9, for the owner:** copy the 11th-session row above into the **frontend 006b-1 notes**, so QA expects a device to be signed out when a user opens an 11th session. Those notes live in `../docs`, outside this repo, so they are not edited here.

### 6. Blockers, questions and contradictions with the prompt

1. **No blockers.**
2. **P4:** the batched delete is plain SQL, not `ExecuteDeleteAsync`, because EF can't use an owned type as a delete root. It touches one table in Identity's own schema, and it is not a stored procedure. Housekeeping on unusable rows, so it is not audited.
3. **P7, remaining callers:**
   - `GetEmployeesQuery` (and its handler and validator) is now used **only by a test**: `EmployeeFlowTests`. Marked for Phase 5's dead-code pass and left in place.
   - `GetEmployeeQuery` is also test-only (`EmployeeFlowTests`), so it is marked too.
   - `EmployeeResponse` is still used in production: `GetEmployeeDetailQueryHandler` builds the detail from it through `ResponseMapping`. It stays.
4. **Semantics are unchanged.** The resolver and the last-holder check still ignore the override's own `IsActive` flag and the user's `IsActive` in `ResolveAsync`. Both are Phase 5 items. The last-holder check weighs only active users, as before.
5. **Left as is, recorded:**
   - the detail route's extra `IUserDirectory.GetAsync` for `LoginActive`, because `EmployeeProfile` doesn't carry the login state and adding it would change a cross-module contract;
   - the duplicate override read in `GetUserEffectivePermissionsQuery`.
6. **Login still loads the user twice:** once in `PasswordSignIn`'s own scope, by design (K4: the lockout bookkeeping commits outside the command), and once in the handler. The owned tokens come with each load, which is why the cap and the prune, not projections, are what bound the cost.

## Phase 5 — Hygiene

### Setup

- **Branch:** `claude/awesome-einstein-mw5xmb`, fast-forwarded to `master` after PR #5 merged (H2: a new PR, nothing stacked).
- **Plan:** the approved "Phase 5, revised" (H1–H17), with the review comments R1–R20 resolved against the code before coding.
- **Decisions (user):** create the two missing test projects; `AnalysisMode=Recommended`; fix stale code comments in place and keep the rest of 5.6 as proposals here; the IL rule is IL type usage in `*.Application`.
- **Analyzers (5.6) are not in this PR.** They go in their own PR after this one merges (H3), with the per-rule count table in that PR's description.

### First item, owner action before production (R19)

**The committed `JwtSettings:Key` in `src/Api/appsettings.json` (location only; value not copied) defeats the fail-fast.** Phase 1B's check in `Api/DI.cs` (lines 36–41) only fires when the key is *unset*. With a value committed, any environment that doesn't override it runs with a key everyone with repo access has, and anyone holding it can mint a token with any permission claim. Remove the value from the committed file (keep a development value in user-secrets or `appsettings.Development.json`, which Production doesn't load) and rotate the key everywhere it was used. Not changed in code here, as the plan says.

### 1. Item table

| Id | Status | Commit | Proving test |
|---|---|---|---|
| 5.1 backoff between attempts (H1) | fixed | `904b2f5` | `OutboxDeadLetterTests.A_failing_message_backs_off_doubling_then_is_dead_lettered_at_max_attempts_with_one_error_log` (asserts the 14 waits 30…3600 s, total 29 010 s, and no pick-up a second early) |
| 5.1 dead letter at `MaxAttempts`, one Error log | fixed | `904b2f5` | the same test (1 Error, 14 Warnings; never selected again after 30 more days) |
| 5.1 poison payload dead-lettered at once; the sweep goes on (H4) | fixed | `904b2f5` | `A_payload_that_does_not_fit_its_type_is_dead_lettered_at_once_and_the_sweep_goes_on` |
| 5.1 unknown type retried with backoff (H4) | fixed | `904b2f5` | `An_unknown_type_is_retried_with_backoff_not_dead_lettered` |
| 5.1 recovery SQL works (H17) | fixed | `904b2f5` | `A_dead_letter_reset_with_the_recovery_sql_is_delivered_on_the_next_sweep` |
| 5.1 migration backfill order (R17) | fixed | `904b2f5` | `The_migration_backfills_pending_rows_from_occurred_utc_and_leaves_none_null` |
| 5.1 sweep query uses the index (H16) | fixed | `904b2f5` | `The_sweep_query_can_use_the_unprocessed_index` (EXPLAIN of the captured SQL) |
| 5.1 existing retry-then-succeed | still passes, adapted | `904b2f5` | `OutboxAndInboxTests.HandlerThrows_RecordsAttemptAndError_RetrySucceeds` (now also asserts no retry before the backoff) |
| 5.2 explicit deny-wins | fixed | `772273d` | `PermissionResolutionTests` (grant + deny → absent; deny before grant → absent) |
| 5.2 one live rule at four sites (H6) | fixed | `772273d` | `PermissionResolutionTests` (inactive deny / grant ignored, query and in-memory forms agree); `InactiveOverrideTests` (resolver, last-holder, `/effective`, renew without 409) |
| 5.3 Rules 13–18 | added | `f7a6c34` | the rules themselves; each proved red (§3a) |
| 5.3 `Approvals.Infrastructure.Tests` | added | `f7a6c34` | migrates on a fresh database; every handler the module registers resolves |
| 5.3 `Notifications.Application.Tests` | added | `f7a6c34` | inbox query round-trip: the recipient's two, newest first, not the other user's |
| 5.4 `ModuleDependencyOrder` comment | fixed | `58a415f` | — (comment) |
| 5.5 dead code | deleted | `4115a92` | build and full suite green; grep finds only historical comments |
| 5.5 `StaleToken` constant (H11/R10) | fixed | `4115a92` | `AuthSessionTests` (asserts the code string in the 401 body), unchanged |
| 5.6 analyzers | separate PR (H3) | — | — |

### What changed

**Outbox (5.1).**
- **States:** `pending → processed | (failed → waiting(backoff) → pending)* → dead-lettered`. In the `OutboxProcessor` doc comment, with the ordering note: a message waiting out its backoff or dead-lettered no longer holds back later messages for the same aggregate. That is safe because the handlers check current state (HR's since Phase 2). A future handler that needs strict order must do the same.
- **Settings:** `Outbox:MaxAttempts` (default **15**), `Outbox:BackoffBaseSeconds` (30), `Outbox:BackoffMaxMinutes` (60). Delay = `min(base × 2^(n−1), max)`.
- **Retry span (H15/R15):** the 14 waits are 30, 60, 120, 240, 480, 960, 1920 s, then 3600 s × 7 = **29 010 s, about 8 h**. That covers an overnight outage. With 10 attempts it would be 11 010 s (about 3 h). **15 is the default chosen.**
- **Columns:** `next_attempt_utc` (`NOT NULL DEFAULT now()`, set from `occurred_utc` at insert) and `dead_lettered_utc`. `ix_outbox_messages_unprocessed` is now on `(next_attempt_utc, occurred_utc)` with filter `processed_utc IS NULL AND dead_lettered_utc IS NULL`. The sweep keeps `ORDER BY occurred_utc`; the index serves the filter (R2).
- **Migration `OutboxDeadLetter` (R17):** add the column with its default (Postgres fills every existing row with the migration time), then `UPDATE … SET next_attempt_utc = occurred_utc WHERE processed_utc IS NULL`. No guard needed; the `Down` drops the columns and restores the old index.
- **Clock:** `OutboxProcessor` takes a `TimeProvider`. `AddOutbox` TryAdds `TimeProvider.System`, like module registration.
- **Two existing tests adapted, not weakened.** `HandlerThrows_…RetrySucceeds` now advances a manual clock past the 30 s step, and also asserts the message is *not* retried before it. `HireActivationRetryTests` sets `Outbox:BackoffBaseSeconds = 0`, because it is about the retry happening, not when.

**Recovery (H5, H17):** fix the cause first, then reset all three fields, or the next failure dead-letters the message again at once:

```sql
UPDATE shared.outbox_messages
SET dead_lettered_utc = NULL, attempts = 0, next_attempt_utc = now()   -- not NULL: `<= now` never matches NULL
WHERE id = '<id>';
```

**Deploy notes.**
- **Rows already at or over `MaxAttempts` (H5)** are dead-lettered on their *next* failure after deploy. List them first with the pre-deploy SQL in §4 and decide on each.
- **Overlapping deploy (R18).** An old instance still running selects on `processed_utc IS NULL` alone. It ignores `dead_lettered_utc` and `next_attempt_utc`, so it retries dead-lettered and backing-off rows on every sweep. That is harmless only because the inbox makes handlers idempotent. Keep the overlap short. Its own inserts get `next_attempt_utc = now()` from the column default.

**Monitoring (H14).** The Error log alone is easy to miss. Proposed: alert when this count is above 0.

```sql
SELECT count(*), min(dead_lettered_utc)
FROM shared.outbox_messages
WHERE dead_lettered_utc IS NOT NULL;
```

**Override resolution (5.2).**
- `UserPermissionOverride.LiveAt(now)` now requires `IsActive` too. It is the one query predicate, used by the resolver, `HasOtherActiveHolderAsync` and the `/effective` listing. The plan called it `IsLive`; the existing name was kept, so there is still one predicate, extended.
- `UserPermissionOverride.IsLive(isActive, expiresAt, now)` is the same rule over plain values, for rows already in memory (the grant/deny existence check) and for the resolver. A unit truth table pins it to `LiveAt` on every edge (active or not; no expiry, past, now, future).
- The expiry boundary is unchanged: live while `ExpiresAt >= now` (Phase 2's `>=`; the plan's `>` would have moved it by one tick).
- **`PermissionResolution.Resolve(roleCodes, overrides, now)`** (Identity.Domain): roles' union, then every live grant added, then every live deny removed. The old loop applied overrides in row order, so a grant read after a deny would have won if the unique index ever allowed both.
- **Renew** reactivates the row (`SetActive(true)`), so a grant over an inactive override renews it in place instead of hitting the unique index (409).
- **The resolver's "two known gaps" comment is extended:** gap 1 is closed (nothing sets `IsActive` false yet; revoke is a hard delete, so the check is for a future soft revoke); gap 2 (the user's own `IsActive` in `ResolveAsync`) stays covered by the security stamp at the API.
- `UserPermissionOverride.IsExpired` now has no code caller (only doc references). It was not on the 5.5 list, so it stays.

**Architecture rules (5.3).** H7: every rule was run against the code as it stood before any probe and found no existing violation. No allow-list entry was added beyond the one the plan names.

| Rule | Checks | Floor |
|---|---|---|
| 13 `Rule13ApplicationAspNetIlTests` | no `Microsoft.AspNetCore*` type in any `*.Application` assembly's IL type references (signatures, bodies, locals, generic arguments). Allowed: `Microsoft.AspNetCore.Identity`, in `Identity.Application` only (Rule 2's reason). Rule 2 unchanged. | 9 assemblies |
| 14 `Rule14LayerDirectionTests` | `X.Infrastructure` never references `X.Application`; no project references another module's Application or Infrastructure. Application → own Infrastructure is **permitted, not required** (R4: all nine have it, but F2 isn't in this checkout and CLAUDE.md doesn't make it mandatory). Reads `ProjectReference` only, never `InternalsVisibleTo`. | 9 Infrastructure projects |
| 15 `Rule15ContractsIsolationTests` | every `X.Contracts` references only SharedKernel and BuildingBlocks. Identity's and Notifications' own isolation tests stay. | 9 |
| 16 `Rule16IntegrationEventPublisherTests` | every integration event is constructed (IL `newobj`) in a `src` assembly other than its own Contracts. Test assemblies are never read (R20). | 29 events |
| 17 `Rule17ModuleListTests` | `Assembly.Load("X.Infrastructure")` names in `Api/Program.cs` equal the module folders in the slnx. | 9 |
| 18 `Rule18ModuleTestProjectsTests` | every module has `UnitTests`, `Application.Tests` and `Infrastructure.Tests` in the slnx (R8: added after the two new projects). | 27 |

Shared helpers: `ProjectFiles` (slnx and csproj reads) and `IlAssemblies` (Mono.Cecil, already present through NetArchTest). `ModuleAssemblies`' stale "Empty today" comment is fixed.

**New test projects.** `Approvals.Infrastructure.Tests` (2 tests) and `Notifications.Application.Tests` (1 test), in the existing per-module csproj shape, each with its own Testcontainers Postgres. **CI time added:** about 13 s and 11 s of wall time locally (10 s and 9 s of test duration), so roughly 25 s more, mostly container start.

**Docs and tooling (5.4).** The `ModuleDependencyOrder` comment now says what is true: only the test fixtures read `SchemaOrder` (`ApiFixture`, Rules 9 and 12), the Api host doesn't migrate, and the list holds all ten schemas. It points at `docs/cross-schema-foreign-keys.md` and `docs/adding-a-module.md` instead of the deleted PLAN/PROGRESS files. Other code comments still cite those files (31 lines across `src`); they were left alone as outside the item.

**Proposals (notes only, not done):**
- **README:** `README.md` was deleted (`0c17505`), but CLAUDE.md says it stays in the repo. Either restore a short README or change CLAUDE.md's line.
- **CLAUDE.md's outbox text** should mention the dead-letter state, `Outbox:MaxAttempts` and the two backoff settings, and the recovery SQL above.
- **Should the host migrate in `SchemaOrder`?** Today a deployment must apply migrations itself, in that order, and nothing in the repo does it outside the tests.
- **Connection string:** `Api/DI.cs:209` and `ModuleDbContextRegistration.cs:45` pass `GetConnectionString("DefaultConnection")` unchecked, and a localhost string is committed in `appsettings.json`. Propose a fail-fast like the JWT key's, and the same "not in the committed file" treatment.
- **JWT key (H10):** see the first item above. The fail-fast exists; the committed value makes it moot.

**Dead code (5.5).** Each item was confirmed by grep to have no `src` caller before it was deleted:
- **Errors:** `DepartmentErrors.DepartmentAlreadyDeleted`, `DepartmentErrors.NoEmployeesToMove`; `AuthErrors.UserNotActive`, `InvalidCode`, `SendResetPasswordError`, `ResetPasswordError`, and `AuthErrors.StaleToken` (R10: it had no use; the live code string was `SessionClaims.StaleTokenCode`); `PermissionErrors.NotFoundForTarget`; `RoleErrors.Unauthorized`, `RoleErrors.UserNotInRole`.
- **Services:** `IPermissionService.GetAllPermissionsAsync`; `IRoleService.UpdateRoleAsync`, `UserInRoleAsync`, `UserIsAdminAsync`.
- **Domain:** `ApplicationRole.Activate`/`Deactivate`; `Department.Activate`.
- **Test-only queries:** `GetEmployeesQuery` and `GetEmployeeQuery`, their handlers, and `GetEmployeesQueryValidator`. `ResponseMapping` moves to its own file (`Features/Employees/Get/ResponseMapping.cs`); `EmployeeResponse` stays.
- **`StaleToken` (H11/R10):** `Identity.Contracts.AuthErrorCodes.StaleToken` is now the one place the string lives; `SessionClaims.StaleTokenCode` reads it. Value unchanged.
- **Comments fixed** where they named a deleted symbol as current: `DeleteDepartmentCommandHandler`, `DepartmentErrors` (D7 note), `RoleService`, `IPermissionService`, `GetAllPermissionsQuery`, `UpdateRoleCommand`, `EmployeeContracts`, `GetEmployeeProfilesQueryHandler`, and one test comment. Comments that say what a code *used to* answer stay as history.
- **Tests adapted:** `EmployeeFlowTests.Create_then_get_then_update_then_list_roundtrips` now uses `GetEmployeeDetailQuery` and `GetEmployeeProfilesQuery`. `DepartmentTests.Deactivate_ThenActivate_RestoresIsActive` became `Deactivate_ClearsIsActive`. No test was removed.
- **Rule 11 (H12):** its runtime count was **183** before and **182** after (one validator deleted). The floor of 180 is unchanged. (A grep of `AbstractValidator<` finds 184; the rule's own count is the one recorded.)
- **Kept, recorded:** `PermissionErrors.InvalidPermission` (live since Phase 3), `employees.Unauthenticated`, and the defensive re-hire check.

### 2. Run 0 for this phase

The read-only checks (R1–R16) were done on 2026-10-20 before coding, and every finding held when coding: the creation column is `occurred_utc`; nothing outside the plan referenced the dead items; all 29 events have a publisher; the Contracts and layer-direction rules passed as is.

**Probe migrations** after `OutboxDeadLetter`: empty for `SharedDbContext` and for all nine module contexts. The module snapshots would only pick up the shared tables they exclude from migrations (`ownsMigration: false`); as in earlier phases, those snapshot edits were discarded with the probes.

### 3. Tests written but not run

None.

### 3a. Red-before-green proofs

Each fix was reverted from a `cp` backup, the named test run, then restored.

| # | Reverted | Went red |
|---|---|---|
| O1 | the backoff returns zero | `…backs_off_doubling…`; `An_unknown_type_is_retried_with_backoff_not_dead_lettered` |
| O2 | never dead-letter at `MaxAttempts` | `…backs_off_doubling…`; `A_dead_letter_reset_with_the_recovery_sql…` |
| O3 | a bad payload treated as an ordinary failure | `A_payload_that_does_not_fit_its_type…` |
| O4 | an unknown type dead-lettered at once | `An_unknown_type_is_retried_with_backoff…` |
| O5 | deserialization back outside the guard (throws out of the sweep) | `A_payload_that_does_not_fit_its_type…sweep_goes_on` |
| O6 | the backfill keyed on `next_attempt_utc IS NULL` | `The_migration_backfills_pending_rows_from_occurred_utc…` |
| O7 | the selection drops the due-check | `…backs_off_doubling…`; `The_sweep_query_can_use_the_unprocessed_index` |
| R1 | the resolver applies overrides in row order | `A_deny_before_a_grant_still_leaves_it_out` |
| R2 | the resolver skips the live filter | `An_inactive_grant_is_ignored`, `An_inactive_deny_is_ignored`, `An_expired_override_is_ignored_either_way` |
| R3 | `LiveAt` without `IsActive` | `An_inactive_grant_confers_nothing_and_does_not_count_as_another_holder`, `Effective_permissions_leave_an_inactive_override_out` |
| R4 | the existence check on `IsExpired` only | `A_grant_over_an_inactive_override_renews_it_and_reactivates_it` (409) |
| R5 | `Renew` leaves the row inactive | the same test |
| A13 | the allow-list removed; separately, a body-only `typeof(HttpContext)` in `Hr.Application` | Rule 13 both times. **Rule 2 stayed green on the body-only probe**, which is the gap Rule 13 closes. |
| A14 | `Hr.Infrastructure → Hr.Application`; separately `Hr.Application → Crm.Infrastructure` | Rule 14 both times |
| A15 | `Sites.Contracts → Sites.Domain` | Rule 15 |
| A16 | an `IIntegrationEvent` in `Hr.Contracts` that nothing constructs | Rule 16 |
| A17 | an extra `Assembly.Load("Probe.Infrastructure")` in `Program.cs` | Rule 17 |
| A18 | `Approvals.Infrastructure.Tests` removed from the slnx | Rule 18 |

**Not proved by revert:** the `ModuleDependencyOrder` comment (no behaviour) and the dead-code deletions (the proof is the green build and suite, and the grep).

### 4. Migration guards added

None. `OutboxDeadLetter` adds two columns (one nullable, one with a default), backfills idempotently, and rebuilds one index.

**Pre-deploy SQL:** list what is pending and how often it has failed, so the rows already at or over `MaxAttempts` can be decided on before they dead-letter (H5):

```sql
SELECT id, type, attempts, error
FROM shared.outbox_messages
WHERE processed_utc IS NULL
ORDER BY attempts DESC;
```

### 5. Frontend contract changes

**None.** The `authentications.StaleToken` code string is unchanged (H11); only where it is defined moved. No endpoint, status code or response shape changed.

### 6. Blockers, questions and contradictions with the prompt

1. **No blockers.**
2. **Owner, before production:** the JWT key (first item above).
3. **Small deviations from the plan's wording, same intent:**
   - the live predicate keeps its Phase 2 name `LiveAt` (one predicate, extended) and its `>=` expiry boundary; `IsLive` is its in-memory twin, pinned by a test;
   - `PermissionResolution.Resolve` also takes `now`, because "considers only live overrides" needs a time;
   - the two existing retry tests were adapted to the backoff (see "What changed"), rather than passing untouched, which they can't once a failure waits 30 s.
4. **A proof-script mistake, caught and fixed.** One revert-and-restore left the restored file older than the built DLL, so a full-suite run picked up the stale build and failed three outbox tests. The script now touches every restored file; a clean rebuild and every later run were green. Nothing committed was affected.
5. **Proposals carried in "What changed":** README, CLAUDE.md's outbox text, host migration in `SchemaOrder`, the connection-string fail-fast.

## Phase 5 follow-up

### Setup

- **Branch:** `claude/phase5-followup`, from `master` @ `d3a06e5` (PR #7 merged). One new PR to `master`.
- **Input:** the Phase 5 implementation review (`reviews/PHASE-5-IMPLEMENTATION-REVIEW.md`, in the docs folder outside this repo), F1–F5. Every file and line it names was re-checked against `master` before coding and matched; the three clarifications are in §6.
- **Environment:** a cloud container. The .NET SDK pinned by `global.json` was installed into `$HOME/.dotnet`, `dockerd` was started for Testcontainers (`postgres:17`), and `dotnet-ef` 10.0.13 was installed for the migration probe.
- **Scope:** F1–F3 in code, F4 and F5 recorded here. Nothing else.

### First item, owner action before production (F5)

**Both secrets are still committed, and the seed password reaches Production.** Locations only; no value is copied here.

- `src/Api/appsettings.json` commits a non-empty `JwtSettings:Key` (line 16), the R19 item from Phase 5, still open.
- The same file commits a non-empty top-level `DMS_SEED_ADMIN_PASSWORD` (line 43).
- `DMS_SEED_ADMIN_PASSWORD` is read at `src/Modules/Identity/Identity.Infrastructure/Persistence/Seeders/AdminUserSeeder.cs:40`, called from `IdentityModule.SeedAsync` (`src/Modules/Identity/Identity.Infrastructure/IdentityModule.cs:99`).
- **It runs in Production.** `src/Api/Program.cs:94-100` runs every module's `SeedAsync` on every startup with no environment check. `WebApplication.CreateBuilder` loads `appsettings.json` in every environment; user secrets load only in Development. Only an environment variable, a command-line argument or an `appsettings.Production.json` overrides it, and none is in the repo. So on a database without the seed admin, the first boot creates the admin with the committed password. The seeder's fail-fast (`AdminUserSeeder.cs:41-50`) fires only when the key is unset, so the committed value defeats it, the same way the committed JWT key defeats the Phase 1B check.
- **Action:**
  - remove both values from the committed file and keep development values in user-secrets or `appsettings.Development.json`, with real values in environment variables;
  - rotate the JWT key everywhere it was used;
  - change the seeded admin's password everywhere it was used.
- **Also:** while checking key presence for this item, a redaction filter in this session failed and printed both values into the session's tool output. They were not written to any file or commit. Treat both as exposed; the rotation above covers it.

### 1. Item table

| Id | Status | Commit | Proving test |
|---|---|---|---|
| F1 any failure in a message's handling is recorded against it; the sweep goes on | fixed | `c460ffe` | `OutboxDeadLetterTests.A_message_whose_save_throws_is_recorded_as_a_failure_and_the_next_message_is_delivered`; `When_recording_the_failure_also_fails_the_sweep_still_goes_on` |
| F1 every payload failure (incl. `InvalidOperationException`) is a bad payload, dead-lettered at once | fixed | `c460ffe` | `A_payload_whose_type_cannot_be_built_is_dead_lettered_at_once_and_the_sweep_goes_on` (first asserts the exception type is `InvalidOperationException`) |
| F2 last-holder check is deny-wins, like `PermissionResolution.Resolve` | fixed | `e0e8637` | `Phase4PermissionQueryTests.A_live_grant_and_a_live_deny_for_the_same_code_is_not_a_holder_and_the_resolver_agrees` |
| F2 still one `EXISTS` statement | unchanged | `e0e8637` | the same test (1 command); `HasOtherActiveHolderAsync_is_one_statement_however_many_candidates`, unchanged; the captured SQL (see "What changed") |
| F2 existing last-holder semantics | still pass, unchanged | — | `A_holder_only_through_an_expired_grant_is_not_a_holder`, `A_role_holder_with_a_live_deny_is_not_a_holder`, `A_live_grant_without_a_role_is_a_holder_and_an_inactive_one_is_not`, `InactiveOverrideTests` |
| F3 superseded doc paragraphs marked | fixed | `d7a0577` | — (comment) |
| F4 single-flight gate is per process | recorded | `c460ffe` (doc sentence) | — (precondition, see "What changed") |
| F5 committed JWT key and seed password | owner action | — | — (first item above) |

### What changed

- **F1, `OutboxProcessor`:**
  - `ProcessPendingAsync` wraps each `ProcessOneAsync(id, ct)` in `try … catch (Exception ex) when (ex is not OperationCanceledException)`. The catch logs at Error with the message id and the exception. It then calls the new `RecordSweepFailureAsync`, which opens a fresh scope and `SharedDbContext`, reloads the row, and, unless the row is processed or dead-lettered, applies the existing `Fail(message, ex.Message, ex)` (backoff, or dead letter at `MaxAttempts`) and saves. If that throws too, it is logged at Error and the sweep continues; the row stays due for a later sweep.
  - `Deserialize` now catches everything except `OperationCanceledException`. Before, its `JsonException`/`NotSupportedException`/`ArgumentException` filter let System.Text.Json's `InvalidOperationException` (a constructor parameter that binds to no property) escape and end the sweep.
  - The single-flight semaphore is unchanged, and cancellation still propagates.
  - The class doc gains two sentences: a failure anywhere in a message's handling is recorded against that message and never stops the sweep; and the F4 limit below.
- **F1, tests:**
  - A test-only `UnbindableEvent` (a constructor parameter with no property).
  - A `FailingOutboxSave` `SaveChangesInterceptor`, which throws when one message is saved as processed, or on every save of it. It uses the `interceptor` parameter `BuildProvider` already had, so no new test seam was built.
  - The JSON options for the exception-type assertion are a cached static, because CA1869 fails the build otherwise.
- **F2, `EffectivePermissionService.HasOtherActiveHolderAsync`:**
  - The predicate becomes `(viaRole || liveGrant) && !liveDeny`. The method doc says it weighs exactly as `PermissionResolution.Resolve` does, deny-wins included.
  - Captured SQL, one command: `SELECT EXISTS (… u."IsActive" AND (EXISTS (role claim) OR EXISTS (live grant)) AND NOT EXISTS (live deny))`.
- **F2, test:**
  - It opens a transaction on a context of its own, then runs `DROP INDEX identity."IX_user_permission_overrides_UserId_Permission"` (the name from `UserPermissionOverrideConfiguration.UniqueUserPermissionIndex`).
  - It inserts a live grant and a live deny for a user with no role, calls the service on that context, and rolls back in `finally`.
  - The DROP holds an exclusive lock on `user_permission_overrides` only for the test's own transaction. The other tests in the collection run serially, and every other test in the same run passed.
- **F3:** the three passages are prefixed "(Superseded in Phase 5, see below.)", with no other change and no re-wrap. The first one is a sentence inside the "Algorithm" paragraph, so the prefix sits before that sentence.
- **F4 (record only):**
  - The sweep's semaphore is per process. With two instances, both can attempt the same message. The inbox dedupes the effect, but both record the failure, so attempts climb twice as fast and a message dead-letters in about half the planned 8 hours.
  - **Precondition before running more than one instance:** claim rows (`FOR UPDATE SKIP LOCKED`, or a lease column).
  - The same sentence is in the `OutboxProcessor` class doc (F1 commit).
- **Proposal (CLAUDE.md is not edited, per decision 5.4):** add to the outbox rule: "The outbox sweep assumes one instance; claim rows before scaling out (F4)."

### 2. Run 0 for this phase

| | Build | Assemblies | Passed | Failed | Skipped |
|---|---|---|---|---|---|
| Run 0 (`master` @ `d3a06e5`) | 0 warnings, 0 errors | 31 | 1402 | 0 | 0 |
| Final (`d7a0577`) | 0 warnings, 0 errors | 31 | 1406 | 0 | 0 |

The difference is +3 in `BuildingBlocks.IntegrationTests` (25 → 28) and +1 in `Identity.Infrastructure.Tests` (25 → 26). `Architecture.Tests`: 34 passed, before and after.

**Probe migrations:** `dotnet ef migrations has-pending-model-changes` is clean for `IdentityModuleDbContext` and `SharedDbContext`, the only contexts whose code was touched; no entity or configuration changed.

**Format:** on a clean tree, `dotnet format DMS.Modular.slnx` followed by `git diff --exit-code --ignore-cr-at-eol` shows no real change. Only line endings changed, on 1003 files, and those LF working copies were restored with `git checkout -- .`.

### 3. Tests written but not run

None.

### 3a. Red-before-green proofs

Each fix was backed up with `cp`, reverted, its tests run (red), then restored with `cp`. The restored file was `touch`ed and the project rebuilt before the green run, to avoid the Phase 5 stale-DLL false failure.

| # | Reverted | Went red | Green after restore |
|---|---|---|---|
| P1 | F1 step 1 alone: `Deserialize`'s old filter | `A_payload_whose_type_cannot_be_built_is_dead_lettered_at_once_and_the_sweep_goes_on` (`Assert.NotNull` on `DeadLetteredUtc`: the outer guard recorded an ordinary failure with backoff instead) | 9/9 `OutboxDeadLetterTests` |
| P2 | F1 step 2 alone: the per-message guard removed | `A_message_whose_save_throws_is_recorded_as_a_failure_and_the_next_message_is_delivered`, `When_recording_the_failure_also_fails_the_sweep_still_goes_on` (the save exception left the sweep; the good message was never reached) | 9/9 |
| P3 | F2: the old grant-wins predicate | `A_live_grant_and_a_live_deny_for_the_same_code_is_not_a_holder_and_the_resolver_agrees` (`Assert.False`: it returned true) | 9/9 `Phase4PermissionQueryTests` |

**Not proved by revert:** F3 (comment). F4 and F5 have no code.

### 4. Migration guards added

None; no migration (see §2).

### 5. Frontend contract changes

**None.** F1 is background processing and F2 changes only an internal check's predicate. No endpoint, status code or response shape changed.

### 6. Blockers, questions and contradictions with the prompt

1. **No blockers.**
2. **Owner, before production:** F5 (first item above), which also covers the session-output exposure.
3. **The red-proof mapping in the prompt, clarified rather than followed literally.**
   - The prompt expected "revert step 2 alone → the good message stays pending" from the bad-payload test. With step 1 in place, that test still passes: step 1 alone dead-letters the payload, so the outer guard is never reached.
   - Step 2's red proof comes from the save-failure tests (P2). That is possible because the seam already existed (`BuildProvider`'s `interceptor`). Both proofs were run, as P1 and P2.
4. **An extra test beyond the prompt's two:** `When_recording_the_failure_also_fails_the_sweep_still_goes_on` covers step 2c (the failure record's own save throws). It is one flag on the same interceptor, not a new seam.
5. **F3 placement:** the first superseded passage is a sentence mid-paragraph, not a paragraph; the prefix is placed before that sentence.
