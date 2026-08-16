# Review — H2 / M1 plan

**Verdict: approved, no revisions. Run it once Docker is up.** All three corrections verified, plus a
fourth the plan makes in passing. One of them is the same failure mode that caused the Meetings gap in
the first place — worth naming as such.

---

## 1. Corrections verified

| Correction | Result |
|---|---|
| **`CrmSchedulerService` was never ported** | ✓ A repo-wide search of `src/` returns **one** file — `Crm.Contracts/Meetings/MeetingContracts.cs`, a doc comment. No implementation, no options class, no DI registration. |
| **`Demote()` internal is not a blocker** | ✓ `Crm.Domain.csproj:8-12` grants `InternalsVisibleTo` to `Crm.Application`, `Crm.Infrastructure`, and all three test projects. |
| **The 56-permission verify asserts against the constant** | ✓ `CrmSmokeTests.cs:36` is `Assert.Equal(CrmPermissions.All.Count, crmModule.Permissions.Count)` — it cannot fail on an added string. The real guard is "don't add any." |
| **No test references `/scalar`** | ✓ Zero matches across `tests/`. `PLAN-PHASE4.md:311` required it as a G9-14 verify and it was never written. |

## 2. The `CrmSchedulerService` correction is the important one — and it is my error

My prompt listed it under *"what already exists, so you don't rebuild it."* That was wrong, and the
provenance is exactly what the plan says: `PROGRESS.md:4481-4490` asserted "already ported," and I
carried it into `REVIEW-PHASE4-COMPLETE.md`, then `REVIEW-E2-COMPLETE.md`, then the prompt.

**This is the Meetings gap's failure mode, recurring one level up.** The Meetings gap happened because
scope prose and the task table were two lists nobody diffed. This happened because a claim about the
repo was written once and inherited across four documents without anyone re-reading the repo. Same
root cause — *an assertion treated as a fact because it was already written down* — and I have now
made it twice in this project (the CRLF misdiagnosis, and this).

Worth adding to M1-03's lesson list in exactly those terms: **"already ported" / "already exists"
claims must be verified against the repo at the moment they are relied on, not inherited from a prior
document — including from a review document.**

The practical consequence is correctly drawn: M1-01 must *build* the service, and my verify clause
about "the G9-03 `FindSystemTimeZoneById` startup-validation fix" referenced a fix that does not
exist. The plan's replacement — build it with `.Validate(...)` + `.ValidateOnStart()` on the
`IdentityModule`/`JwtOptions` pattern, turning a per-request `TimeZoneNotFoundException` into a boot
failure — is the right shape and is genuinely new work, correctly scoped into M1-01.

## 3. The contract reconciliation is the plan's strongest section

Eleven backend features against twelve contracts, with a verdict per row and — crucially — *why* each
mismatch matters rather than just that it exists. Three stand out:

- **`RescheduleCrmMeetingCommand(MeetingId)` carries no new time.** The command literally cannot do its
  job. That is the clearest possible evidence for the general point: those twelve records were written
  at G9-01 with no handler to validate them, and were the least-tested code in the module.
- **`GetFreeMeetingSlotsQuery` missing `ProjectId`** — and the accompanying note that in the backend
  `ProjectId` is used *purely* for authorization and never reaches the scheduler, so slots are
  office-wide. Keeping the behaviour and documenting it so it does not read as a bug later is the right
  call.
- **Splitting `RequestCrmMeetingCommand` into the employee and client flows** the backend actually has,
  rather than forcing one command to express both.

The actor convention — pre-resolved `Guid` ids, endpoints resolving via `CrmEndpointActors`, matching
`CreateCrmClientNoteCommand.WrittenById` — is consistent with the module's own precedent and avoids
putting `AppUserId` on commands that are not `ICrmScoped`.

Skipping `SlotUnavailable` because the backend declares but never throws it is right. Porting dead code
because it exists upstream is how a fresh module inherits an old one's cruft.

## 4. The Portal dispositions are both correct

**`GetMyPortalMeetingsQuery` — resolve.** The `IProjectDirectory.GetProjectsByClientAsync` +
batch-sibling addition is the shape `PROGRESS.md` already named as natural, `ProjectSummary` already
carries `ClientId`, and the one-and-many rule is honoured. The observation that G9-14's original
objection ("would only ever return an empty list") *dissolves once M1-01 lands the handlers* is exactly
the kind of re-check a deferral deserves when its blocking condition changes.

**`DecideMyPortalApprovalCommand` — defer, question named.** The blocker is verified and concrete:
`IApprovalRequester` has no decide method, and adding one would be a synchronous cross-module write,
which `CLAUDE.md` forbids. Meanwhile `ApproverKind.Client` and `ApprovalsPermissions.Decide` both
already exist, so Approvals *already models a client decider*. Naming the question — *should the
client-portal approval decision be an Approvals-owned portal route rather than a Crm command?* — and
assigning it to Approvals is a far more useful deferral than "needs design."

That is the difference between the good deferral here and the bad one at G9-14 that produced the
Meetings gap: this one names the question, the owner, and the condition under which it reopens.

## 5. Two traps worth their place in the plan

Both are the kind of thing that costs an attempt when discovered at runtime:

- **`ux_crm_meeting_slots_meeting_accepted`** throws `DbUpdateException` on a second accepted slot
  unless the prior is demoted first — which is precisely what the reschedule orchestration does, so the
  ordering is load-bearing.
- **`CrmEnumMapping` static calls are not EF-translatable inside `Select`.** Naming the existing
  handler that already solves it (`GetMyPortalBriefQueryHandler`) is better than describing the fix.

And the client-path trap — `CrmAccessGuard`'s client branch can return a null `ContactId` because the
`CrmContacts` lookup is `FirstOrDefaultAsync` with no `IsPrimary` filter and no ordering, while
`CrmMeeting.Create` requires non-null — is a real defect waiting for the first portal meeting request.
Failing explicitly rather than surfacing a confusing `InvalidRequester` is right.

## 6. On the two weak verifies

Both were mine, and the plan is right that they are weaker than they sound. Writing the missing
`/scalar` and six-module-boot tests in M1-02 is the correct response — a plan's `**Verify:**` clause
that was never turned into a test is a claim, not a check.

That generalizes into the third M1-03 lesson the plan already proposes: **a plan's verify clauses
should be diffed against the suite at close-out**, the same way scope prose is diffed against the task
table. Two lists, same discipline.

## 7. Docker

Correctly identified as a hard blocker before execution, not something to discover mid-run. Nothing to
add.

---

**Run it.** H2-01 → M1-01 → M1-02 → M1-03, standing execution contract, Sites planning still out of
scope.
