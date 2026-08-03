# Review 2 — autonomous build plan, revision 2

**Verdict: approved. Start at G0-01 after applying S1–S4 below.** `PLAN.md` has been updated for all of
them plus the numbering fix. Re-read it — it changed again.

R1–R7 all landed correctly. The G0-09 checkpoint is the right call.

---

## Correct as written — no further comment

D1 (`InternalsVisibleTo`), D2 (rule 4 scoping), D3 (`ContractType` not the CRM enums), D4 (seven
Projects-reading HR queries dropped), D5 (permission strings), D6 (`PagedResult` rewrite), D7 (bug list
+ `EffectivePermissionService` ported unchanged), D8 (dev config).

The numbering slip you flagged was real: G0-09 and G2-03 both said "rule 8" where G0-07 defines the
cross-schema FK check as rule 9. **Fixed in `PLAN.md`** — you no longer have to read around it.

---

## S1 — Domain-event dispatch belongs in `BuildingBlocks`, not G1-03

> *"Domain-event dispatch is **not** wired here (it needs a `DbContext`); it lands at G1-03."*

If it is built inside Identity, HR at G2-03 has two options: duplicate it, or reference
`Identity.Infrastructure`. The second is a boundary violation, and the first is the kind of duplication
that diverges within a month. The "it needs a `DbContext`" objection is real but is solved by a type
parameter, not by relocation:

```csharp
internal sealed class DomainEventDispatcher<TContext>(TContext db, IDomainEventQueue queue, ...)
    : IDomainEventDispatcher where TContext : DbContext
```

Wire it from `AddModuleCqrs<TContext>(assembly)`, which G0-03 already introduces — it just needs the
type parameter. Each module calls it with its own context and gets its own dispatcher.

Keep the drain-both-sources behaviour (EF `ChangeTracker` **and** `IDomainEventQueue`) — the queue
exists because set-based writes bypass the change tracker, and that reason survives the rewrite.

**Moved to G0-03 in `PLAN.md`**, with a second verify case for it.

## S2 — `docs/cross-schema-foreign-keys.md` already exists

> *"Referenced by PLAN.md but not assigned to a task. I write it at G0-07."*

It is already on disk at `dms-modular/docs/cross-schema-foreign-keys.md` — written before your plan, and
it is what R6 was derived from. **Read it, do not write it.** It covers the `ON DELETE RESTRICT`
reasoning, the migration-ordering trap, why you index the referencing column yourself, the
`pg_constraint` query behind rule 9, and the allow-list of permitted schema pairs.

Extending it at G2-03 once the first real FK exists is a good idea — keep that half.

Same applies to `REVIEW.md` and `PROMPTS.md`: existing files, not yours to generate.

## S3 — The migration order must live in one place

> *"list lives in `Api` with a comment tying it to the module graph"*

The G0-09 fixture also applies migrations in dependency order. Two hand-maintained lists will drift, and
the failure surfaces in only one of them — a migration error in CI that does not reproduce locally, or
the reverse.

Put the order in `BuildingBlocks.Web`: either a single static ordered list, or an `Order`/`DependsOn`
member on `IModule` that the host topologically sorts. The test fixture consumes the same source.
**Updated in G0-08.**

## S4 — Auditing silently stops working

`SharedKernel`'s 9 files include `IAuditable` and `AuditIgnoreAttribute`, so they come across. The EF
interceptor that acts on them belongs to Notifications & Audit, which is out of scope. `Employee` is
`IAuditable` in the old solution and will be here too — and **no audit rows will be written**.

That is correct for this scope. It is also exactly the kind of thing that gets discovered six months
later during an incident. Record it in `PROGRESS.md` and the final report as a known gap with a named
owner (the Notifications & Audit module). **Added to G0-02.**

---

## Minor

- **Rename the context.** `IdentityDbContext` deriving from
  `Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext<,,>` compiles with qualification
  at the base-type position, but every future reader has to parse that. `IdentityModuleDbContext` costs
  nothing and removes the collision. Your call.
- **Respawn** needs `SchemasToInclude = ["identity", "hr", "shared"]` and must not reset
  `__EFMigrationsHistory`, or the fixture re-migrates between tests. Added to G0-09.
- `dotnet format` before `--verify-no-changes` makes that step a formatter rather than a gate. That is
  the right trade given generated migrations — noting it so it is deliberate rather than accidental.

---

## At the G0-09 checkpoint, report on

1. Does `AddModuleCqrs<TContext>` actually produce an isolated pipeline per module, or did anything end
   up shared by accident?
2. The R1 kill-before-return test — did it genuinely fail before the fix? A test that passes both ways
   is not testing anything.
3. Which of the nine arch rules pass vacuously today, and what makes each of them non-vacuous later.
4. Anything in G0 that felt like ceremony rather than structure. Foundation is the cheapest place to
   remove weight, and the last place anyone looks.
