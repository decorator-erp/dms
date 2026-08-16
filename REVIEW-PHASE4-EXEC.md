# Review — Phase 4 executable plan (final)

**Verdict: approved. Run it.** One number settled below that the plan left open, and one internal
inconsistency it resolves.

---

## The entity count — settled, not deferred

The plan says *"neither 24 nor 25 is trusted, count at G9-07."* That was the right instruction when
nobody had counted. I counted:

- **`ClientChild` is JSON-owned — confirmed.** `ClientConfiguration.cs:47-51`:
  `builder.OwnsMany(x => x.Children, child => child.ToJson("children"))`, with the comment *"Children
  persisted as JSONB inside the clients row."* No table, no separate configuration. The plan's
  correction is right.
- **The backend has exactly 23 `Crm*Configuration` files**, plus `ClientConfiguration` = **24**.
- **No `Crm*` configuration uses an owned type.** Across the whole backend, only three configurations
  reference `OwnsMany`/`OwnsOne`/`ToJson` at all: `AppUserConfiguration`, `ApprovalConfiguration`,
  `ClientConfiguration`. So there is no second hidden `ClientChild`-style case lurking in CRM.

**24 configurations.** `CatalogAttributeValueRules` (static class) and `ClientChild` (JSON-owned) are
the two files that don't get one, which is exactly why the file count reads higher.

This also resolves the plan's internal inconsistency: the corrections table says "neither 24 nor 25 is
trusted," then G9-02→G9-06 and G9-07 both state 24. Those aren't in conflict any more — 24 is correct,
and the task text was right.

**Keep the G9-07 assert regardless.** Its value was never "check the plan's arithmetic" — it is to
catch a configuration that was *forgotten* while writing 24 of them. That risk is unchanged.

## Everything else

All five corrections, the three scope decisions, the enum-rename section (values preserved, both
recorded as deliberate divergences), the `ClientId` ripple with its eight call sites and three fixtures,
and the phase-level verification list all stand as reviewed across the previous three rounds. Nothing
new to add.

Two things I'd keep visible during the run, both already in the plan and both easy to let slide:

- **Prove the subject-type filters with a real row's id under the wrong subject type.** A random-guid
  test passes with the filter deleted. This is E1's lesson applied before the fact.
- **The three hand-maintained schema lists** (`ModuleDependencyOrder.cs:18-26`, `ApiFixture.cs:139-181`
  switch, `ApiFixture.cs:72` Respawner). Missing one surfaces as a confusing test failure several tasks
  later, not at G9-07.

## Run it

Resume G9-01 — the uncommitted `Crm.Contracts.csproj`, `Enums.cs`, and `CrmPermissions.cs` are that
task's work and land in its commit. Then G9-02 → G10-01 unattended per the execution contract.
