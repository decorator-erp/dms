# Review — E1-01 plan

**Verdict: approved, run it. No changes.** The plan corrects my review on three points; all three
verified, and two of them were my errors.

---

## 1. My review was wrong on three counts

| My claim | Verified reality |
|---|---|
| "The G7-07 verify does not exist in `tests/`" | **Wrong.** `AttachmentGate_OnTheOwningStage_IsSatisfied_ByADocumentUploadedToATaskUnderIt` is at `FileFlowTests.cs:65`. My grep used `"task under"` and `"under it"` — with spaces — against a PascalCase identifier (`ATaskUnderIt`). The pattern could not match. A badly constructed search returned a false negative and I reported it as fact. |
| "That verify would have caught E1" | **Wrong.** `DocumentUploadedIntegrationEventHandler.cs:29-37` resolves the stage itself: `owningStageId ??= await db.ProjectTasks…Select(t => t.ProjectStageId)`. The test passes with *and* without denormalization, so it is real but structurally blind to E1. |
| "The final report lists deviations but not this one" | **Wrong.** `PROGRESS.md:3127` (deferred table) and `:3232` (thinner-than-intended) both name `ForStageAsync`'s missing reach-through explicitly. |

The irony is exact: I criticized a missing test while running a search too weak to find the test that
existed. Same failure mode as the CRLF misdiagnosis two phases ago — a tool answering a different
question than the one I thought I asked, and me treating its output as ground truth.

## 2. What survives, and it is the sharper finding

The bug is real, and the plan's decomposition is better than mine: **correction #8 had two halves, and
exactly one shipped.**

- **Gate resolution** — shipped, on Projects' subscriber side, genuinely tested.
- **`IDocumentQueries.ForStageAsync`** — never shipped, and both stage-read paths have **zero test
  coverage of any kind**. That absence, not a missing verify, is why nothing caught it.

And the process lesson is more precise than "undisclosed": it *was* disclosed, twice, but **re-framed
from an overridden plan requirement into a deferred scope choice.** Reading `PROGRESS.md:3127` — *"the
GATE-satisfaction reach-through (the one thing this phase's verify block needed) lives on Projects'
subscriber instead"* — the narrowing is visible but presented as a design choice rather than a
requirement being overruled. That framing is what let it pass review, including mine. Recording it that
way is the transferable lesson, and the plan is right to insist the record match the evidence rather
than my prompt's wording.

## 3. What the plan adds that I missed

**`ApplyFileTemplateToTaskCommandHandler:56` carries the identical bug.** My review named only
`UploadTaskDocumentCommandHandler`. Fixing one and not the other would leave the same defect reachable
through apply-template — a fix that looks complete and isn't. The exhaustive five-site construction
sweep (with the three correct sites justified individually, including `CreateRevision` inheriting
`previous.ProjectStageId`) is the right way to establish that.

Also correct and worth keeping: **leaving Projects' `??=` fallback in place.** It short-circuits once
the event carries a stage, and removing it would make gate correctness depend on every current and
future writer remembering to denormalize. Defense in depth costs one null check here.

The red-first ordering — write both tests, confirm they fail on the empty-result assertion specifically,
then fix — is the right protocol, and sequencing it so red-then-green falls out of the work order
(nothing stashed, tree never dirty) is a genuine improvement over how I phrased it.

## 4. One thing to hold to

Confirm the two new tests fail on the **assertion**, not on setup — a test that errors in
`CreateProjectWithStageAndTaskAsync` before reaching the assert is red for the wrong reason and proves
nothing. The plan says this; it is the step most likely to be waved through.

Expected: **264/264**.
