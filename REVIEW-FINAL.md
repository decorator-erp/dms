# Final review — G0-01 → G3-03 complete

Reviewed against the working tree at `4d360ce`, code first, report second.

**Verdict: accepted, with one required follow-up task (F1) before this solution takes on a third
module.** The run held the quality bar G0 set. Both "biggest" findings are real; one of them is
partly my fault and its workaround has a cost the report does not name — that cost is F1.

---

## 1. The circular-reference finding — the plan's fault, and the fix has a hidden cost

The contradiction was mine. Three of my constraints cannot coexist:

1. No repository pattern — handlers use the module's `DbContext` directly (non-negotiable).
2. The `DbContext` lives in `*.Infrastructure` (layout).
3. Endpoints live in `*.Infrastructure` and construct command types from `*.Application`
   (reference matrix: Infrastructure → Application).

(1)+(2) force `Application → Infrastructure`. (3) forces `Infrastructure → Application`. MSBuild
rightly refuses. The reference matrix never stated whether Application may reference its own
Infrastructure because I never noticed the tension. The executor's csproj comment justifying the
resolution is a fair reading of an ambiguous document.

**But the chosen resolution — commands/queries made `public` in `*.Contracts` — quietly breaks a
non-negotiable.** Verified in code: `CreateDepartmentCommand`, `GrantPermissionOverrideCommand`, etc.
are public in Contracts, and every `ICommandHandler<T>` is registered in the one shared DI container.
So module B can reference module A's Contracts (allowed), inject `ICommandHandler<ACommand>` (nothing
forbids it), and perform a **synchronous cross-module write that bypasses the outbox**. Rule 3 does not
catch it — B never references A's Application. Rule 4 is satisfied — the types are in Contracts.
"Cross-module writes only through integration events" is now enforced by convention only, and this
architecture was built on the premise that conventions erode.

Nobody does this today. The hole is structural, not exploited — which is exactly when it is cheap to
close.

### F1 (required): arch rule 10 — commands and queries are module-private

For every pair of distinct modules A ≠ B: no type in B may depend on a type in `A.Contracts` that
implements `ICommand`, `ICommand<T>`, or `IQuery<T>`. Contracts interfaces, DTOs, permission constants
and integration events remain fair game — that is what Contracts is for. Fault-inject it the way rules
7/8b were: a scratch reference from `Hr.Application` to `LoginCommand` must turn it red.

Optionally also: no type outside module A may resolve `ICommandHandler<>`/`IQueryHandler<>` closed over
A's types. The type-dependency rule above already implies it, since naming the closed interface
requires naming the command type.

### F2 (optional, structural): collapse `.Application` + `.Infrastructure` per module

The tension F1 patches around disappears if each module is `Contracts` + one implementation project
(+ `Domain` if desired). Commands return to `internal`, the matrix ambiguity evaporates, and the
`adding-a-module.md` pattern gets simpler instead of documenting a workaround. Mechanical refactor,
tests green throughout, three projects per module instead of four. Worth doing before module #3, not
worth doing today. If F2 is ever done, F1's rule stays anyway — defense in depth.

## 2. The outbox wiring gap — real, fixed, and carrying a lesson worth keeping

`SharedDbContext`/`AddOutbox()` missing from `Program.cs` from G0-05 to G2-04 means: every claim about
delivery made before G2-04 was true of the test harness and false of the application. G0-05's verify
ran against its own host; the app itself could enqueue but never deliver, and R2's "zero-handler events
are marked processed" was not true in the running app until commit `6f479be`.

It was found exactly where the design says it should be found — the first flow that depended on
delivery — and the fix is correctly commented in `Program.cs`. The lesson for the remaining eight
modules: **a component's verify block passing in its own harness says nothing about the composition
root.** G3-01-style end-to-end tests are the only ones that check wiring; every future module's plan
should include one from the start, not discover the need at its equivalent of G2-04.

## 3. Thinner-than-intended — all four accepted

| Item | Ruling |
|---|---|
| T1 sync best-effort attempt not built | Fine — explicitly optional in T1; outbox is the mechanism of record; the ~5s window was the accepted trade. Record the window in `docs/adding-a-module.md` where the deactivation pattern is described. |
| No HR command renames the Identity user | Correct, not thin — C8 assigned name changes to Identity's own features. The backend's version was a cross-module write; its absence is the architecture working. |
| Deactivate-department + bulk-move not one command | Fine — two commands compose at the endpoint. One command spanning both would have been convenience, not correctness. |
| 3 of 7 default roles seeded | Right call — the other four exist for out-of-scope modules, and seeding them with zero claims (as the backend does for Vendor/Contractor) asserts nothing. Note for the future: when those modules arrive, their role seeding must be part of *their* module plan, or it will be forgotten — the backend seeded all roles centrally and that central place no longer exists. |

## 4. Plan-error admissions — all three fair

- **G2-02's "port the domain services"** — correct criticism. `EmployeeDomainService` in the backend is
  repository-bound application logic wearing a Domain namespace; following the task text literally
  would have reintroduced repositories through the side door. Resolved right.
- **Reference matrix silent on Application → own Infrastructure** — mine, root cause of §1, now
  documented in `adding-a-module.md`. F2 is the durable fix.
- **The circularity is structural and recurs per module** — yes; that is why F1 is required and F2 is
  on the table.

## 5. Bugs found by tests during the run

The stale-tracked-entity bug in `UserDirectory.GetAsync` caught by a failing test, and the two D7
backend bugs fixed on port, are the system working as designed: Testcontainers over mocks was chosen
precisely so this class of bug surfaces in the run rather than in production.

## 6. Where this leaves the whole effort

- **dms-modular**: foundation + Identity + HR, 12 arch rule files + meta-test, self-enforcing
  discovery, real integration coverage. Do F1, consider F2, then the next module per
  `adding-a-module.md` — recommended order from the earlier plans: Approvals (small sink) before
  anything that depends on it.
- **The old backend**: untouched, as designed. Phases 0–2 of its own plan
  (`docs/backend/modular-monolith-execution-plan.md`) remain worth doing independently if it keeps
  serving production while dms-modular grows.
- **The docs**: `PROGRESS.md` is now the best design record either codebase has. When the next module
  starts, that standard — decisions with reasoning, limits stated, claims distinguished from hopes —
  is the deliverable alongside the code.

**Bottom line: two modules, zero boundary violations in the code that exists, one structural hole
(F1) identified before any code exploits it, and a plan contradiction resolved sensibly and honestly
attributed. This is what the four review rounds were for.**

---

## 7. Execution instructions (for the follow-up run)

**Scope: task F1 only.** Do not implement F2. Do not start a third module. Do not modify anything
under `..\backend`.

### F1 — arch rule 10: commands and queries are module-private

One task, one commit (`F1: arch rule 10 — cross-module command/query references forbidden`), the usual
gate (`dotnet build -warnaserror` / `dotnet test` / `dotnet format --verify-no-changes`).

1. Add `tests/Architecture.Tests/Rule10CommandQueryPrivacyTests.cs`. For every pair of distinct
   modules A ≠ B (module = prefix per `ModuleAssemblies.ModuleOf`): no type in any of B's four
   assemblies may depend on a type in `A.Contracts` that implements `ICommand`, `ICommand<T>`, or
   `IQuery<T>`. Interfaces, DTOs, permission constants and `IIntegrationEvent` types in `A.Contracts`
   remain allowed — the rule targets command/query types only.
2. Register the rule in `MetaSelfEnforcementTests`' expectations if that test enumerates rule files.
3. **Fault-inject before trusting** (rules 7/8b standard): add a scratch reference from a type in
   `Hr.Application` to `Identity.Contracts`' `LoginCommand` — the new test must go red; remove it —
   green. Both states observed, not assumed.
4. Update `docs/adding-a-module.md`: in the section documenting the commands-in-Contracts pattern, add
   that rule 10 makes those public types unusable across modules, so the pattern's cost is contained.
5. Append one line to `PROGRESS.md` for F1, plus a decision-record line for **F2**: *"F2 (collapse
   .Application/.Infrastructure per module) — deliberately deferred; decide before starting module #3.
   If adopted, rule 10 stays regardless (defense in depth)."*

**Verify:** all tests green including the new rule; the fault-injection red/green cycle is described
in the PROGRESS.md line; no other files changed beyond the five listed areas.

**Stop after F1 and report.** The report is: the rule's red/green evidence, and anything rule 10
flagged in the existing code (expected: nothing — endpoints construct their own module's commands,
which the rule permits since A = B there).
