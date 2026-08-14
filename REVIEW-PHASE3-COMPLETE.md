# Phase 3 completion review — verified against code at `63c13b2`

**Verdict: accepted with one required fix (E1). One plan requirement was silently dropped, and the
verify that would have caught it was never written.**

---

## 1. Verified — the report is accurate

| Claim | Result |
|---|---|
| 27 commits, H1-01 → G8-01 | ✓ `git rev-list --count e4be811..HEAD` = 27, every task present with its `PROGRESS.md` companion commit |
| Bug 2 — `AddModuleIntegrationEvents` double-wrapping | ✓ real, and worse than the summary conveys. See §2. |
| Bug 4 — `GlobalExceptionHandler` forcing 500 | ✓ `GlobalExceptionHandler.cs:22-26` now respects `BadHttpRequestException.StatusCode`; `DisableAntiforgery()` present on all five upload endpoints |
| A1 landed | ✓ `GateCondition.cs:33` property, `:78` factory threading |
| File storage hardening, `RootDocumentId`/`IsLocked` lineage | ✓ present in the model and `DocumentQueries` projection |

## 2. Bug 2 deserves more credit than it got

It is the **same bug class as the G0-03 Scrutor finding, in the code path that fix didn't cover.**
G0-03 correctly abandoned open-generic `Decorate` for commands. The integration-event path kept using
`Decorate(serviceType, decoratorType)` per *closed* type — which looks scoped but still wraps every
descriptor already registered under that service type.

It stayed invisible for four phases because no two modules subscribed to the same event. The moment
Documents became the second `ApprovalDecidedIntegrationEvent` subscriber, Documents' call re-wrapped
**Projects'** already-decorated handler in an `InboxDedupeDecorator<DocumentsDbContext,…>` — the wrong
`TContext` for Projects' handler — and the resulting closed-generic `Type.FullName` overflowed
`InboxMessage.HandlerName`'s `varchar(256)`. The fix (decorate only descriptors this call's own `Scan`
added, by captured list position) is correct and the doc comment explains it properly.

Worth noting for Phase 4: this is the second time "a registration API that looks scoped isn't" has cost
a real bug here. CRM will be the *third* subscriber to `ApprovalDecidedIntegrationEvent` — that path is
now proven, but the same suspicion should apply to any other shared-container registration helper.

## 3. E1 (required) — the stage reach-through was dropped

Plan correction #8 was explicit:

> `GetByStageAsync`/`ExistsForStageAsync` reach through tasks — they include documents attached to a
> task *under* that stage… `IDocumentQueries.ForStageAsync` must match, **or the `Attachment` gate on a
> stage will read as unsatisfied when a task under it has the file.**

What shipped:

```csharp
// DocumentQueries.cs:38-42
public async Task<IReadOnlyList<DocumentSummary>> ForStageAsync(Guid stageId, CancellationToken ct) =>
    await db.ProjectDocuments.AsNoTracking()
        .Where(d => d.ProjectStageId == stageId && d.IsActive)   // directly-scoped rows only
```

and the task-upload path never denormalizes the stage:

```csharp
// UploadTaskDocumentCommandHandler.cs:62
uploadedById.Value, folderId, projectTaskId: command.ProjectTaskId, description: command.Description);
```

`GetProjectDocumentsByStageQueryHandler.cs:19` has the same predicate. So a document uploaded to a task
has `ProjectTaskId = taskId`, `ProjectStageId = null`, and is invisible to every stage-scoped read.
**The exact failure correction #8 predicted is live.**

The G7-07 verify that would have caught it — *"an `Attachment` gate on a **stage** is satisfied by a
document uploaded to a **task under it**"* — does not exist in `tests/`. This is the one place in three
phases where a named verify was not written and its absence went unreported; the final report lists
deviations but not this one.

**Two viable fixes.** The handler already fetches `task` via `projectDirectory.GetTaskAsync`, and
`TaskSummary` carries the stage id:

- **(a) Denormalize at upload** — set `projectStageId: task.ProjectStageId` alongside `projectTaskId`.
  One-line change, keeps every read a single indexed predicate, and matches how this codebase already
  denormalizes across boundaries (`StageName` in `ProjectStageCompletedIntegrationEvent`).
  **Recommended.** Note that `ProjectStageId` then means "the stage this document belongs to, directly
  or via its task" — document that on the property, since it changes the field's meaning.
- **(b) Reach through at query time** — `ForStageAsync` would need the set of task ids under the stage,
  which is Projects' data, so it means an `IProjectDirectory` call per query. Slower, more coupling,
  and the gate-satisfaction path would need it too.

Take (a).

## 4. Also fix while there

`GetProjectDocumentsByStageQueryHandler.cs:19` carries the same predicate and must stay consistent with
`ForStageAsync` — whichever fix is chosen, both change together, or the API and the gate logic disagree
about what "stage documents" means.

## 5. Standing

Four real bugs caught by their own verify blocks, two of them in shared infrastructure that all five
modules depend on. That is the process working. E1 is the counter-example that proves the same point
from the other side: **the one requirement with no test is the one that shipped broken.**
