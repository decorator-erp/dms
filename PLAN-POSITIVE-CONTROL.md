# Positive-control sweep (PC-01 → PC-04)

Runs before Phase 7 (Finance). Same execution contract, same gate.

Head: `3d5dd01`. Eight modules, 846 tests across 26 suites, 19 architecture facts, clean tree.

---

# Why

N1-08 found that `ApiFixture.CapturedSql` **had never captured anything** — `UseSerilog` without
`writeToProviders: true` was replacing the whole `ILoggerProvider` set. Four schema-isolation
assertions were green across three phases while constraining nothing.

A negative assertion against a dead instrument is indistinguishable from a negative assertion against
a clean system. Both pass. This is the fourth instance of one family in this project:

| Where | Instrument | Failure |
|---|---|---|
| E1-01 | stage-gate test | passed with *and* without the fix |
| P5F-01 | rule 8a injection | the obvious injection leaves it green |
| D1 (+2 earlier) | grep patterns | too narrow to match the positive case |
| N1-08 | `CapturedSql` | never captured anything |

**The standing rule this sweep exists to establish:** *an assertion that something never happens
proves nothing until the instrument has been shown to detect it happening.*

This sweep validates the 846 tests that already exist. That is worth more right now than adding a
ninth module's tests to a suite whose instruments are unaudited.

---

# Scope — which assertions are actually at risk

Measured: 25 `Assert.DoesNotContain`, 29 `Assert.Empty`, 52 `Assert.False`, 67 `Assert.Null` — 173
negative assertions. **Most are not at risk and must not be touched.** Adding controls to all 173
would be weeks of work and mostly noise.

**The risk criterion — apply it literally:**

> An assertion is at risk **iff the thing asserted about is gathered by machinery** — a logger
> provider, an EF interceptor, a captured list, a reflected type set, a `pg_*` catalog query — rather
> than being a direct return value of the call under test.

If the machinery breaks, the gathered set is empty and every negative assertion over it passes.

| Shape | Example | At risk? |
|---|---|---|
| Gathered by a provider/interceptor | `Assert.DoesNotContain(fixture.CapturedSql, …)` | **Yes** — this is the `CapturedSql` class |
| Gathered by reflection | `Assert.Empty(violatingTypes)` in an arch rule | **Yes** |
| Gathered by a catalog query | `Assert.DoesNotContain(found, fk => fk.From == "shared")` | **Yes** |
| Gathered by a DB query | `Assert.Empty(await db.X.Where(…).ToListAsync())` | **Yes** |
| Direct return value | `Assert.False(result.IsSuccess)` | No — the object exists, the property is read directly |
| Direct null check | `Assert.Null(await handler.HandleAsync(…))` | No |

Expect the at-risk set to be **far smaller than 173** — plausibly 20–40 sites. PC-01's job is to find
out; if the answer is "twelve," that is a good outcome and PC-02/03 shrink accordingly.

---

# What a positive control is here

Two shapes, and the cheap one is mandatory:

**1. Liveness — required for every at-risk site.** Assert the gathered set is non-empty in the *same
test*, before asserting what it does not contain:

```csharp
Assert.NotEmpty(fixture.CapturedSql);                       // the instrument is alive
Assert.DoesNotContain(fixture.CapturedSql, s => s.Contains("crm."));   // and it saw no violation
```

One line. **`CapturedSql` could not have survived three phases with it.** This is the whole point of
the sweep — not elaborate machinery, one assertion that the collector collected.

**2. Discrimination — where cheap.** Prove the assertion goes red when the violation exists: inject a
real violation, confirm the failure names it, revert. P5F-01 established the discipline and its own
finding applies — **the injection must match the mechanism the rule uses**, or it produces a false
negative about the rule rather than about the code.

Liveness everywhere; discrimination where a violation can be injected in a few lines.

---

# Tasks

### PC-01 — Inventory and classify

**No code changes.** Produce a table in `PROGRESS.md`, one row per negative assertion over a gathered
set:

| File:line | What is gathered | How | Has liveness? | Has discrimination? | Action |

Classify every one of the 173 as **at-risk** (gathered) or **direct** (not). Direct ones are listed
once as a count with the reasoning, not enumerated — the point is to show they were considered, not
to pad the table.

Known starting points from the survey, not exhaustive:
`AuditMechanismTests`, `AuditInterceptorTests`, `AuditTrailTests`, `Rule8BuildingBlocksWebTests`,
`Rule9CrossSchemaForeignKeyTests`, `SiteScopeCoverageTests`, `NotificationsSchemaObjectTests`,
`SitesSchemaObjectTests`, `HrSchemaTests`, `SitesDbContextConfigurationTests`, `SiteSchemaCacheTests`,
`CrmAccessGuardTests`, `ProjectAccessGuardTests`, and every `*SchemaIsolationTests`.

**Several already have discrimination** — arch rules 1/4/6/8a (P5F-01), 7/8b (G0-07), 9 (G5-04), the
audit mechanism (N1-02). Mark those and do not redo them; the sweep is about what *lacks* a control.

**Verify:** the table is complete — every one of the 173 appears in it or in the direct-count line.
Full gate green (no code changed).

### PC-02 — Liveness for capture- and interceptor-gathered assertions

The `CapturedSql` family and the audit family. Add the one-line liveness assertion to each at-risk
site PC-01 identified.

**Also check `ClearCapturedSql()` ordering** — a liveness assertion is worthless if the capture is
cleared after the action rather than before it. N1-05 already hit this shape with the background
outbox processor race.

**Verify:** every touched test still passes; then **prove the liveness assertions work** — temporarily
break the collector once (revert `writeToProviders: true`) and confirm the newly-added assertions go
red across the whole family, then restore. That single revert is the strongest possible evidence, and
it is the exact failure that occurred.

### PC-03 — Liveness for reflection- and catalog-gathered assertions

Arch rules with no discrimination yet, schema-object tests, `SiteScopeCoverageTests`, DbContext
configuration-count tests.

For catalog queries (`pg_constraint`, `pg_indexes`), liveness means asserting the query returned rows
at all before asserting which rows it did not return — a typo'd schema name yields an empty set and a
passing test.

Add discrimination where it costs a few lines; record where it does not and why.

**Verify:** each new liveness assertion demonstrated red by breaking its collector once (wrong schema
name in the catalog query, empty assembly list in the reflection scan), then restored.

### PC-04 — Close-out

`docs/adding-a-module.md` — a new section, **Positive controls**, stating the rule and the two shapes,
with `CapturedSql` as the worked example. Every future module's schema-isolation and schema-object
tests inherit it.

`PROGRESS.md` — the standing rule alongside the grep rule and the fault-injection rule; the final
count (at-risk sites found, controls added, sites already covered); anything found broken by the
sweep, which is the sweep's real output.

**Then: what this changes about the Finance plan.** Finance will add schema-isolation and schema-object
tests of exactly the shape that was dead for three phases — its plan should require liveness in the
verify clause, not as a follow-up.

---

# Expected outcome, stated before starting

Most at-risk sites will already be sound — the instruments work, the assertions mean what they say.
The sweep's value is the small number that are not, plus a rule that stops the next one.

**If PC-01 finds zero broken instruments, that is a successful outcome, not a wasted task** — it
converts 846 tests from "green" to "green and audited," and that distinction is what this whole
project has been built on.

---

# Execution contract

Per task: restate → implement → task verify → full gate → ≤3 attempts → one commit → one `PROGRESS.md`
line → next task immediately.

```bash
DOCKER_HOST=tcp://127.0.0.1:2375 dotnet build DMS.Modular.slnx -warnaserror
DOCKER_HOST=tcp://127.0.0.1:2375 dotnet test DMS.Modular.slnx
DOCKER_HOST=tcp://127.0.0.1:2375 dotnet format DMS.Modular.slnx && git diff --exit-code
```

**Hard stops:** a verify failing 3 attempts; any fix needing an architecture rule relaxed, a test
deleted, or an internal type made public; **weakening an assertion to make it pass** — if a liveness
assertion reveals a test was measuring nothing, the fix is to make the instrument work, not to drop
the assertion; any write under `..\backend`; starting Finance.

**One thing to expect:** adding liveness may turn currently-green tests red, because a dead instrument
was hiding a real violation. That is the sweep succeeding. Fix the violation, or record it as a
deferred item with an owner — do not silence the assertion.
