# G0 checkpoint review — implementation, not report

Reviewed against the working tree at `654ac04`, not against the report. Claims were spot-checked in
code and by re-running the numbers.

**Verdict: G0 is genuinely good. Proceed to G1 after the one required fix in §3.**

---

## 1. Verified claims

| Claim | Check | Result |
|---|---|---|
| 9 commits, one per task | `git log --oneline` | ✓ (G0-01 has two — skeleton + PROGRESS.md — acceptable) |
| 23 tests (3+5+12+3) | counted `[Fact]`/`[Theory]` per project | ✓ exactly 12 arch, 3 unit, 5 integration, 3 smoke |
| SharedKernel 9 files, zero refs | `ls` + csproj | ✓ |
| SharedKernel "byte-for-byte" | `diff` vs `..\backend` | **Overstated** — 8 of 9 files differ, but only in BOM/CRLF/trailing-newline. Substantively identical. Say "verbatim modulo line endings" next time; a reviewer who diffs without checking whitespace will think the report lied. |
| `BuildingBlocks` has no ASP.NET | csproj inspection | ✓ EF + Hosting.Abstractions only |
| R1: marker + work in one `SaveChangesAsync` | read `InboxDedupeDecorator` | ✓ single commit point, contract documented |
| R2: zero handlers = success | `EventBus` remarks + processor flow | ✓ |
| Rule 9 allow-list matches the FK doc | read `Rule9CrossSchemaForeignKeyTests` | ✓ full ten-module graph, correctly annotated as vacuous |
| No committed secrets | grep appsettings | ✓ JWT key absent; only the local docker-compose Postgres password, which is fine |

## 2. The Scrutor finding is real and important

Confirmed in `ModuleCqrsRegistration`: the backend's open-generic
`Decorate(typeof(ICommandHandler<,>), ...)` would have re-wrapped **every** registered handler on each
module's `AddModuleCqrs` call — the second module's `DomainEventDispatcher<TContext>` around the first
module's handlers, constructing the wrong `DbContext` on requests that never touch it. The per-closed-type
decoration fix is correct, correctly reasoned (two modules never declare the same command type), applied
consistently to the inbox dedupe path, and documented where the next person will look. This is exactly
the class of bug that G0 exists to catch, and it would have been near-impossible to diagnose at G2.

Also confirmed: query handlers deliberately get no `DomainEventDispatchDecorator` — correct, queries
should not dispatch.

The honest limits (module isolation unprovable until two real assemblies exist; the R1 test proves
atomicity-by-construction rather than simulating a crash) are stated accurately and are acceptable.
G3-01 closes the first; the second is a genuine limit of automated testing, and the design guarantee
(one save call, no window) is the right thing to prove.

## 3. Required before G1 — the arch tests fail open

`ModuleAssemblies` discovers module assemblies from the test project's **own output directory**, which
only contains what `Architecture.Tests.csproj` references. Its own doc comment says it: rules go live
only when each module is *"added as project references to Architecture.Tests.csproj"*.

That is a manual step in the exact failure mode arch tests exist to prevent. Forget the reference at
G1-02 and every module rule stays vacuously green while `Identity.Domain` does whatever it likes — nine
green ticks, zero enforcement, and nothing that says so.

**Fix (small, do it as G1-00):** a meta-test in `Architecture.Tests` that parses `DMS.Modular.slnx`
(via the existing `SolutionRoot` helper), takes every project under `src/Modules/`, and asserts each
matching `*.Domain`/`*.Application`/`*.Infrastructure`/`*.Contracts` assembly appears in
`ModuleAssemblies.All`. Then a missing reference is a **red** test, and the vacuity table becomes
self-enforcing instead of a promise. This is the same fail-open/fail-closed distinction as R1 — the
suite must fail closed.

## 4. Known gaps to record (not build now)

- **Poison messages retry forever.** `OutboxProcessor` has no max-attempts / backoff / dead-letter;
  a permanently failing message re-runs every poll and logs a warning each time, and `attempts` climbs
  unbounded. Fine for this scope — the field exists to build on — but put it in `PROGRESS.md` as a known
  gap with an owner, like the audit interceptor. First real handler (T1's deactivation, G2-04) is where
  it starts to matter.
- **The `InboxDedupeDecorator` contract is unenforceable.** "Handlers stage but never call their own
  `SaveChangesAsync`" is documented, but a handler that does call it silently splits the commit and
  breaks R1 — no test can catch it generically. Mitigation for now: the planned re-documentation at
  G2-04, plus making the first real handler (deactivation) the reference example. If it ever breaks in
  practice, the fix is a decorator-owned transaction instead of a convention.
- **Concurrency note, satisfied but worth stating:** two concurrent deliveries of the same event both
  pass the `AnyAsync` check; the second's `SaveChangesAsync` then violates the `(event_id, handler_name)`
  PK and rolls back its whole transaction, business write included. The dedupe is safe **because** the
  marker and the work share a transaction — one more reason the §4 contract must hold.

## 5. Minor

- The extra test projects (`BuildingBlocks.UnitTests`, `BuildingBlocks.IntegrationTests`) were the right
  call over shoehorning into G0-09's harness — verify blocks needed real assertions at G0-03/G0-05, and
  InMemory EF genuinely cannot demonstrate transaction boundaries.
- Fault-injecting rules 7/8b before trusting them, and reporting the circular-reference restore error as
  itself a form of enforcement, is the standard the module-rule activations should meet at G1-02/G2-02
  per the vacuity table.
- The `Npgsql` → `Relational` floor-resolution pin in `BuildingBlocks.csproj` is correctly commented.
  Keep that comment; it will confuse someone in a year otherwise.
- `public partial class Program;` for `WebApplicationFactory`, `UseSetting` over
  `ConfigureAppConfiguration` (with the failed first attempt documented), and the Respawn
  `DbConnection`-overload discovery are all correct and usefully recorded.

## 6. Proceed

G1 (Identity) is approved to run unattended once the §3 meta-test exists and is red-then-green
(red with a deliberately-omitted reference, green with it restored — prove it discriminates before
trusting it). Everything else stands as built.

The quality bar set by `PROGRESS.md` — decisions with reasoning, limits stated instead of elided,
claims distinguished from hopes — is the bar for G1 and G2. Hold it.
