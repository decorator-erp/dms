# Review — Phase 4 plan, final

**Verdict: approved. Resume from G9-01 and run through G10-01.**

Everything from `REVIEW-PHASE4-PLAN.md` is carried correctly, and the entity count now reads
"neither 24 nor 25 is trusted — count at G9-07 and assert against registered `DbSet`s," which is the
right resolution.

## The one new thing, verified

The enum renames disclosed in the work-in-progress note are correct on both counts:

- **The collision is real.** `Approvals.Contracts.ApprovalState` is public and already taken
  (`Approvals.Contracts/Enums.cs:27`). CRM's artifact-confirmation state genuinely needs a different
  name in a codebase where a file may `using` both namespaces.
- **The values are preserved, which is what actually mattered.**
  `CrmApprovalState` = Draft 1 / PendingApproval 2 / Approved 3 / Rejected 4 / NotRequired 5 — identical
  to the backend's `ApprovalState`. `RequirementPriority` = Low 1 / Medium 2 / High 3 / Urgent 4 —
  identical to `TaskPriority`.

That second point is the one worth stating: these persist as ints. A rename is cosmetic; a rename that
also renumbers silently changes what every stored row means, and nothing in the test suite would catch
it because the tests write and read through the same enum. Renaming while holding values fixed is
exactly right, and `RequirementPriority` is better domain naming than the backend's reused
`TaskPriority` — a requirement item's priority is not a task's.

Also noted in passing: `ProjectSector` landing in `Crm.Contracts` is correct and consistent with Phase
2's decision #6 (verified then as CRM lead-intake data, never a `Project` field). The Arabic domain
terms carried into the doc comments are worth keeping — they are the vocabulary the business actually
uses.

## Standing instruction for the run

Record the two renames in `PROGRESS.md` as deliberate divergences with the values-preserved note. A
future reader comparing CRM's enums to the backend will otherwise see two names that don't match and
have to re-derive why.

Resume at G9-01 — the uncommitted `Crm.Contracts.csproj`, `Enums.cs`, and `CrmPermissions.cs` are that
task's work and land in its commit.
