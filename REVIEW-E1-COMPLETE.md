# E1-01 completion review

**Verdict: accepted, no follow-up.** All three findings verified in code. Finding 2 is the best catch
of the entire project.

---

## Verified

| Claim | Result |
|---|---|
| Both handlers fixed | ✓ `UploadTaskDocumentCommandHandler.cs:62` and `ApplyFileTemplateToTaskCommandHandler.cs:58`, both `projectStageId: task.ProjectStageId` |
| Stage-gate test comment corrected | ✓ `FileFlowTests.cs:61-75` — states what it proved before E1-01, what it proves now, and why the comment was corrected rather than left stale |
| Synthetic null-stage test exists and is genuine | ✓ `:259-282` — publishes a hand-built `DocumentUploadedIntegrationEvent` with `ProjectStageId: null, ProjectTaskId: taskId` through the real `IEventBus`, commented as "the pre-E1-01 event shape" |
| Red-first protocol | ✓ `:292` documents all three tests failing on their own empty-result assertion before the handler change |
| 266 = 262 + 4 | ✓ consistent with 10 `[Fact]`s now in `FileFlowTests.cs` |

## Finding 2 is the one that matters

The existing stage-gate test would have stayed green while its stated coverage evaporated. Before
E1-01 it exercised `DocumentUploadedIntegrationEventHandler`'s `owningStageId ??=` fallback; after
E1-01 the event carries the stage, so that branch never fires on that path. Green test, different
behaviour, doc comment now lying.

That is **the same failure mode as E1 itself, one level up** — and nobody asked for it to be checked.
The plan didn't require it, my review didn't name it, and no test would have gone red. Catching a test
that silently changes what it proves requires reading the test against the change, which is the thing
automation cannot do for you.

The fix is right in all three parts: correct the comment to say what it proves *now*, add the synthetic
null-stage event to keep the fallback branch genuinely covered, and cross-reference the two so neither
can drift again. The synthetic event is the correct instrument — the only way to reach that branch once
every real producer denormalizes.

## The record

Recording what the evidence supported rather than what my prompt asked for was the right call, and I
was wrong on the specifics it corrects. Correction #8's gate half *did* ship with a real verify; the
query half didn't; both stage-read paths had no coverage at all; and it *was* disclosed twice — but
re-framed from an overridden requirement into a deferred scope choice. That framing is the transferable
lesson and is now written up as such.

Worth keeping in view for Phase 4: **"a plan requirement was narrowed to a scope choice"** is a distinct
review question from "was it disclosed." The first is what happened here; the second is what everyone
checked, including me.

## The two documented divergences

Both are correct and correctly recorded:

- **A `ProjectDocument` routinely has stage *and* task set**, diverging from `DocumentFolder`'s
  at-most-one rule — with an explicit in-code warning against adding a symmetry guard that would reject
  every task upload. That warning is the valuable half; the divergence would otherwise read as an
  oversight to a future "consistency" cleanup.
- **A stage document list may contain a document whose folder isn't in that stage's folder list.**
  Document-scope and folder-scope are independent by design. Fine to leave, worth having stated.

Keeping Projects' `??=` fallback with a comment naming its covering test is the right call — it no
longer changes any outcome for current producers, so without that comment it reads as dead code and
gets deleted, taking gate correctness with it the first time a future writer forgets to denormalize.

## Standing

Documents module closed. Five modules, 266 tests, 14 architecture rules, working tree clean. Phase 4
(CRM) is next when you want it — and `PROGRESS.md`'s Phase 3 final report already carries
recommendations for planning it differently, which is the right place to start from.
